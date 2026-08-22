using System.Linq;
using System.Threading.Tasks;
using Content.Server.EUI;
using Content.Shared._RuMC14.Governance;
using Content.Shared.Eui;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._RuMC14.Governance;

public sealed class GovernanceAHelpQueueEui : BaseEui
{
    private readonly GovernanceAHelpSystem _system =
        IoCManager.Resolve<IEntityManager>().System<GovernanceAHelpSystem>();

    private GovernanceAHelpQueueItem[] _tickets = [];
    private GovernanceAHelpTranscriptEntry[] _transcript = [];
    private GovernanceAHelpModerationActionEntry[] _incidentActions = [];
    private GovernanceAHelpPendingApprovalEntry[] _pendingApprovals = [];
    private GovernanceAHelpLogEntry[] _logs = [];
    private long _selectedTicketId;
    private long _incidentId;
    private long _courtCaseId;
    private string _incidentTargetName = string.Empty;
    private string _incidentTargetCharacterName = string.Empty;
    private string _incidentType = string.Empty;
    private string? _error;
    private bool _busy;

    public override void Opened()
    {
        base.Opened();
        _system.RegisterResponderEui(this);
        _ = HandleAsync(new GovernanceAHelpQueueMessage(GovernanceAHelpQueueAction.Refresh));
    }

    public override void Closed()
    {
        _system.UnregisterResponderEui(this);
        base.Closed();
    }

