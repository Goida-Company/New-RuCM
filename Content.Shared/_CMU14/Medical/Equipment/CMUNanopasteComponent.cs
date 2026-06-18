using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;

namespace Content.Shared._CMU14.Medical.Equipment;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CMUNanopasteComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Uses = 10;

    [DataField, AutoNetworkedField]
    public FixedPoint2 RepairAmount = FixedPoint2.New(15);
}
