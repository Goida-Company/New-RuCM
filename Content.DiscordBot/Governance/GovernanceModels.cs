namespace Content.DiscordBot.Governance;

public sealed class GovernanceUser
{
    public Guid Id { get; set; }
    public Guid Ss14UserId { get; set; }
    public long? DiscordUserId { get; set; }
    public int CivicRatingCache { get; set; }
    public bool IsGovernanceSuspended { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class GovernanceIdentityLink
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public long DiscordUserId { get; set; }
    public DateTime LinkedAt { get; set; }
    public DateTime? UnlinkedAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Metadata { get; set; } = "{}";
}

public sealed class GovernanceServicePath
{
    public Guid UserId { get; set; }
    public short Slot { get; set; }
    public string Track { get; set; } = string.Empty;
    public DateTime SelectedAt { get; set; }
    public DateTime ChangedAt { get; set; }
}

public sealed class GovernanceQualification
{
    public Guid UserId { get; set; }
    public string Track { get; set; } = string.Empty;
    public short Level { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class GovernanceRatingEntry
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public int Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatedByType { get; set; } = string.Empty;
    public string? CreatedById { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Metadata { get; set; } = "{}";
}

public sealed class GovernanceReputationObservation
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string Track { get; set; } = string.Empty;
    public double SuccessWeight { get; set; }
    public double FailureWeight { get; set; }
    public bool SeriousNegative { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedByType { get; set; } = string.Empty;
    public string? CreatedById { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Metadata { get; set; } = "{}";
}

public sealed class GovernanceReputationSnapshot
{
    public Guid UserId { get; set; }
    public string Track { get; set; } = string.Empty;
    public double Alpha { get; set; }
    public double Beta { get; set; }
    public double Mean { get; set; }
    public double LowerBound { get; set; }
    public double EvidenceWeight { get; set; }
    public int Score { get; set; }
    public DateTime CalculatedAt { get; set; }
}

public sealed class GovernanceGameActivitySnapshot
{
    public Guid UserId { get; set; }
    public double OverallHours { get; set; }
    public int ActiveWeeks { get; set; }
    public int AccountAgeDays { get; set; }
    public double ActivityIndex { get; set; }
    public double EvidenceWeight { get; set; }
    public DateTime CalculatedAt { get; set; }
}

public sealed class GovernanceContributionEvent
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string ContributionKind { get; set; } = string.Empty;
    public double Impact { get; set; }
    public double Quality { get; set; }
    public double Stability { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public long? CreatedByDiscordId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string Metadata { get; set; } = "{}";
}

public sealed class GovernanceConflict
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? RelatedUserId { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public string CreatedByType { get; set; } = string.Empty;
    public string? CreatedById { get; set; }
}

public sealed class GovernanceInvitation
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public string? RecusalReason { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime? DiscordNotifiedAt { get; set; }
}

public sealed class GovernanceCourtCase
{
    public long Id { get; set; }
    public Guid ClaimantUserId { get; set; }
    public Guid DefendantUserId { get; set; }
    public int RoundId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime FiledAt { get; set; }
    public DateTime DefenseDeadline { get; set; }
    public DateTime? GuiltStartedAt { get; set; }
    public DateTime? GuiltDeadline { get; set; }
    public DateTime? SentencingStartedAt { get; set; }
    public DateTime? SentencingDeadline { get; set; }
    public string? Verdict { get; set; }
    public string? SanctionType { get; set; }
    public short? SanctionDays { get; set; }
    public string? SanctionRole { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public string? ExecutionReference { get; set; }
    public int Version { get; set; }
    public long? DiscordThreadId { get; set; }
    public long? VerdictMessageId { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? OverturnedAt { get; set; }
    public string? OverturnReason { get; set; }
    public DateTime? FalseReportAt { get; set; }
}

public sealed class GovernanceCourtStatement
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? EvidenceReference { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class GovernanceJuror
{
    public long CaseId { get; set; }
    public Guid UserId { get; set; }
    public long InvitationId { get; set; }
    public bool Active { get; set; }
    public DateTime AssignedAt { get; set; }
}

public sealed class GovernanceGuiltVote
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public Guid JurorUserId { get; set; }
    public string Verdict { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class GovernanceSentencingVote
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public Guid JurorUserId { get; set; }
    public string SanctionType { get; set; } = string.Empty;
    public short? SanctionDays { get; set; }
    public string? SanctionRole { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class GovernanceAuditEvent
{
    public long Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ActorType { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Payload { get; set; } = "{}";
}

public sealed class GovernanceCourtParticipant
{
    public long CaseId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; }
}

public sealed class GovernanceFriendship
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public Guid FriendUserId { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}

public sealed class GovernanceServiceAssignment
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string Track { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
}

public sealed class GovernanceDutySession
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public int RoundId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public short QualificationAtStart { get; set; }
    public bool ObserverConfirmed { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public int Version { get; set; }
}

public sealed class GovernanceCapabilityGrant
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string Capability { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Scope { get; set; } = "{}";
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class GovernancePunishmentExecution
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public string SanctionType { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime? RevertedAt { get; set; }
}

public sealed class GovernanceLeadershipOverride
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long ActorDiscordId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class GovernanceAHelpTicket
{
    public long Id { get; set; }
    public int RoundId { get; set; }
    public Guid? ReporterUserId { get; set; }
    public Guid ReporterSs14UserId { get; set; }
    public Guid? TargetUserId { get; set; }
    public Guid? ClaimedByUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long? DiscordThreadId { get; set; }
}

public sealed class GovernanceLiveIncident
{
    public long Id { get; set; }
    public int RoundId { get; set; }
    public Guid TargetUserId { get; set; }
    public Guid? ReporterUserId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public long? CourtCaseId { get; set; }
}

public sealed class GovernanceModerationAction
{
    public long Id { get; set; }
    public long IncidentId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid TargetUserId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int? DurationSeconds { get; set; }
    public string Status { get; set; } = string.Empty;
    public short RequiredApprovals { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class GovernanceModerationApproval
{
    public long ActionId { get; set; }
    public Guid ApproverUserId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class GovernanceModerationReview
{
    public long Id { get; set; }
    public long ActionId { get; set; }
    public Guid ReviewerUserId { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class GovernanceEventProposal
{
    public long Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string Manifest { get; set; } = "[]";
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ReviewDeadline { get; set; }
    public long? DiscordThreadId { get; set; }
}

public sealed class GovernanceEventReview
{
    public long Id { get; set; }
    public long ProposalId { get; set; }
    public Guid ReviewerUserId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
}

public sealed class GovernanceEventSession
{
    public long Id { get; set; }
    public long ProposalId { get; set; }
    public Guid DirectorUserId { get; set; }
    public int? RoundId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? EndedAt { get; set; }
}

public sealed class GovernanceEventManifestItem
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public string Capability { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public int MaxUses { get; set; }
    public int UsedCount { get; set; }
}

public sealed class GovernanceEventAction
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Capability { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Payload { get; set; } = "{}";
    public string ServerStatus { get; set; } = "pending";
    public DateTime? ServerExecutedAt { get; set; }
    public string? ServerExecutionError { get; set; }
}
