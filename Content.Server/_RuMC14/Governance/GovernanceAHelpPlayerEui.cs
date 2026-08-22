using System.Linq;
using System.Threading.Tasks;
using Content.Server.EUI;
using Content.Shared._RuMC14.Governance;
using Content.Shared.Eui;

namespace Content.Server._RuMC14.Governance;

public sealed class GovernanceAHelpPlayerEui(GovernanceAHelpSystem system) : BaseEui
{
    private long? _ticketId;
    private string _status = "new";
    private string _responderName = string.Empty;
    private GovernanceAHelpTranscriptEntry[] _transcript = [];
    private string? _error;
    private bool _busy;

    public override void Opened()
    {
        base.Opened();
        system.RegisterPlayerEui(Player.UserId, this);
        _ = RefreshAsync();
    }

    public override void Closed()
    {
        system.UnregisterPlayerEui(Player.UserId, this);
        base.Closed();
    }

    public override EuiStateBase GetNewState() => new GovernanceAHelpPlayerEuiState(
        _ticketId,
        _status,
        _responderName,
        _transcript,
        _status != "escalated_to_court",
        _error);

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (_busy || msg is not GovernanceAHelpPlayerMessage message)
            return;

        _ = HandleAsync(message);
    }

    public async Task RefreshFromSystemAsync()
    {
        if (!_busy)
            await RefreshAsync();
    }

    private async Task HandleAsync(GovernanceAHelpPlayerMessage message)
    {
        _busy = true;
        _error = null;
        try
        {
            switch (message.Action)
            {
                case GovernanceAHelpPlayerAction.Refresh:
                    break;
                case GovernanceAHelpPlayerAction.SendMessage:
                    if (string.IsNullOrWhiteSpace(message.Text) ||
                        !await system.SendPlayerMessageAsync(Player, message.Text))
                        _error = Loc.GetString("governance-ahelp-player-send-failed");
                    break;
                case GovernanceAHelpPlayerAction.Resolve:
                    if (!await system.ResolveByPlayerAsync(Player))
                        _error = Loc.GetString("governance-ahelp-player-resolve-failed");
                    break;
            }

            await RefreshAsync();
        }
        catch (Exception exception)
        {
            Logger.GetSawmill("governance.ahelp").Error(
                $"Governance player AHelp EUI failed for {Player.UserId}: {exception}");
            _error = Loc.GetString("governance-ahelp-unavailable");
            StateDirty();
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RefreshAsync()
    {
        var ticket = await system.GetPlayerTicketAsync(Player);
        var transcript = await system.GetPlayerTranscriptAsync(Player);

        _ticketId = ticket?.Id;
        _status = ticket?.Status ?? "new";
        _responderName = ticket?.ResponderName ?? string.Empty;
        _transcript = transcript.Select(line => new GovernanceAHelpTranscriptEntry(
            line.SenderName,
            line.Body,
            line.CreatedAt.UtcDateTime,
            line.SenderUserId != Player.UserId)).ToArray();
        StateDirty();
    }
}
