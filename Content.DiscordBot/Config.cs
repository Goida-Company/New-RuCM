namespace Content.DiscordBot;

public sealed class Config
{
    public string Token { get; set; } = string.Empty;

    public string DatabaseString { get; set; } = string.Empty;

    public ulong Guild { get; set; } = 1168210010233376858UL;

    public bool CourtEnabled { get; set; }

    public bool CourtTestMode { get; set; }

    public ulong CourtChannel { get; set; }

    public int CourtSchedulerSeconds { get; set; } = 30;

    public int CourtComplaintWindowHours { get; set; } = 72;

    public int CourtDefenseHours { get; set; } = 48;

    public int CourtVoteHours { get; set; } = 48;

    public int CourtInvitationHours { get; set; } = 24;

    public int CourtJurySize { get; set; } = 3;

    public int CourtDecisionThreshold { get; set; } = 2;

    // Legacy linear Civic Rating knobs are retained for configuration compatibility only.
    // Reputation v2 evaluates completed/failed obligations as Bayesian evidence instead.
    public int CourtAcceptReward { get; set; } = 10;

    public int CourtDeclinePenalty { get; set; } = 15;

    public int CourtExpiryPenalty { get; set; } = 20;

    public int CourtJuryReward { get; set; } = 15;

    public int CourtFailurePenalty { get; set; } = 30;

    public int CourtFalseReportPenalty { get; set; } = 50;

    public int CourtSelectionCooldownHours { get; set; } = 24;

    public ulong CourtLeadershipRole { get; set; }

    public ulong GovernanceChannel { get; set; }

    /// <summary>
    /// Event Governance is intentionally disabled by default while its production workflow is deferred.
    /// Historical data remains readable and is not deleted.
    /// </summary>
    public bool EventEnabled { get; set; } = false;

    public int EventReviewHours { get; set; } = 48;

    public int EventReviewers { get; set; } = 3;

    public int EventApprovalThreshold { get; set; } = 2;

    public int EventReviewInvitationHours { get; set; } = 24;

    public int EventReviewAcceptReward { get; set; } = 10;

    public int EventReviewCompletionReward { get; set; } = 15;

    public int EventReviewDeclinePenalty { get; set; } = 15;

    public int EventReviewExpiryPenalty { get; set; } = 20;

    public int EventReviewFailurePenalty { get; set; } = 30;

    public int ModerationReviewMinimumQualification { get; set; } = 2;

    public int ModerationReviewInvitationHours { get; set; } = 24;

    public int ModerationReviewHours { get; set; } = 48;

    public int ModerationReviewSelectionCooldownHours { get; set; } = 24;

    public int ModerationReviewAcceptReward { get; set; } = 10;

    public int ModerationReviewCompletionReward { get; set; } = 15;

    public int ModerationReviewDeclinePenalty { get; set; } = 15;

    public int ModerationReviewExpiryPenalty { get; set; } = 20;

    public int ModerationReviewFailurePenalty { get; set; } = 30;

    public int ModerationReviewSamplePercent { get; set; } = 25;

    public int ModerationReviewSchedulerSeconds { get; set; } = 30;

    public int ModerationReviewBatchSize { get; set; } = 5;

    public int ModerationAppealReviewers { get; set; } = 3;

    public int ModerationAppealWindowHours { get; set; } = 72;

    public int ReputationSchedulerSeconds { get; set; } = 300;
}
