using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared._RMC14.Mentor;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._RMC14.Mentor;

/// <summary>
/// Keeps the legacy mentor eligibility/active-state bookkeeping used by a few non-chat mentor tools.
/// Mentor Help itself has been retired in favor of the Governance Support Center: this manager no
/// longer registers or sends any mentor NetMessage and no longer owns a mentor chat/help transport.
/// </summary>
public sealed partial class MentorManager : IPostInjectInit
{
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private UserDbDataManager _userDb = default!;

    private readonly HashSet<ICommonSession> _activeMentors = new();
    private readonly Dictionary<NetUserId, bool> _mentors = new();

    private async Task LoadData(ICommonSession player, CancellationToken cancel)
    {
        var userId = player.UserId;
        var isMentor = await _db.IsJobWhitelisted(player.UserId, MentorConstants.Job, cancel);

        if (!isMentor)
        {
            var dbData = await _db.GetAdminDataForAsync(userId, cancel);
            var flags = AdminFlags.None;
            if (dbData?.AdminRank?.Flags != null)
                flags |= ReadAdminFlags(player, dbData.AdminRank.Flags.Select(p => p.Flag));

            if (dbData?.Flags != null)
                flags |= ReadAdminFlags(player, dbData.Flags.Select(p => p.Flag));

            isMentor = flags.HasFlag(AdminFlags.MentorHelp);
        }

        _mentors[player.UserId] = isMentor;

        if (isMentor)
            _activeMentors.Add(player);
    }

    private AdminFlags ReadAdminFlags(ICommonSession player, IEnumerable<string> flagNames)
    {
        var flags = AdminFlags.None;
        foreach (var flagName in flagNames)
        {
            if (AdminFlagsHelper.TryNameToFlag(flagName, out var flag))
            {
                flags |= flag;
                continue;
            }

            _log.RootSawmill.Warning(
                "Ignoring unknown admin flag {Flag} while loading mentor status for {Player}",
                flagName,
                player.UserId);
        }

        return flags;
    }

    private void ClientDisconnected(ICommonSession player)
    {
        _mentors.Remove(player.UserId);
        _activeMentors.Remove(player);
    }

    public bool IsMentor(NetUserId player)
    {
        return _mentors.TryGetValue(player, out var mentor) && mentor;
    }

    public IEnumerable<ICommonSession> GetActiveMentors()
    {
        return _activeMentors;
    }

    public void ReMentor(NetUserId user)
    {
        if (!_player.TryGetSessionById(user, out var session) ||
            !_mentors.TryGetValue(session.UserId, out var mentor) ||
            !mentor)
        {
            return;
        }

        _activeMentors.Add(session);
    }

    public void DeMentor(INetChannel user)
    {
        if (!_player.TryGetSessionByChannel(user, out var session))
            return;

        _activeMentors.Remove(session);
    }

    void IPostInjectInit.PostInject()
    {
        _userDb.AddOnLoadPlayer(LoadData);
        _userDb.AddOnPlayerDisconnect(ClientDisconnected);
    }
}
