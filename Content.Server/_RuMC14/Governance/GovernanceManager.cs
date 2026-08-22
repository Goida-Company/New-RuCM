using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared.Corvax.CCCVars;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._RuMC14.Governance;

public sealed class GovernanceManager : IPostInjectInit
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IServerDbManager _database = default!;

    private readonly ConcurrentDictionary<NetUserId, GovernanceDutySession> _duty = new();
    private ISawmill _log = default!;

    public bool Enabled => _cfg.GetCVar(CCCVars.GovernanceEnabled);

    void IPostInjectInit.PostInject()
    {
        _log = _logManager.GetSawmill("governance");
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public bool HasActiveDuty(NetUserId userId, int roundId)
    {
        if (!Enabled || !_duty.TryGetValue(userId, out var duty))
            return false;

        if (duty.RoundId == roundId && duty.ExpiresAt > DateTimeOffset.UtcNow)
            return true;

        _duty.TryRemove(userId, out _);
        return false;
    }

    public async Task<GovernanceDutySession?> RefreshDutyAsync(NetUserId userId)
    {
        if (!Enabled)
        {
            _duty.TryRemove(userId, out _);
            return null;
        }

        try
        {
            var duty = await _database.GetGovernanceDutySessionAsync(userId);
            if (duty == null)
                _duty.TryRemove(userId, out _);
            else
                _duty[userId] = duty;
            return duty;
        }
        catch (Exception exception)
        {
            _duty.TryRemove(userId, out _);
            _log.Error($"Failed to refresh governance duty for {userId}: {exception}");
            return null;
        }
    }

    public async Task<GovernanceAuthorization?> AuthorizeAsync(
        NetUserId userId,
        int roundId,
        string capability)
    {
        if (!Enabled)
            return null;

        try
        {
            var authorization = await _database.AuthorizeGovernanceCapabilityAsync(
                userId,
                roundId,
                capability);
            if (authorization == null)
                _duty.TryRemove(userId, out _);
            else
                _duty[userId] = authorization.Duty;
            return authorization;
        }
        catch (Exception exception)
        {
            _duty.TryRemove(userId, out _);
            _log.Error($"Failed to authorize {capability} for {userId}: {exception}");
            return null;
        }
    }

    public async Task AuditAsync(
        string eventType,
        NetUserId actor,
        NetUserId? target,
        string entityType,
        string entityId,
        object payload)
    {
        if (!Enabled)
            return;

        try
        {
            await _database.AppendGovernanceAuditAsync(
                eventType,
                actor,
                target,
                entityType,
                entityId,
                payload);
        }
        catch (Exception exception)
        {
            _log.Error($"Failed to append governance audit event {eventType}: {exception}");
        }
    }

    public async Task<GovernanceModerationActionAuthorization?> AuthorizeActionAsync(
        NetUserId actor,
        NetUserId target,
        int roundId,
        long actionId,
        string actionType)
    {
        if (!Enabled)
            return null;
        try
        {
            return await _database.AuthorizeGovernanceModerationActionAsync(actor, target, roundId, actionId, actionType);
        }
        catch (Exception exception)
        {
            _log.Error($"Failed to authorize moderation action {actionId}: {exception}");
            return null;
        }
    }

    public Task CompleteActionAsync(long actionId) => _database.CompleteGovernanceModerationActionAsync(actionId);

    // ReSharper disable once AsyncVoidMethod
    private async void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
        {
            _duty.TryRemove(args.Session.UserId, out _);
            return;
        }

        await RefreshDutyAsync(args.Session.UserId);
    }
}
