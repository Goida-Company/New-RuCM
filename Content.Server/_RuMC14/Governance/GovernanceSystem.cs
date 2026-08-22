using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Ghost;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._RuMC14.Governance;

public sealed class GovernanceSystem : EntitySystem
{
    public const string FreezeCapability = "moderation.freeze";
    public const string RoundRemoveCapability = "moderation.round_remove";
    public const string ExplanationCapability = "moderation.request_explanation";
    public const string ViewLogsCapability = "moderation.view_logs";

    [Dependency] private readonly IAdminLogManager _adminLogs = default!;
    [Dependency] private readonly AdminFrozenSystem _adminFrozen = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly GovernanceManager _governance = default!;
    [Dependency] private readonly IServerDbManager _database = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    private readonly Dictionary<NetUserId, int> _roundRemoved = new();

    public override void Initialize()
    {
        base.Initialize();
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        base.Shutdown();
    }

    public async Task<GovernanceActionResult> TryFreezeAsync(
        ICommonSession actor,
        ICommonSession target,
        int durationSeconds,
        long actionId,
        string reason)
    {
        var actorIsObserver = actor.AttachedEntity is { } actorEntity && HasComp<GhostComponent>(actorEntity);
        var maximumSeconds = Math.Clamp(_cfg.GetCVar(CCCVars.GovernanceFreezeMaxSeconds), 1, 120);
        var denial = GovernancePolicy.ValidateFreeze(
            _governance.Enabled,
            actorIsObserver,
            actor.UserId,
            target.UserId,
            durationSeconds,
            maximumSeconds);

        if (denial != GovernanceDenial.None)
            return await DenyAsync(denial, actor, target, actionId, durationSeconds);

        if (actionId <= 0 || string.IsNullOrWhiteSpace(reason) || reason.Length > 512)
        {
            return await DenyAsync(
                GovernanceDenial.InvalidInput,
                actor,
                target,
                actionId,
                durationSeconds);
        }

        var authorization = await _governance.AuthorizeAsync(actor.UserId, _gameTicker.RoundId, FreezeCapability);
        if (authorization == null)
            return await DenyAsync(GovernanceDenial.NotOnDuty, actor, target, actionId, durationSeconds);

        var action = await _governance.AuthorizeActionAsync(
            actor.UserId, target.UserId, _gameTicker.RoundId, actionId, "freeze");
        if (action == null)
            return await DenyAsync(GovernanceDenial.ActionNotApproved, actor, target, actionId, durationSeconds);

        // State may have changed while the database request was in flight.
        if (actor.AttachedEntity is not { } currentActor || !HasComp<GhostComponent>(currentActor))
            return await DenyAsync(GovernanceDenial.NotObserver, actor, target, actionId, durationSeconds);
        if (target.AttachedEntity is not { } targetEntity || Deleted(targetEntity))
            return await DenyAsync(GovernanceDenial.TargetUnavailable, actor, target, actionId, durationSeconds);
        if (HasComp<AdminFrozenComponent>(targetEntity))
            return await DenyAsync(GovernanceDenial.AlreadyFrozen, actor, target, actionId, durationSeconds);

        var token = Guid.NewGuid();
        var governanceFrozen = EnsureComp<GovernanceFrozenComponent>(targetEntity);
        governanceFrozen.Token = token;
        _adminFrozen.FreezeAndMute(targetEntity);

        Timer.Spawn(TimeSpan.FromSeconds(durationSeconds), () => ReleaseFreeze(targetEntity, token));
        await _governance.AuditAsync(
            "moderation.freeze.executed",
            actor.UserId,
            target.UserId,
            "live_incident",
            action.IncidentId.ToString(),
            new
            {
                round_id = _gameTicker.RoundId,
                duration_seconds = durationSeconds,
                reason,
                duty_session_id = authorization.Duty.Id,
                capability_expires_at = authorization.ExpiresAt,
                moderation_action_id = actionId,
            });
        await _governance.CompleteActionAsync(actionId);

        return GovernanceActionResult.Success;
    }

