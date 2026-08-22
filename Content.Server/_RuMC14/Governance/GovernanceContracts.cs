using System;
using System.Collections.Generic;
using Robust.Shared.Network;

namespace Content.Server._RuMC14.Governance;

public sealed record GovernanceDutySession(
    long Id,
    Guid GovernanceUserId,
    NetUserId Ss14UserId,
    int RoundId,
    DateTimeOffset ExpiresAt);

public sealed record GovernanceAuthorization(
    GovernanceDutySession Duty,
    string Capability,
    DateTimeOffset ExpiresAt);

public sealed record GovernanceModerationActionAuthorization(
    long ActionId,
    long IncidentId,
    string ActionType);

public sealed record GovernanceDutyInvitation(
    long Id,
    NetUserId UserId,
    int RoundId,
    DateTimeOffset ExpiresAt);

public sealed record GovernanceJuryInvitation(
    long Id,
    NetUserId UserId,
    string CaseId,
    DateTimeOffset ExpiresAt);

public sealed record GovernanceAHelpTicketInfo(
    long Id,
    int RoundId,
    NetUserId ReporterUserId,
    string ReporterName,
    string Summary,
    string Status,
    DateTimeOffset CreatedAt,
    bool ClaimedByMe);

public sealed record GovernanceAHelpTranscriptLine(
    NetUserId SenderUserId,
    string SenderName,
    string Body,
    DateTimeOffset CreatedAt);

public enum GovernanceDutyInvitationChoice
{
    Accept,
    Decline,
    Recuse,
}

public enum GovernanceDutyResponseStatus
{
    Accepted,
    Declined,
    Recused,
    Expired,
    AlreadyHandled,
    Invalid,
    NotObserver,
}

public sealed record GovernanceDutyResponse(
    GovernanceDutyResponseStatus Status,
    int CivicRating,
    GovernanceDutySession? Duty = null);

public enum GovernanceDenial
{
    None,
    Disabled,
    DatabaseUnavailable,
    NotOnDuty,
    NotObserver,
    SelfTarget,
    InvalidDuration,
    InvalidInput,
    TargetUnavailable,
    AlreadyFrozen,
    ActionNotApproved,
    AHelpUnavailable,
}

public readonly record struct GovernanceActionResult(GovernanceDenial Denial)
{
    public bool Allowed => Denial == GovernanceDenial.None;

    public static GovernanceActionResult Success => new(GovernanceDenial.None);
}

public sealed record GovernanceLogLine(DateTimeOffset CreatedAt, string Type, string Message);

public sealed record GovernanceLogAccessResult(
    GovernanceDenial Denial,
    IReadOnlyList<GovernanceLogLine> Logs)
{
    public bool Allowed => Denial == GovernanceDenial.None;
}

public static class GovernancePolicy
{
    public static GovernanceDenial ValidateFreeze(
        bool enabled,
        bool actorIsObserver,
        NetUserId actor,
        NetUserId target,
        int durationSeconds,
        int maximumSeconds)
    {
        if (!enabled)
            return GovernanceDenial.Disabled;
        if (!actorIsObserver)
            return GovernanceDenial.NotObserver;
        if (actor == target)
            return GovernanceDenial.SelfTarget;
        if (durationSeconds < 1 || durationSeconds > maximumSeconds)
            return GovernanceDenial.InvalidDuration;

        return GovernanceDenial.None;
    }
}
