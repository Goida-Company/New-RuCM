using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed record EventGovernanceStatusSnapshot(
    GovernanceEventSession Session,
    IReadOnlyList<GovernanceEventManifestItem> Manifest,
    IReadOnlyList<GovernanceEventAction> Actions);

public sealed class EventGovernanceStatusService(Func<GovernanceDbContext> governanceFactory)
{
    public async Task<EventGovernanceStatusSnapshot> GetAsync(long sessionId, ulong directorDiscordId)
    {
        if (directorDiscordId > long.MaxValue)
            throw new CourtRuleException("Discord ID не поддерживается Governance.");

        await using var governance = governanceFactory();
        var directorUserId = await governance.Users.AsNoTracking()
            .Where(value => value.DiscordUserId == checked((long) directorDiscordId))
            .Select(value => (Guid?) value.Id)
            .SingleOrDefaultAsync()
            ?? throw new CourtRuleException("Discord-аккаунт не привязан к аккаунту SS14.");

        var session = await governance.EventSessions.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == sessionId)
            ?? throw new CourtRuleException("Сессия события не найдена.");
        if (session.DirectorUserId != directorUserId)
            throw new CourtRuleException("Статус EventSession доступен только её директору.");

        var manifest = await governance.EventManifestItems.AsNoTracking()
            .Where(value => value.SessionId == sessionId)
            .OrderBy(value => value.Id)
            .ToListAsync();
        var actions = await governance.EventActions.AsNoTracking()
            .Where(value => value.SessionId == sessionId)
            .OrderByDescending(value => value.Id)
            .Take(10)
            .ToListAsync();

        return new EventGovernanceStatusSnapshot(session, manifest, actions);
    }
}
