governance-ahelp-workspace-header = [bold]Support workspace[/bold]
governance-ahelp-workspace-subtitle = Queue, conversation and responder actions in one place.
governance-ahelp-workspace-subtitle-modern = Conversation in the center; investigation and live actions on the right.
governance-ahelp-list-heading = [bold]Queue[/bold]
governance-ahelp-list-hint = Claim an open ticket first. Its full conversation will then be available here.
governance-ahelp-filter-placeholder = Search player, ticket ID or message…
governance-ahelp-filter-placeholder-short = Search queue…
governance-ahelp-filter-empty = [color=#8c96a8]No tickets match this search.[/color]
governance-ahelp-reply-placeholder = Write a reply to the player…
governance-ahelp-counter-modern = Open: {$open} • Mine: {$mine}
governance-ahelp-template-greeting = Greeting
governance-ahelp-template-greeting-text = Hello. I have taken your ticket and I am reviewing the situation now.
governance-ahelp-template-details = Ask details
governance-ahelp-template-details-text = Please clarify what happened, where it happened, and who was involved.
governance-ahelp-template-wait = Need time
governance-ahelp-template-wait-text = Thank you. I need a little time to review the information and logs.
governance-ahelp-send = Send
governance-ahelp-empty-modern = [color=#8c96a8]There are no open support tickets right now.[/color]
governance-ahelp-selected-marker = ▶
governance-ahelp-ticket-card-modern = {$selected} #{$id} • {$reporter} • {$status} • {$time} • {$summary}
governance-ahelp-ticket-card-compact = {$selected}#{$id} • {$reporter} • {$status} • {$time}
governance-ahelp-no-selection-hint = [color=#8c96a8]Select a ticket on the left to view its details.[/color]
governance-ahelp-conversation-header = [bold]Ticket #{$id}[/bold] • {$reporter}
governance-ahelp-conversation-meta = Status: {$status} • Created: {$time} • SS14: {$uuid}
governance-ahelp-unclaimed-preview = [color=#8c96a8]Ticket preview[/color] • {$summary} • [italic]Claim the ticket to open the conversation and reply to the player.[/italic]
governance-ahelp-transcript-empty = [color=#8c96a8]There are no messages in this ticket yet.[/color]
governance-ahelp-message-role-responder = Responder
governance-ahelp-message-role-player = Player
governance-ahelp-message-line = {$time} • {$role} • {$sender}: {$body}
governance-ahelp-status-waiting-player = Waiting for player
governance-ahelp-send-failed = Could not send the message. Make sure the ticket is still assigned to you.
governance-ahelp-player-unavailable = The support center is currently unavailable.
governance-ahelp-player-send-failed = Could not send your message. Please try again.
governance-ahelp-player-resolve-failed = Could not close the ticket.
governance-ahelp-player-title = Support Center
governance-ahelp-player-header = [bold]Need help?[/bold]
governance-ahelp-player-description = Describe the issue in your own words. The ticket will enter the responder queue and the entire conversation will stay here.
governance-ahelp-player-conversation-title = [bold]Conversation[/bold]
governance-ahelp-player-tips = [color=#8c96a8]Tell us what happened, where you are, and who is involved. Do not create multiple tickets for the same issue.[/color]
governance-ahelp-player-message-placeholder = Describe the problem or reply to the responder…
governance-ahelp-player-send = Send
governance-ahelp-player-resolve = Problem solved
governance-ahelp-player-status = [bold]Status:[/bold] {$status}
governance-ahelp-player-assignee-waiting = [bold]Responder:[/bold] waiting
governance-ahelp-player-assignee = [color=#ff5a5a][bold]● Responder:[/bold] {$name}[/color]
governance-ahelp-player-empty = [color=#8c96a8]You do not have an active ticket yet. Send a message below to create one.[/color]
governance-ahelp-player-status-new = New ticket
governance-ahelp-player-status-open = In queue
governance-ahelp-player-status-claimed = In progress
governance-ahelp-player-status-waiting = Waiting for your reply
governance-ahelp-player-status-escalated = Escalated to incident
governance-ahelp-player-status-court = Referred to Community Court

governance-ahelp-records-heading = [bold]Responder tools[/bold]
governance-ahelp-records-target-placeholder = Player name or SS14 UUID for notes…
governance-ahelp-records-open-notes = Player notes
governance-ahelp-records-open-logs = Full logs
governance-ahelp-records-access-denied = Full moderation records are available only to an active duty responder.
governance-ahelp-notes-target-required = Enter a player name or SS14 UUID whose notes should be opened.
governance-ahelp-notes-target-not-found = No player with that name or SS14 UUID was found in the database.
governance-ahelp-context-heading = [bold]Investigation[/bold]
governance-ahelp-tool-full-logs = Full logs
governance-ahelp-tool-reporter-notes = Reporter notes
governance-ahelp-tool-target-notes = Target notes
governance-duty-verb-notes = Player notes
governance-duty-verb-teleport-to = Teleport to player

governance-ahelp-incident-heading = [bold]Incident[/bold]
governance-ahelp-incident-none = [color=#8c96a8]No active incident has been created for this ticket.[/color]
governance-ahelp-incident-active = [bold]LiveIncident #{$id}[/bold] • target: {$target} • type: {$type}
governance-ahelp-incident-active-character = [bold]LiveIncident #{$id}[/bold] • Account: {$target} • Character: [bold]{$character}[/bold] • Type: {$type}
governance-ahelp-incident-court = [color=#d8a34a][bold]LiveIncident #{$incident} → Community Court #{$case}[/bold][/color] • Account: {$target} • Character: [bold]{$character}[/bold]
governance-ahelp-incident-target-placeholder = Player name or SS14 UUID
governance-ahelp-incident-type-placeholder = Incident type
governance-ahelp-incident-type-default = rules violation
governance-ahelp-incident-create = Create incident
governance-ahelp-incident-target-required = Specify the player targeted by the incident.
governance-ahelp-incident-target-not-found = No player with that name or SS14 UUID is currently available on the server.
governance-ahelp-incident-self-target = A responder cannot create an incident against themselves.
governance-ahelp-incident-type-invalid = Incident type must be between 2 and 64 characters.
governance-ahelp-incident-access-denied = You do not have the temporary capability required to create a live incident.
governance-ahelp-incident-create-failed = Could not create the incident. Make sure the ticket is still assigned to you.

governance-ahelp-court-none = [color=#8c96a8]No Community Court case has been created for this incident.[/color]
governance-ahelp-court-active = [color=#d8a34a][bold]Referred to Community Court • case #{$id}[/bold][/color]
governance-ahelp-court-escalate = Refer to Community Court
governance-ahelp-court-reason-invalid = Enter a court referral reason between 10 and 1500 characters.
governance-ahelp-court-access-denied = Only the active responder handling this ticket may refer the incident to court.
governance-ahelp-court-create-failed = Could not create the Community Court case. Check the incident and database state.

governance-ahelp-actions-heading = [bold]Incident actions[/bold]
governance-ahelp-containment-heading = [bold]Live containment[/bold]
governance-ahelp-action-reason-placeholder = Action / court referral reason…
governance-ahelp-action-freeze-seconds-placeholder = Sec.
governance-ahelp-action-request-explanation = Request explanation
governance-ahelp-action-view-logs = View logs
governance-ahelp-action-freeze = Freeze for 60s
governance-ahelp-action-round-remove = Remove for round
governance-ahelp-action-round-remove-short = Remove from round
governance-ahelp-action-history-heading = [bold]Intervention history[/bold]
governance-ahelp-action-history-empty = [color=#8c96a8]No live interventions have been created for this incident.[/color]
governance-ahelp-action-card = #{$id} • [bold]{$type}[/bold] • {$status} • {$approvals}/{$required}{$duration} • {$reason}
governance-ahelp-action-duration =  • {$seconds}s
governance-ahelp-action-type-explanation = Explanation request
governance-ahelp-action-type-logs = Log access
governance-ahelp-action-type-freeze = Freeze
governance-ahelp-action-type-round-remove = Round removal
governance-ahelp-action-status-proposed = [color=#ffd166]waiting for approval[/color]
governance-ahelp-action-status-approved = [color=#72d572]approved[/color]
governance-ahelp-action-status-executed = [color=#72d572]executed[/color]
governance-ahelp-action-status-rejected = [color=#ff5a5a]rejected[/color]
governance-ahelp-action-status-expired = expired

governance-ahelp-approval-heading = [bold]Requires second decision[/bold]
governance-ahelp-approval-empty = [color=#8c96a8]No actions currently require a second vote.[/color]
governance-ahelp-approval-card = Action #{$id} • incident #{$incident} • {$actor} → {$target} • [bold]{$type}[/bold] • {$approvals}/{$required} • {$reason}
governance-ahelp-approval-approve = Approve
governance-ahelp-approval-reject = Reject

governance-ahelp-logs-heading = [bold]Target logs[/bold]
governance-ahelp-logs-empty = [color=#8c96a8]Logs are not loaded.[/color]
governance-ahelp-log-line = [color=#8c96a8]{$time}[/color] [bold]{$type}[/bold] {$message}

governance-ahelp-action-access-denied = You do not have the temporary capability required for this action.
governance-ahelp-action-no-incident = Create an incident for this ticket first.
governance-ahelp-action-invalid = Unknown incident moderation action.
governance-ahelp-action-reason-invalid = Enter a reason between 10 and 512 characters.
governance-ahelp-action-freeze-duration-invalid = Freeze duration must be between 1 and 120 seconds.
governance-ahelp-action-create-failed = Could not create the moderation action.
governance-ahelp-action-target-unavailable = The action target or its author is currently unavailable on the server.
governance-ahelp-action-execution-failed = The action was approved, but the server could not execute it. Check target state and capabilities.
governance-ahelp-action-review-failed = Could not record the decision. You may be ineligible to vote or the action may already be resolved.
governance-ahelp-action-court-escalated = This incident has already been referred to Community Court; new live actions are disabled.
