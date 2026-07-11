// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 wray-git
// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Maths;

namespace Content.Server._AU14.SavedBuilds;

/// <summary>
/// Test/dev command for saved-build placement: places the given build file at your feet, optionally
/// rotated. Usage: <c>placebuild &lt;fileId&gt; [degrees]</c>. The menu "Place Build" button uses the same path.
/// </summary>
[AnyCommand]
public sealed class PlaceBuildCommand : IConsoleCommand
{
    public string Command => "placebuild";
    public string Description => "Place a saved build (by file name) at your position.";
    public string Help => "placebuild <fileId> [degrees]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { AttachedEntity: { } user } player)
        {
            shell.WriteError("This command can only be run by a player.");
            return;
        }

        if (args.Length < 1)
        {
            shell.WriteLine(Help);
            return;
        }

        var degrees = 0.0;
        if (args.Length >= 2 && !double.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out degrees))
        {
            shell.WriteError("Degrees must be a number.");
            return;
        }

        var entMan = IoCManager.Resolve<IEntityManager>();
        var system = entMan.System<SavedBuildSystem>();
        var coords = entMan.GetComponent<TransformComponent>(user).Coordinates;
        system.PlaceBuild(player, args[0], coords, Angle.FromDegrees(degrees));
    }
}
