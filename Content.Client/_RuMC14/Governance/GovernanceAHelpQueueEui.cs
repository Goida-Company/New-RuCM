using Content.Client.Eui;
using Content.Shared._RuMC14.Governance;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._RuMC14.Governance;

[UsedImplicitly]
public sealed class GovernanceAHelpQueueEui : BaseEui
{
    private GovernanceAHelpQueueWindow? _window;

    public override void Opened()
    {
        base.Opened();
        _window = new GovernanceAHelpQueueWindow();
        _window.ActionRequested += SendAction;
        _window.OnClose += OnClosed;
        _window.OpenCentered();
    }

    public override void Closed()
    {
        base.Closed();
        if (_window == null)
            return;
        _window.ActionRequested -= SendAction;
        _window.OnClose -= OnClosed;
        _window.Close();
        _window = null;
    }

    public override void HandleState(EuiStateBase state)
    {
        base.HandleState(state);
        if (state is GovernanceAHelpQueueEuiState queue)
            _window?.UpdateState(queue);
    }

    private void SendAction(
        GovernanceAHelpQueueAction action,
        long ticketId,
        string? text,
        string? auxiliaryText)
    {
        SendMessage(new GovernanceAHelpQueueMessage(action, ticketId, text, auxiliaryText));
    }

    private void OnClosed()
    {
        SendMessage(new CloseEuiMessage());
    }
}
