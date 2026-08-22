using Content.Client._RuMC14.Governance;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;

namespace Content.Client._RMC14.Mentor;

/// <summary>
/// Legacy controller name retained for existing menu references.
/// Mentor Help is retired: callers are redirected to the Governance Support Center.
/// </summary>
public sealed partial class StaffHelpUIController : UIController
{
    [UISystemDependency] private GovernanceAHelpClientSystem _governanceAHelp = default!;

    public bool IsMentor => false;

    // Legacy compatibility only. Mentor status can no longer change, but ChatUIController still
    // subscribes to this API. Explicit no-op accessors avoid a dead backing event (CS0067) while
    // keeping existing callers source-compatible until the remaining Mentor chat surface is removed.
    public event Action? MentorStatusUpdated
    {
        add { }
        remove { }
    }

    public void ToggleWindow()
    {
        _governanceAHelp.RequestOpen();
    }
}
