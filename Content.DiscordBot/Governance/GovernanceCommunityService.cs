using System.Text.Json;
using Content.Server.Database;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed record GovernanceProfile(
    Guid UserId,
    Guid Ss14UserId,
    long? DiscordId,
    string Name,
    int Rating,
    bool Suspended,
    IReadOnlyDictionary<string, short> Qualifications);

public sealed class GovernanceCommunityService(
    GovernanceIdentityService identities,
    Func<GovernanceDbContext> governanceFactory,
    Func<ServerDbContext> gameFactory)
{
    public Task<GovernanceUser> RequireUserAsync(ulong discordId) => identities.RequireDiscordUserAsync(discordId);

    public Task<GovernanceUser> RequireSs14UserAsync(Guid ss14UserId) => identities.RequireSs14UserAsync(ss14UserId);

    public Task<GovernanceUser> RequireSs14UserByNicknameAsync(string nickname) => identities.RequireSs14UserByNicknameAsync(nickname);

    public Task ValidatePermanentLinkAsync(Guid ss14UserId, ulong discordId) =>
        identities.ValidatePermanentLinkAsync(ss14UserId, discordId);

    public Task SyncPermanentLinkAsync(Guid ss14UserId, ulong discordId) =>
        identities.SyncLinkedAccountAsync(ss14UserId, discordId);

    public async Task<GovernanceProfile> GetProfileAsync(ulong discordId)
    {
        var user = await RequireUserAsync(discordId);
        await using var governance = governanceFactory();
        await using var game = gameFactory();
        var name = await game.Player.AsNoTracking().Where(value => value.UserId == user.Ss14UserId)
            .Select(value => value.LastSeenUserName).SingleAsync();
        var qualifications = await governance.Qualifications.AsNoTracking().Where(value => value.UserId == user.Id)
            .ToDictionaryAsync(value => value.Track, value => value.Level);
        return new GovernanceProfile(user.Id, user.Ss14UserId, user.DiscordUserId, name, user.CivicRatingCache,
            user.IsGovernanceSuspended, qualifications);
    }

    public async Task<string> RequestFriendshipAsync(ulong requesterDiscordId, ulong friendDiscordId)
    {
        var requester = await RequireUserAsync(requesterDiscordId);
        var friend = await RequireUserAsync(friendDiscordId);
        if (requester.Id == friend.Id)
            throw new CourtRuleException("Нельзя добавить в друзья самого себя.");
        var first = requester.Id.CompareTo(friend.Id) < 0 ? requester.Id : friend.Id;
        var second = requester.Id.CompareTo(friend.Id) < 0 ? friend.Id : requester.Id;
        await using var governance = governanceFactory();
        var friendship = await governance.Friendships.SingleOrDefaultAsync(value => value.UserId == first && value.FriendUserId == second);
        var now = DateTime.UtcNow;
        if (friendship == null)
        {
            governance.Friendships.Add(new GovernanceFriendship
            {
                UserId = first,
                FriendUserId = second,
                RequestedByUserId = requester.Id,
                CreatedAt = now,
            });
            AddAudit(governance, "friendship.requested", requesterDiscordId, "friendship", $"{first}:{second}",
                new { friend_user_id = friend.Id });
            await governance.SaveChangesAsync();
            return "Запрос дружбы сохранён. Связь начнёт исключать совместный отбор после подтверждения второй стороной.";
        }
        if (friendship.ConfirmedAt != null)
            return "Дружба уже подтверждена.";
        if (friendship.RequestedByUserId == requester.Id)
            return "Запрос уже ожидает подтверждения второй стороны.";
        friendship.ConfirmedAt = now;
        AddAudit(governance, "friendship.confirmed", requesterDiscordId, "friendship", friendship.Id.ToString(), new { });
        await governance.SaveChangesAsync();
        return "Дружба подтверждена. Вы не будете вместе отбираться в конфликтующие роли.";
    }

    public async Task RemoveFriendshipAsync(ulong actorDiscordId, ulong friendDiscordId)
    {
        var actor = await RequireUserAsync(actorDiscordId);
        var friend = await RequireUserAsync(friendDiscordId);
        var first = actor.Id.CompareTo(friend.Id) < 0 ? actor.Id : friend.Id;
        var second = actor.Id.CompareTo(friend.Id) < 0 ? friend.Id : actor.Id;
        await using var governance = governanceFactory();
        var row = await governance.Friendships.SingleOrDefaultAsync(value => value.UserId == first && value.FriendUserId == second)
            ?? throw new CourtRuleException("Такая связь не найдена.");
        governance.Friendships.Remove(row);
        AddAudit(governance, "friendship.removed", actorDiscordId, "friendship", row.Id.ToString(), new { });
        await governance.SaveChangesAsync();
    }

    public async Task SetQualificationAsync(ulong actorDiscordId, ulong targetDiscordId, string track, short level)
    {
        if (!ReputationTracks.IsPath(track) || level is < 0 or > 4)
            throw new CourtRuleException("Направление должно быть support/moderation/jury/event/contributor, уровень — от 0 до 4.");
        var target = await RequireUserAsync(targetDiscordId);
        await using var governance = governanceFactory();
        var row = await governance.Qualifications.SingleOrDefaultAsync(value => value.UserId == target.Id && value.Track == track);
        if (row == null)
        {
            row = governance.Qualifications.Add(new GovernanceQualification
            {
                UserId = target.Id,
                Track = track,
                UpdatedAt = DateTime.UtcNow,
            }).Entity;
        }
        row.Level = level;
        row.UpdatedAt = DateTime.UtcNow;
        AddAudit(governance, "qualification.changed", actorDiscordId, "user", target.Id.ToString(), new { track, level });
        await governance.SaveChangesAsync();
    }

    public async Task SetSuspendedAsync(ulong actorDiscordId, ulong targetDiscordId, bool suspended, string reason)
    {
        if (reason.Trim().Length < 10)
            throw new CourtRuleException("Укажите содержательную причину (не менее 10 символов).");
        var target = await RequireUserAsync(targetDiscordId);
        await using var governance = governanceFactory();
        var user = await governance.Users.SingleAsync(value => value.Id == target.Id);
        user.IsGovernanceSuspended = suspended;
        user.UpdatedAt = DateTime.UtcNow;
        if (suspended)
        {
            var now = DateTime.UtcNow;
            foreach (var grant in await governance.CapabilityGrants.Where(value => value.UserId == user.Id && value.RevokedAt == null).ToListAsync())
                grant.RevokedAt = now;
            foreach (var duty in await governance.DutySessions.Where(value => value.UserId == user.Id && value.Status == "active").ToListAsync())
            {
                duty.Status = "revoked";
                duty.EndedAt = now;
                duty.Version++;
            }
        }
        governance.LeadershipOverrides.Add(new GovernanceLeadershipOverride
        {
            EntityType = "user",
            EntityId = user.Id.ToString(),
            Action = suspended ? "suspend" : "restore",
            Reason = reason.Trim(),
            ActorDiscordId = checked((long) actorDiscordId),
            CreatedAt = DateTime.UtcNow,
        });
        AddAudit(governance, suspended ? "leadership.user_suspended" : "leadership.user_restored", actorDiscordId,
            "user", user.Id.ToString(), new { reason });
        await governance.SaveChangesAsync();
    }

    public async Task MarkFalseReportAsync(long caseId, ulong actorDiscordId, string reason)
    {
        if (reason.Trim().Length < 20)
            throw new CourtRuleException("Причина должна содержать не менее 20 символов.");
        await using var governance = governanceFactory();
        var courtCase = await governance.CourtCases.SingleOrDefaultAsync(value => value.Id == caseId)
            ?? throw new CourtRuleException("Дело не найдено.");
        if (courtCase.FalseReportAt != null)
            return;
        if (courtCase.Status is not (CourtStatuses.Verdict or CourtStatuses.Executed or CourtStatuses.Overturned))
            throw new CourtRuleException("Ложность жалобы можно фиксировать только после решения по делу.");

        // The immutable false-report timestamp is the source event. ReputationCoordinator turns it
        // into Bayesian negative evidence. There is deliberately no second linear Civic Rating penalty.
        courtCase.FalseReportAt = DateTime.UtcNow;
        governance.LeadershipOverrides.Add(new GovernanceLeadershipOverride
        {
            EntityType = "court_case",
            EntityId = caseId.ToString(),
            Action = "false_report",
            Reason = reason.Trim(),
            ActorDiscordId = checked((long) actorDiscordId),
            CreatedAt = DateTime.UtcNow,
        });
        AddAudit(governance, "leadership.false_report", actorDiscordId, "court_case", caseId.ToString(),
            new { reason, reputation_model = "bayesian" });
        await governance.SaveChangesAsync();
    }

    private static void AddAudit(
        GovernanceDbContext db,
        string eventType,
        ulong actorDiscordId,
        string entityType,
        string entityId,
        object payload)
    {
        db.AuditEvents.Add(new GovernanceAuditEvent
        {
            EventType = eventType,
            ActorType = "discord_user",
            ActorId = actorDiscordId.ToString(),
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow,
            Payload = JsonSerializer.Serialize(payload),
        });
    }
}
