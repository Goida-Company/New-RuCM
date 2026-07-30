using Content.Server.EUI;
using Robust.Shared.Console;

namespace Content.Server._CMU14.Yautja;

public sealed partial class YautjaClanInfoCommand : LocalizedCommands
{
    [Dependency] private EuiManager _eui = default!;

    public override string Command => "yautja_clan_info";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        _eui.OpenEui(new YautjaClanInfoEui(), player);
    }
}
