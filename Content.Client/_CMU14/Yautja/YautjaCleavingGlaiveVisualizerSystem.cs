using Content.Shared._CMU14.Yautja;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Item;
using Content.Shared.Toggleable;
using Robust.Client.GameObjects;

namespace Content.Client._CMU14.Yautja;

public sealed partial class YautjaCleavingGlaiveVisualizerSystem : VisualizerSystem<YautjaCleavingGlaiveComponent>
{
    [Dependency] private ClothingSystem _clothing = default!;
    [Dependency] private SharedItemSystem _item = default!;

    protected override void OnAppearanceChange(EntityUid uid, YautjaCleavingGlaiveComponent component, ref AppearanceChangeEvent args)
    {
        if (!TryComp<ClothingComponent>(uid, out var clothing) ||
            !AppearanceSystem.TryGetData<bool>(uid, ToggleableVisuals.Enabled, out var skull, args.Component))
            return;
        _clothing.SetLayerState(clothing, "back", "glaive-back", skull ? "glaive_skull" : "glaive");
        _item.VisualsChanged(uid);
    }
}
