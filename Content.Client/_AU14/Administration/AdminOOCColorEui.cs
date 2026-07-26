using Content.Client.Eui;
using Content.Shared._AU14.Administration;
using Content.Shared.Eui;

namespace Content.Client._AU14.Administration;

public sealed class AdminOOCColorEui : BaseEui
{
    private readonly AdminOOCColorWindow _window;

    public AdminOOCColorEui()
    {
        _window = new AdminOOCColorWindow();
        _window.OnSetColor += (rankId, color) => SendMessage(new SetAdminRankOOCColor
        {
            RankId = rankId,
            Color = color,
        });
        _window.OnClose += () => SendMessage(new CloseEuiMessage());
    }

    public override void Opened()
    {
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _window.Close();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is AdminOOCColorEuiState colorState)
            _window.Populate(colorState);
    }
}