    public async Task<GovernanceActionResult> TryRoundRemoveAsync(
        ICommonSession actor,
        ICommonSession target,
        long actionId,
        string reason)
    {
        if (!_governance.Enabled)
            return await DenyAsync(GovernanceDenial.Disabled, actor, target, actionId, 0, "round_remove");
        if (actor.AttachedEntity is not { } actorEntity || !HasComp<GhostComponent>(actorEntity))
            return await DenyAsync(GovernanceDenial.NotObserver, actor, target, actionId, 0, "round_remove");
        if (actor.UserId == target.UserId || actionId <= 0 || string.IsNullOrWhiteSpace(reason) || reason.Length > 512)
            return await DenyAsync(GovernanceDenial.InvalidInput, actor, target, actionId, 0, "round_remove");
        var capability = await _governance.AuthorizeAsync(actor.UserId, _gameTicker.RoundId, RoundRemoveCapability);
        if (capability == null)
            return await DenyAsync(GovernanceDenial.NotOnDuty, actor, target, actionId, 0, "round_remove");
        var action = await _governance.AuthorizeActionAsync(
            actor.UserId, target.UserId, _gameTicker.RoundId, actionId, "round_remove");
        if (action == null)
            return await DenyAsync(GovernanceDenial.ActionNotApproved, actor, target, actionId, 0, "round_remove");

        _roundRemoved[target.UserId] = _gameTicker.RoundId;
        await _governance.CompleteActionAsync(actionId);
        await _governance.AuditAsync(
            "moderation.round_remove.executed", actor.UserId, target.UserId, "live_incident", action.IncidentId.ToString(),
            new { round_id = _gameTicker.RoundId, reason, moderation_action_id = actionId, duty_session_id = capability.Duty.Id });
        target.Channel.Disconnect("Вы удалены до конца раунда решением дежурных сообщества.");
        return GovernanceActionResult.Success;
    }

    public async Task<GovernanceActionResult> TryRequestExplanationAsync(
        ICommonSession actor,
        ICommonSession target,
        long actionId,
        string reason)
    {
        var denial = ValidateBoundedAction(actor, target, actionId, reason);
        if (denial != GovernanceDenial.None)
            return await DenyAsync(denial, actor, target, actionId, 0, "request_explanation");
        if (await _governance.AuthorizeAsync(actor.UserId, _gameTicker.RoundId, ExplanationCapability) == null)
            return await DenyAsync(GovernanceDenial.NotOnDuty, actor, target, actionId, 0, "request_explanation");
        var action = await _governance.AuthorizeActionAsync(
            actor.UserId, target.UserId, _gameTicker.RoundId, actionId, "request_explanation");
        if (action == null)
            return await DenyAsync(GovernanceDenial.ActionNotApproved, actor, target, actionId, 0, "request_explanation");
        if (actor.AttachedEntity is not { } actorEntity || !HasComp<GhostComponent>(actorEntity))
            return await DenyAsync(GovernanceDenial.NotObserver, actor, target, actionId, 0, "request_explanation");
        if (target.AttachedEntity is not { } targetEntity || Deleted(targetEntity))
            return await DenyAsync(GovernanceDenial.TargetUnavailable, actor, target, actionId, 0, "request_explanation");

        long ticketId;
        try
        {
            var created = await _database.OpenGovernanceExplanationAHelpAsync(
                target.UserId,
                actor.UserId,
                _gameTicker.RoundId,
                reason);
            if (created == null)
                return await DenyAsync(GovernanceDenial.AHelpUnavailable, actor, target, actionId, 0, "request_explanation");

            ticketId = created.Value;
        }
        catch (Exception exception)
        {
            Log.Error($"Could not open Governance explanation AHelp: {exception}");
            return await DenyAsync(GovernanceDenial.DatabaseUnavailable, actor, target, actionId, 0, "request_explanation");
        }

        var ahelp = EntityManager.System<GovernanceAHelpSystem>();
        await ahelp.RefreshResponderEuisAsync();
        ahelp.OpenPlayerHelp(target);
        await ahelp.RefreshPlayerEuisAsync(target.UserId);

        await _governance.CompleteActionAsync(actionId);
        await _governance.AuditAsync(
            "moderation.request_explanation.executed",
            actor.UserId,
            target.UserId,
            "live_incident",
            action.IncidentId.ToString(),
            new { round_id = _gameTicker.RoundId, reason, moderation_action_id = actionId, ahelp_ticket_id = ticketId });
        return GovernanceActionResult.Success;
    }

