using Content.Server.Administration;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._AU14.Administration;

[AdminCommand(AdminFlags.Host)]
public sealed class AdminOOCColorCommand : IConsoleCommand
{
    public string Command => "adminooccolors";
    public string Description => "Configure OOC colors for admin groups.";
    public string Help => "adminooccolors";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player == null)
        {
            shell.WriteError("This command can only be run by a player.");
            return;
        }

        IoCManager.Resolve<EuiManager>().OpenEui(new AdminOOCColorEui(), shell.Player);
    }
}
