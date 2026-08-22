using System;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared.Corvax.CCCVars;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._RuMC14.Governance;

/// <summary>
/// Restores the round_remove enforcement boundary from PostgreSQL on every reconnect.
/// GovernanceSystem keeps an in-process fast path, while this system makes the decision survive
/// a Content.Server restart in the same round.
/// </summary>
public sealed class GovernanceRoundRemovePersistenceSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IServerDbManager _database = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    public override void Initialize()
    {
        base.Initialize();
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
        base.Shutdown();
    }

    // ReSharper disable once AsyncVoidMethod
    private async void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (!_cfg.GetCVar(CCCVars.GovernanceEnabled) ||
            _ticker.RoundId <= 0 ||
            args.NewStatus is not (SessionStatus.Connected or SessionStatus.InGame))
        {
            return;
        }

        try
        {
            if (!await _database.IsGovernanceRoundRemovedAsync(args.Session.UserId, _ticker.RoundId))
                return;

            // The database query is asynchronous; re-check the current session state before acting.
            if (args.Session.Status is SessionStatus.Connected or SessionStatus.InGame)
                args.Session.Channel.Disconnect("Вы удалены до конца текущего раунда решением дежурных сообщества.");
        }
        catch (Exception exception)
        {
            Log.Error($"Could not restore Governance round removal for {args.Session.UserId}: {exception}");
        }
    }
}
