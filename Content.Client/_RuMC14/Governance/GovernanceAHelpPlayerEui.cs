using Content.Client.Eui;
using Content.Shared._RuMC14.Governance;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._RuMC14.Governance;

[UsedImplicitly]
public sealed class GovernanceAHelpPlayerEui : BaseEui
{
    private GovernanceAHelpPlayerWindow? _window;

    public override void Opened()
    {
        base.Opened();
        _window = new GovernanceAHelpPlayerWindow();
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
        if (state is GovernanceAHelpPlayerEuiState playerState)
            _window?.UpdateState(playerState);
    }

    private void SendAction(GovernanceAHelpPlayerAction action, string? text)
    {
        SendMessage(new GovernanceAHelpPlayerMessage(action, text));
    }

    private void OnClosed()
    {
        SendMessage(new CloseEuiMessage());
    }
}
