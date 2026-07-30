using Content.Shared._RMC14.Xenonids;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._RMC14.Medical.HUD;

/// <summary>
/// Provides the dynamic xenonid health icon used by viewers with a Xeno health HUD.
/// </summary>
public sealed partial class CMXenoHealthIconsSystem : EntitySystem
{
    private static readonly ResPath HealthHudRsi = new("/Textures/_RMC14/Interface/xeno_hud.rsi");

    [Dependency] private MobThresholdSystem _mobThresholds = default!;

    public bool TryGetIcon(Entity<XenoComponent> entity, out StatusIconData? statusIcon)
    {
        statusIcon = null;

        if (!TryComp<DamageableComponent>(entity, out var damageable) ||
            !TryComp<MobStateComponent>(entity, out var mobState) ||
            !TryComp<MobThresholdsComponent>(entity, out var mobThresholds))
        {
            return false;
        }

        _mobThresholds.TryGetThresholdForState(
            entity,
            MobState.Critical,
            out FixedPoint2? criticalThreshold,
            mobThresholds);
        _mobThresholds.TryGetDeadThreshold(entity, out FixedPoint2? deadThreshold, mobThresholds);

        var state = CMXenoHealthIconState.GetState(
            damageable.TotalDamage,
            mobState.CurrentState,
            criticalThreshold,
            deadThreshold);
        if (state is null)
            return false;

        statusIcon = new StatusIconData
        {
            Icon = new SpriteSpecifier.Rsi(HealthHudRsi, state),
            Priority = 1,
            LocationPreference = StatusIconLocationPreference.Left,
            IsShaded = false,
        };
        return true;
    }
}
