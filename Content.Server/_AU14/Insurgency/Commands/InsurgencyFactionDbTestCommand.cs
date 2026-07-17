using System;
using Content.Server.Administration;
using Content.Server._AU14.Insurgency.Database;
using Content.Shared._AU14.Insurgency;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._AU14.Insurgency.Commands;

/// <summary>
///     Debug round-trip for the faction DB layer: saves a throwaway faction, reads it back, then
///     deletes it, reporting each step. Proves create / read / delete against the live SQLite (or
///     Postgres) DB without needing the editor UI. Not a shipping feature.
///
///     Runs as async void and awaits the DB. Never block the main thread on a DB task: the DB
///     manager marshals completion back through the game loop, so blocking (GetResult / Wait)
///     deadlocks the whole server.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class InsurgencyFactionDbTestCommand : IConsoleCommand
{
    public string Command => "insforfactiondbtest";
    public string Description => Loc.GetString("cmd-insforfactiondbtest-desc");
    public string Help => Loc.GetString("cmd-insforfactiondbtest-help");

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        var db = entMan.System<InsurgencyFactionDbSystem>();

        var def = new FactionDefinition
        {
            Metadata =
            {
                Title = Loc.GetString("cmd-insforfactiondbtest-title"),
                Description = Loc.GetString("cmd-insforfactiondbtest-description"),
                RoleplayText = Loc.GetString("cmd-insforfactiondbtest-roleplay"),
            },
        };

        try
        {
            var id = await db.AddFactionAsync(def, isDefault: true);
            shell.WriteLine(Loc.GetString("cmd-insforfactiondbtest-saved", ("id", id)));

            var loaded = await db.GetFactionAsync(id);
            shell.WriteLine(loaded == null
                ? Loc.GetString("cmd-insforfactiondbtest-read-error")
                : Loc.GetString("cmd-insforfactiondbtest-read", ("title", loaded.Metadata.Title), ("version", loaded.SchemaVersion)));

            var deleted = await db.DeleteFactionAsync(id);
            shell.WriteLine(deleted
                ? Loc.GetString("cmd-insforfactiondbtest-deleted")
                : Loc.GetString("cmd-insforfactiondbtest-delete-error"));
        }
        catch (Exception e)
        {
            shell.WriteError(Loc.GetString("cmd-insforfactiondbtest-failed", ("message", e.Message)));
        }
    }
}
