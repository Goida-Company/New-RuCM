using Content.Client._RMC14.Medical.HUD;
using Content.Shared._RMC14.Xenonids;
using Content.Shared.Damage;
using Content.Shared.Inventory.Events;
using Content.Shared.Overlays;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client.Overlays;

/// <summary>
/// Shows a healthy icon on mobs.
/// </summary>
public sealed partial class ShowHealthIconsSystem : EquipmentHudSystem<ShowHealthIconsComponent>
{
    [Dependency] private IPrototypeManager _prototypeMan = default!;
    [Dependency] private CMHealthIconsSystem _healthIcons = default!;
    [Dependency] private CMXenoHealthIconsSystem _xenoHealthIcons = default!;

    [ViewVariables]
    public HashSet<string> DamageContainers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DamageableComponent, GetStatusIconsEvent>(OnGetStatusIconsEvent);
        SubscribeLocalEvent<XenoComponent, GetStatusIconsEvent>(OnGetXenoStatusIcons);
        SubscribeLocalEvent<ShowHealthIconsComponent, AfterAutoHandleStateEvent>(OnHandleState);
    }

    protected override void UpdateInternal(RefreshEquipmentHudEvent<ShowHealthIconsComponent> component)
    {
        base.UpdateInternal(component);

        DamageContainers.Clear();

        foreach (var comp in component.Components)
        {
            foreach (var damageContainerId in comp.DamageContainers)
            {
                DamageContainers.Add(damageContainerId);
            }
        }
    }

    protected override void DeactivateInternal()
    {
        base.DeactivateInternal();

        DamageContainers.Clear();
    }

    private void OnHandleState(Entity<ShowHealthIconsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        RefreshOverlay();
    }

    private void OnGetStatusIconsEvent(Entity<DamageableComponent> entity, ref GetStatusIconsEvent args)
    {
        if (!IsActive || HasComp<XenoComponent>(entity) || !IsAllowedDamageContainer(entity.Comp))
            return;

        if (_healthIcons.TryGetIcon(entity, out var healthIcon))
            args.StatusIcons.Add(healthIcon);
    }

    private void OnGetXenoStatusIcons(Entity<XenoComponent> entity, ref GetStatusIconsEvent args)
    {
        if (!IsActive || !TryComp<DamageableComponent>(entity, out var damageable) ||
            !IsAllowedDamageContainer(damageable))
        {
            return;
        }

        if (_xenoHealthIcons.TryGetIcon(entity, out var healthIcon) && healthIcon is not null)
            args.StatusIcons.Add(healthIcon);
    }

    private bool IsAllowedDamageContainer(DamageableComponent damageable)
    {
        return damageable.DamageContainerID is { } id && DamageContainers.Contains(id);
    }
}
