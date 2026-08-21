using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._RuMC14.Governance;

[CVarDefs]
public sealed class GovernanceCVars : CVars
{
    /// <summary>
    /// Seconds an accepted moderation-duty responder may be disconnected or leave observer state
    /// before the duty is marked abandoned and the slot is made available for replacement.
    /// </summary>
    public static readonly CVarDef<int> DutyDisconnectGraceSeconds =
        CVarDef.Create("governance.duty_disconnect_grace_seconds", 300, CVar.SERVERONLY | CVar.ARCHIVE);
}
