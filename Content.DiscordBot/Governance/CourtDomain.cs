namespace Content.DiscordBot.Governance;

public static class CourtStatuses
{
    public const string Defense = "defense";
    public const string AwaitingJury = "awaiting_jury";
    public const string Jury = "jury";
    public const string Sentencing = "sentencing";
    public const string Verdict = "verdict";
    public const string Executed = "executed";
    public const string Overturned = "overturned";
}

public static class CourtVerdicts
{
    public const string Guilty = "guilty";
    public const string NotGuilty = "not_guilty";
    public const string InsufficientEvidence = "insufficient_evidence";
}

public static class CourtSanctions
{
    public const string Warning = "warning";
    public const string GameBan = "game_ban";
    public const string JobBan = "job_ban";
}

public static class InvitationStates
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string Recused = "recused";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}

public sealed record CourtPolicy(
    TimeSpan ComplaintWindow,
    TimeSpan DefensePeriod,
    TimeSpan VotePeriod,
    TimeSpan InvitationPeriod,
    int JurySize,
    int DecisionThreshold,
    int AcceptReward,
    int DeclinePenalty,
    int ExpiryPenalty,
    int JuryReward,
    int FailurePenalty,
    TimeSpan SelectionCooldown)
{
    public static CourtPolicy FromConfig(Config config)
    {
        if (config.CourtJurySize < 3)
            throw new ArgumentOutOfRangeException(nameof(config.CourtJurySize), "Court jury size must be at least three.");
        if (config.CourtDecisionThreshold < 2 || config.CourtDecisionThreshold > config.CourtJurySize)
            throw new ArgumentOutOfRangeException(nameof(config.CourtDecisionThreshold), "Court decision threshold is invalid.");

        return new CourtPolicy(
            TimeSpan.FromHours(config.CourtComplaintWindowHours),
            TimeSpan.FromHours(config.CourtDefenseHours),
            TimeSpan.FromHours(config.CourtVoteHours),
            TimeSpan.FromHours(config.CourtInvitationHours),
            config.CourtJurySize,
            config.CourtDecisionThreshold,
            config.CourtAcceptReward,
            config.CourtDeclinePenalty,
            config.CourtExpiryPenalty,
            config.CourtJuryReward,
            config.CourtFailurePenalty,
            TimeSpan.FromHours(config.CourtSelectionCooldownHours));
    }
}

public sealed class CourtRuleException(string message) : InvalidOperationException(message);

public sealed record LinkedGameAccount(Guid PlayerId, ulong? DiscordId, string Name);

public sealed record CourtVoteOutcome(string? Verdict, string? SanctionType = null, short? SanctionDays = null, string? SanctionRole = null)
{
    public bool Completed => Verdict != null && (Verdict != CourtVerdicts.Guilty || SanctionType != null);
}

public static class CourtDecisionPolicy
{
    public static string? ResolveGuilt(IEnumerable<string> votes, int threshold, int jurySize)
    {
        var materialized = votes.ToArray();
        foreach (var group in materialized.GroupBy(vote => vote))
        {
            if (group.Count() >= threshold)
                return group.Key;
        }

        return materialized.Length >= jurySize ? CourtVerdicts.InsufficientEvidence : null;
    }

    public static (string Type, short? Days, string? Role)? ResolveSentence(
        IEnumerable<(string Type, short? Days, string? Role)> votes,
        int threshold,
        int jurySize)
    {
        var materialized = votes.ToArray();
        var groups = materialized
            .GroupBy(vote => vote)
            .Select(group => (Vote: group.Key, Count: group.Count()))
            .ToArray();
        var majority = groups.FirstOrDefault(group => group.Count >= threshold);
        if (majority.Count >= threshold)
            return majority.Vote;
        if (materialized.Length < jurySize)
            return null;

        return groups
            .OrderBy(group => Severity(group.Vote.Type))
            .ThenBy(group => group.Vote.Days ?? 0)
            .ThenBy(group => group.Vote.Role, StringComparer.Ordinal)
            .First().Vote;
    }

    private static int Severity(string sanction)
    {
        return sanction switch
        {
            CourtSanctions.Warning => 0,
            CourtSanctions.JobBan => 1,
            CourtSanctions.GameBan => 2,
            _ => int.MaxValue,
        };
    }
}
