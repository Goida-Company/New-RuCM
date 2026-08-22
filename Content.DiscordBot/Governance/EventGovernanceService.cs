using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed record EventManifestRequest(string Capability, string Resource, int MaxUses);
public sealed record EventReviewOutcome(long ProposalId, string Status, int Approvals, int Rejections);

public sealed class EventGovernanceService(
    Func<GovernanceDbContext> governanceFactory,
    GovernanceCommunityService community,
    CandidateSelectionService selection,
    Config config)
{
    public async Task<IReadOnlyList<(GovernanceInvitation Invitation, GovernanceUser User, GovernanceEventProposal Proposal)>> PendingReviewNotificationsAsync()
    {
        if (!config.EventEnabled)
            return [];

        await using var governance = governanceFactory();
        var rows = await governance.Invitations.AsNoTracking()
            .Where(value => value.Purpose == "event_review" && value.State == InvitationStates.Pending &&
                            value.DiscordNotifiedAt == null && value.ExpiresAt > DateTime.UtcNow)
            .Join(governance.Users, invitation => invitation.UserId, user => user.Id, (invitation, user) => new { invitation, user })
            .Join(governance.EventProposals, row => row.invitation.EntityId, proposal => proposal.Id.ToString(),
                (row, proposal) => new { row.invitation, row.user, proposal })
            .ToListAsync();
        return rows.Select(value => (value.invitation, value.user, value.proposal)).ToArray();
    }

    public async Task MarkInvitationNotifiedAsync(long invitationId)
    {
        if (!config.EventEnabled)
            return;

        await using var governance = governanceFactory();
        var invitation = await governance.Invitations.SingleAsync(value => value.Id == invitationId);
        invitation.DiscordNotifiedAt = DateTime.UtcNow;
        await governance.SaveChangesAsync();
    }

    public async Task<GovernanceEventProposal> GetProposalAsync(long proposalId)
    {
        await using var governance = governanceFactory();
        return await governance.EventProposals.AsNoTracking().SingleOrDefaultAsync(value => value.Id == proposalId)
            ?? throw new CourtRuleException("Заявка события не найдена.");
    }

    public async Task AttachThreadAsync(long proposalId, ulong threadId)
    {
        RequireEnabled();
        await using var governance = governanceFactory();
        var proposal = await governance.EventProposals.SingleAsync(value => value.Id == proposalId);
        if (proposal.DiscordThreadId != null && proposal.DiscordThreadId != checked((long) threadId))
            throw new CourtRuleException("К заявке уже привязан другой Discord-тред.");
        proposal.DiscordThreadId = checked((long) threadId);
        await governance.SaveChangesAsync();
    }

    public async Task<GovernanceEventProposal> ProposeAsync(ulong ownerDiscordId, string title, string description,
        int durationMinutes, string manifestText)
    {
        RequireEnabled();
        title = title.Trim();
        description = description.Trim();
        if (title.Length is < 5 or > 100 || description.Length is < 30 or > 3000)
            throw new CourtRuleException("Название: 5–100 символов; описание: 30–3000 символов.");
        if (durationMinutes is < 10 or > 480)
            throw new CourtRuleException("Продолжительность события — от 10 до 480 минут.");
        var manifest = ParseManifest(manifestText);
        var owner = await community.RequireUserAsync(ownerDiscordId);
        await using var governance = governanceFactory();
        var now = DateTime.UtcNow;
        var proposal = governance.EventProposals.Add(new GovernanceEventProposal
        {
            OwnerUserId = owner.Id,
            Title = title,
            Description = description,
            DurationMinutes = durationMinutes,
            Manifest = JsonSerializer.Serialize(manifest),
            Status = "review",
            CreatedAt = now,
            ReviewDeadline = now.AddHours(config.EventReviewHours),
        }).Entity;
        await governance.SaveChangesAsync();
        AddAudit(governance, "event.proposal_created", ownerDiscordId, "event_proposal", proposal.Id.ToString(),
            new { manifest });
        await governance.SaveChangesAsync();
        await EnsureReviewerInvitationsAsync(proposal.Id);
        return proposal;
    }

    public async Task<string> RespondToReviewInvitationAsync(
        long proposalId,
        ulong reviewerDiscordId,
        string response,
        string? recusalReason)
    {
        RequireEnabled();
        if (response is not (InvitationStates.Accepted or InvitationStates.Declined or InvitationStates.Recused))
            throw new CourtRuleException("Неизвестный ответ на приглашение.");
        if (response == InvitationStates.Recused && string.IsNullOrWhiteSpace(recusalReason))
            throw new CourtRuleException("Для самоотвода нужно указать причину.");

        var reviewer = await community.RequireUserAsync(reviewerDiscordId);
        await using var governance = governanceFactory();
        var proposal = await governance.EventProposals.SingleOrDefaultAsync(value => value.Id == proposalId)
            ?? throw new CourtRuleException("Заявка события не найдена.");
        if (proposal.Status != "review" || proposal.ReviewDeadline <= DateTime.UtcNow)
            throw new CourtRuleException("Рецензирование этой заявки уже завершено.");

        var invitation = await governance.Invitations.SingleOrDefaultAsync(value =>
            value.UserId == reviewer.Id && value.EntityType == "event_proposal" &&
            value.EntityId == proposalId.ToString() && value.Purpose == "event_review")
            ?? throw new CourtRuleException("У вас нет приглашения на рецензирование этой заявки.");
        if (invitation.State != InvitationStates.Pending)
        {
            if (invitation.State == response)
                return invitation.State;
            throw new CourtRuleException("Ответ на приглашение уже зафиксирован.");
        }

        var now = DateTime.UtcNow;
        if (invitation.ExpiresAt <= now)
        {
            invitation.State = InvitationStates.Expired;
            invitation.RespondedAt = now;
            invitation.Version++;
            AddAudit(governance, "event.review_invitation.expired", 0, "event_proposal", proposalId.ToString(),
                new { invitation_id = invitation.Id, reviewer_user_id = reviewer.Id });
            await governance.SaveChangesAsync();
            await EnsureReviewerInvitationsAsync(proposalId);
            throw new CourtRuleException("Срок ответа на приглашение истёк.");
        }

        invitation.State = response;
        invitation.RespondedAt = now;
        invitation.RecusalReason = response == InvitationStates.Recused ? recusalReason!.Trim() : null;
        invitation.Version++;

        if (response == InvitationStates.Accepted)
        {
            invitation.ExpiresAt = proposal.ReviewDeadline;
            if (!await governance.ServiceAssignments.AnyAsync(value => value.UserId == reviewer.Id && value.Track == "event" &&
                    value.EntityType == "event_proposal" && value.EntityId == proposalId.ToString()))
            {
                governance.ServiceAssignments.Add(new GovernanceServiceAssignment
                {
                    UserId = reviewer.Id,
                    Track = "event",
                    EntityType = "event_proposal",
                    EntityId = proposalId.ToString(),
                    AssignedAt = now,
                });
            }
        }

        AddAudit(governance, $"event.review_invitation.{response}", reviewerDiscordId,
            "event_proposal", proposalId.ToString(), new { invitation_id = invitation.Id, recusal_reason = invitation.RecusalReason });
        await governance.SaveChangesAsync();

        if (response is InvitationStates.Declined or InvitationStates.Recused)
            await EnsureReviewerInvitationsAsync(proposalId);
        return response;
    }

    public async Task<EventReviewOutcome> ReviewAsync(long proposalId, ulong reviewerDiscordId, string decision, string reasoning)
    {
        RequireEnabled();
        if (decision is not ("approve" or "reject"))
            throw new CourtRuleException("Решение должно быть approve или reject.");
        if (reasoning.Trim().Length is < 20 or > 1500)
            throw new CourtRuleException("Обоснование должно содержать от 20 до 1500 символов.");
        var reviewer = await community.RequireUserAsync(reviewerDiscordId);
        await using var governance = governanceFactory();
        var proposal = await governance.EventProposals.SingleOrDefaultAsync(value => value.Id == proposalId)
            ?? throw new CourtRuleException("Заявка события не найдена.");
        if (proposal.Status != "review" || proposal.ReviewDeadline <= DateTime.UtcNow)
            throw new CourtRuleException("Рецензирование этой заявки завершено.");
        var invitation = await governance.Invitations.AsNoTracking().SingleOrDefaultAsync(value =>
            value.UserId == reviewer.Id && value.EntityType == "event_proposal" && value.EntityId == proposalId.ToString() &&
            value.Purpose == "event_review");
        if (invitation == null || invitation.State != InvitationStates.Accepted || invitation.ExpiresAt <= DateTime.UtcNow)
            throw new CourtRuleException("Сначала примите действующее приглашение на рецензирование.");
        if (!await governance.ServiceAssignments.AnyAsync(value => value.UserId == reviewer.Id && value.Track == "event" &&
                value.EntityType == "event_proposal" && value.EntityId == proposalId.ToString()))
            throw new CourtRuleException("Вы не назначены независимым рецензентом этой заявки.");
        if (await governance.EventReviews.AnyAsync(value => value.ProposalId == proposalId && value.ReviewerUserId == reviewer.Id))
            throw new CourtRuleException("Ваша рецензия уже принята.");

        governance.EventReviews.Add(new GovernanceEventReview
        {
            ProposalId = proposalId,
            ReviewerUserId = reviewer.Id,
            Decision = decision,
            Reasoning = reasoning.Trim(),
            SubmittedAt = DateTime.UtcNow,
        });
        var assignment = await governance.ServiceAssignments.SingleAsync(value => value.UserId == reviewer.Id &&
            value.Track == "event" && value.EntityType == "event_proposal" && value.EntityId == proposalId.ToString());
        assignment.CompletedAt = DateTime.UtcNow;
        await governance.SaveChangesAsync();

        var approvals = await governance.EventReviews.CountAsync(value => value.ProposalId == proposalId && value.Decision == "approve");
        var rejections = await governance.EventReviews.CountAsync(value => value.ProposalId == proposalId && value.Decision == "reject");
        if (approvals >= config.EventApprovalThreshold)
            proposal.Status = "approved";
        else if (rejections > config.EventReviewers - config.EventApprovalThreshold)
            proposal.Status = "rejected";
        AddAudit(governance, "event.review_submitted", reviewerDiscordId, "event_proposal", proposalId.ToString(),
            new { decision, approvals, rejections, status = proposal.Status });
        await governance.SaveChangesAsync();
        return new EventReviewOutcome(proposalId, proposal.Status, approvals, rejections);
    }

    public async Task<GovernanceEventSession> StartAsync(long proposalId, ulong directorDiscordId, int roundId)
    {
        RequireEnabled();
        var director = await community.RequireUserAsync(directorDiscordId);
        await using var governance = governanceFactory();
        var proposal = await governance.EventProposals.SingleOrDefaultAsync(value => value.Id == proposalId)
            ?? throw new CourtRuleException("Заявка события не найдена.");
        if (proposal.OwnerUserId != director.Id)
            throw new CourtRuleException("Запустить одобренное событие может только его автор-директор.");
        if (proposal.Status != "approved")
            throw new CourtRuleException("Событие ещё не одобрено либо уже запущено.");
        if (await governance.EventSessions.AnyAsync(value => value.ProposalId == proposalId))
            throw new CourtRuleException("Для этой заявки сессия уже создана.");
        var manifest = JsonSerializer.Deserialize<EventManifestRequest[]>(proposal.Manifest) ?? [];
        var now = DateTime.UtcNow;
        var session = governance.EventSessions.Add(new GovernanceEventSession
        {
            ProposalId = proposalId,
            DirectorUserId = director.Id,
            RoundId = roundId,
            Status = "active",
            StartedAt = now,
            ExpiresAt = now.AddMinutes(proposal.DurationMinutes),
        }).Entity;
        proposal.Status = "active";
        await governance.SaveChangesAsync();
        foreach (var item in manifest)
        {
            governance.EventManifestItems.Add(new GovernanceEventManifestItem
            {
                SessionId = session.Id,
                Capability = item.Capability,
                Resource = item.Resource,
                MaxUses = item.MaxUses,
            });
        }
        foreach (var capability in manifest.Select(value => value.Capability).Distinct())
        {
            governance.CapabilityGrants.Add(new GovernanceCapabilityGrant
            {
                UserId = director.Id,
                Capability = capability,
                SourceType = "event_session",
                SourceId = session.Id.ToString(),
                Scope = JsonSerializer.Serialize(new { round_id = roundId, event_session_id = session.Id }),
                IssuedAt = now,
                ExpiresAt = session.ExpiresAt,
                IdempotencyKey = $"event-session:{session.Id}:{capability}",
            });
        }
        AddAudit(governance, "event.session_started", directorDiscordId, "event_session", session.Id.ToString(),
            new { proposal_id = proposalId, round_id = roundId, manifest });
        await governance.SaveChangesAsync();
        return session;
    }

    public async Task<GovernanceEventAction> RecordActionAsync(long sessionId, ulong actorDiscordId,
        string capability, string resource, string? payload)
    {
        RequireEnabled();
        var actor = await community.RequireUserAsync(actorDiscordId);
        await using var governance = governanceFactory();
        var session = await governance.EventSessions.SingleOrDefaultAsync(value => value.Id == sessionId)
            ?? throw new CourtRuleException("Сессия события не найдена.");
        if (session.DirectorUserId != actor.Id || session.Status != "active" || session.ExpiresAt <= DateTime.UtcNow)
            throw new CourtRuleException("Нет активных полномочий директора этой сессии.");
        var item = await governance.EventManifestItems.SingleOrDefaultAsync(value => value.SessionId == sessionId &&
            value.Capability == capability && value.Resource == resource);
        var allowed = item != null && item.UsedCount < item.MaxUses && await governance.CapabilityGrants.AnyAsync(value =>
            value.UserId == actor.Id && value.SourceType == "event_session" && value.SourceId == sessionId.ToString() &&
            value.Capability == capability && value.RevokedAt == null && value.ExpiresAt > DateTime.UtcNow);
        var action = governance.EventActions.Add(new GovernanceEventAction
        {
            SessionId = sessionId,
            ActorUserId = actor.Id,
            Capability = capability,
            Resource = resource,
            Status = allowed ? "executed" : "denied",
            CreatedAt = DateTime.UtcNow,
            Payload = NormalizeJson(payload),
        }).Entity;
        if (allowed)
            item!.UsedCount++;
        AddAudit(governance, allowed ? "event.action_executed" : "event.action_denied", actorDiscordId,
            "event_session", sessionId.ToString(), new { capability, resource });
        await governance.SaveChangesAsync();
        if (!allowed)
            throw new CourtRuleException("Действие отсутствует в манифесте, лимит исчерпан или полномочие истекло.");
        return action;
    }

    public async Task EndAsync(long sessionId, ulong directorDiscordId, bool aborted)
    {
        RequireEnabled();
        var director = await community.RequireUserAsync(directorDiscordId);
        await using var governance = governanceFactory();
        var session = await governance.EventSessions.SingleOrDefaultAsync(value => value.Id == sessionId)
            ?? throw new CourtRuleException("Сессия события не найдена.");
        if (session.DirectorUserId != director.Id || session.Status != "active")
            throw new CourtRuleException("Завершить может только действующий директор.");
        var now = DateTime.UtcNow;
        session.Status = aborted ? "aborted" : "completed";
        session.EndedAt = now;
        var proposal = await governance.EventProposals.SingleAsync(value => value.Id == session.ProposalId);
        proposal.Status = session.Status;
        foreach (var grant in await governance.CapabilityGrants.Where(value => value.SourceType == "event_session" &&
                     value.SourceId == sessionId.ToString() && value.RevokedAt == null).ToListAsync())
            grant.RevokedAt = now;
        AddAudit(governance, "event.session_ended", directorDiscordId, "event_session", sessionId.ToString(), new { status = session.Status });
        await governance.SaveChangesAsync();
    }

    public async Task ProcessDeadlinesAsync()
    {
        if (!config.EventEnabled)
            return;

        var now = DateTime.UtcNow;
        List<long> proposalsToRefill;
        await using (var governance = governanceFactory())
        {
            var expiredInvitations = await governance.Invitations
                .Where(value => value.Purpose == "event_review" && value.ExpiresAt <= now &&
                                (value.State == InvitationStates.Pending || value.State == InvitationStates.Accepted))
                .ToListAsync();
            foreach (var invitation in expiredInvitations)
            {
                var proposalId = long.Parse(invitation.EntityId);
                if (invitation.State == InvitationStates.Pending)
                {
                    invitation.State = InvitationStates.Expired;
                    invitation.RespondedAt = now;
                    invitation.Version++;
                    AddAudit(governance, "event.review_invitation.expired", 0, "event_proposal", proposalId.ToString(),
                        new { invitation_id = invitation.Id, reviewer_user_id = invitation.UserId });
                }
                else
                {
                    var completed = await governance.EventReviews.AsNoTracking().AnyAsync(value =>
                        value.ProposalId == proposalId && value.ReviewerUserId == invitation.UserId);
                    if (completed)
                        continue;
                    invitation.State = InvitationStates.Failed;
                    invitation.Version++;
                    var assignment = await governance.ServiceAssignments.SingleOrDefaultAsync(value =>
                        value.UserId == invitation.UserId && value.Track == "event" &&
                        value.EntityType == "event_proposal" && value.EntityId == proposalId.ToString());
                    if (assignment != null && assignment.CompletedAt == null && assignment.FailedAt == null)
                        assignment.FailedAt = now;
                    AddAudit(governance, "event.review_failed", 0, "event_proposal", proposalId.ToString(),
                        new { invitation_id = invitation.Id, reviewer_user_id = invitation.UserId });
                }
            }

            foreach (var proposal in await governance.EventProposals
                         .Where(value => value.Status == "review" && value.ReviewDeadline <= now)
                         .ToListAsync())
            {
                proposal.Status = "rejected";
                AddAudit(governance, "event.review_deadline_rejected", 0, "event_proposal", proposal.Id.ToString(), new { });
            }

            foreach (var session in await governance.EventSessions
                         .Where(value => value.Status == "active" && value.ExpiresAt <= now)
                         .ToListAsync())
            {
                session.Status = "completed";
                session.EndedAt = now;
                var proposal = await governance.EventProposals.SingleAsync(value => value.Id == session.ProposalId);
                proposal.Status = "completed";
                foreach (var grant in await governance.CapabilityGrants.Where(value => value.SourceType == "event_session" &&
                             value.SourceId == session.Id.ToString() && value.RevokedAt == null).ToListAsync())
                    grant.RevokedAt = now;
            }

            proposalsToRefill = await governance.EventProposals.AsNoTracking()
                .Where(value => value.Status == "review" && value.ReviewDeadline > now)
                .Select(value => value.Id)
                .ToListAsync();
            await governance.SaveChangesAsync();
        }

        foreach (var proposalId in proposalsToRefill)
            await EnsureReviewerInvitationsAsync(proposalId);
    }

    private async Task EnsureReviewerInvitationsAsync(long proposalId)
    {
        if (!config.EventEnabled)
            return;

        await using var governance = governanceFactory();
        var proposal = await governance.EventProposals.AsNoTracking().SingleOrDefaultAsync(value => value.Id == proposalId)
            ?? throw new CourtRuleException("Заявка события не найдена.");
        var now = DateTime.UtcNow;
        if (proposal.Status != "review" || proposal.ReviewDeadline <= now)
            return;

        var invitations = await governance.Invitations.AsNoTracking()
            .Where(value => value.EntityType == "event_proposal" && value.EntityId == proposalId.ToString() &&
                            value.Purpose == "event_review")
            .ToListAsync();
        var reviewedUsers = await governance.EventReviews.AsNoTracking()
            .Where(value => value.ProposalId == proposalId)
            .Select(value => value.ReviewerUserId)
            .ToListAsync();
        var activeInvitedUsers = invitations
            .Where(value => value.State is InvitationStates.Pending or InvitationStates.Accepted)
            .Select(value => value.UserId)
            .Concat(reviewedUsers)
            .Distinct()
            .ToHashSet();
        var needed = Math.Max(0, config.EventReviewers - activeInvitedUsers.Count);
        if (needed == 0)
            return;

        var excluded = invitations.Select(value => value.UserId)
            .Concat(reviewedUsers)
            .Append(proposal.OwnerUserId)
            .ToHashSet();
        var reviewers = await selection.SelectAsync("event", 1, "event_proposal", proposalId.ToString(),
            needed, excluded, null, TimeSpan.FromHours(config.CourtSelectionCooldownHours));
        if (reviewers.Count == 0)
            return;

        var expiresAt = now.AddHours(config.EventReviewInvitationHours);
        if (expiresAt > proposal.ReviewDeadline)
            expiresAt = proposal.ReviewDeadline;
        foreach (var reviewer in reviewers)
        {
            governance.Invitations.Add(new GovernanceInvitation
            {
                UserId = reviewer.Id,
                EntityType = "event_proposal",
                EntityId = proposalId.ToString(),
                Purpose = "event_review",
                State = InvitationStates.Pending,
                CreatedAt = now,
                ExpiresAt = expiresAt,
                IdempotencyKey = $"event:{proposalId}:reviewer:{reviewer.Id}",
            });
        }
        AddAudit(governance, "event.review_candidates_invited", 0, "event_proposal", proposalId.ToString(),
            new { reviewers = reviewers.Select(value => value.Id).ToArray(), expires_at = expiresAt });
        await governance.SaveChangesAsync();
    }

    public static EventManifestRequest[] ParseManifest(string text)
    {
        var result = new List<EventManifestRequest>();
        foreach (var raw in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = raw.Split(':', 3, StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || !parts[0].StartsWith("event.", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(parts[1]) || !int.TryParse(parts[2], out var maxUses) || maxUses is < 1 or > 1000)
                throw new CourtRuleException("Манифест: `event.полномочие:ресурс:лимит`, элементы через запятую; лимит 1–1000.");
            result.Add(new EventManifestRequest(parts[0], parts[1], maxUses));
        }
        if (result.Count is < 1 or > 30 || result.DistinctBy(value => (value.Capability, value.Resource)).Count() != result.Count)
            throw new CourtRuleException("Манифест должен содержать от 1 до 30 уникальных ресурсов.");
        return result.ToArray();
    }

    private void RequireEnabled()
    {
        if (!config.EventEnabled)
            throw new CourtRuleException("Event Governance временно отключена. Существующие данные сохранены, новые действия недоступны.");
    }

    private static string NormalizeJson(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return "{}";
        try { return JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(payload)); }
        catch (JsonException) { throw new CourtRuleException("payload должен быть корректным JSON."); }
    }

    private static void AddAudit(GovernanceDbContext db, string eventType, ulong actorDiscordId, string entityType, string entityId, object payload)
    {
        db.AuditEvents.Add(new GovernanceAuditEvent
        {
            EventType = eventType,
            ActorType = actorDiscordId == 0 ? "system" : "discord_user",
            ActorId = actorDiscordId == 0 ? null : actorDiscordId.ToString(),
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow,
            Payload = JsonSerializer.Serialize(payload),
        });
    }
}
