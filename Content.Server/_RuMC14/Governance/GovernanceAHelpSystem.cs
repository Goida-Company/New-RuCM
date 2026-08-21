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
public sealed class GovernanceAHelpSystem : EntitySystem
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
        if (string.IsNullOrWhiteSpace(targetQuery))
            return "governance-ahelp-notes-target-required";

        Guid targetId;
        if (Guid.TryParse(targetQuery, out var parsed))
        {
            targetId = parsed;
        }
        else
        {
            var located = await _locator.LookupIdByNameAsync(targetQuery);
            if (located == null)
                return "governance-ahelp-notes-target-not-found";
            targetId = located.UserId;
        }

        var notes = new AdminNotesEui(governanceDutyReadOnly: true);
        _euis.OpenEui(notes, player);
        await notes.ChangeNotedPlayer(targetId);

        await _governance.AuditAsync(
            "moderation.notes.opened",
            player.UserId,
            new NetUserId(targetId),
            "duty_session",
            authorization.Duty.Id.ToString(),
            new { round_id = _ticker.RoundId, access = "full_read" });
        return null;
    }

    public async Task<string?> EscalateIncidentToCourtAsync(
        ICommonSession player,
        long ticketId,
        string reason)
    {
        if (!await CanUseResponderAsync(player))
            return "governance-ahelp-court-access-denied";

        reason = reason.Trim();
        if (reason.Length is < 10 or > 1500)
            return "governance-ahelp-court-reason-invalid";

        var incident = await GetIncidentAsync(player, ticketId);
        if (incident == null)
            return "governance-ahelp-action-no-incident";

        var court = await _database.EscalateGovernanceIncidentToCourtAsync(
            ticketId,
            player.UserId,
            _ticker.RoundId,
            reason);
        if (court == null)
            return "governance-ahelp-court-create-failed";

        await RefreshResponderEuisAsync();
        return null;
    }

    public async Task<IReadOnlyList<GovernanceIncidentActionInfo>> GetIncidentActionsAsync(
        ICommonSession player,
        long incidentId)
    {
        if (!await CanUseResponderAsync(player))
            return [];

        return await _database.GetGovernanceIncidentActionsAsync(incidentId, player.UserId, _ticker.RoundId);
    }

    public async Task<IReadOnlyList<GovernanceIncidentActionInfo>> GetPendingActionApprovalsAsync(ICommonSession player)
    {
        if (!await CanUseResponderAsync(player))
            return [];

        return await _database.GetGovernancePendingActionApprovalsAsync(player.UserId, _ticker.RoundId);
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

        var incident = await GetIncidentAsync(player, ticketId);
        if (incident == null)
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-no-incident", []);
        if (incident.CourtCaseId != null)
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-court-escalated", []);

        if (actionType is not ("freeze" or "round_remove" or "request_explanation" or "view_logs"))
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-invalid", []);

        reason = reason.Trim();
        if (reason.Length is < 10 or > 512)
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-reason-invalid", []);

        var capability = $"moderation.{actionType}";
        if (await _governance.AuthorizeAsync(player.UserId, _ticker.RoundId, capability) == null)
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-access-denied", []);

        if (actionType == "freeze")
        {
            var maxSeconds = Math.Clamp(_cfg.GetCVar(CCCVars.GovernanceFreezeMaxSeconds), 1, 120);
            if (durationSeconds is null || durationSeconds < 1 || durationSeconds > maxSeconds)
                return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-freeze-duration-invalid", []);
        }
        else
        {
            durationSeconds = null;
        }

        short requiredApprovals = actionType == "round_remove" ? (short) 2 : (short) 1;
        var action = await _database.ProposeGovernanceIncidentActionAsync(
            incident.Id,
            player.UserId,
            _ticker.RoundId,
            actionType,
            reason,
            durationSeconds,
            requiredApprovals);
        if (action == null)
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-create-failed", []);

        GovernanceAHelpActionExecutionResult execution = GovernanceAHelpActionExecutionResult.Success;
        if (action.Status == "approved")
            execution = await ExecuteApprovedActionAsync(player, action);

        await RefreshResponderEuisAsync();
        return execution;
    }

    public async Task<GovernanceAHelpActionExecutionResult> ReviewIncidentActionAsync(
        ICommonSession player,
        long actionId,
        string decision)
    {
        if (!await CanUseResponderAsync(player) || decision is not ("approve" or "reject"))
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-review-failed", []);

        var action = await _database.ReviewGovernanceIncidentActionAsync(
            actionId,
            player.UserId,
            _ticker.RoundId,
            decision);
        if (action == null)
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-review-failed", []);

        GovernanceAHelpActionExecutionResult execution = GovernanceAHelpActionExecutionResult.Success;
        if (action.Status == "approved")
            execution = await ExecuteApprovedActionAsync(player, action);

        await RefreshResponderEuisAsync();
        return execution;
    }

    private async Task<GovernanceAHelpActionExecutionResult> ExecuteApprovedActionAsync(
        ICommonSession executor,
        GovernanceIncidentActionInfo action)
    {
        if (!_players.TryGetSessionById(action.TargetUserId, out var target))
            return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-target-unavailable", []);

        var system = EntityManager.System<GovernanceSystem>();
        switch (action.ActionType)
        {
            case "request_explanation":
            {
                var result = await system.TryRequestExplanationAsync(executor, target, action.Id, action.Reason);
                return result.Allowed
                    ? GovernanceAHelpActionExecutionResult.Success
                    : new GovernanceAHelpActionExecutionResult("governance-ahelp-action-execution-failed", []);
            }
            case "view_logs":
            {
                var result = await system.TryViewLogsAsync(executor, target, action.Id);
                return result.Allowed
                    ? new GovernanceAHelpActionExecutionResult(null, result.Logs)
                    : new GovernanceAHelpActionExecutionResult("governance-ahelp-action-execution-failed", []);
            }
            case "freeze":
            {
                var result = await system.TryFreezeAsync(
                    executor,
                    target,
                    action.DurationSeconds ?? 0,
                    action.Id,
                    action.Reason);
                return result.Allowed
                    ? GovernanceAHelpActionExecutionResult.Success
                    : new GovernanceAHelpActionExecutionResult("governance-ahelp-action-execution-failed", []);
            }
            case "round_remove":
            {
                var result = await system.TryRoundRemoveAsync(executor, target, action.Id, action.Reason);
                return result.Allowed
                    ? GovernanceAHelpActionExecutionResult.Success
                    : new GovernanceAHelpActionExecutionResult("governance-ahelp-action-execution-failed", []);
            }
            default:
                return new GovernanceAHelpActionExecutionResult("governance-ahelp-action-invalid", []);
        }
    }

    public Task<GovernanceAHelpPlayerTicketInfo?> GetPlayerTicketAsync(ICommonSession player)
    {
        return _database.GetGovernanceAHelpPlayerTicketAsync(player.UserId, _ticker.RoundId);
    }

    public Task<IReadOnlyList<GovernanceAHelpModernTranscriptLine>> GetPlayerTranscriptAsync(ICommonSession player)
    {
        return _database.GetGovernanceAHelpPlayerTranscriptAsync(player.UserId, _ticker.RoundId);
    }

    public async Task<bool> SendPlayerMessageAsync(ICommonSession player, string text)
    {
        if (!_governance.Enabled || _ticker.RoundId <= 0 || string.IsNullOrWhiteSpace(text))
            return false;

        var ticketId = await _database.SendGovernanceAHelpPlayerMessageAsync(
            player.UserId,
            _ticker.RoundId,
            text);
        if (ticketId == null)
            return false;

        await RefreshPlayerEuisAsync(player.UserId);
        await RefreshResponderEuisAsync();

        var responderId = await _database.GetGovernanceAHelpResponderAsync(player.UserId, _ticker.RoundId);
        if (responderId != null && _players.TryGetSessionById(responderId.Value, out var responderSession))
        {
            var preview = text.Trim();
            if (preview.Length > 160)
                preview = preview[..160] + "…";
            RaiseNetworkEvent(
                new GovernanceAHelpResponderReplyReceived(ticketId.Value, player.Name, preview),
                responderSession);
        }

        return true;
    }

    public async Task<bool> ResolveByPlayerAsync(ICommonSession player)
    {
        if (!_governance.Enabled || _ticker.RoundId <= 0)
            return false;

        var resolved = await _database.ResolveGovernanceAHelpByReporterAsync(player.UserId, _ticker.RoundId);
        if (resolved)
        {
            await RefreshPlayerEuisAsync(player.UserId);
            await RefreshResponderEuisAsync();
        }

        return resolved;
    }

    public void RegisterResponderEui(GovernanceAHelpQueueEui eui)
    {
        _responderEuis.Add(eui);
    }

    public void UnregisterResponderEui(GovernanceAHelpQueueEui eui)
    {
        _responderEuis.Remove(eui);
    }

    public void RegisterPlayerEui(NetUserId userId, GovernanceAHelpPlayerEui eui)
    {
        if (!_playerEuis.TryGetValue(userId, out var euis))
        {
            euis = new HashSet<GovernanceAHelpPlayerEui>();
            _playerEuis[userId] = euis;
        }

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

    public async Task RefreshResponderEuisAsync()
    {
        foreach (var eui in _responderEuis.ToArray())
            await eui.RefreshFromSystemAsync();
    }

    public async Task RefreshPlayerEuisAsync(NetUserId userId)
    {
        if (!_playerEuis.TryGetValue(userId, out var euis))
            return;

        foreach (var eui in euis.ToArray())
            await eui.RefreshFromSystemAsync();
    }

    private async Task RefreshTicketReporterAsync(long ticketId, ICommonSession responder)
    {
        var queue = await _database.GetGovernanceAHelpQueueAsync(responder.UserId, _ticker.RoundId);
        var ticket = queue.SingleOrDefault(value => value.Id == ticketId);
        if (ticket != null)
            await RefreshPlayerEuisAsync(ticket.ReporterUserId);
    }
}
