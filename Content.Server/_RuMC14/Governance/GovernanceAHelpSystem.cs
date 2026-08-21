using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Notes;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.GameTicking;
using Content.Shared._RuMC14.Governance;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Ghost;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._RuMC14.Governance;

public sealed record GovernanceAHelpActionExecutionResult(
    string? ErrorLocaleKey,
    IReadOnlyList<GovernanceLogLine> Logs)
{
    public static GovernanceAHelpActionExecutionResult Success => new(null, []);
}

/// <summary>
/// Owns the modern in-game Governance AHelp surface for both players and temporary responders.
/// This system does not depend on BwoinkSystem: messages, assignment and status live in PostgreSQL
/// and are presented directly through Governance EUIs.
/// </summary>
public sealed partial class GovernanceAHelpSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IServerDbManager _database = default!;
    [Dependency] private readonly EuiManager _euis = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly GovernanceManager _governance = default!;
    [Dependency] private readonly IPlayerLocator _locator = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    private readonly HashSet<GovernanceAHelpQueueEui> _responderEuis = new();
    private readonly Dictionary<NetUserId, HashSet<GovernanceAHelpPlayerEui>> _playerEuis = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GovernanceAHelpOpenRequest>(OnOpenRequest);
    }

    private async void OnOpenRequest(GovernanceAHelpOpenRequest message, EntitySessionEventArgs args)
    {
        if (await CanUseResponderAsync(args.SenderSession))
        {
            EntityManager.System<GovernanceDutySystem>().OpenAHelpQueue(args.SenderSession);
            return;
        }

        OpenPlayerHelp(args.SenderSession);
    }

    public void OpenPlayerHelp(ICommonSession player)
    {
        if (!_governance.Enabled || _ticker.RoundId <= 0)
        {
            _chat.DispatchServerMessage(player, Loc.GetString("governance-ahelp-player-unavailable"));
            return;
        }

        _euis.OpenEui(new GovernanceAHelpPlayerEui(this), player);
    }

    public async Task<bool> CanUseResponderAsync(ICommonSession player)
    {
        if (!_governance.Enabled || _ticker.RoundId <= 0 ||
            player.AttachedEntity is not { } entity || !HasComp<GhostComponent>(entity))
            return false;

        return await _governance.AuthorizeAsync(player.UserId, _ticker.RoundId, "moderation.ahelp") != null;
    }

    public async Task<IReadOnlyList<GovernanceAHelpTicketInfo>> GetQueueAsync(ICommonSession player)
    {
        if (!await CanUseResponderAsync(player))
            return [];

        return await _database.GetGovernanceAHelpQueueAsync(player.UserId, _ticker.RoundId);
    }

    public async Task<bool> ClaimAsync(ICommonSession player, long ticketId)
    {
        if (!await CanUseResponderAsync(player))
            return false;

        var claimed = await _database.ClaimGovernanceAHelpAsync(ticketId, player.UserId, _ticker.RoundId);
        if (claimed)
        {
            await RefreshResponderEuisAsync();
            await RefreshTicketReporterAsync(ticketId, player);
        }

        return claimed;
    }

    public async Task<IReadOnlyList<GovernanceAHelpModernTranscriptLine>> GetResponderTranscriptAsync(
        ICommonSession player,
        long ticketId)
    {
        if (!await CanUseResponderAsync(player))
            return [];

        return await _database.GetGovernanceAHelpResponderTranscriptAsync(
            ticketId,
            player.UserId,
            _ticker.RoundId);
    }

    public async Task<bool> SendResponderMessageAsync(ICommonSession player, long ticketId, string text)
    {
        if (!await CanUseResponderAsync(player) || string.IsNullOrWhiteSpace(text))
            return false;

        var reporter = await _database.SendGovernanceAHelpResponderMessageAsync(
            ticketId,
            player.UserId,
            _ticker.RoundId,
            text);
        if (reporter == null)
            return false;

        await RefreshResponderEuisAsync();
        await RefreshPlayerEuisAsync(reporter.Value);

        if (_players.TryGetSessionById(reporter.Value, out var reporterSession))
        {
            var preview = text.Trim();
            if (preview.Length > 160)
                preview = preview[..160] + "…";
            RaiseNetworkEvent(new GovernanceAHelpPlayerReplyReceived(ticketId, preview), reporterSession);
        }

        return true;
    }

    public async Task<bool> SetStatusAsync(ICommonSession player, long ticketId, string status)
    {
        if (!await CanUseResponderAsync(player))
            return false;

        var queue = await _database.GetGovernanceAHelpQueueAsync(player.UserId, _ticker.RoundId);
        var ticket = queue.SingleOrDefault(value => value.Id == ticketId && value.ClaimedByMe);
        if (ticket == null)
            return false;

        var changed = await _database.SetGovernanceAHelpStatusAsync(
            ticketId,
            player.UserId,
            _ticker.RoundId,
            status);
        if (!changed)
            return false;

        await RefreshResponderEuisAsync();
        await RefreshPlayerEuisAsync(ticket.ReporterUserId);
        return true;
    }

    public async Task<string?> CreateIncidentAsync(
        ICommonSession player,
        long ticketId,
        string targetQuery,
        string incidentType)
    {
        // Creating the formal case container is an AHelp workflow operation. The actual containment
        // actions below still require their own stronger capabilities and quorum.
        if (!await CanUseResponderAsync(player))
            return "governance-ahelp-incident-access-denied";

        targetQuery = targetQuery.Trim();
        if (string.IsNullOrWhiteSpace(targetQuery))
            return "governance-ahelp-incident-target-required";

        incidentType = incidentType.Trim();
        if (incidentType.Length is < 2 or > 64)
            return "governance-ahelp-incident-type-invalid";

        ICommonSession? target = null;
        if (Guid.TryParse(targetQuery, out var targetGuid))
        {
            _players.TryGetSessionById(new NetUserId(targetGuid), out target);
        }
        else
        {
            target = _players.Sessions.FirstOrDefault(session =>
                session.Status is SessionStatus.Connected or SessionStatus.InGame &&
                session.Name.Equals(targetQuery, StringComparison.OrdinalIgnoreCase));
        }

        if (target == null)
            return "governance-ahelp-incident-target-not-found";
        if (target.UserId == player.UserId)
            return "governance-ahelp-incident-self-target";

        var targetCharacterName = target.AttachedEntity is { } targetEntity
            ? MetaData(targetEntity).EntityName
            : target.Name;
        var incident = await _database.CreateGovernanceAHelpIncidentAsync(
            ticketId,
            player.UserId,
            target.UserId,
            target.Name,
            targetCharacterName,
            _ticker.RoundId,
            incidentType);
        if (incident == null)
            return "governance-ahelp-incident-create-failed";

        await RefreshResponderEuisAsync();
        return null;
    }

    public async Task<GovernanceAHelpIncidentInfo?> GetIncidentAsync(ICommonSession player, long ticketId)
    {
        if (!await CanUseResponderAsync(player))
            return null;

        return await _database.GetGovernanceAHelpIncidentAsync(ticketId, player.UserId, _ticker.RoundId);
    }

    public async Task<string?> OpenFullLogsAsync(ICommonSession player)
    {
        if (!await CanUseResponderAsync(player))
            return "governance-ahelp-records-access-denied";

        var authorization = await _governance.AuthorizeAsync(player.UserId, _ticker.RoundId, "moderation.view_logs");
        if (authorization == null)
            return "governance-ahelp-records-access-denied";

        _euis.OpenEui(new AdminLogsEui(governanceDutyAccess: true), player);
        await _governance.AuditAsync(
            "moderation.logs.opened",
            player.UserId,
            null,
            "duty_session",
            authorization.Duty.Id.ToString(),
            new { round_id = _ticker.RoundId, access = "full_read" });
        return null;
    }

    public async Task<string?> OpenPlayerNotesAsync(ICommonSession player, string targetQuery)
    {
        if (!await CanUseResponderAsync(player))
            return "governance-ahelp-records-access-denied";

        var authorization = await _governance.AuthorizeAsync(player.UserId, _ticker.RoundId, "moderation.view_logs");
        if (authorization == null)
            return "governance-ahelp-records-access-denied";

        targetQuery = targetQuery.Trim();

        var located = await _locator.LookupIdByNameOrIdAsync(targetQuery);
        if (located == null)
            return "governance-ahelp-notes-target-not-found";

        _euis.OpenEui(new AdminNotesEui(located.UserId, governanceDutyAccess: true), player);
        await _governance.AuditAsync(
            "moderation.notes.opened",
            player.UserId,
            located.UserId,
            "duty_session",
            authorization.Duty.Id.ToString(),
            new { round_id = _ticker.RoundId, target = located.UserId.ToString() });
        return null;
    }

    public async Task<GovernanceAHelpActionExecutionResult> ProposeIncidentActionAsync(
        ICommonSession player,
        long ticketId,
        string actionType,
        string reason,
        int? durationSeconds)
    {
        if (!await CanUseResponderAsync(player))
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-access-denied", []);

        if (actionType is not ("freeze" or "round_remove"))
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-invalid", []);

        reason = reason.Trim();
        if (reason.Length is < 10 or > 512)
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-reason-invalid", []);

        if (actionType == "freeze" && durationSeconds is < 1 or > 120)
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-freeze-duration-invalid", []);

        var incident = await _database.GetGovernanceAHelpIncidentAsync(ticketId, player.UserId, _ticker.RoundId);
        if (incident == null)
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-no-incident", []);
        if (incident.CourtCaseId != null)
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-court-escalated", []);

        var result = await _database.CreateGovernanceModerationActionAsync(
            incident.Id,
            player.UserId,
            incident.TargetUserId,
            _ticker.RoundId,
            actionType,
            reason,
            durationSeconds);
        if (result == null)
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-create-failed", []);

        if (result.Status == "approved")
        {
            var execution = await ExecuteModerationActionAsync(player, result);
            if (execution != null)
                return execution;
        }

        await RefreshResponderEuisAsync();
        return GovernanceAHelpActionExecutionResult.Success;
    }

    public async Task<GovernanceAHelpActionExecutionResult> ReviewIncidentActionAsync(
        ICommonSession player,
        long actionId,
        string decision)
    {
        if (!await CanUseResponderAsync(player))
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-access-denied", []);

        var result = await _database.ReviewGovernanceModerationActionAsync(
            actionId,
            player.UserId,
            _ticker.RoundId,
            decision);
        if (result == null)
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-review-failed", []);

        if (result.Status == "approved")
        {
            var execution = await ExecuteModerationActionAsync(player, result);
            if (execution != null)
                return execution;
        }

        await RefreshResponderEuisAsync();
        return GovernanceAHelpActionExecutionResult.Success;
    }

    private async Task<GovernanceAHelpActionExecutionResult?> ExecuteModerationActionAsync(
        ICommonSession player,
        GovernanceModerationActionInfo action)
    {
        if (!_players.TryGetSessionById(action.TargetUserId, out var target) ||
            target.AttachedEntity is not { } targetEntity ||
            player.AttachedEntity is not { } actorEntity)
        {
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-target-unavailable", []);
        }

        var result = await EntityManager.System<GovernanceEventActionSystem>().ExecuteModerationActionAsync(
            player,
            actorEntity,
            target,
            targetEntity,
            action);
        if (!result)
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-execution-failed", []);

        await RefreshResponderEuisAsync();
        return null;
    }

    public async Task<IReadOnlyList<GovernanceModerationActionInfo>> GetIncidentActionsAsync(
        ICommonSession player,
        long ticketId)
    {
        if (!await CanUseResponderAsync(player))
            return [];

        return await _database.GetGovernanceAHelpIncidentActionsAsync(ticketId, player.UserId, _ticker.RoundId);
    }

    public async Task<IReadOnlyList<GovernancePendingModerationApprovalInfo>> GetPendingActionApprovalsAsync(
        ICommonSession player)
    {
        if (!await CanUseResponderAsync(player))
            return [];

        return await _database.GetGovernancePendingModerationApprovalsAsync(player.UserId, _ticker.RoundId);
    }

    public async Task<string?> EscalateIncidentToCourtAsync(ICommonSession player, long ticketId, string reason)
    {
        if (!await CanUseResponderAsync(player))
            return "governance-ahelp-court-access-denied";

        reason = reason.Trim();
        if (reason.Length is < 10 or > 1500)
            return "governance-ahelp-court-reason-invalid";

        var result = await _database.EscalateGovernanceAHelpIncidentToCourtAsync(
            ticketId,
            player.UserId,
            _ticker.RoundId,
            reason);
        if (result == null)
            return "governance-ahelp-court-create-failed";

        await RefreshResponderEuisAsync();
        return null;
    }

    public async Task RefreshResponderEuisAsync()
    {
        foreach (var eui in _responderEuis.ToArray())
            await eui.RefreshFromSystemAsync();
    }

    public void RegisterResponderEui(GovernanceAHelpQueueEui eui) => _responderEuis.Add(eui);
    public void UnregisterResponderEui(GovernanceAHelpQueueEui eui) => _responderEuis.Remove(eui);

    private async Task RefreshTicketReporterAsync(long ticketId, ICommonSession? responder = null)
    {
        var ticket = await _database.GetGovernanceAHelpTicketAsync(ticketId);
        if (ticket == null)
            return;

        await RefreshPlayerEuisAsync(ticket.ReporterUserId);
        if (_players.TryGetSessionById(ticket.ReporterUserId, out var reporter))
        {
            RaiseNetworkEvent(
                new GovernanceAHelpResponderReplyReceived(
                    ticket.Id,
                    responder?.Name ?? string.Empty,
                    ticket.Summary.Length > 160 ? ticket.Summary[..160] + "…" : ticket.Summary),
                reporter);
        }
    }

    public async Task RefreshPlayerEuisAsync(NetUserId userId)
    {
        if (!_playerEuis.TryGetValue(userId, out var euis))
            return;

        foreach (var eui in euis.ToArray())
            await eui.RefreshFromSystemAsync();
    }

    public void RegisterPlayerEui(NetUserId userId, GovernanceAHelpPlayerEui eui)
    {
        if (!_playerEuis.TryGetValue(userId, out var euis))
            _playerEuis[userId] = euis = new HashSet<GovernanceAHelpPlayerEui>();
        euis.Add(eui);
    }

    public void UnregisterPlayerEui(NetUserId userId, GovernanceAHelpPlayerEui eui)
    {
        if (!_playerEuis.TryGetValue(userId, out var euis))
            return;
        euis.Remove(eui);
        if (euis.Count == 0)
            _playerEuis.Remove(userId);
    }
}