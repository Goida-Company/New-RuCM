using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._RuMC14.Governance;

/// <summary>
/// Exposes only the investigation verbs a temporary Governance responder needs. This intentionally
/// does not add the normal Admin verb set and therefore does not promote Duty responders to admins.
/// </summary>
public sealed class GovernanceDutyVerbSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IServerDbManager _database = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly GovernanceManager _governance = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (_ticker.RoundId <= 0 ||
            !TryComp<ActorComponent>(args.User, out var actor) ||
            !HasComp<GhostComponent>(args.User) ||
            !_governance.HasActiveDuty(actor.PlayerSession.UserId, _ticker.RoundId) ||
            !TryComp<ActorComponent>(args.Target, out var targetActor))
        {
            return;
        }

        var targetUserId = targetActor.PlayerSession.UserId;
        var targetName = targetActor.PlayerSession.Name;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("governance-duty-verb-notes"),
            Category = VerbCategory.Admin,
            Act = () => _ = EntityManager.System<GovernanceAHelpSystem>()
                .OpenPlayerNotesAsync(actor.PlayerSession, targetUserId.ToString()),
        });

        if (args.User == args.Target)
            return;

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("governance-duty-verb-teleport-to"),
            Category = VerbCategory.Admin,
            Act = () => _ = TeleportResponderToPlayerAsync(actor.PlayerSession, targetUserId),
        });

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("governance-duty-verb-teleport-here"),
            Category = VerbCategory.Admin,
            Act = () => _ = TeleportHereAsync(actor.PlayerSession, args.User, args.Target, targetUserId, targetName),
        });
    }

    /// <summary>
    /// Shared server-authoritative implementation used by both the entity verb and AHelp workspace.
    /// The target is resolved from a server-side NetUserId; clients never provide coordinates.
    /// </summary>
    public async Task<bool> TeleportResponderToPlayerAsync(ICommonSession responder, NetUserId targetUserId)
    {
        if (_ticker.RoundId <= 0 ||
            responder.AttachedEntity is not { } responderEntity ||
            !HasComp<GhostComponent>(responderEntity) ||
            !_players.TryGetSessionById(targetUserId, out var targetSession) ||
            targetSession.Status is not (SessionStatus.Connected or SessionStatus.InGame) ||
            targetSession.AttachedEntity is not { } targetEntity ||
            Deleted(targetEntity) ||
            Deleted(responderEntity))
        {
            return false;
        }

        var authorization = await _governance.AuthorizeAsync(responder.UserId, _ticker.RoundId, "moderation.ahelp");
        if (authorization == null ||
            Deleted(targetEntity) ||
            Deleted(responderEntity) ||
            !HasComp<GhostComponent>(responderEntity))
        {
            return false;
        }

        var coordinates = _transform.GetMapCoordinates(targetEntity);
        if (coordinates.MapId == MapId.Nullspace)
            return false;

        _transform.SetMapCoordinates(responderEntity, coordinates);
        await _governance.AuditAsync(
            "moderation.teleport_to_player",
            responder.UserId,
            targetUserId,
            "duty_session",
            authorization.Duty.Id.ToString(),
            new { round_id = _ticker.RoundId, target_name = targetSession.Name });
        return true;
    }

    private async Task TeleportHereAsync(
        ICommonSession responder,
        EntityUid responderEntity,
        EntityUid targetEntity,
        NetUserId targetUserId,
        string targetName)
    {
        if (_ticker.RoundId <= 0 || !HasComp<GhostComponent>(responderEntity) || responder.UserId == targetUserId)
            return;

        var authorization = await _governance.AuthorizeAsync(responder.UserId, _ticker.RoundId, "moderation.ahelp");
        if (authorization == null || Deleted(targetEntity) || Deleted(responderEntity))
            return;

        var coordinates = _transform.GetMapCoordinates(responderEntity);
        if (coordinates.MapId == MapId.Nullspace)
            return;

        // Bringing a player to a responder is materially more intrusive than moving the observer.
        // Persist the live incident first and fail closed if Governance cannot record it.
        var incidentId = await _database.CreateGovernanceDutyTeleportIncidentAsync(
            responder.UserId,
            targetUserId,
            _ticker.RoundId);
        if (incidentId == null)
        {
            _chat.DispatchServerMessage(responder, Loc.GetString("governance-duty-teleport-here-failed"));
            return;
        }

        if (Deleted(targetEntity) || Deleted(responderEntity) || !HasComp<GhostComponent>(responderEntity))
            return;

        coordinates = _transform.GetMapCoordinates(responderEntity);
        if (coordinates.MapId == MapId.Nullspace)
            return;

        _transform.SetMapCoordinates(targetEntity, coordinates);
        await _governance.AuditAsync(
            "moderation.teleport_player_to_self",
            responder.UserId,
            targetUserId,
            "live_incident",
            incidentId.Value.ToString(),
            new
            {
                round_id = _ticker.RoundId,
                target_name = targetName,
                duty_id = authorization.Duty.Id,
                incident_id = incidentId.Value,
            });

        _chat.DispatchServerMessage(
            responder,
            Loc.GetString(
                "governance-duty-teleport-here-success",
                ("target", targetName),
                ("incident", incidentId.Value)));
    }
}