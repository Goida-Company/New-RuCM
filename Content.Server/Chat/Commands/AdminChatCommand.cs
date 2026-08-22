using Content.Server._RuMC14.Governance;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Chat.Commands
{
    [AnyCommand]
    internal sealed partial class AdminChatCommand : LocalizedCommands
    {
        [Dependency] private IEntitySystemManager _systems = default!;

        public override string Command => "asay";

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            var player = shell.Player;

            if (player == null)
            {
                shell.WriteError(Loc.GetString($"shell-cannot-run-command-from-server"));
                return;
            }

            if (args.Length < 1)
                return;

            var message = string.Join(" ", args).Trim();
            if (string.IsNullOrEmpty(message))
                return;

            _systems.GetEntitySystem<GovernanceAdminChatSystem>().TrySendAdminChat(player, message);
        }
    }
}
