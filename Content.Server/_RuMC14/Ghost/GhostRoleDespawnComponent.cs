namespace Content.Server._RuMC14.Ghost;

[RegisterComponent]
public sealed partial class GhostRoleDespawnComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(60);

    [ViewVariables]
    public TimeSpan ExpiresAt;
}
