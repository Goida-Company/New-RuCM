using System.Linq;
using System.Text;
using Content.Server.Administration.Managers;
using Content.Server.Afk;
using Content.Server.GameTicking;
using Content.Server._RuMC14.Governance;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Utility;

namespace Content.Server.Administration.Commands;

[AnyCommand]
public sealed partial class AdminWhoCommand : LocalizedEntityCommands // RuMC edit
{
    [Dependency] private IAfkManager _afkManager = default!;
    [Dependency] private IAdminManager _adminManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private GovernanceManager _governance = default!;

    public override string Command => "adminwho";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var seeStealth = true;

        // If null it (hopefully) means it is being called from the console.
        if (shell.Player != null)
        {
            var playerData = _adminManager.GetAdminData(shell.Player);

            seeStealth = playerData != null && playerData.CanStealth();
        }

        var sb = new StringBuilder();
        var first = true;
        foreach (var admin in _adminManager.ActiveAdmins)
        {
            var adminData = _adminManager.GetAdminData(admin)!;
            DebugTools.AssertNotNull(adminData);

            if (adminData.Stealth && !seeStealth)
                continue;

            if (!first)
                sb.Append('\n');
            first = false;

            sb.Append(admin.Name);
            if (adminData.Title is { } title)
                sb.Append($": [{title}]");

            if (adminData.Stealth)
                sb.Append(" (S)");

            if (shell.Player is { } player && _adminManager.HasAdminFlag(player, AdminFlags.Admin))
            {
                if (_afkManager.IsAfk(admin))
                    sb.Append(" [AFK]");
            }
        }

        var dutyResponders = _playerManager.Sessions
            .Where(player => _governance.HasActiveDuty(player.UserId, _ticker.RoundId))
            .Where(player =>
            {
                var adminData = _adminManager.GetAdminData(player);
                return adminData == null || !adminData.Stealth || seeStealth;
            })
            .OrderBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sb.Length > 0)
            sb.AppendLine().AppendLine();

        sb.AppendLine("Дежурные RUCM:");
        if (dutyResponders.Length == 0)
        {
            sb.Append("Нет активных дежурных.");
        }
        else
        {
            for (var index = 0; index < dutyResponders.Length; index++)
            {
                if (index > 0)
                    sb.AppendLine();
                sb.Append("• ").Append(dutyResponders[index].Name);
            }
        }

        shell.WriteLine(sb.ToString());
    }
}
