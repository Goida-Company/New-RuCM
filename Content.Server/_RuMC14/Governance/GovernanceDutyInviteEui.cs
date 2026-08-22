using Content.Server.EUI;
using Content.Shared._RuMC14.Governance;
using Content.Shared.Eui;

namespace Content.Server._RuMC14.Governance;

public sealed class GovernanceDutyInviteEui(
    long invitationId,
    GovernanceInviteKind kind,
    string entityId,
    DateTimeOffset expiresAt,
    int acceptReward,
    int declinePenalty,
    int expiryPenalty,
    GovernanceDutySystem dutySystem) : BaseEui
{
    private bool _responding;

    public override EuiStateBase GetNewState()
    {
        return new GovernanceDutyInviteEuiState(
            kind,
            entityId,
            expiresAt.UtcDateTime,
            acceptReward,
            declinePenalty,
            expiryPenalty);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (_responding || msg is not GovernanceDutyInviteChoiceMessage choice)
            return;

        _responding = true;
        Close();
        _ = dutySystem.RespondToInvitationAsync(Player, invitationId, kind, choice.Choice);
    }
}
