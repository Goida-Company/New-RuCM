using System.Linq;
using System.Numerics;
using Content.Client.Lobby.UI;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Shared._RuMC14.Governance;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.IoC;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._RuMC14.Governance;

public sealed class GovernanceAHelpQueueWindow : DefaultWindow
{
    public event Action<GovernanceAHelpQueueAction, long, string?, string?>? ActionRequested;

    private readonly BoxContainer _ticketList;
    private readonly BoxContainer _approvalList;
    private readonly BoxContainer _transcript;
    private readonly BoxContainer _incidentActionList;
    private readonly Label _counter;
    private readonly RichTextLabel _ticketHeader;
    private readonly RichTextLabel _ticketMeta;
    private readonly RichTextLabel _incidentStatus;
    private readonly Label _error;
    private readonly LineEdit _filter;
    private readonly LineEdit _reply;
    private readonly LineEdit _incidentTarget;
    private readonly LineEdit _incidentType;
    private readonly LineEdit _actionReason;
    private readonly Button _claim;
    private readonly Button _send;
    private readonly Button _waiting;
    private readonly Button _resolve;
    private readonly Button _createIncident;
    private readonly Button _freeze;
    private readonly Button _roundRemove;
    private readonly Button _court;
    private readonly Button _fullLogs;
    private readonly Button _reporterNotes;
    private readonly Button _targetNotes;

    private IReadOnlyList<GovernanceAHelpQueueItem> _tickets = [];
    private long _selectedTicketId;
    private long _incidentId;
    private long _courtCaseId;
    private string _incidentTargetName = string.Empty;
    private string _incidentTargetCharacterName = string.Empty;
    private long _lastRenderedTicketId;

