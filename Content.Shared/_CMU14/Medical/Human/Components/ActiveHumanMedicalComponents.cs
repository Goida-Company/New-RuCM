using System;

namespace Content.Shared._CMU14.Medical.Human.Components;

[RegisterComponent]
public sealed partial class ActiveBleedingComponent : Component;

[RegisterComponent]
public sealed partial class ActiveOrganSymptomsComponent : Component;

[RegisterComponent]
public sealed partial class ActiveBoneKnittingComponent : Component
{
    public TimeSpan LastUpdate;
    public TimeSpan NextUpdate;
}

[RegisterComponent]
public sealed partial class ActiveUnsplintedFractureRiskComponent : Component;

[RegisterComponent]
public sealed partial class ActiveEmbeddedObjectMovementComponent : Component;

[RegisterComponent]
public sealed partial class ActiveTourniquetComponent : Component
{
    public TimeSpan LastUpdate;
    public TimeSpan NextUpdate;
}

[RegisterComponent]
public sealed partial class ActiveTreatedWoundHealingComponent : Component
{
    public TimeSpan LastUpdate;
    public TimeSpan NextUpdate;
}

[RegisterComponent]
public sealed partial class ActiveMedicalSummaryDirtyComponent : Component;
