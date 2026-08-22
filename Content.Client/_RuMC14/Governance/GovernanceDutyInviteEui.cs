using Content.Client.Eui;
using Content.Shared._RuMC14.Governance;
using Content.Shared.Eui;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client._RuMC14.Governance;

[UsedImplicitly]
public sealed class GovernanceDutyInviteEui : BaseEui
{
    private GovernanceDutyInviteWindow? _window;
    private bool _responded;

    public override void Opened()
    {
        base.Opened();
        _responded = false;
        _window = new GovernanceDutyInviteWindow();
        _window.AcceptButton.OnPressed += _ => Respond(GovernanceDutyInviteChoice.Accept);
        _window.DeclineButton.OnPressed += _ => Respond(GovernanceDutyInviteChoice.Decline);
        _window.RecuseButton.OnPressed += _ => Respond(GovernanceDutyInviteChoice.Recuse);
        _window.OnClose += OnWindowClosed;
        IoCManager.Resolve<IClyde>().RequestWindowAttention();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        if (_window == null)
            return;

        _window.OnClose -= OnWindowClosed;
        _window.Close();
        _window = null;
    }

    public override void HandleState(EuiStateBase state)
    {
        base.HandleState(state);
        if (state is GovernanceDutyInviteEuiState invitation)
        {
            _window?.UpdateInvitation(
                invitation.Kind,
                invitation.EntityId,
                invitation.ExpiresAt,
                invitation.AcceptReward,
                invitation.DeclinePenalty,
                invitation.ExpiryPenalty);
        }
    }

    private void Respond(GovernanceDutyInviteChoice choice)
    {
        if (_responded)
            return;

        _responded = true;
        SendMessage(new GovernanceDutyInviteChoiceMessage(choice));
        _window?.Close();
    }

    private void OnWindowClosed()
    {
        if (!_responded)
            SendMessage(new CloseEuiMessage());
    }
}
