using System.Linq;
using Robust.Shared.Network;

namespace Content.Server._RMC14.Onboarding;

internal enum RMCOnboardingStartResult : byte
{
    Added,
    AlreadyActive,
    CapacityReached,
}

/// <summary>
/// Owns the uniqueness and capacity invariants for active onboarding sessions.
/// Keeping these rules independent from map spawning makes them cheap to regression-test.
/// </summary>
internal sealed class RMCOnboardingSessionStore<TSession> where TSession : class
{
    private readonly Dictionary<NetUserId, TSession> _sessions = new();

    public int Count => _sessions.Count;

    public bool Contains(NetUserId userId)
    {
        return _sessions.ContainsKey(userId);
    }

    public bool TryGetValue(NetUserId userId, out TSession session)
    {
        return _sessions.TryGetValue(userId, out session!);
    }

    public RMCOnboardingStartResult TryAdd(NetUserId userId, TSession session, int capacity)
    {
        if (_sessions.ContainsKey(userId))
            return RMCOnboardingStartResult.AlreadyActive;

        if (_sessions.Count >= Math.Max(0, capacity))
            return RMCOnboardingStartResult.CapacityReached;

        _sessions.Add(userId, session);
        return RMCOnboardingStartResult.Added;
    }

    public bool TryRemove(NetUserId userId, out TSession session)
    {
        return _sessions.Remove(userId, out session!);
    }

    public KeyValuePair<NetUserId, TSession>[] Snapshot()
    {
        return _sessions.ToArray();
    }

    public NetUserId[] UserSnapshot()
    {
        return _sessions.Keys.ToArray();
    }
}