    public GovernanceAHelpQueueWindow()
    {
        Title = Loc.GetString("governance-ahelp-title");
        // 1420px made the investigation column physically unreachable on common 1366x768 layouts.
        MinSize = new Vector2(1120, 700);
        Stylesheet = IoCManager.Resolve<IStylesheetManager>().SheetNano;
        CrtLobbyTheme.ApplyWindow(this, includeChat: true, useCrtTypography: false);

        var root = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 8,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var header = new PanelContainer
        {
            StyleClasses = { StyleNano.StyleClassCrtPanel },
            HorizontalExpand = true,
        };
        var headerRow = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            HorizontalExpand = true,
        };
        var titleColumn = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 1,
            HorizontalExpand = true,
        };
        titleColumn.AddChild(new RichTextLabel { Text = Loc.GetString("governance-ahelp-workspace-header") });
        titleColumn.AddChild(new RichTextLabel { Text = Loc.GetString("governance-ahelp-workspace-subtitle-modern") });
        _counter = new Label();
        var refresh = new Button { Text = Loc.GetString("governance-ahelp-refresh") };
        refresh.OnPressed += _ => ActionRequested?.Invoke(GovernanceAHelpQueueAction.Refresh, 0, null, null);
        _fullLogs = new Button { Text = Loc.GetString("governance-ahelp-tool-full-logs") };
        _fullLogs.OnPressed += _ => ActionRequested?.Invoke(GovernanceAHelpQueueAction.OpenFullLogs, 0, null, null);
        headerRow.AddChild(titleColumn);
        headerRow.AddChild(_counter);
        headerRow.AddChild(_fullLogs);
        headerRow.AddChild(refresh);
        header.AddChild(headerRow);
        root.AddChild(header);

        var body = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 8,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var queuePanel = new PanelContainer
        {
            StyleClasses = { StyleNano.StyleClassCrtInsetPanel },
            HorizontalExpand = false,
            VerticalExpand = true,
            MinSize = new Vector2(250, 0),
        };
        var queueColumn = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        queueColumn.AddChild(new RichTextLabel
        {
            Text = Loc.GetString("governance-ahelp-list-heading"),
            HorizontalExpand = true,
        });
        _filter = new LineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = Loc.GetString("governance-ahelp-filter-placeholder-short"),
        };
        _filter.OnTextChanged += _ => RebuildTicketList();
        queueColumn.AddChild(_filter);
        var queueScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            VScrollEnabled = true,
        };
        _ticketList = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };
        queueScroll.AddChild(_ticketList);
        queueColumn.AddChild(queueScroll);
        queuePanel.AddChild(queueColumn);
        body.AddChild(queuePanel);

        var conversationPanel = new PanelContainer
        {
            StyleClasses = { StyleNano.StyleClassCrtInsetPanel },
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        var conversation = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 7,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        _ticketHeader = new RichTextLabel { Text = Loc.GetString("governance-ahelp-select-ticket") };
        _ticketMeta = new RichTextLabel();
        conversation.AddChild(_ticketHeader);
        conversation.AddChild(_ticketMeta);

        var transcriptScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            VScrollEnabled = true,
        };
        _transcript = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 8,
            HorizontalExpand = true,
        };
        transcriptScroll.AddChild(_transcript);
        conversation.AddChild(transcriptScroll);

        var templates = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 5 };
        var greeting = new Button { Text = Loc.GetString("governance-ahelp-template-greeting") };
        var details = new Button { Text = Loc.GetString("governance-ahelp-template-details") };
        var wait = new Button { Text = Loc.GetString("governance-ahelp-template-wait") };
        templates.AddChild(greeting);
        templates.AddChild(details);
        templates.AddChild(wait);
        conversation.AddChild(templates);

        var composer = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 5 };
        _reply = new LineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = Loc.GetString("governance-ahelp-reply-placeholder"),
        };
        greeting.OnPressed += _ => _reply.Text = Loc.GetString("governance-ahelp-template-greeting-text");
        details.OnPressed += _ => _reply.Text = Loc.GetString("governance-ahelp-template-details-text");
        wait.OnPressed += _ => _reply.Text = Loc.GetString("governance-ahelp-template-wait-text");
        _reply.OnTextEntered += args => SendReply(args.Text);
        _send = new Button { Text = Loc.GetString("governance-ahelp-send") };
        _send.OnPressed += _ => SendReply(_reply.Text);
        composer.AddChild(_reply);
        composer.AddChild(_send);
        conversation.AddChild(composer);

        var ticketActions = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 5 };
        _claim = TicketActionButton(Loc.GetString("governance-ahelp-claim"), GovernanceAHelpQueueAction.Claim);
        _waiting = TicketActionButton(Loc.GetString("governance-ahelp-waiting"), GovernanceAHelpQueueAction.WaitingPlayer);
        _resolve = TicketActionButton(Loc.GetString("governance-ahelp-resolve"), GovernanceAHelpQueueAction.Resolve);
        ticketActions.AddChild(_claim);
        ticketActions.AddChild(_waiting);
        ticketActions.AddChild(_resolve);
        conversation.AddChild(ticketActions);

        _error = new Label { StyleClasses = { "LabelDanger" }, ClipText = true, HorizontalExpand = true };
        conversation.AddChild(_error);
        conversationPanel.AddChild(conversation);
        body.AddChild(conversationPanel);

        var investigationPanel = new PanelContainer
        {
            StyleClasses = { StyleNano.StyleClassCrtInsetPanel },
            HorizontalExpand = false,
            VerticalExpand = true,
            MinSize = new Vector2(320, 0),
        };
        var investigationScroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = false,
            VScrollEnabled = true,
        };
        var investigation = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 7,
            HorizontalExpand = true,
        };
        investigation.AddChild(new RichTextLabel
        {
            Text = Loc.GetString("governance-ahelp-context-heading"),
            HorizontalExpand = true,
        });

        var contextButtons = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 5,
            HorizontalExpand = true,
        };
        _reporterNotes = new Button
        {
            Text = Loc.GetString("governance-ahelp-tool-reporter-notes"),
            HorizontalExpand = true,
            ClipText = true,
        };
        _reporterNotes.OnPressed += _ => OpenReporterNotes();
        _targetNotes = new Button
        {
            Text = Loc.GetString("governance-ahelp-tool-target-notes"),
            HorizontalExpand = true,
            ClipText = true,
        };
        _targetNotes.OnPressed += _ => OpenTargetNotes();
        contextButtons.AddChild(_reporterNotes);
        contextButtons.AddChild(_targetNotes);
        investigation.AddChild(contextButtons);

        var incidentPanel = new PanelContainer
        {
            StyleClasses = { StyleNano.StyleClassCrtPanel },
            HorizontalExpand = true,
        };
        var incidentColumn = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true,
        };
        incidentColumn.AddChild(new RichTextLabel
        {
            Text = Loc.GetString("governance-ahelp-incident-heading"),
            HorizontalExpand = true,
        });
        _incidentStatus = new RichTextLabel
        {
            Text = Loc.GetString("governance-ahelp-incident-none"),
            HorizontalExpand = true,
        };
        incidentColumn.AddChild(_incidentStatus);

        _incidentTarget = new LineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = Loc.GetString("governance-ahelp-incident-target-placeholder"),
        };
        _incidentType = new LineEdit
        {
            HorizontalExpand = true,
            Text = Loc.GetString("governance-ahelp-incident-type-default"),
            PlaceHolder = Loc.GetString("governance-ahelp-incident-type-placeholder"),
        };
        _createIncident = new Button
        {
            HorizontalExpand = true,
            ClipText = true,
            Text = Loc.GetString("governance-ahelp-incident-create"),
        };
        _createIncident.OnPressed += _ => CreateIncident();
        incidentColumn.AddChild(_incidentTarget);
        incidentColumn.AddChild(_incidentType);
        incidentColumn.AddChild(_createIncident);

        incidentColumn.AddChild(new RichTextLabel
        {
            Text = Loc.GetString("governance-ahelp-containment-heading"),
            HorizontalExpand = true,
        });
        _actionReason = new LineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = Loc.GetString("governance-ahelp-action-reason-placeholder"),
        };
        incidentColumn.AddChild(_actionReason);

        var containment = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 5,
            HorizontalExpand = true,
        };
        _freeze = new Button
        {
            Text = Loc.GetString("governance-ahelp-action-freeze"),
            HorizontalExpand = true,
            ClipText = true,
        };
        _freeze.OnPressed += _ => RunIncidentAction(GovernanceAHelpQueueAction.Freeze);
        _roundRemove = new Button
        {
            Text = Loc.GetString("governance-ahelp-action-round-remove-short"),
            HorizontalExpand = true,
            ClipText = true,
        };
        _roundRemove.OnPressed += _ => RunIncidentAction(GovernanceAHelpQueueAction.RoundRemove);
        containment.AddChild(_freeze);
        containment.AddChild(_roundRemove);
        incidentColumn.AddChild(containment);

        _court = new Button
        {
            Text = Loc.GetString("governance-ahelp-court-escalate"),
            HorizontalExpand = true,
            ClipText = true,
        };
        _court.OnPressed += _ => EscalateToCourt();
        incidentColumn.AddChild(_court);

        incidentColumn.AddChild(new RichTextLabel
        {
            Text = Loc.GetString("governance-ahelp-action-history-heading"),
            HorizontalExpand = true,
        });
        _incidentActionList = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 4,
            HorizontalExpand = true,
        };
        incidentColumn.AddChild(_incidentActionList);
        incidentPanel.AddChild(incidentColumn);
        investigation.AddChild(incidentPanel);

        investigation.AddChild(new RichTextLabel
        {
            Text = Loc.GetString("governance-ahelp-approval-heading"),
            HorizontalExpand = true,
        });
        _approvalList = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 5,
            HorizontalExpand = true,
        };
        investigation.AddChild(_approvalList);

        investigationScroll.AddChild(investigation);
        investigationPanel.AddChild(investigationScroll);
        body.AddChild(investigationPanel);

        root.AddChild(body);
        Contents.AddChild(root);
        UpdateActionState();
    }

    public void UpdateState(GovernanceAHelpQueueEuiState state)
    {
        _tickets = state.Tickets;
        _selectedTicketId = state.SelectedTicketId;
        _incidentId = state.IncidentId;
        _courtCaseId = state.CourtCaseId;
        _incidentTargetName = state.IncidentTargetName;
        _incidentTargetCharacterName = state.IncidentTargetCharacterName;
        _error.Text = state.Error ?? string.Empty;

        var mine = state.Tickets.Count(ticket => ticket.ClaimedByMe);
        var open = state.Tickets.Count(ticket => !ticket.ClaimedByMe && ticket.Status == "open");
        _counter.Text = Loc.GetString("governance-ahelp-counter-modern", ("open", open), ("mine", mine));

        RebuildTicketList();
        RebuildPendingApprovals(state.PendingApprovals);
        UpdateSelectedTicket(state.Transcript, state.IncidentType, state.IncidentActions);
        UpdateActionState();
    }

    private void RebuildTicketList()
    {
        _ticketList.RemoveAllChildren();
        var filter = _filter.Text.Trim();
        var visible = _tickets.Where(ticket =>
                string.IsNullOrWhiteSpace(filter) ||
                ticket.Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                ticket.ReporterName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                ticket.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (visible.Length == 0)
        {
            _ticketList.AddChild(new RichTextLabel
            {
                Text = _tickets.Count == 0
                    ? Loc.GetString("governance-ahelp-empty-modern")
                    : Loc.GetString("governance-ahelp-filter-empty"),
                HorizontalExpand = true,
            });
            return;
        }

        foreach (var ticket in visible)
        {
            var selected = ticket.Id == _selectedTicketId ? "▶ " : string.Empty;
            var button = new Button
            {
                HorizontalExpand = true,
                ClipText = true,
                Text = Loc.GetString(
                    "governance-ahelp-ticket-card-compact",
                    ("selected", selected),
                    ("id", ticket.Id),
                    ("reporter", ticket.ReporterName),
                    ("status", StatusText(ticket)),
                    ("time", ticket.CreatedAt.ToLocalTime().ToString("HH:mm"))),
            };
            var id = ticket.Id;
            button.OnPressed += _ => ActionRequested?.Invoke(GovernanceAHelpQueueAction.SelectTicket, id, null, null);
            _ticketList.AddChild(button);
        }
    }

    private void UpdateSelectedTicket(
        IReadOnlyList<GovernanceAHelpTranscriptEntry> transcript,
        string incidentType,
        IReadOnlyList<GovernanceAHelpModerationActionEntry> actions)
    {
        var selected = _tickets.FirstOrDefault(ticket => ticket.Id == _selectedTicketId);
        _transcript.RemoveAllChildren();
        _incidentActionList.RemoveAllChildren();

        if (selected == null)
        {
            _ticketHeader.Text = Loc.GetString("governance-ahelp-select-ticket");
            _ticketMeta.Text = string.Empty;
            _incidentStatus.Text = Loc.GetString("governance-ahelp-incident-none");
            _transcript.AddChild(new RichTextLabel { Text = Loc.GetString("governance-ahelp-no-selection-hint") });
            _incidentActionList.AddChild(new RichTextLabel { Text = Loc.GetString("governance-ahelp-action-history-empty") });
            return;
        }

        if (_lastRenderedTicketId != selected.Id)
        {
            _lastRenderedTicketId = selected.Id;
            _incidentTarget.Clear();
            _incidentType.Text = Loc.GetString("governance-ahelp-incident-type-default");
            _actionReason.Clear();
        }

        _ticketHeader.Text = Loc.GetString(
            "governance-ahelp-conversation-header",
            ("id", selected.Id),
            ("reporter", FormattedMessage.EscapeText(selected.ReporterName)));
        _ticketMeta.Text = Loc.GetString(
            "governance-ahelp-conversation-meta",
            ("status", StatusText(selected)),
            ("time", selected.CreatedAt.ToLocalTime().ToString("HH:mm:ss")),
            ("uuid", selected.ReporterUserId.ToString()));

        if (_courtCaseId > 0)
        {
            _incidentStatus.Text = Loc.GetString(
                "governance-ahelp-incident-court",
                ("incident", _incidentId),
                ("case", _courtCaseId),
                ("target", FormattedMessage.EscapeText(_incidentTargetName)),
                ("character", FormattedMessage.EscapeText(_incidentTargetCharacterName)));
        }
        else if (_incidentId > 0)
        {
            _incidentStatus.Text = Loc.GetString(
                "governance-ahelp-incident-active-character",
                ("id", _incidentId),
                ("target", FormattedMessage.EscapeText(_incidentTargetName)),
                ("character", FormattedMessage.EscapeText(_incidentTargetCharacterName)),
                ("type", FormattedMessage.EscapeText(incidentType)));
        }
        else
        {
            _incidentStatus.Text = Loc.GetString("governance-ahelp-incident-none");
        }

        RebuildIncidentActions(actions);

        if (!selected.ClaimedByMe)
        {
            _transcript.AddChild(new RichTextLabel
            {
                Text = Loc.GetString(
                    "governance-ahelp-unclaimed-preview",
                    ("summary", FormattedMessage.EscapeText(selected.Summary))),
            });
            return;
        }

        if (transcript.Count == 0)
        {
            _transcript.AddChild(new RichTextLabel { Text = Loc.GetString("governance-ahelp-transcript-empty") });
            return;
        }

        foreach (var line in transcript)
        {
            var time = line.CreatedAt.ToLocalTime().ToString("HH:mm");
            var sender = FormattedMessage.EscapeText(line.SenderName);
            var body = FormattedMessage.EscapeText(line.Body);
            var role = line.FromResponder
                ? Loc.GetString("governance-ahelp-message-role-responder")
                : Loc.GetString("governance-ahelp-message-role-player");
            var text = line.FromResponder
                ? $"[color=#8c96a8]{time}[/color] • [color=#ff5a5a][bold]● {role} • {sender}[/bold][/color]: {body}"
                : $"[color=#8c96a8]{time}[/color] • [bold]{role} • {sender}[/bold]: {body}";
            _transcript.AddChild(new RichTextLabel { Text = text, HorizontalExpand = true });
        }
    }

    private void RebuildIncidentActions(IReadOnlyList<GovernanceAHelpModerationActionEntry> actions)
    {
        var visible = actions.Where(action => action.ActionType is "freeze" or "round_remove").ToArray();
        if (visible.Length == 0)
        {
            _incidentActionList.AddChild(new RichTextLabel { Text = Loc.GetString("governance-ahelp-action-history-empty") });
            return;
        }

        foreach (var action in visible)
        {
            var duration = action.DurationSeconds > 0
                ? Loc.GetString("governance-ahelp-action-duration", ("seconds", action.DurationSeconds))
                : string.Empty;
            _incidentActionList.AddChild(new RichTextLabel
            {
                Text = Loc.GetString(
                    "governance-ahelp-action-card",
                    ("id", action.Id),
                    ("type", ActionTypeText(action.ActionType)),
                    ("status", ActionStatusText(action.Status)),
                    ("approvals", action.Approvals),
                    ("required", action.RequiredApprovals),
                    ("duration", duration),
                    ("reason", FormattedMessage.EscapeText(action.Reason))),
                HorizontalExpand = true,
            });
        }
    }

    private void RebuildPendingApprovals(IReadOnlyList<GovernanceAHelpPendingApprovalEntry> approvals)
    {
        _approvalList.RemoveAllChildren();
        if (approvals.Count == 0)
        {
            _approvalList.AddChild(new RichTextLabel
            {
                Text = Loc.GetString("governance-ahelp-approval-empty"),
                HorizontalExpand = true,
            });
            return;
        }

        foreach (var approval in approvals)
        {
            var card = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                SeparationOverride = 3,
                HorizontalExpand = true,
            };
            card.AddChild(new RichTextLabel
            {
                Text = Loc.GetString(
                    "governance-ahelp-approval-card",
                    ("id", approval.ActionId),
                    ("incident", approval.IncidentId),
                    ("actor", FormattedMessage.EscapeText(approval.ActorName)),
                    ("target", FormattedMessage.EscapeText(approval.TargetName)),
                    ("type", ActionTypeText(approval.ActionType)),
                    ("reason", FormattedMessage.EscapeText(approval.Reason)),
                    ("approvals", approval.Approvals),
                    ("required", approval.RequiredApprovals)),
                HorizontalExpand = true,
            });
            var buttons = new BoxContainer { Orientation = LayoutOrientation.Horizontal, SeparationOverride = 4 };
            var approve = new Button
            {
                Text = Loc.GetString("governance-ahelp-approval-approve"),
                HorizontalExpand = true,
                ClipText = true,
            };
            var reject = new Button
            {
                Text = Loc.GetString("governance-ahelp-approval-reject"),
                HorizontalExpand = true,
                ClipText = true,
            };
            var id = approval.ActionId;
            approve.OnPressed += _ => ActionRequested?.Invoke(GovernanceAHelpQueueAction.ApproveModerationAction, id, null, null);
            reject.OnPressed += _ => ActionRequested?.Invoke(GovernanceAHelpQueueAction.RejectModerationAction, id, null, null);
            buttons.AddChild(approve);
            buttons.AddChild(reject);
            card.AddChild(buttons);
            _approvalList.AddChild(card);
        }
    }

    private void CreateIncident()
    {
        if (_selectedTicketId == 0)
            return;
        var target = _incidentTarget.Text.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            _error.Text = Loc.GetString("governance-ahelp-incident-target-required");
            return;
        }
        ActionRequested?.Invoke(
            GovernanceAHelpQueueAction.CreateIncident,
            _selectedTicketId,
            target,
            _incidentType.Text.Trim());
    }

    private void RunIncidentAction(GovernanceAHelpQueueAction action)
    {
        if (_selectedTicketId == 0 || _incidentId == 0)
            return;
        var reason = _actionReason.Text.Trim();
        if (reason.Length is < 10 or > 512)
        {
            _error.Text = Loc.GetString("governance-ahelp-action-reason-invalid");
            return;
        }

        // Freeze is deliberately fixed to one minute in the workspace. A separate, unlabeled numeric
        // input made the containment UI look broken and encouraged arbitrary-duration moderation.
        var auxiliary = action == GovernanceAHelpQueueAction.Freeze ? "60" : null;
        ActionRequested?.Invoke(action, _selectedTicketId, reason, auxiliary);
    }

    private void EscalateToCourt()
    {
        if (_selectedTicketId == 0 || _incidentId == 0 || _courtCaseId > 0)
            return;
        var reason = _actionReason.Text.Trim();
        if (reason.Length is < 10 or > 1500)
        {
            _error.Text = Loc.GetString("governance-ahelp-court-reason-invalid");
            return;
        }
        ActionRequested?.Invoke(GovernanceAHelpQueueAction.EscalateToCourt, _selectedTicketId, reason, null);
    }

    private void OpenReporterNotes()
    {
        var selected = _tickets.FirstOrDefault(ticket => ticket.Id == _selectedTicketId);
        if (selected == null)
            return;
        ActionRequested?.Invoke(GovernanceAHelpQueueAction.OpenPlayerNotes, 0, selected.ReporterUserId.ToString(), null);
    }

    private void OpenTargetNotes()
    {
        if (_incidentId == 0 || string.IsNullOrWhiteSpace(_incidentTargetName))
            return;
        ActionRequested?.Invoke(GovernanceAHelpQueueAction.OpenPlayerNotes, 0, _incidentTargetName, null);
    }

    private void SendReply(string text)
    {
        if (_selectedTicketId == 0 || string.IsNullOrWhiteSpace(text))
            return;
        ActionRequested?.Invoke(GovernanceAHelpQueueAction.SendMessage, _selectedTicketId, text.Trim(), null);
        _reply.Clear();
    }

    private Button TicketActionButton(string text, GovernanceAHelpQueueAction action)
    {
        var button = new Button { Text = text, HorizontalExpand = true, ClipText = true };
        button.OnPressed += _ =>
        {
            if (_selectedTicketId != 0)
                ActionRequested?.Invoke(action, _selectedTicketId, null, null);
        };
        return button;
    }

    private void UpdateActionState()
    {
        var selected = _tickets.FirstOrDefault(ticket => ticket.Id == _selectedTicketId);
        var mine = selected?.ClaimedByMe == true;
        var escalatedToCourt = selected?.Status == "escalated_to_court" || _courtCaseId > 0;
        var canReply = mine && !escalatedToCourt;

        _claim.Disabled = selected == null || mine;
        _waiting.Disabled = !canReply;
        _resolve.Disabled = !mine;
        _reply.Editable = canReply;
        _send.Disabled = !canReply;
        _reporterNotes.Disabled = selected == null;
        _targetNotes.Disabled = !mine || _incidentId == 0;

        var canCreateIncident = mine && !escalatedToCourt && _incidentId == 0;
        _incidentTarget.Editable = canCreateIncident;
        _incidentType.Editable = canCreateIncident;
        _createIncident.Disabled = !canCreateIncident;

        var canContain = mine && _incidentId > 0 && !escalatedToCourt;
        _actionReason.Editable = canContain;
        _freeze.Disabled = !canContain;
        _roundRemove.Disabled = !canContain;
        _court.Disabled = !canContain;
    }

    private static string StatusText(GovernanceAHelpQueueItem ticket)
    {
        if (ticket.ClaimedByMe)
        {
            return ticket.Status switch
            {
                "waiting_player" => Loc.GetString("governance-ahelp-status-waiting-player"),
                "escalated_to_court" => Loc.GetString("governance-ahelp-player-status-court"),
                _ => Loc.GetString("governance-ahelp-status-mine"),
            };
        }

        return ticket.Status == "open" ? Loc.GetString("governance-ahelp-status-open") : ticket.Status;
    }

    private static string ActionTypeText(string actionType)
    {
        return actionType switch
        {
            "freeze" => Loc.GetString("governance-ahelp-action-type-freeze"),
            "round_remove" => Loc.GetString("governance-ahelp-action-type-round-remove"),
            _ => actionType,
        };
    }

    private static string ActionStatusText(string status)
    {
        return status switch
        {
            "proposed" => Loc.GetString("governance-ahelp-action-status-proposed"),
            "approved" => Loc.GetString("governance-ahelp-action-status-approved"),
            "executed" => Loc.GetString("governance-ahelp-action-status-executed"),
            "rejected" => Loc.GetString("governance-ahelp-action-status-rejected"),
            "expired" => Loc.GetString("governance-ahelp-action-status-expired"),
            _ => status,
        };
    }
}
