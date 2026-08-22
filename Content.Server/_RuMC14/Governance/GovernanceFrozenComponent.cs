using System;

namespace Content.Server._RuMC14.Governance;

[RegisterComponent]
public sealed partial class GovernanceFrozenComponent : Component
{
    [DataField]
    public Guid Token;
}
