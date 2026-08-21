using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Shared._RuMC14.Governance;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Ghost;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;

namespace Content.Server._RuMC14.Governance;

/// <summary>
/// Gives an active responder a short reconnect grace period. If they do not return as an observer,
/// their duty is abandoned, temporary capabilities are revoked and owned AHelp tickets are requeued.
/// </summary>
public sealed class GovernanceDutyDisconnectSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IServerDbManager _database = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly GovernanceManager _governance = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    private readonly Dictionary<NetUserId, DateTimeOffset> _unavailableSince = new();
    private float _elapsed = float.MaxValue;
    private bool _checking;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_governance.Enabled || _ticker.RunLevel != GameRunLevel.InRound)
        {
            _unavailableSince.Clear();
            return;
        }

        _elapsed += frameTime;
        var interval = Math.Clamp(_cfg.GetCVar(CCCVars.GovernanceDutyCheckSeconds), 10, 600);
        if (_checking || _elapsed < interval)
            return;

        _elapsed = 0;
        _ = CheckDisconnectedDutiesAsync();
    }

    private async Task CheckDisconnectedDutiesAsync()
    {
        if (_checking)
            return;

        _checking = true;
        try
        {
            var activeUsers = await _database.GetActiveGovernanceDutyUsersAsync(_ticker.RoundId);
            var activeSet = activeUsers.ToHashSet();
            var now = DateTimeOffset.UtcNow;
            var grace = TimeSpan.FromSeconds(Math.Clamp(
                _cfg.GetCVar(GovernanceCVars.DutyDisconnectGraceSeconds),
                10,
                1800));

            foreach (var tracked in _unavailableSince.Keys.ToArray())
            {
                if (!activeSet.Contains(tracked) || IsAvailableObserver(tracked))
                    _unavailableSince.Remove(tracked);
            }

            foreach (var userId in activeUsers)
            {
                if (IsAvailableObserver(userId))
                {
                    _unavailableSince.Remove(userId);
                    continue;
                }

                if (!_unavailableSince.TryGetValue(userId, out var unavailableAt))
                {
                    _unavailableSince[userId] = now;
                    continue;
                }

                if (now - unavailableAt < grace)
                    continue;

                _unavailableSince.Remove(userId);
                if (!await _database.AbandonGovernanceDutyAsync(userId, _ticker.RoundId))
                    continue;

                await _governance.RefreshDutyAsync(userId);
                Log.Info($"Governance duty for {userId} was abandoned after {grace.TotalSeconds:0}s unavailable; staffing can replace the responder.");
            }
        }
        catch (Exception exception)
        {
            Log.Error($"Governance duty disconnect check failed: {exception}");
        }
        finally
        {
            _checking = false;
        }
    }

    private bool IsAvailableObserver(NetUserId userId)
    {
        return _players.TryGetSessionById(userId, out var session) &&
               session.Status is SessionStatus.Connected or SessionStatus.InGame &&
               session.AttachedEntity is { } entity &&
               HasComp<GhostComponent>(entity);
    }
}
