using Npgsql;

namespace Content.DiscordBot;

public static class ConfigurationLoader
{
    public static void LoadEnvironmentFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Environment file was not found.", path);
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 &&
                (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
            {
                value = value[1..^1];
            }
            if (Environment.GetEnvironmentVariable(key) == null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    public static void ApplyEnvironment(Config config, ref string? token, ref string? connectionString, ref ulong guild)
    {
        token = Value("DISCORD_TOKEN") ?? token;
        connectionString = Value("DATABASE_STRING") ?? Value("GOVERNANCE_DATABASE_URL") ?? Value("GAME_DATABASE_URL") ?? connectionString;
        connectionString = NormalizePostgresConnectionString(connectionString);
        guild = ULong("DISCORD_GUILD") ?? ULong("DISCORD_GUILD_ID") ?? guild;

        var courtChannel = ULong("COURT_CHANNEL") ?? ULong("COURT_FORUM_CHANNEL_ID");
        if (courtChannel is { } channel)
        {
            config.CourtChannel = channel;
            if (Value("COURT_ENABLED") == null)
                config.CourtEnabled = true;
        }
        config.CourtEnabled = Bool("COURT_ENABLED") ?? config.CourtEnabled;
        config.CourtTestMode = Bool("COURT_TEST_MODE") ?? config.CourtTestMode;
        config.CourtSchedulerSeconds = Int("COURT_SCHEDULER_SECONDS") ?? config.CourtSchedulerSeconds;
        config.CourtComplaintWindowHours = Int("COURT_COMPLAINT_WINDOW_HOURS") ?? config.CourtComplaintWindowHours;
        config.CourtDefenseHours = Int("COURT_DEFENSE_HOURS") ?? config.CourtDefenseHours;
        config.CourtVoteHours = Int("COURT_VOTE_HOURS") ?? config.CourtVoteHours;
        config.CourtInvitationHours = Int("COURT_JUROR_RESPONSE_HOURS") ?? config.CourtInvitationHours;
        config.CourtJurySize = Int("COURT_JURY_SIZE") ?? config.CourtJurySize;
        config.CourtDecisionThreshold = Int("COURT_DECISION_THRESHOLD") ?? config.CourtDecisionThreshold;
        config.CourtAcceptReward = Int("COURT_JUROR_ACCEPT_REWARD") ?? config.CourtAcceptReward;
        config.CourtDeclinePenalty = Int("COURT_JUROR_DECLINE_PENALTY") ?? config.CourtDeclinePenalty;
        config.CourtExpiryPenalty = Int("COURT_JUROR_EXPIRY_PENALTY") ?? config.CourtExpiryPenalty;
        config.CourtJuryReward = Int("COURT_JURY_REWARD") ?? config.CourtJuryReward;
        config.CourtFailurePenalty = Int("COURT_JUROR_FAILURE_PENALTY") ?? config.CourtFailurePenalty;
        config.CourtFalseReportPenalty = Int("COURT_FALSE_REPORT_PENALTY") ?? config.CourtFalseReportPenalty;
        config.CourtSelectionCooldownHours = Int("COURT_SELECTION_COOLDOWN_HOURS") ?? config.CourtSelectionCooldownHours;
        config.CourtLeadershipRole = ULong("COURT_LEADERSHIP_ROLE_ID") ?? config.CourtLeadershipRole;
        config.GovernanceChannel = ULong("GOVERNANCE_CHANNEL_ID") ?? config.GovernanceChannel;
        config.EventEnabled = Bool("EVENT_ENABLED") ?? config.EventEnabled;
        config.EventReviewHours = Int("EVENT_REVIEW_HOURS") ?? config.EventReviewHours;
        config.EventReviewers = Int("EVENT_REVIEWERS") ?? config.EventReviewers;
        config.EventApprovalThreshold = Int("EVENT_APPROVAL_THRESHOLD") ?? config.EventApprovalThreshold;
        config.EventReviewInvitationHours = Int("EVENT_REVIEW_INVITATION_HOURS") ?? config.EventReviewInvitationHours;
        config.EventReviewAcceptReward = Int("EVENT_REVIEW_ACCEPT_REWARD") ?? config.EventReviewAcceptReward;
        config.EventReviewCompletionReward = Int("EVENT_REVIEW_COMPLETION_REWARD") ?? config.EventReviewCompletionReward;
        config.EventReviewDeclinePenalty = Int("EVENT_REVIEW_DECLINE_PENALTY") ?? config.EventReviewDeclinePenalty;
        config.EventReviewExpiryPenalty = Int("EVENT_REVIEW_EXPIRY_PENALTY") ?? config.EventReviewExpiryPenalty;
        config.EventReviewFailurePenalty = Int("EVENT_REVIEW_FAILURE_PENALTY") ?? config.EventReviewFailurePenalty;
        config.ModerationReviewMinimumQualification = Int("MODERATION_REVIEW_MIN_QUALIFICATION") ?? config.ModerationReviewMinimumQualification;
        config.ModerationReviewInvitationHours = Int("MODERATION_REVIEW_INVITATION_HOURS") ?? config.ModerationReviewInvitationHours;
        config.ModerationReviewHours = Int("MODERATION_REVIEW_HOURS") ?? config.ModerationReviewHours;
        config.ModerationReviewSelectionCooldownHours = Int("MODERATION_REVIEW_SELECTION_COOLDOWN_HOURS") ?? config.ModerationReviewSelectionCooldownHours;
        config.ModerationReviewAcceptReward = Int("MODERATION_REVIEW_ACCEPT_REWARD") ?? config.ModerationReviewAcceptReward;
        config.ModerationReviewCompletionReward = Int("MODERATION_REVIEW_COMPLETION_REWARD") ?? config.ModerationReviewCompletionReward;
        config.ModerationReviewDeclinePenalty = Int("MODERATION_REVIEW_DECLINE_PENALTY") ?? config.ModerationReviewDeclinePenalty;
        config.ModerationReviewExpiryPenalty = Int("MODERATION_REVIEW_EXPIRY_PENALTY") ?? config.ModerationReviewExpiryPenalty;
        config.ModerationReviewFailurePenalty = Int("MODERATION_REVIEW_FAILURE_PENALTY") ?? config.ModerationReviewFailurePenalty;
        config.ModerationReviewSamplePercent = Int("MODERATION_REVIEW_SAMPLE_PERCENT") ?? config.ModerationReviewSamplePercent;
        config.ModerationReviewSchedulerSeconds = Int("MODERATION_REVIEW_SCHEDULER_SECONDS") ?? config.ModerationReviewSchedulerSeconds;
        config.ModerationReviewBatchSize = Int("MODERATION_REVIEW_BATCH_SIZE") ?? config.ModerationReviewBatchSize;
        config.ReputationSchedulerSeconds = Int("REPUTATION_SCHEDULER_SECONDS") ?? config.ReputationSchedulerSeconds;
    }

    public static string? NormalizePostgresConnectionString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("postgres" or "postgresql"))
        {
            return value;
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
        };
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase) &&
                Enum.TryParse<SslMode>(Uri.UnescapeDataString(parts[1]), true, out var sslMode))
            {
                builder.SslMode = sslMode;
            }
        }
        return builder.ConnectionString;
    }

    private static string? Value(string key) => Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
        ? value.Trim()
        : null;

    private static int? Int(string key) => int.TryParse(Value(key), out var value) ? value : null;

    private static ulong? ULong(string key) => ulong.TryParse(Value(key), out var value) ? value : null;

    private static bool? Bool(string key) => bool.TryParse(Value(key), out var value) ? value : null;
}