    public override EuiStateBase GetNewState() => new GovernanceAHelpQueueEuiState(
        _tickets,
        _selectedTicketId,
        _transcript,
        _incidentId,
        _incidentTargetName,
        _incidentTargetCharacterName,
        _incidentType,
        _courtCaseId,
        _incidentActions,
        _pendingApprovals,
        _logs,
        _error);

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (_busy || msg is not GovernanceAHelpQueueMessage action)
            return;
        _ = HandleAsync(action);
    }

    public async Task RefreshFromSystemAsync()
    {
        if (!_busy)
            await RefreshAsync();
    }

    private async Task HandleAsync(GovernanceAHelpQueueMessage message)
    {
        _busy = true;
        _error = null;
        try
        {
            switch (message.Action)
            {
                case GovernanceAHelpQueueAction.Refresh:
                    break;
                case GovernanceAHelpQueueAction.SelectTicket:
                    _selectedTicketId = message.TicketId;
                    _logs = [];
                    break;
                case GovernanceAHelpQueueAction.Claim:
                    if (!await _system.ClaimAsync(Player, message.TicketId))
                        _error = Loc.GetString("governance-ahelp-claim-failed");
                    else
                        _selectedTicketId = message.TicketId;
                    break;
                case GovernanceAHelpQueueAction.SendMessage:
                    if (string.IsNullOrWhiteSpace(message.Text) ||
                        !await _system.SendResponderMessageAsync(Player, message.TicketId, message.Text))
                        _error = Loc.GetString("governance-ahelp-send-failed");
                    break;
                case GovernanceAHelpQueueAction.WaitingPlayer:
                    if (!await _system.SetStatusAsync(Player, message.TicketId, "waiting_player"))
                        _error = Loc.GetString("governance-ahelp-status-failed");
                    break;
                case GovernanceAHelpQueueAction.Resolve:
                    if (!await _system.SetStatusAsync(Player, message.TicketId, "resolved"))
                        _error = Loc.GetString("governance-ahelp-status-failed");
                    break;
                case GovernanceAHelpQueueAction.CreateIncident:
                {
                    var incidentError = await _system.CreateIncidentAsync(
                        Player,
                        message.TicketId,
                        message.Text ?? string.Empty,
                        message.AuxiliaryText ?? string.Empty);
                    if (incidentError != null)
                        _error = Loc.GetString(incidentError);
                    break;
                }
                case GovernanceAHelpQueueAction.Freeze:
                {
                    int? duration = int.TryParse(message.AuxiliaryText, out var seconds) ? seconds : null;
                    await RunIncidentActionAsync(message, "freeze", duration);
                    break;
                }
                case GovernanceAHelpQueueAction.RoundRemove:
                    await RunIncidentActionAsync(message, "round_remove", null);
                    break;
                case GovernanceAHelpQueueAction.ApproveModerationAction:
                    await ReviewActionAsync(message.TicketId, "approve");
                    break;
                case GovernanceAHelpQueueAction.RejectModerationAction:
                    await ReviewActionAsync(message.TicketId, "reject");
                    break;
                case GovernanceAHelpQueueAction.OpenFullLogs:
                {
                    var recordsError = await _system.OpenFullLogsAsync(Player);
                    if (recordsError != null)
                        _error = Loc.GetString(recordsError);
                    break;
                }
                case GovernanceAHelpQueueAction.OpenPlayerNotes:
                {
                    var notesError = await _system.OpenPlayerNotesAsync(Player, message.Text ?? string.Empty);
                    if (notesError != null)
                        _error = Loc.GetString(notesError);
                    break;
                }
                case GovernanceAHelpQueueAction.EscalateToCourt:
                {
                    var courtError = await _system.EscalateIncidentToCourtAsync(
                        Player,
                        message.TicketId,
                        message.Text ?? string.Empty);
                    if (courtError != null)
                        _error = Loc.GetString(courtError);
                    break;
                }
                case GovernanceAHelpQueueAction.RequestExplanation:
                case GovernanceAHelpQueueAction.ViewLogs:
                    // Retired from the responder workspace. Conversation is handled in AHelp and logs
                    // are available through the full native Admin Logs viewer.
                    break;
            }

            await RefreshAsync();
        }
        catch (Exception exception)
        {
            Logger.GetSawmill("governance.ahelp").Error(
                $"Governance AHelp EUI failed for {Player.UserId}: {exception}");
            _error = Loc.GetString("governance-ahelp-unavailable");
            StateDirty();
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RunIncidentActionAsync(
        GovernanceAHelpQueueMessage message,
        string actionType,
        int? durationSeconds)
    {
        var result = await _system.ProposeIncidentActionAsync(
            Player,
            message.TicketId,
            actionType,
            message.Text ?? string.Empty,
            durationSeconds);
        ApplyActionResult(result);
    }

    private async Task ReviewActionAsync(long actionId, string decision)
    {
        var result = await _system.ReviewIncidentActionAsync(Player, actionId, decision);
        ApplyActionResult(result);
    }

    private void ApplyActionResult(GovernanceAHelpActionExecutionResult result)
    {
        if (result.ErrorLocaleKey != null)
            _error = Loc.GetString(result.ErrorLocaleKey);

        _logs = result.Logs.Select(log => new GovernanceAHelpLogEntry(
            log.CreatedAt.UtcDateTime,
            log.Type,
            log.Message)).ToArray();
    }

    private async Task RefreshAsync()
    {
        var queue = await _system.GetQueueAsync(Player);
        _tickets = queue.Select(value => new GovernanceAHelpQueueItem(
            value.Id,
            value.ReporterUserId,
            value.ReporterName,
            value.Summary,
            value.Status,
            value.CreatedAt.UtcDateTime,
            value.ClaimedByMe)).ToArray();

        _pendingApprovals = (await _system.GetPendingActionApprovalsAsync(Player))
            .Select(action => new GovernanceAHelpPendingApprovalEntry(
                action.Id,
                action.IncidentId,
                action.ActorName,
                action.TargetName,
                action.ActionType,
                action.Reason,
                action.DurationSeconds ?? 0,
                action.Approvals,
                action.RequiredApprovals))
            .ToArray();

        if (_selectedTicketId == 0 || _tickets.All(ticket => ticket.Id != _selectedTicketId))
        {
            _selectedTicketId = _tickets.FirstOrDefault(ticket => ticket.ClaimedByMe)?.Id
                ?? _tickets.FirstOrDefault()?.Id
                ?? 0;
        }

        var selected = _tickets.FirstOrDefault(ticket => ticket.Id == _selectedTicketId);
        _incidentId = 0;
        _courtCaseId = 0;
        _incidentTargetName = string.Empty;
        _incidentTargetCharacterName = string.Empty;
        _incidentType = string.Empty;
        _incidentActions = [];

        if (selected?.ClaimedByMe == true)
        {
            var transcript = await _system.GetResponderTranscriptAsync(Player, selected.Id);
            _transcript = transcript.Select(line => new GovernanceAHelpTranscriptEntry(
                line.SenderName,
                line.Body,
                line.CreatedAt.UtcDateTime,
                line.SenderUserId == Player.UserId)).ToArray();

            var incident = await _system.GetIncidentAsync(Player, selected.Id);
            if (incident != null)
            {
                _incidentId = incident.Id;
                _courtCaseId = incident.CourtCaseId ?? 0;
                _incidentTargetName = incident.TargetName;
                _incidentTargetCharacterName = incident.TargetCharacterName;
                _incidentType = incident.Type;
                _incidentActions = (await _system.GetIncidentActionsAsync(Player, incident.Id))
                    .Select(action => new GovernanceAHelpModerationActionEntry(
                        action.Id,
                        action.ActionType,
                        action.Reason,
                        action.DurationSeconds ?? 0,
                        action.Status,
                        action.Approvals,
                        action.RequiredApprovals))
                    .ToArray();
            }
        }
        else
        {
            _transcript = [];
        }

        StateDirty();
    }
}
