namespace Content.DiscordBot.Governance;

public static class ModerationAppealStatuses
{
    public const string Reviewing = "reviewing";
    public const string Resolved = "resolved";
}

public sealed class GovernanceModerationAppeal
{
    public long Id { get; set; }
    public long ActionId { get; set; }
    public Guid AppellantUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Result { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public sealed record ModerationAppealOutcome(
    long AppealId,
    long ActionId,
    string Status,
    string? Result,
    int Reviews,
    int RequiredReviews);

public static class ModerationAppealDecisionPolicy
{
    public static string? Resolve(IEnumerable<string> outcomes, int requiredReviews)
    {
        var materialized = outcomes.ToArray();
        if (materialized.Length < requiredReviews)
            return null;

        var majority = materialized
            .GroupBy(value => value)
            .Select(group => new { Outcome = group.Key, Count = group.Count() })
            .OrderByDescending(value => value.Count)
            .First();
        if (majority.Count >= 2)
            return majority.Outcome;

        return materialized
            .OrderBy(Severity)
            .ElementAt(materialized.Length / 2);
    }

    private static int Severity(string outcome) => outcome switch
    {
        ModerationReviewOutcomes.Correct => 0,
        ModerationReviewOutcomes.ReasonableButWrong => 1,
        ModerationReviewOutcomes.ProceduralError => 2,
        ModerationReviewOutcomes.Negligent => 3,
        ModerationReviewOutcomes.Abuse => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };
}
