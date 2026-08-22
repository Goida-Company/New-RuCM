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
using System.Linq;
using System.Threading.Tasks;

namespace Content.Server._RuMC14.Governance;

/// <summary>
/// Keeps the current round staffed with temporary community responders and owns the in-game
/// invitation flow. PostgreSQL remains authoritative for eligibility, responses, reputation and grants.
/// Governance AHelp is handled exclusively by <see cref="GovernanceAHelpSystem"/>.
/// </summary>
public sealed class GovernanceDutySystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IServerDbManager _database = default!;
    [Dependency] private readonly EuiManager _euis = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly GovernanceManager _governance = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    private float _elapsed = float.MaxValue;
    private bool _checking;
    private readonly HashSet<long> _shownJuryInvitations = new();
    private readonly HashSet<NetUserId> _ahelpAccess = new();
    private readonly HashSet<long> _knownOpenAHelpTickets = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_governance.Enabled || _ticker.RunLevel != GameRunLevel.InRound)
        {
            RevokeAHelpAccess();
            _knownOpenAHelpTickets.Clear();
            return;
        }

        _elapsed += frameTime;
        var interval = Math.Clamp(_cfg.GetCVar(CCCVars.GovernanceDutyCheckSeconds), 10, 600);
        if (_checking || _elapsed < interval)
            return;

        _elapsed = 0;
        _ = CheckStaffingAsync();
    }

    private async Task CheckStaffingAsync()
    {
        if (_checking)
            return;

        _checking = true;
        try
        {
            var observers = new List<NetUserId>();
            var connected = new List<NetUserId>();
            foreach (var session in _players.Sessions)
            {
                if (session.Status is not (SessionStatus.Connected or SessionStatus.InGame))
                    continue;

                connected.Add(session.UserId);
                if (session.AttachedEntity is { } entity && HasComp<GhostComponent>(entity))
                    observers.Add(session.UserId);
            }

            var connectedSet = connected.ToHashSet();
            _ahelpAccess.RemoveWhere(userId => !connectedSet.Contains(userId));

            var onlineTarget = connected.Count switch
            {
                < 30 => 1,
                < 70 => 2,
                < 120 => 3,
                _ => 4,
            };
            var backlog = await _database.GetGovernanceOpenAHelpCountAsync();
            var backlogTarget = backlog == 0 ? 0 : 1 + (backlog - 1) / 5;
            var configuredTarget = _cfg.GetCVar(CCCVars.GovernanceDutyTargetResponders);
            var target = Math.Clamp(Math.Max(configuredTarget, onlineTarget + backlogTarget), 0, 16);
            var inviteSeconds = Math.Clamp(_cfg.GetCVar(CCCVars.GovernanceDutyInviteSeconds), 30, 600);
            var invitations = await _database.CreateGovernanceDutyInvitationsV2Async(
                _ticker.RoundId,
                observers,
                target,
                TimeSpan.FromSeconds(inviteSeconds));

            foreach (var invitation in invitations)
            {
                if (!_players.TryGetSessionById(invitation.UserId, out var session) ||
                    session.AttachedEntity is not { } entity ||
                    !HasComp<GhostComponent>(entity))
                {
                    // Never penalize a candidate for an invitation that could not be delivered.
                    await _database.RespondGovernanceDutyInvitationAsync(
                        invitation.Id,
                        invitation.UserId,
                        invitation.RoundId,
                        GovernanceDutyInvitationChoice.Recuse,
                        TimeSpan.FromMinutes(1),
                        0,
                        0,
                        0);
                    continue;
                }

                _euis.OpenEui(
                    new GovernanceDutyInviteEui(
                        invitation.Id,
                        GovernanceInviteKind.ModerationDuty,
                        invitation.RoundId.ToString(),
                        invitation.ExpiresAt,
                        0,
                        0,
                        0,
                        this),
                    session);
            }

            var juryInvitations = await _database.GetPendingGovernanceJuryInvitationsAsync(connected);
            var pendingJuryIds = juryInvitations.Select(invitation => invitation.Id).ToHashSet();
            _shownJuryInvitations.RemoveWhere(id => !pendingJuryIds.Contains(id));
            foreach (var invitation in juryInvitations)
            {
                if (!_shownJuryInvitations.Add(invitation.Id) ||
                    !_players.TryGetSessionById(invitation.UserId, out var session))
                {
                    continue;
                }

                _euis.OpenEui(
                    new GovernanceDutyInviteEui(
                        invitation.Id,
                        GovernanceInviteKind.Jury,
                        invitation.CaseId,
                        invitation.ExpiresAt,
                        0,
                        0,
                        0,
                        this),
                    session);
            }

            foreach (var session in _players.Sessions)
            {
                if (session.Status is not (SessionStatus.Connected or SessionStatus.InGame))
                    continue;

                var active = _governance.HasActiveDuty(session.UserId, _ticker.RoundId);
                if (active)
                    _ahelpAccess.Add(session.UserId);
                else
                    _ahelpAccess.Remove(session.UserId);
            }

            await NotifyOpenAHelpsAsync();
        }
        catch (Exception exception)
        {
            Log.Error($"Community duty staffing check failed: {exception}");
        }
        finally
        {
            _checking = false;
        }
    }

    private async Task NotifyOpenAHelpsAsync()
    {
        var responders = _players.Sessions
            .Where(session => (session.Status is SessionStatus.Connected or SessionStatus.InGame) &&
                              _ahelpAccess.Contains(session.UserId))
            .ToArray();
        if (responders.Length == 0)
            return;

        IReadOnlyList<GovernanceAHelpTicketInfo>? queue = null;
        foreach (var responder in responders)
        {
            queue = await GetAHelpQueueAsync(responder);
            if (queue.Count > 0)
                break;
        }

        queue ??= [];
        var open = queue.Where(ticket => ticket.Status == "open").ToArray();
        var openIds = open.Select(ticket => ticket.Id).ToHashSet();
        _knownOpenAHelpTickets.IntersectWith(openIds);
        var fresh = open.Where(ticket => _knownOpenAHelpTickets.Add(ticket.Id)).ToArray();
        if (fresh.Length == 0)
            return;

        foreach (var ticket in fresh)
        {
            var summary = ticket.Summary.Length > 180 ? ticket.Summary[..180] + "…" : ticket.Summary;
            foreach (var responder in responders)
            {
                RaiseNetworkEvent(new GovernanceAHelpQueueChanged(
                    ticket.Id,
                    ticket.ReporterUserId,
                    ticket.ReporterName,
                    summary,
                    open.Length), responder);
                _chat.DispatchServerMessage(
                    responder,
                    Loc.GetString(
                        "governance-ahelp-new-alert",
                        ("ticket", ticket.Id),
                        ("reporter", ticket.ReporterName),
                        ("count", open.Length)));
            }
        }

        await EntityManager.System<GovernanceAHelpSystem>().RefreshResponderEuisAsync();
    }

    public async Task RespondToInvitationAsync(
        ICommonSession player,
        long invitationId,
        GovernanceInviteKind kind,
        GovernanceDutyInviteChoice choice)
    {
        if (!_governance.Enabled || _ticker.RunLevel != GameRunLevel.InRound)
        {
            _chat.DispatchServerMessage(
                player,
                Loc.GetString(ResponseLocale(kind, GovernanceDutyResponseStatus.Invalid)));
            return;
        }

        if (kind == GovernanceInviteKind.ModerationDuty &&
            choice == GovernanceDutyInviteChoice.Accept &&
            (player.AttachedEntity is not { } entity || !HasComp<GhostComponent>(entity)))
        {
            // The player returned to a body after receiving the observer-only invitation.
            // Treat this as unavailable instead of leaving it to expire.
            try
            {
                await _database.RespondGovernanceDutyInvitationAsync(
                    invitationId,
                    player.UserId,
                    _ticker.RoundId,
                    GovernanceDutyInvitationChoice.Recuse,
                    TimeSpan.FromMinutes(1),
                    0,
                    0,
                    0);
            }
            catch (Exception exception)
            {
                Log.Error($"Could not recuse invalid duty acceptance {invitationId}: {exception}");
            }

            _chat.DispatchServerMessage(player, Loc.GetString("governance-duty-response-observer-required"));
            _elapsed = float.MaxValue;
            return;
        }

        try
        {
            var databaseChoice = choice switch
            {
                GovernanceDutyInviteChoice.Accept => GovernanceDutyInvitationChoice.Accept,
                GovernanceDutyInviteChoice.Decline => GovernanceDutyInvitationChoice.Decline,
                _ => GovernanceDutyInvitationChoice.Recuse,
            };
            var response = kind == GovernanceInviteKind.Jury
                ? await _database.RespondGovernanceJuryInvitationAsync(
                    invitationId,
                    player.UserId,
                    databaseChoice,
                    0,
                    0,
                    0)
                : await _database.RespondGovernanceDutyInvitationAsync(
                    invitationId,
                    player.UserId,
                    _ticker.RoundId,
                    databaseChoice,
                    TimeSpan.FromMinutes(Math.Clamp(
                        _cfg.GetCVar(CCCVars.GovernanceDutySessionMinutes),
                        1,
                        1440)),
                    0,
                    0,
                    0);

            if (kind == GovernanceInviteKind.ModerationDuty &&
                response.Status == GovernanceDutyResponseStatus.Accepted)
            {
                await _governance.RefreshDutyAsync(player.UserId);
                _ahelpAccess.Add(player.UserId);
                OpenAHelpQueue(player);
                _elapsed = float.MaxValue;
            }

            if (kind == GovernanceInviteKind.Jury)
                _shownJuryInvitations.Remove(invitationId);

            _chat.DispatchServerMessage(
                player,
                Loc.GetString(
                    ResponseLocale(kind, response.Status),
                    ("rating", response.CivicRating)));
            _elapsed = float.MaxValue;
        }
        catch (Exception exception)
        {
            Log.Error($"Governance invitation {invitationId} response failed: {exception}");
            _chat.DispatchServerMessage(
                player,
                Loc.GetString(ResponseLocale(kind, GovernanceDutyResponseStatus.Invalid)));
        }
    }

    public async Task<bool> CanUseAHelpAsync(ICommonSession player)
    {
        if (!_governance.Enabled || _ticker.RunLevel != GameRunLevel.InRound ||
            player.AttachedEntity is not { } entity || !HasComp<GhostComponent>(entity))
            return false;

        return await _governance.AuthorizeAsync(player.UserId, _ticker.RoundId, "moderation.ahelp") != null;
    }

    public async Task<IReadOnlyList<GovernanceAHelpTicketInfo>> GetAHelpQueueAsync(ICommonSession player)
    {
        if (!await CanUseAHelpAsync(player))
            return [];

        return await _database.GetGovernanceAHelpQueueAsync(player.UserId, _ticker.RoundId);
    }

    public async void OpenAHelpQueue(ICommonSession player)
    {
        if (!await CanUseAHelpAsync(player))
        {
            _chat.DispatchServerMessage(player, Loc.GetString("governance-ahelp-access-denied"));
            return;
        }

        _euis.OpenEui(new GovernanceAHelpQueueEui(), player);
    }

    private void RevokeAHelpAccess()
    {
        _ahelpAccess.Clear();
    }

    private static string ResponseLocale(
        GovernanceInviteKind kind,
        GovernanceDutyResponseStatus status)
    {
        var prefix = kind == GovernanceInviteKind.Jury ? "governance-jury" : "governance-duty";
        return status switch
        {
            GovernanceDutyResponseStatus.Accepted => $"{prefix}-response-accepted",
            GovernanceDutyResponseStatus.Declined => $"{prefix}-response-declined",
            GovernanceDutyResponseStatus.Recused => $"{prefix}-response-recused",
            GovernanceDutyResponseStatus.Expired => $"{prefix}-response-expired",
            GovernanceDutyResponseStatus.AlreadyHandled => $"{prefix}-response-handled",
            GovernanceDutyResponseStatus.NotObserver => "governance-duty-response-observer-required",
            _ => $"{prefix}-response-invalid",
        };
    }
}
