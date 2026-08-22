using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed class ModerationTrustService(
    Func<GovernanceDbContext> governanceFactory,
    GovernanceCommunityService community,
    CandidateSelectionService selection,
    Config config)
{
    public async Task<ModerationTrustProfile> GetProfileAsync(ulong discordId)
    {
        var user = await community.RequireUserAsync(discordId);
        await using var governance = governanceFactory();

        var reviews = await governance.ModerationReviews.AsNoTracking()
            .Join(
                governance.ModerationActions.AsNoTracking().Where(action => action.ActorUserId == user.Id),
                review => review.ActionId,
                action => action.Id,
                (review, _) => review)
            .ToListAsync();

        var completedDuties = await governance.DutySessions.AsNoTracking().CountAsync(value =>
            value.UserId == user.Id && (value.Status == "completed" || value.Status == "round_ended"));
        var failedDuties = await governance.DutySessions.AsNoTracking().CountAsync(value =>
            value.UserId == user.Id && (value.Status == "abandoned" || value.Status == "revoked"));
        var seriousInterventions = await governance.ModerationActions.AsNoTracking().CountAsync(value =>
            value.ActorUserId == user.Id && value.Status == "executed" &&
            (value.ActionType == "freeze" || value.ActionType == "round_remove"));

        var decisionAccuracy = reviews.Count == 0
            ? 75
            : checked((int) Math.Round(reviews.Average(value => ModerationReviewOutcomes.AccuracyWeight(value.Outcome))));
        var proceduralScore = reviews.Count == 0
            ? 75
            : checked((int) Math.Round(reviews.Average(value => ModerationReviewOutcomes.ProcedureWeight(value.Outcome))));
        var closedDuties = completedDuties + failedDuties;
        var reliabilityScore = closedDuties == 0
            ? 75
            : checked((int) Math.Round(completedDuties * 100d / closedDuties));
        var reviewedActions = reviews.Select(value => value.ActionId).Distinct().Count();
        var confidence = Math.Clamp(reviewedActions * 12 + closedDuties * 2, 0, 100);
        var trustScore = Math.Clamp(checked((int) Math.Round(
            (decisionAccuracy * 0.50d + proceduralScore * 0.30d + reliabilityScore * 0.20d) * 10d)), 0, 1000);

        return new ModerationTrustProfile(
            user.Id,
            trustScore,
            decisionAccuracy,
            proceduralScore,
            reliabilityScore,
            confidence,
            reviewedActions,
            reviews.Count,
            completedDuties,
            failedDuties,
            seriousInterventions);
    }

    public static bool ShouldAudit(long actionId, string actionType, int samplePercent)
    {
        if (actionType == "round_remove")
            return true;
        samplePercent = Math.Clamp(samplePercent, 0, 100);
        if (samplePercent == 0)
            return false;
        if (samplePercent == 100)
            return true;
        return actionId % 100 < samplePercent;
    }

    public async Task<IReadOnlyList<ModerationReviewAssignment>> EnsureAutomaticReviewsAsync(
        IReadOnlySet<ulong>? availableDiscordIds = null)
    {
        var batchSize = Math.Clamp(config.ModerationReviewBatchSize, 1, 50);
        await using var governance = governanceFactory();
        var candidates = await governance.ModerationActions.AsNoTracking()
            .Where(action => action.Status == "executed")
            .Where(action => !governance.ModerationReviews.Any(review => review.ActionId == action.Id))
            .Where(action => !governance.Invitations.Any(invitation =>
                invitation.Purpose == "moderation_review" && invitation.EntityType == "moderation_action" &&
                invitation.EntityId == action.Id.ToString() &&
                (invitation.State == InvitationStates.Pending || invitation.State == InvitationStates.Accepted)))
            .Where(action => !governance.ServiceAssignments.Any(assignment =>
                assignment.Track == "moderation" && assignment.EntityType == "moderation_action_review" &&
                assignment.EntityId == action.Id.ToString() && assignment.CompletedAt == null && assignment.FailedAt == null))
            .OrderByDescending(action => action.ExecutedAt ?? action.CreatedAt)
            .Take(batchSize * 4)
            .ToListAsync();

        var result = new List<ModerationReviewAssignment>();
        foreach (var action in candidates)
        {
            if (result.Count >= batchSize || !ShouldAudit(action.Id, action.ActionType, config.ModerationReviewSamplePercent))
                continue;
            try
            {
                result.Add(await AssignRandomReviewAsync(action.Id, availableDiscordIds));
            }
            catch (CourtRuleException exception) when (
                exception.Message.Contains("нет независимого кандидата", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("уже назначен", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("уже прошло", StringComparison.OrdinalIgnoreCase))
            {
                // Temporary lack of eligible reviewers is expected. A later scheduler pass retries.
            }
        }
        return result;
    }

    public async Task<ModerationReviewAssignment> AssignRandomReviewAsync(
        long actionId,
        IReadOnlySet<ulong>? availableDiscordIds = null)
    {
        await using var governance = governanceFactory();
        var action = await governance.ModerationActions.AsNoTracking().SingleOrDefaultAsync(value => value.Id == actionId)
            ?? throw new CourtRuleException("Действие модерации не найдено.");
        if (action.Status != "executed")
            throw new CourtRuleException("На независимый аудит можно отправить только исполненное действие.");
        if (await governance.ModerationReviews.AsNoTracking().AnyAsync(value => value.ActionId == actionId))
            throw new CourtRuleException("Это действие уже прошло независимый аудит.");
        if (await governance.Invitations.AsNoTracking().AnyAsync(value =>
                value.Purpose == "moderation_review" && value.EntityType == "moderation_action" &&
                value.EntityId == actionId.ToString() &&
                (value.State == InvitationStates.Pending || value.State == InvitationStates.Accepted)))
            throw new CourtRuleException("Для этого действия уже назначен независимый рецензент.");
        if (await governance.ServiceAssignments.AsNoTracking().AnyAsync(value =>
                value.Track == "moderation" && value.EntityType == "moderation_action_review" &&
                value.EntityId == actionId.ToString() && value.CompletedAt == null && value.FailedAt == null))
            throw new CourtRuleException("Для этого действия уже назначен независимый рецензент.");

        var approvers = await governance.ModerationApprovals.AsNoTracking()
            .Where(value => value.ActionId == actionId)
            .Select(value => value.ApproverUserId)
            .ToListAsync();
        var previousReviewers = await governance.ServiceAssignments.AsNoTracking()
            .Where(value => value.Track == "moderation" && value.EntityType == "moderation_action_review" &&
                            value.EntityId == actionId.ToString())
            .Select(value => value.UserId)
            .ToListAsync();
        var previouslyInvited = await governance.Invitations.AsNoTracking()
            .Where(value => value.Purpose == "moderation_review" && value.EntityType == "moderation_action" &&
                            value.EntityId == actionId.ToString())
            .Select(value => value.UserId)
            .ToListAsync();
        var excluded = approvers
            .Concat(previousReviewers)
            .Concat(previouslyInvited)
            .Append(action.ActorUserId)
            .Append(action.TargetUserId)
            .ToHashSet();
        var candidates = await selection.SelectAsync(
            "moderation",
            checked((short) config.ModerationReviewMinimumQualification),
            "moderation_action",
            actionId.ToString(),
            1,
            excluded,
            availableDiscordIds,
            TimeSpan.FromHours(config.ModerationReviewSelectionCooldownHours));
        var reviewer = candidates.SingleOrDefault()
            ?? throw new CourtRuleException("Сейчас нет независимого кандидата для проверки этого действия.");
        var reviewerDiscordId = reviewer.DiscordUserId
            ?? throw new CourtRuleException("Независимый аудит сейчас требует привязанный Discord-аккаунт.");

        var now = DateTime.UtcNow;
        var invitation = governance.Invitations.Add(new GovernanceInvitation
        {
            UserId = reviewer.Id,
            EntityType = "moderation_action",
            EntityId = actionId.ToString(),
            Purpose = "moderation_review",
            State = InvitationStates.Pending,
            CreatedAt = now,
            ExpiresAt = now.AddHours(config.ModerationReviewInvitationHours),
            IdempotencyKey = $"moderation-review:{actionId}:reviewer:{reviewer.Id}",
        }).Entity;
        AddAudit(governance, "moderation.review_assigned", "system", null, "moderation_action", actionId.ToString(),
            new { reviewer_user_id = reviewer.Id, invitation_expires_at = invitation.ExpiresAt });
        await governance.SaveChangesAsync();
        return new ModerationReviewAssignment(actionId, invitation.Id, reviewer.Id, reviewerDiscordId, invitation.ExpiresAt);
    }

    public async Task<IReadOnlyList<(GovernanceInvitation Invitation, GovernanceUser User)>> PendingReviewNotificationsAsync()
    {
        await using var governance = governanceFactory();
        return await governance.Invitations.AsNoTracking()
            .Where(value => value.Purpose == "moderation_review" && value.State == InvitationStates.Pending &&
                            value.DiscordNotifiedAt == null && value.ExpiresAt > DateTime.UtcNow)
            .Join(governance.Users, invitation => invitation.UserId, user => user.Id,
                (invitation, user) => ValueTuple.Create(invitation, user))
            .ToListAsync();
    }

    public async Task MarkInvitationNotifiedAsync(long invitationId)
    {
        await using var governance = governanceFactory();
        var invitation = await governance.Invitations.SingleAsync(value => value.Id == invitationId);
        invitation.DiscordNotifiedAt = DateTime.UtcNow;
        await governance.SaveChangesAsync();
    }

    public async Task<ModerationReviewPacket> GetReviewPacketAsync(long actionId, ulong reviewerDiscordId)
    {
        var reviewer = await community.RequireUserAsync(reviewerDiscordId);
        await using var governance = governanceFactory();
        var invitation = await governance.Invitations.AsNoTracking().SingleOrDefaultAsync(value =>
            value.UserId == reviewer.Id && value.EntityType == "moderation_action" &&
            value.EntityId == actionId.ToString() && value.Purpose == "moderation_review")
            ?? throw new CourtRuleException("Вы не назначены рецензентом этого действия.");
        if (invitation.State != InvitationStates.Accepted || invitation.ExpiresAt <= DateTime.UtcNow)
            throw new CourtRuleException("Сначала примите действующее приглашение на аудит.");

        var action = await governance.ModerationActions.AsNoTracking().SingleAsync(value => value.Id == actionId);
        var incident = await governance.LiveIncidents.AsNoTracking().SingleAsync(value => value.Id == action.IncidentId);
        var approvals = await governance.ModerationApprovals.AsNoTracking().CountAsync(value =>
            value.ActionId == actionId && value.Decision == "approve");
        var rejections = await governance.ModerationApprovals.AsNoTracking().CountAsync(value =>
            value.ActionId == actionId && value.Decision == "reject");
        return new ModerationReviewPacket(
            action.Id,
            action.ActionType,
            action.Reason,
            action.DurationSeconds,
            action.CreatedAt,
            action.ExecutedAt,
            action.RequiredApprovals,
            approvals,
            rejections,
            incident.Id,
            incident.Type,
            incident.Summary,
            incident.RoundId,
            incident.Status,
            incident.CourtCaseId != null);
    }

    public async Task<string> RespondToInvitationAsync(
        long actionId,
        ulong reviewerDiscordId,
        string response,
        string? recusalReason)
    {
        if (response is not (InvitationStates.Accepted or InvitationStates.Declined or InvitationStates.Recused))
            throw new CourtRuleException("Неизвестный ответ на приглашение.");
        if (response == InvitationStates.Recused && string.IsNullOrWhiteSpace(recusalReason))
            throw new CourtRuleException("Для самоотвода нужно указать причину.");

        var reviewer = await community.RequireUserAsync(reviewerDiscordId);
        await using var governance = governanceFactory();
        var invitation = await governance.Invitations.SingleOrDefaultAsync(value =>
            value.UserId == reviewer.Id && value.EntityType == "moderation_action" &&
            value.EntityId == actionId.ToString() && value.Purpose == "moderation_review")
            ?? throw new CourtRuleException("У вас нет приглашения на аудит этого действия.");
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
            await governance.SaveChangesAsync();
            throw new CourtRuleException("Срок ответа на приглашение истёк.");
        }

        invitation.State = response;
        invitation.RespondedAt = now;
        invitation.RecusalReason = response == InvitationStates.Recused ? recusalReason!.Trim() : null;
        invitation.Version++;
        if (response == InvitationStates.Accepted)
        {
            invitation.ExpiresAt = now.AddHours(config.ModerationReviewHours);
            if (!await governance.ServiceAssignments.AnyAsync(value =>
                    value.UserId == reviewer.Id && value.Track == "moderation" &&
                    value.EntityType == "moderation_action_review" && value.EntityId == actionId.ToString()))
            {
                governance.ServiceAssignments.Add(new GovernanceServiceAssignment
                {
                    UserId = reviewer.Id,
                    Track = "moderation",
                    EntityType = "moderation_action_review",
                    EntityId = actionId.ToString(),
                    AssignedAt = now,
                });
            }
        }
        AddAudit(governance, $"moderation.review_invitation.{response}", "discord_user", reviewerDiscordId.ToString(),
            "moderation_action", actionId.ToString(), new { recusal_reason = invitation.RecusalReason });
        await governance.SaveChangesAsync();
        return response;
    }

    public async Task<GovernanceModerationReview> SubmitReviewAsync(
        long actionId,
        ulong reviewerDiscordId,
        string outcome,
        string reasoning)
    {
        if (!ModerationReviewOutcomes.IsValid(outcome))
            throw new CourtRuleException("Неизвестный итог проверки.");
        reasoning = reasoning.Trim();
        if (reasoning.Length is < 40 or > 3000)
            throw new CourtRuleException("Обоснование проверки должно содержать от 40 до 3000 символов.");

        var reviewer = await community.RequireUserAsync(reviewerDiscordId);
        await using var governance = governanceFactory();
        var action = await governance.ModerationActions.AsNoTracking().SingleOrDefaultAsync(value => value.Id == actionId)
            ?? throw new CourtRuleException("Действие модерации не найдено.");
        if (action.ActorUserId == reviewer.Id || action.TargetUserId == reviewer.Id)
            throw new CourtRuleException("Нельзя проверять собственное действие или действие против себя.");
        var invitation = await governance.Invitations.SingleOrDefaultAsync(value =>
            value.UserId == reviewer.Id && value.EntityType == "moderation_action" &&
            value.EntityId == actionId.ToString() && value.Purpose == "moderation_review")
            ?? throw new CourtRuleException("Вы не назначены рецензентом этого действия.");
        if (invitation.State != InvitationStates.Accepted || invitation.ExpiresAt <= DateTime.UtcNow)
            throw new CourtRuleException("Активного принятого приглашения на аудит нет.");
        if (await governance.ModerationReviews.AnyAsync(value => value.ActionId == actionId && value.ReviewerUserId == reviewer.Id))
            throw new CourtRuleException("Ваша проверка уже сохранена.");

        var review = governance.ModerationReviews.Add(new GovernanceModerationReview
        {
            ActionId = actionId,
            ReviewerUserId = reviewer.Id,
            Outcome = outcome,
            Reasoning = reasoning,
            SubmittedAt = DateTime.UtcNow,
            IdempotencyKey = $"moderation-review:{actionId}:result:{reviewer.Id}",
        }).Entity;
        var assignment = await governance.ServiceAssignments.SingleAsync(value =>
            value.UserId == reviewer.Id && value.Track == "moderation" &&
            value.EntityType == "moderation_action_review" && value.EntityId == actionId.ToString());
        assignment.CompletedAt = DateTime.UtcNow;
        AddAudit(governance, "moderation.review_submitted", "discord_user", reviewerDiscordId.ToString(),
            "moderation_action", actionId.ToString(), new { outcome, actor_user_id = action.ActorUserId });
        await governance.SaveChangesAsync();
        return review;
    }

    public async Task ProcessDeadlinesAsync()
    {
        await using var governance = governanceFactory();
        var now = DateTime.UtcNow;
        var invitations = await governance.Invitations
            .Where(value => value.Purpose == "moderation_review" && value.ExpiresAt <= now &&
                            (value.State == InvitationStates.Pending || value.State == InvitationStates.Accepted))
            .ToListAsync();

        foreach (var invitation in invitations)
        {
            var actionId = long.Parse(invitation.EntityId);
            if (invitation.State == InvitationStates.Pending)
            {
                invitation.State = InvitationStates.Expired;
                invitation.RespondedAt = now;
                invitation.Version++;
                AddAudit(governance, "moderation.review_invitation.expired", "system", null,
                    "moderation_action", actionId.ToString(), new { invitation_id = invitation.Id });
                continue;
            }

            var completed = await governance.ModerationReviews.AsNoTracking().AnyAsync(value =>
                value.ActionId == actionId && value.ReviewerUserId == invitation.UserId);
            if (completed)
                continue;
            invitation.State = InvitationStates.Failed;
            invitation.Version++;
            await FailAssignmentAsync(governance, invitation.UserId, actionId, now);
            AddAudit(governance, "moderation.review_failed", "system", null,
                "moderation_action", actionId.ToString(), new { invitation_id = invitation.Id });
        }
        await governance.SaveChangesAsync();
    }

    private static async Task FailAssignmentAsync(GovernanceDbContext governance, Guid userId, long actionId, DateTime now)
    {
        var assignment = await governance.ServiceAssignments.SingleOrDefaultAsync(value =>
            value.UserId == userId && value.Track == "moderation" &&
            value.EntityType == "moderation_action_review" && value.EntityId == actionId.ToString());
        if (assignment != null && assignment.CompletedAt == null && assignment.FailedAt == null)
            assignment.FailedAt = now;
    }

    private static void AddAudit(
        GovernanceDbContext governance,
        string eventType,
        string actorType,
        string? actorId,
        string entityType,
        string entityId,
        object payload)
    {
        governance.AuditEvents.Add(new GovernanceAuditEvent
        {
            EventType = eventType,
            ActorType = actorType,
            ActorId = actorId,
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow,
            Payload = JsonSerializer.Serialize(payload),
        });
    }
}
