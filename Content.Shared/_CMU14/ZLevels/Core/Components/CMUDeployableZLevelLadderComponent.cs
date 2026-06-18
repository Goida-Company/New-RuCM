using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._CMU14.ZLevels.Core.Components;

/// <summary>
/// A one-use item that deploys paired CMU Z-level ladders between the current map and the map above it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CMUDeployableZLevelLadderComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId LowerPrototype = "CMUZLevelLadderThroughUp3";

    [DataField, AutoNetworkedField]
    public EntProtoId UpperPrototype = "CMUZLevelLadderThroughDown3";

    [DataField, AutoNetworkedField]
    public EntProtoId PackedPrototype = "CMUDeployableZLevelLadder";

    [DataField, AutoNetworkedField]
    public float ExistingLadderRadius = 0.3f;
}
