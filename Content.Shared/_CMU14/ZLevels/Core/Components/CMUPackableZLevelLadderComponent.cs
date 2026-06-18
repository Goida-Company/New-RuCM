using Robust.Shared.Prototypes;

namespace Content.Shared._CMU14.ZLevels.Core.Components;

/// <summary>
/// Marks a ladder half spawned by a deployable ladder kit so it can be packed back up with its paired half.
/// </summary>
[RegisterComponent]
public sealed partial class CMUPackableZLevelLadderComponent : Component
{
    [DataField]
    public EntProtoId PackedPrototype = "CMUDeployableZLevelLadder";

    public EntityUid? Partner;
}
