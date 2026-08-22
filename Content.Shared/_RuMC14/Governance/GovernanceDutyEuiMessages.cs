using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._RuMC14.Governance;

[Serializable, NetSerializable]
public enum GovernanceInviteKind
{
    ModerationDuty,
    Jury,
}

[Serializable, NetSerializable]
public enum GovernanceDutyInviteChoice
{
    Accept,
    Decline,
    Recuse,
}

[Serializable, NetSerializable]
public sealed class GovernanceDutyInviteChoiceMessage(
    GovernanceDutyInviteChoice choice) : EuiMessageBase
{
    public readonly GovernanceDutyInviteChoice Choice = choice;
}

[Serializable, NetSerializable]
public sealed class GovernanceDutyInviteEuiState(
    GovernanceInviteKind kind,
    string entityId,
    DateTime expiresAt,
    int acceptReward,
    int declinePenalty,
    int expiryPenalty) : EuiStateBase
{
    public readonly GovernanceInviteKind Kind = kind;
    public readonly string EntityId = entityId;
    public readonly DateTime ExpiresAt = expiresAt;
    public readonly int AcceptReward = acceptReward;
    public readonly int DeclinePenalty = declinePenalty;
    public readonly int ExpiryPenalty = expiryPenalty;
}
