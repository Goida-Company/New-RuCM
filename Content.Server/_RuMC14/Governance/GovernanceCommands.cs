using System;
using Content.Server.Commands;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;

namespace Content.Server._RuMC14.Governance;

[AnyCommand]
public sealed partial class GovernanceStatusCommand : IConsoleCommand
{
    public string Command => "governance_status";
    public string Description => Loc.GetString("cmd-governance-status-description");
    public string Help => Loc.GetString("cmd-governance-status-help", ("command", Command));

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-governance-player-only"));
            return;
        }

        var governance = IoCManager.Resolve<GovernanceManager>();
        var ticker = IoCManager.Resolve<IEntityManager>().System<GameTicker>();
        var duty = await governance.RefreshDutyAsync(player.UserId);
        if (duty == null || duty.RoundId != ticker.RoundId)
        {
            shell.WriteLine(Loc.GetString("cmd-governance-status-inactive"));
            return;
        }

        shell.WriteLine(Loc.GetString(
            "cmd-governance-status-active",
            ("session", duty.Id),
            ("round", duty.RoundId),
            ("expires", duty.ExpiresAt)));
    }
}

[AnyCommand]
public sealed partial class GovernanceAHelpCommand : IConsoleCommand
{
    public string Command => "governance_ahelp";
    public string Description => Loc.GetString("cmd-governance-ahelp-description");
    public string Help => Loc.GetString("cmd-governance-ahelp-help", ("command", Command));

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-governance-player-only"));
            return;
        }
        IoCManager.Resolve<IEntityManager>().System<GovernanceDutySystem>().OpenAHelpQueue(player);
    }
}

[AnyCommand]
public sealed partial class GovernanceFreezeCommand : IConsoleCommand
{
    public string Command => "governance_freeze";
    public string Description => Loc.GetString("cmd-governance-freeze-description");
    public string Help => Loc.GetString("cmd-governance-freeze-help", ("command", Command));

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } actor)
        {
            shell.WriteError(Loc.GetString("cmd-governance-player-only"));
            return;
        }

        if (args.Length < 4 || !int.TryParse(args[1], out var seconds) || !long.TryParse(args[2], out var actionId))
        {
            shell.WriteError(Help);
            return;
        }

        if (!CommandUtils.TryGetSessionByUsernameOrId(shell, args[0], actor, out var target))
            return;

        var reason = string.Join(' ', args[3..]);
        var system = IoCManager.Resolve<IEntityManager>().System<GovernanceSystem>();
        var result = await system.TryFreezeAsync(actor, target, seconds, actionId, reason);
        if (!result.Allowed)
        {
            shell.WriteError(Loc.GetString("cmd-governance-freeze-denied", ("reason", DenialText(result.Denial))));
            return;
        }

        shell.WriteLine(Loc.GetString(
            "cmd-governance-freeze-success",
            ("target", target.Name),
            ("seconds", seconds),
            ("incident", actionId)));
    }

    private static string DenialText(GovernanceDenial denial)
    {
        var key = denial switch
        {
            GovernanceDenial.Disabled => "governance-denial-disabled",
            GovernanceDenial.DatabaseUnavailable => "governance-denial-invalid-input",
            GovernanceDenial.InvalidInput => "governance-denial-invalid-input",
            GovernanceDenial.NotOnDuty => "governance-denial-not-on-duty",
            GovernanceDenial.NotObserver => "governance-denial-not-observer",
            GovernanceDenial.SelfTarget => "governance-denial-self-target",
            GovernanceDenial.InvalidDuration => "governance-denial-invalid-duration",
            GovernanceDenial.TargetUnavailable => "governance-denial-target-unavailable",
            GovernanceDenial.AlreadyFrozen => "governance-denial-already-frozen",
            GovernanceDenial.ActionNotApproved => "governance-denial-action-not-approved",
            _ => "governance-denial-unknown",
        };
        return Loc.GetString(key);
    }
}