    public async Task<GovernanceLogAccessResult> TryViewLogsAsync(
        ICommonSession actor,
        ICommonSession target,
        long actionId)
    {
        var denial = ValidateBoundedAction(actor, target, actionId, "view logs");
        if (denial != GovernanceDenial.None)
        {
            await DenyAsync(denial, actor, target, actionId, 0, "view_logs");
            return new GovernanceLogAccessResult(denial, []);
        }
        if (await _governance.AuthorizeAsync(actor.UserId, _gameTicker.RoundId, ViewLogsCapability) == null)
        {
            await DenyAsync(GovernanceDenial.NotOnDuty, actor, target, actionId, 0, "view_logs");
            return new GovernanceLogAccessResult(GovernanceDenial.NotOnDuty, []);
        }
        var action = await _governance.AuthorizeActionAsync(
            actor.UserId, target.UserId, _gameTicker.RoundId, actionId, "view_logs");
        if (action == null)
        {
            await DenyAsync(GovernanceDenial.ActionNotApproved, actor, target, actionId, 0, "view_logs");
            return new GovernanceLogAccessResult(GovernanceDenial.ActionNotApproved, []);
        }
        if (actor.AttachedEntity is not { } actorEntity || !HasComp<GhostComponent>(actorEntity))
        {
            await DenyAsync(GovernanceDenial.NotObserver, actor, target, actionId, 0, "view_logs");
            return new GovernanceLogAccessResult(GovernanceDenial.NotObserver, []);
        }

        List<Content.Shared.Administration.Logs.SharedAdminLog> logs;
        try
        {
            logs = await _adminLogs.All(new LogFilter
            {
                Round = _gameTicker.RoundId,
                AnyPlayers = [target.UserId.UserId],
                IncludePlayers = true,
                IncludeNonPlayers = false,
                Limit = 100,
            });
        }
        catch (Exception exception)
        {
            Log.Error($"Could not read Governance incident logs: {exception}");
            await DenyAsync(GovernanceDenial.DatabaseUnavailable, actor, target, actionId, 0, "view_logs");
            return new GovernanceLogAccessResult(GovernanceDenial.DatabaseUnavailable, []);
        }
        var result = logs.Select(log => new GovernanceLogLine(
            new DateTimeOffset(log.Date),
            log.Type.ToString(),
            log.Message)).ToArray();
        await _governance.CompleteActionAsync(actionId);
        await _governance.AuditAsync(
            "moderation.view_logs.executed",
            actor.UserId,
            target.UserId,
            "live_incident",
            action.IncidentId.ToString(),
            new { round_id = _gameTicker.RoundId, moderation_action_id = actionId, returned_logs = result.Length });
        return new GovernanceLogAccessResult(GovernanceDenial.None, result);
    }

    private GovernanceDenial ValidateBoundedAction(
        ICommonSession actor,
        ICommonSession target,
        long actionId,
        string reason)
    {
        if (!_governance.Enabled)
            return GovernanceDenial.Disabled;
        if (actor.AttachedEntity is not { } actorEntity || !HasComp<GhostComponent>(actorEntity))
            return GovernanceDenial.NotObserver;
        if (actor.UserId == target.UserId)
            return GovernanceDenial.SelfTarget;
        if (actionId <= 0 || string.IsNullOrWhiteSpace(reason) || reason.Length > 512)
            return GovernanceDenial.InvalidInput;
        if (target.AttachedEntity is not { } targetEntity || Deleted(targetEntity))
            return GovernanceDenial.TargetUnavailable;
        return GovernanceDenial.None;
    }

    private void ReleaseFreeze(EntityUid target, Guid token)
    {
        if (Deleted(target) || !TryComp<GovernanceFrozenComponent>(target, out var governanceFrozen) ||
            governanceFrozen.Token != token)
        {
            return;
        }

        RemComp<GovernanceFrozenComponent>(target);
        RemComp<AdminFrozenComponent>(target);
    }

    private async Task<GovernanceActionResult> DenyAsync(
        GovernanceDenial denial,
        ICommonSession actor,
        ICommonSession target,
        long actionId,
        int durationSeconds,
        string actionType = "freeze")
    {
        await _governance.AuditAsync(
            $"moderation.{actionType}.denied",
            actor.UserId,
            target.UserId,
            "moderation_action",
            actionId <= 0 ? "invalid" : actionId.ToString(),
            new
            {
                round_id = _gameTicker.RoundId,
                duration_seconds = durationSeconds,
                denial = denial.ToString(),
            });
        return new GovernanceActionResult(denial);
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (!_roundRemoved.TryGetValue(args.Session.UserId, out var roundId))
            return;
        if (roundId != _gameTicker.RoundId)
        {
            _roundRemoved.Remove(args.Session.UserId);
            return;
        }
        if (args.NewStatus is SessionStatus.Connected or SessionStatus.InGame)
            args.Session.Channel.Disconnect("Вы удалены до конца текущего раунда решением дежурных сообщества.");
    }
}
