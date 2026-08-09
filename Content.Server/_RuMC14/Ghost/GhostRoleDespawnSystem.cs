using Content.Server.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Mind.Components;
using Robust.Shared.Timing;

namespace Content.Server._RuMC14.Ghost;

public sealed class GhostRoleDespawnSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GhostRoleDespawnComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<GhostRoleDespawnComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.ExpiresAt = _timing.CurTime + ent.Comp.Delay;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var time = _timing.CurTime;
        var query = EntityQueryEnumerator<GhostRoleDespawnComponent>();
        while (query.MoveNext(out var uid, out var despawn))
        {
            if (TryComp(uid, out MindContainerComponent? mind) && mind.HasMind)
            {
                RemCompDeferred<GhostRoleDespawnComponent>(uid);
                continue;
            }

            if (time < despawn.ExpiresAt)
                continue;

            _adminLog.Add(LogType.Action, LogImpact.Low,
                $"{ToPrettyString(uid)} was deleted after its ghost role went unclaimed for {despawn.Delay.TotalSeconds} seconds");

            QueueDel(uid);
        }
    }
}