[AnyCommand]
public sealed partial class GovernanceRoundRemoveCommand : IConsoleCommand
{
    public string Command => "governance_round_remove";
    public string Description => Loc.GetString("cmd-governance-round-remove-description");
    public string Help => Loc.GetString("cmd-governance-round-remove-help", ("command", Command));

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } actor)
        {
            shell.WriteError(Loc.GetString("cmd-governance-player-only"));
            return;
        }
        if (args.Length < 3 || !long.TryParse(args[1], out var actionId))
        {
            shell.WriteError(Help);
            return;
        }
        if (!CommandUtils.TryGetSessionByUsernameOrId(shell, args[0], actor, out var target))
            return;
        var reason = string.Join(' ', args[2..]);
        var system = IoCManager.Resolve<IEntityManager>().System<GovernanceSystem>();
        var result = await system.TryRoundRemoveAsync(actor, target, actionId, reason);
        if (!result.Allowed)
        {
            shell.WriteError(Loc.GetString("cmd-governance-freeze-denied", ("reason", GovernanceCommandText.Denial(result.Denial))));
            return;
        }
        shell.WriteLine(Loc.GetString("cmd-governance-round-remove-success", ("target", target.Name), ("action", actionId)));
    }
}

[AnyCommand]
public sealed partial class GovernanceExplanationCommand : IConsoleCommand
{
    public string Command => "governance_explanation";
    public string Description => Loc.GetString("cmd-governance-explanation-description");
    public string Help => Loc.GetString("cmd-governance-explanation-help", ("command", Command));

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } actor)
        {
            shell.WriteError(Loc.GetString("cmd-governance-player-only"));
            return;
        }
        if (args.Length < 3 || !long.TryParse(args[1], out var actionId))
        {
            shell.WriteError(Help);
            return;
        }
        if (!CommandUtils.TryGetSessionByUsernameOrId(shell, args[0], actor, out var target))
            return;
        var reason = string.Join(' ', args[2..]);
        var system = IoCManager.Resolve<IEntityManager>().System<GovernanceSystem>();
        var result = await system.TryRequestExplanationAsync(actor, target, actionId, reason);
        if (!result.Allowed)
        {
            shell.WriteError(Loc.GetString("cmd-governance-explanation-denied", ("reason", GovernanceCommandText.Denial(result.Denial))));
            return;
        }
        shell.WriteLine(Loc.GetString(
            "cmd-governance-explanation-success",
            ("target", target.Name),
            ("action", actionId)));
    }
}

[AnyCommand]
public sealed partial class GovernanceLogsCommand : IConsoleCommand
{
    public string Command => "governance_logs";
    public string Description => Loc.GetString("cmd-governance-logs-description");
    public string Help => Loc.GetString("cmd-governance-logs-help", ("command", Command));

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } actor)
        {
            shell.WriteError(Loc.GetString("cmd-governance-player-only"));
            return;
        }
        if (args.Length != 2 || !long.TryParse(args[1], out var actionId))
        {
            shell.WriteError(Help);
            return;
        }
        if (!CommandUtils.TryGetSessionByUsernameOrId(shell, args[0], actor, out var target))
            return;
        var system = IoCManager.Resolve<IEntityManager>().System<GovernanceSystem>();
        var result = await system.TryViewLogsAsync(actor, target, actionId);
        if (!result.Allowed)
        {
            shell.WriteError(Loc.GetString("cmd-governance-logs-denied", ("reason", GovernanceCommandText.Denial(result.Denial))));
            return;
        }
        shell.WriteLine(Loc.GetString(
            "cmd-governance-logs-header",
            ("target", target.Name),
            ("count", result.Logs.Count)));
        foreach (var log in result.Logs)
            shell.WriteLine($"[{log.CreatedAt.LocalDateTime:HH:mm:ss}] {log.Type}: {log.Message}");
    }
}

internal static class GovernanceCommandText
{
    public static string Denial(GovernanceDenial denial)
    {
        var key = denial switch
        {
            GovernanceDenial.Disabled => "governance-denial-disabled",
            GovernanceDenial.DatabaseUnavailable => "governance-denial-invalid-input",
            GovernanceDenial.InvalidInput => "governance-denial-invalid-input",
            GovernanceDenial.NotOnDuty => "governance-denial-not-on-duty",
            GovernanceDenial.NotObserver => "governance-denial-not-observer",
            GovernanceDenial.SelfTarget => "governance-denial-self-target",
            GovernanceDenial.InvalidDuration => "governance-denial-invalid-duration",
            GovernanceDenial.TargetUnavailable => "governance-denial-target-unavailable",
            GovernanceDenial.AlreadyFrozen => "governance-denial-already-frozen",
            GovernanceDenial.ActionNotApproved => "governance-denial-action-not-approved",
            GovernanceDenial.AHelpUnavailable => "governance-denial-ahelp-unavailable",
            _ => "governance-denial-unknown",
        };
        return Loc.GetString(key);
    }
}
