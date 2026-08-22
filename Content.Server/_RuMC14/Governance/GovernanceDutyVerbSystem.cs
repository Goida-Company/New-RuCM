using System.Threading.Tasks;
using Content.Server.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
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
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly GovernanceManager _governance = default!;
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
            Act = () => _ = TeleportToAsync(actor.PlayerSession, args.User, args.Target, targetUserId, targetName),
        });
    }

    private async Task TeleportToAsync(
        ICommonSession responder,
        EntityUid responderEntity,
        EntityUid targetEntity,
        NetUserId targetUserId,
        string targetName)
    {
        if (_ticker.RoundId <= 0 || !HasComp<GhostComponent>(responderEntity))
            return;

        var authorization = await _governance.AuthorizeAsync(responder.UserId, _ticker.RoundId, "moderation.ahelp");
        if (authorization == null || Deleted(targetEntity) || Deleted(responderEntity))
            return;

        var coordinates = _transform.GetMapCoordinates(targetEntity);
        if (coordinates.MapId == MapId.Nullspace)
            return;

        _transform.SetMapCoordinates(responderEntity, coordinates);
        await _governance.AuditAsync(
            "moderation.teleport_to_player",
            responder.UserId,
            targetUserId,
            "duty_session",
            authorization.Duty.Id.ToString(),
            new { round_id = _ticker.RoundId, target_name = targetName });
    }
}
