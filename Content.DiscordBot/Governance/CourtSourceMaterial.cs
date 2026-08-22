namespace Content.DiscordBot.Governance;

public sealed record CourtTranscriptEntry(
    DateTime CreatedAt,
    Guid SenderSs14UserId,
    string SenderName,
    string Body,
    bool FromResponder);

public sealed record CourtPlayerHistoryEntry(
    DateTime CreatedAt,
    string Kind,
    string Message);

public sealed record CourtSourceMaterial(
    long IncidentId,
    long AHelpTicketId,
    Guid ClaimantSs14UserId,
    string ClaimantName,
    Guid DefendantSs14UserId,
    string DefendantName,
    string DefendantCharacterName,
    IReadOnlyList<CourtTranscriptEntry> Transcript,
    IReadOnlyList<CourtPlayerHistoryEntry> PlayerHistory);
