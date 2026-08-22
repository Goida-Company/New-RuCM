using System.Text.Json;
using Content.Server.Database;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed record ModerationActionOutcome(long ActionId, string Status, short Approvals, short RequiredApprovals);
public sealed record GovernanceAHelpReporter(string Name, ulong? DiscordId);

public sealed class ModerationGovernanceService(
    Func<GovernanceDbContext> governanceFactory,
    Func<ServerDbContext> gameFactory,
    GovernanceCommunityService community)
{
    public async Task<GovernanceAHelpTicket> GetAHelpAsync(long ticketId)
    {
        await using var governance = governanceFactory();
        return await governance.AHelpTickets.AsNoTracking().SingleOrDefaultAsync(value => value.Id == ticketId)
            ?? throw new CourtRuleException("AHelp не найден.");
    }

    public async Task AttachThreadAsync(long ticketId, ulong threadId)
    {
        await using var governance = governanceFactory();
        var ticket = await governance.AHelpTickets.SingleAsync(value => value.Id == ticketId);
        if (ticket.DiscordThreadId != null && ticket.DiscordThreadId != checked((long) threadId))
            throw new CourtRuleException("К AHelp уже привязан другой Discord-тред.");
        ticket.DiscordThreadId = checked((long) threadId);
        ticket.UpdatedAt = DateTime.UtcNow;
        await governance.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<GovernanceAHelpTicket>> AHelpsWithoutThreadsAsync()
    {
        await using var governance = governanceFactory();
        return await governance.AHelpTickets.AsNoTracking()
            .Where(value => value.DiscordThreadId == null && value.Status != "resolved")
            .OrderBy(value => value.Id)
            .ToListAsync();
    }

    public async Task<GovernanceAHelpReporter> GetReporterAsync(GovernanceAHelpTicket ticket)
    {
        await using var game = gameFactory();
        return await game.Player.AsNoTracking()
            .Where(value => value.UserId == ticket.ReporterSs14UserId)
            .Select(value => new GovernanceAHelpReporter(
                value.LastSeenUserName,
                value.LinkedAccount == null ? null : (ulong?) value.LinkedAccount.DiscordId))
            .SingleAsync();
    }

    public async Task<GovernanceAHelpTicket> CreateAHelpAsync(ulong reporterDiscordId, ulong? targetDiscordId,
        int roundId, string summary)
    {
        summary = summary.Trim();
        if (summary.Length is < 20 or > 1500)
            throw new CourtRuleException("Описание AHelp должно содержать от 20 до 1500 символов.");
        var reporter = await community.RequireUserAsync(reporterDiscordId);
        GovernanceUser? target = targetDiscordId == null ? null : await community.RequireUserAsync(targetDiscordId.Value);
        await using (var game = gameFactory())
        {
            if (!await game.Round.AsNoTracking().AnyAsync(value => value.Id == roundId))
                throw new CourtRuleException("Раунд не найден.");
        }
        await using var governance = governanceFactory();
        var activeStatuses = new[] { "open", "claimed", "waiting_player", "escalated_to_incident" };
        if (await governance.AHelpTickets.AsNoTracking().AnyAsync(value =>
                value.RoundId == roundId && value.ReporterSs14UserId == reporter.Ss14UserId &&
                activeStatuses.Contains(value.Status)))
            throw new CourtRuleException("У вас уже есть активный AHelp в этом раунде.");

        await using var transaction = await governance.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var ticket = governance.AHelpTickets.Add(new GovernanceAHelpTicket
        {
            RoundId = roundId, ReporterUserId = reporter.Id, ReporterSs14UserId = reporter.Ss14UserId,
            TargetUserId = target?.Id,
            Status = "open", Summary = summary, CreatedAt = now, UpdatedAt = now,
        }).Entity;
        await governance.SaveChangesAsync();
        await governance.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO governance.ahelp_messages(ticket_id, sender_ss14_user_id, body)
            VALUES ({ticket.Id}, {reporter.Ss14UserId}, {summary})
            """);
        AddAudit(governance, "ahelp.created", reporterDiscordId, "ahelp_ticket", ticket.Id.ToString(),
            new { round_id = roundId, target_user_id = target?.Id });
        await governance.SaveChangesAsync();
        await transaction.CommitAsync();
        return ticket;
    }

    public async Task SetAHelpStatusAsync(long ticketId, ulong responderDiscordId, string status)
    {
        if (status is not ("waiting_player" or "resolved" or "open"))
            throw new CourtRuleException("Допустимые состояния: open, waiting_player, resolved.");
        var responder = await community.RequireUserAsync(responderDiscordId);
        await using var governance = governanceFactory();
        var ticket = await governance.AHelpTickets.SingleOrDefaultAsync(value => value.Id == ticketId)
            ?? throw new CourtRuleException("AHelp не найден.");
        await RequireDutyCapabilityAsync(governance, responder.Id, ticket.RoundId, "moderation.ahelp");
        if (ticket.ClaimedByUserId != responder.Id)
            throw new CourtRuleException("Изменять состояние может взявший AHelp дежурный.");
        ticket.Status = status;
        ticket.UpdatedAt = DateTime.UtcNow;
        AddAudit(governance, "ahelp.status_changed", responderDiscordId, "ahelp_ticket", ticket.Id.ToString(), new { status });
        await governance.SaveChangesAsync();
    }

    public async Task<GovernanceLiveIncident> EscalateToIncidentAsync(long ticketId, ulong responderDiscordId, string type)
    {
        var responder = await community.RequireUserAsync(responderDiscordId);
        await using var governance = governanceFactory();
        var ticket = await governance.AHelpTickets.SingleOrDefaultAsync(value => value.Id == ticketId)
            ?? throw new CourtRuleException("AHelp не найден.");
        await RequireDutyCapabilityAsync(governance, responder.Id, ticket.RoundId, "moderation.freeze");
        if (ticket.ClaimedByUserId != responder.Id || ticket.TargetUserId == null)
            throw new CourtRuleException("Нужен взятый вами AHelp с указанной целью.");
        if (ticket.Status == "escalated_to_incident")
            return await governance.LiveIncidents.SingleAsync(value => value.ReporterUserId == ticket.ReporterUserId &&
                value.RoundId == ticket.RoundId && value.TargetUserId == ticket.TargetUserId && value.Status == "active");
        var incident = governance.LiveIncidents.Add(new GovernanceLiveIncident
        {
            RoundId = ticket.RoundId, TargetUserId = ticket.TargetUserId.Value, ReporterUserId = ticket.ReporterUserId,
            CreatedByUserId = responder.Id, Type = type.Trim(), Summary = ticket.Summary,
            Status = "active", CreatedAt = DateTime.UtcNow,
        }).Entity;
        ticket.Status = "escalated_to_incident";
        ticket.UpdatedAt = DateTime.UtcNow;
        await governance.SaveChangesAsync();
        AddAudit(governance, "incident.created", responderDiscordId, "live_incident", incident.Id.ToString(), new { ticket_id = ticketId });
        await governance.SaveChangesAsync();
        return incident;
    }

    public async Task<ModerationActionOutcome> ProposeActionAsync(long incidentId, ulong actorDiscordId,
        string actionType, string reason, int? durationSeconds)
    {
        if (actionType is not ("freeze" or "round_remove" or "request_explanation" or "view_logs"))
            throw new CourtRuleException("Неизвестное действие модерации.");
        if (reason.Trim().Length is < 20 or > 1500)
            throw new CourtRuleException("Причина должна содержать от 20 до 1500 символов.");
        if (actionType == "freeze" && durationSeconds is < 10 or > 600)
            throw new CourtRuleException("Заморозка может длиться от 10 до 600 секунд.");
        var actor = await community.RequireUserAsync(actorDiscordId);
        await using var governance = governanceFactory();
        var incident = await governance.LiveIncidents.SingleOrDefaultAsync(value => value.Id == incidentId)
            ?? throw new CourtRuleException("Инцидент не найден.");
        if (incident.Status != "active")
            throw new CourtRuleException("Инцидент уже закрыт.");
        var capability = $"moderation.{actionType}";
        await RequireDutyCapabilityAsync(governance, actor.Id, incident.RoundId, capability);
        var required = ModerationQuorum.RequiredApprovals(actionType);
        var now = DateTime.UtcNow;
        var action = governance.ModerationActions.Add(new GovernanceModerationAction
        {
            IncidentId = incidentId, ActorUserId = actor.Id, TargetUserId = incident.TargetUserId,
            ActionType = actionType, Reason = reason.Trim(), DurationSeconds = durationSeconds,
            Status = required == 1 ? "approved" : "proposed", RequiredApprovals = required,
            CreatedAt = now, IdempotencyKey = $"incident:{incidentId}:{actionType}:{Guid.NewGuid()}",
        }).Entity;
        await governance.SaveChangesAsync();
        governance.ModerationApprovals.Add(new GovernanceModerationApproval
        {
            ActionId = action.Id, ApproverUserId = actor.Id, Decision = "approve", CreatedAt = now,
        });
        AddAudit(governance, "moderation.action_proposed", actorDiscordId, "moderation_action", action.Id.ToString(),
            new { incident_id = incidentId, action_type = actionType, required_approvals = required });
        await governance.SaveChangesAsync();
        return new ModerationActionOutcome(action.Id, action.Status, 1, required);
    }

    public async Task<ModerationActionOutcome> ReviewActionAsync(long actionId, ulong approverDiscordId, string decision)
    {
        if (decision is not ("approve" or "reject" or "more_information"))
            throw new CourtRuleException("Неизвестное решение.");
        var approver = await community.RequireUserAsync(approverDiscordId);
        await using var governance = governanceFactory();
        var action = await governance.ModerationActions.SingleOrDefaultAsync(value => value.Id == actionId)
            ?? throw new CourtRuleException("Действие не найдено.");
        var incident = await governance.LiveIncidents.SingleAsync(value => value.Id == action.IncidentId);
        await RequireDutyCapabilityAsync(governance, approver.Id, incident.RoundId, $"moderation.{action.ActionType}");
        if (action.Status is "executed" or "rejected" or "expired")
            throw new CourtRuleException("Рассмотрение этого действия завершено.");
        if (await governance.ModerationApprovals.AnyAsync(value => value.ActionId == actionId && value.ApproverUserId == approver.Id))
            throw new CourtRuleException("Ваше решение уже зафиксировано.");
        governance.ModerationApprovals.Add(new GovernanceModerationApproval
        {
            ActionId = actionId, ApproverUserId = approver.Id, Decision = decision, CreatedAt = DateTime.UtcNow,
        });
        await governance.SaveChangesAsync();
        if (decision == "reject")
            action.Status = "rejected";
        else if (decision == "approve")
        {
            var count = await governance.ModerationApprovals.CountAsync(value => value.ActionId == actionId && value.Decision == "approve");
            if (count >= action.RequiredApprovals)
                action.Status = "approved";
        }
        AddAudit(governance, "moderation.action_reviewed", approverDiscordId, "moderation_action", action.Id.ToString(), new { decision });
        await governance.SaveChangesAsync();
        var approvals = checked((short) await governance.ModerationApprovals.CountAsync(value => value.ActionId == actionId && value.Decision == "approve"));
        return new ModerationActionOutcome(action.Id, action.Status, approvals, action.RequiredApprovals);
    }

    public async Task CloseIncidentAsync(long incidentId, ulong actorDiscordId)
    {
        var actor = await community.RequireUserAsync(actorDiscordId);
        await using var governance = governanceFactory();
        var incident = await governance.LiveIncidents.SingleOrDefaultAsync(value => value.Id == incidentId)
            ?? throw new CourtRuleException("Инцидент не найден.");
        await RequireDutyCapabilityAsync(governance, actor.Id, incident.RoundId, "moderation.freeze");
        incident.Status = "closed";
        incident.ClosedAt = DateTime.UtcNow;
        AddAudit(governance, "incident.closed", actorDiscordId, "live_incident", incident.Id.ToString(), new { });
        await governance.SaveChangesAsync();
    }

    private static async Task RequireDutyCapabilityAsync(GovernanceDbContext governance, Guid userId, int roundId, string capability)
    {
        var now = DateTime.UtcNow;
        var authorized = await governance.DutySessions.AsNoTracking()
            .Where(value => value.UserId == userId && value.RoundId == roundId && value.Status == "active" &&
                            value.ObserverConfirmed && value.ExpiresAt > now)
            .AnyAsync(duty => governance.CapabilityGrants.Any(grant => grant.UserId == userId &&
                grant.SourceType == "duty_session" && grant.SourceId == duty.Id.ToString() &&
                grant.Capability == capability && grant.ExpiresAt > now && grant.RevokedAt == null));
        if (!authorized)
            throw new CourtRuleException($"Нет активного временного полномочия `{capability}` для этого раунда.");
    }

    private static void AddAudit(GovernanceDbContext db, string eventType, ulong actorDiscordId, string entityType, string entityId, object payload)
    {
        db.AuditEvents.Add(new GovernanceAuditEvent
        {
            EventType = eventType, ActorType = "discord_user", ActorId = actorDiscordId.ToString(),
            EntityType = entityType, EntityId = entityId, CreatedAt = DateTime.UtcNow,
            Payload = JsonSerializer.Serialize(payload),
        });
    }
}

public static class ModerationQuorum
{
    public static short RequiredApprovals(string actionType) => actionType switch
    {
        "round_remove" => 2,
        "freeze" or "request_explanation" or "view_logs" => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(actionType)),
    };
}
