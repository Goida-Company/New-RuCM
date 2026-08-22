using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed record ReputationHistoryEntry(
    DateTime OccurredAt,
    string Track,
    string Reason,
    double SuccessWeight,
    double FailureWeight,
    bool SeriousNegative,
    string EntityType,
    string EntityId);

public sealed class ReputationHistoryService(Func<GovernanceDbContext> governanceFactory)
{
    public async Task<IReadOnlyList<ReputationHistoryEntry>> GetAsync(Guid userId, int limit = 25)
    {
        limit = Math.Clamp(limit, 1, 100);
        await using var governance = governanceFactory();
        return await governance.ReputationObservations.AsNoTracking()
            .Where(value => value.UserId == userId)
            .OrderByDescending(value => value.OccurredAt)
            .Take(limit)
            .Select(value => new ReputationHistoryEntry(
                value.OccurredAt,
                value.Track,
                value.Reason,
                value.SuccessWeight,
                value.FailureWeight,
                value.SeriousNegative,
                value.EntityType,
                value.EntityId))
            .ToListAsync();
    }
}
