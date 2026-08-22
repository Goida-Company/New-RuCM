namespace Content.DiscordBot.Governance;

public static class ModerationReviewOutcomes
{
    public const string Correct = "correct";
    public const string ReasonableButWrong = "reasonable_but_wrong";
    public const string ProceduralError = "procedural_error";
    public const string Negligent = "negligent";
    public const string Abuse = "abuse";

    public static bool IsValid(string outcome) => outcome is
        Correct or ReasonableButWrong or ProceduralError or Negligent or Abuse;

    public static int AccuracyWeight(string outcome) => outcome switch
    {
        Correct => 100,
        ReasonableButWrong => 85,
        ProceduralError => 60,
        Negligent => 25,
        Abuse => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    public static int ProcedureWeight(string outcome) => outcome switch
    {
        Correct or ReasonableButWrong => 100,
        ProceduralError => 35,
        Negligent => 20,
        Abuse => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };
}

public sealed record ModerationTrustProfile(
    Guid UserId,
    int TrustScore,
    int DecisionAccuracy,
    int ProceduralScore,
    int ReliabilityScore,
    int Confidence,
    int ReviewedActions,
    int ReviewCount,
    int CompletedDuties,
    int FailedDuties,
    int SeriousInterventions);

public sealed record ModerationReviewAssignment(
    long ActionId,
    long InvitationId,
    Guid ReviewerUserId,
    long? ReviewerDiscordId,
    DateTime ExpiresAt);

public sealed record ModerationReviewPacket(
    long ActionId,
    string ActionType,
    string Reason,
    int? DurationSeconds,
    DateTime CreatedAt,
    DateTime? ExecutedAt,
    short RequiredApprovals,
    int Approvals,
    int Rejections,
    long IncidentId,
    string IncidentType,
    string IncidentSummary,
    int RoundId,
    string IncidentStatus,
    bool EscalatedToCourt);
