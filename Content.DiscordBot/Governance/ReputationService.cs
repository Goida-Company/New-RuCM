using System.Globalization;
using System.Text.Json;
using Content.Server.Database;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed record GovernanceReputationProfile(
    Guid UserId,
    Guid Ss14UserId,
    long? DiscordUserId,
    string Name,
    ReputationPosterior General,
    IReadOnlyDictionary<string, ReputationPosterior> Tracks,
    IReadOnlyList<GovernanceServicePath> Paths,
    GameActivityEvidence Activity,
    bool Suspended);

public sealed class ReputationService(
    Func<GovernanceDbContext> governanceFactory,
    Func<ServerDbContext> gameFactory)
{
    public async Task AppendObservationAsync(ReputationObservationInput input)
    {
        ValidateObservation(input);
        await using var governance = governanceFactory();
        await AppendObservationAsync(governance, input);
    }

    public async Task AppendPolicyObservationAsync(
        Guid userId,
        string track,
        string reason,
        string entityType,
        string entityId,
        DateTime occurredAt,
        string idempotencyKey,
        string createdByType = "system",
        string? createdById = null,
        object? metadata = null)
    {
        var evidence = ReputationPolicy.EvidenceFor(reason);
        await AppendObservationAsync(new ReputationObservationInput(
            userId,
            track,
            evidence.Success,
            evidence.Failure,
            evidence.Serious,
            reason,
            entityType,
            entityId,
            occurredAt,
            createdByType,
            createdById,
            idempotencyKey,
            JsonSerializer.Serialize(metadata ?? new { })));
    }

    public async Task<GovernanceContributionEvent> RecordContributionAsync(
        Guid userId,
        string reference,
        string contributionKind,
        double impact,
        double quality,
        double stability,
        DateTime occurredAt,
        ulong? actorDiscordId,
        string? metadata = null)
    {
        reference = reference.Trim();
        contributionKind = contributionKind.Trim();
        if (reference.Length is < 3 or > 500)
            throw new CourtRuleException("Ссылка или идентификатор вклада должен содержать от 3 до 500 символов.");
        if (contributionKind.Length is < 2 or > 80)
            throw new CourtRuleException("Тип вклада должен содержать от 2 до 80 символов.");
        if (impact is <= 0 or > 3 || quality is <= 0 or > 1.5 || stability is <= 0 or > 1.5)
            throw new CourtRuleException("Вклад должен иметь impact 0–3, quality 0–1.5 и stability 0–1.5; нулевые оценки не принимаются.");

        var weight = ReputationPolicy.ContributionSuccessWeight(impact, quality, stability);
        if (weight <= 0)
            throw new CourtRuleException("Вклад не образует положительного статистического наблюдения.");

        var normalizedMetadata = string.IsNullOrWhiteSpace(metadata) ? "{}" : NormalizeJson(metadata);
        var idempotency = $"contribution:{userId}:{reference}";
        await using var governance = governanceFactory();
        await using var transaction = await governance.Database.BeginTransactionAsync();
        var existing = await governance.ContributionEvents.SingleOrDefaultAsync(value => value.IdempotencyKey == idempotency);
        if (existing != null)
        {
            await transaction.CommitAsync();
            return existing;
        }

        var contribution = governance.ContributionEvents.Add(new GovernanceContributionEvent
        {
            UserId = userId,
            Reference = reference,
            ContributionKind = contributionKind,
            Impact = impact,
            Quality = quality,
            Stability = stability,
            OccurredAt = occurredAt,
            CreatedAt = DateTime.UtcNow,
            CreatedByDiscordId = actorDiscordId == null ? null : checked((long) actorDiscordId.Value),
            IdempotencyKey = idempotency,
            Metadata = normalizedMetadata,
        }).Entity;
        await governance.SaveChangesAsync();
        await AppendObservationAsync(governance, new ReputationObservationInput(
            userId,
            ReputationTracks.Contributor,
            weight,
            0,
            false,
            ReputationReasons.ContributionAccepted,
            "contribution",
            contribution.Id.ToString(CultureInfo.InvariantCulture),
            occurredAt,
            actorDiscordId == null ? "system" : "discord_user",
            actorDiscordId?.ToString(),
            $"{idempotency}:reputation",
            JsonSerializer.Serialize(new { reference, contributionKind, impact, quality, stability })));
        AddAudit(governance, "reputation.contribution_recorded", actorDiscordId?.ToString(), userId,
            new { contribution.Id, reference, contributionKind, impact, quality, stability, weight });
        await governance.SaveChangesAsync();
        await transaction.CommitAsync();
        await RefreshUserAsync(userId);
        return contribution;
    }

    public async Task SetPathsAsync(Guid userId, string primary, string? secondary)
    {
        primary = primary.Trim().ToLowerInvariant();
        secondary = string.IsNullOrWhiteSpace(secondary) ? null : secondary.Trim().ToLowerInvariant();
        if (!ReputationTracks.IsPath(primary) || secondary != null && !ReputationTracks.IsPath(secondary))
            throw new CourtRuleException("Доступные пути: moderation, jury, event, contributor.");
        if (secondary == primary)
            throw new CourtRuleException("Основной и дополнительный путь должны различаться.");

        await using var governance = governanceFactory();
        var user = await governance.Users.SingleOrDefaultAsync(value => value.Id == userId)
            ?? throw new CourtRuleException("Профиль Governance не найден.");
        var current = await governance.ServicePaths.Where(value => value.UserId == userId).ToListAsync();
        var now = DateTime.UtcNow;
        var desired = new Dictionary<short, string> { [1] = primary };
        if (secondary != null)
            desired[2] = secondary;

        foreach (var existing in current)
        {
            if (desired.TryGetValue(existing.Slot, out var next) && next == existing.Track)
                continue;
            var cooldown = existing.Slot == 1 ? TimeSpan.FromDays(30) : TimeSpan.FromDays(14);
            if (now - existing.ChangedAt < cooldown)
            {
                var availableAt = existing.ChangedAt + cooldown;
                throw new CourtRuleException($"Путь в слоте {existing.Slot} можно изменить после {availableAt:dd.MM.yyyy HH:mm} UTC.");
            }
        }

        foreach (var existing in current.Where(value => !desired.ContainsKey(value.Slot)).ToArray())
            governance.ServicePaths.Remove(existing);

        foreach (var (slot, track) in desired)
        {
            var row = current.SingleOrDefault(value => value.Slot == slot);
            if (row == null)
            {
                governance.ServicePaths.Add(new GovernanceServicePath
                {
                    UserId = userId,
                    Slot = slot,
                    Track = track,
                    SelectedAt = now,
                    ChangedAt = now,
                });
            }
            else if (row.Track != track)
            {
                row.Track = track;
                row.ChangedAt = now;
            }

            var qualification = await governance.Qualifications.SingleOrDefaultAsync(value => value.UserId == userId && value.Track == track);
            if (qualification == null)
            {
                governance.Qualifications.Add(new GovernanceQualification
                {
                    UserId = userId,
                    Track = track,
                    Level = 1,
                    UpdatedAt = now,
                });
            }
            else if (qualification.Level < 1)
            {
                qualification.Level = 1;
                qualification.UpdatedAt = now;
            }
        }

        user.UpdatedAt = now;
        AddAudit(governance, "reputation.paths_changed", user.DiscordUserId?.ToString(), userId,
            new { primary, secondary });
        await governance.SaveChangesAsync();
    }

    public async Task<GovernanceReputationProfile> GetProfileAsync(Guid userId, bool refresh = true)
    {
        if (refresh)
            await RefreshUserAsync(userId);

        await using var governance = governanceFactory();
        var user = await governance.Users.AsNoTracking().SingleOrDefaultAsync(value => value.Id == userId)
            ?? throw new CourtRuleException("Профиль Governance не найден.");
        var snapshots = await governance.ReputationSnapshots.AsNoTracking()
            .Where(value => value.UserId == userId)
            .ToListAsync();
        var activityRow = await governance.GameActivitySnapshots.AsNoTracking().SingleOrDefaultAsync(value => value.UserId == userId);
        var paths = await governance.ServicePaths.AsNoTracking().Where(value => value.UserId == userId)
            .OrderBy(value => value.Slot).ToListAsync();
        var generalRow = snapshots.SingleOrDefault(value => value.Track == ReputationTracks.General);
        var general = generalRow == null
            ? ReputationMath.Posterior(ReputationTracks.General, [], DateTime.UtcNow)
            : ToPosterior(generalRow);
        var tracks = snapshots.Where(value => ReputationTracks.ServicePaths.Contains(value.Track, StringComparer.Ordinal))
            .ToDictionary(value => value.Track, ToPosterior, StringComparer.Ordinal);
        var activity = activityRow == null
            ? ReputationMath.Activity(0, 0, 0)
            : new GameActivityEvidence(activityRow.OverallHours, activityRow.ActiveWeeks, activityRow.AccountAgeDays,
                activityRow.ActivityIndex, activityRow.EvidenceWeight);

        await using var game = gameFactory();
        var name = await game.Player.AsNoTracking().Where(value => value.UserId == user.Ss14UserId)
            .Select(value => value.LastSeenUserName).SingleOrDefaultAsync() ?? user.Ss14UserId.ToString();
        return new GovernanceReputationProfile(user.Id, user.Ss14UserId, user.DiscordUserId, name, general, tracks, paths, activity,
            user.IsGovernanceSuspended);
    }

    public async Task RefreshAllAsync()
    {
        await using var governance = governanceFactory();
        var ids = await governance.Users.AsNoTracking().Select(value => value.Id).ToListAsync();
        foreach (var id in ids)
            await RefreshUserAsync(id);
    }

    public async Task RefreshUsersAsync(IEnumerable<Guid> userIds)
    {
        foreach (var userId in userIds.Distinct())
            await RefreshUserAsync(userId);
    }

    public async Task RefreshUserAsync(Guid userId)
    {
        GovernanceUser user;
        List<GovernanceReputationObservation> observations;
        Dictionary<string, double> communityMeans;
        await using (var read = governanceFactory())
        {
            user = await read.Users.AsNoTracking().SingleOrDefaultAsync(value => value.Id == userId)
                ?? throw new CourtRuleException("Профиль Governance не найден.");
            observations = await read.ReputationObservations.AsNoTracking().Where(value => value.UserId == userId)
                .OrderBy(value => value.OccurredAt).ToListAsync();
            communityMeans = await read.ReputationSnapshots.AsNoTracking()
                .Where(value => value.UserId != userId)
                .GroupBy(value => value.Track)
                .Select(group => new { Track = group.Key, Mean = group.Average(value => value.Mean) })
                .ToDictionaryAsync(value => value.Track, value => value.Mean, StringComparer.Ordinal);
        }

        var activity = await GetGameActivityAsync(user.Ss14UserId);
        var now = DateTime.UtcNow;
        var values = observations.Select(ToValue).ToArray();
        var posteriors = new Dictionary<string, ReputationPosterior>(StringComparer.Ordinal);
        foreach (var track in ReputationTracks.ServicePaths)
        {
            // The retired support track remains immutable in history. Its AHelp evidence now belongs
            // to moderation, so fold those historical rows into the moderation posterior exactly once.
            var trackValues = values.Where((_, index) =>
                observations[index].Track == track ||
                track == ReputationTracks.Moderation && observations[index].Track == ReputationTracks.Support).ToArray();
            var priorMean = communityMeans.GetValueOrDefault(track, 0.5);
            posteriors[track] = ReputationMath.Posterior(track, trackValues, now, priorMean, ReputationPolicy.TrackPriorStrength);
        }

        var generalValues = new List<ReputationObservationValue>();
        for (var index = 0; index < observations.Count; index++)
        {
            var observation = observations[index];
            if (observation.Track == ReputationTracks.General)
            {
                generalValues.Add(ToValue(observation));
                continue;
            }
            generalValues.Add(new ReputationObservationValue(
                observation.OccurredAt,
                $"spillover:{observation.Reason}",
                observation.SuccessWeight * ReputationPolicy.GeneralPathSpillover,
                observation.FailureWeight * ReputationPolicy.GeneralNegativeSpillover,
                observation.SeriousNegative));
        }
        var general = ReputationMath.Posterior(ReputationTracks.General, generalValues, now,
            extraSuccessEvidence: activity.EvidenceWeight);
        posteriors[ReputationTracks.General] = general;

        await using var governance = governanceFactory();
        var storedUser = await governance.Users.SingleAsync(value => value.Id == userId);
        storedUser.CivicRatingCache = general.Score;
        storedUser.UpdatedAt = now;

        var activityRow = await governance.GameActivitySnapshots.SingleOrDefaultAsync(value => value.UserId == userId);
        if (activityRow == null)
        {
            activityRow = governance.GameActivitySnapshots.Add(new GovernanceGameActivitySnapshot { UserId = userId }).Entity;
        }
        activityRow.OverallHours = activity.OverallHours;
        activityRow.ActiveWeeks = activity.ActiveWeeks;
        activityRow.AccountAgeDays = activity.AccountAgeDays;
        activityRow.ActivityIndex = activity.ActivityIndex;
        activityRow.EvidenceWeight = activity.EvidenceWeight;
        activityRow.CalculatedAt = now;

        foreach (var posterior in posteriors.Values)
            await UpsertSnapshotAsync(governance, userId, posterior, now);
        await governance.SaveChangesAsync();
    }

    public async Task ReconcileOperationalEvidenceAsync()
    {
        await using var governance = governanceFactory();
        var now = DateTime.UtcNow;

        var resolvedAHelps = await governance.AHelpTickets.AsNoTracking()
            .Where(value => value.Status == "resolved" && value.ClaimedByUserId != null)
            .Select(value => new { value.Id, UserId = value.ClaimedByUserId!.Value, value.UpdatedAt })
            .ToListAsync();
        foreach (var item in resolvedAHelps)
            await AppendPolicyObservationAsync(governance, item.UserId, ReputationTracks.Moderation, ReputationReasons.AHelpResolved,
                "ahelp_ticket", item.Id.ToString(), item.UpdatedAt, $"reputation:ahelp:{item.Id}:resolved");

        var duties = await governance.DutySessions.AsNoTracking()
            .Where(value => value.Status != "active")
            .ToListAsync();
        foreach (var duty in duties)
        {
            if (duty.Status is "completed" or "round_ended")
            {
                await AppendPolicyObservationAsync(governance, duty.UserId, ReputationTracks.Moderation, ReputationReasons.DutyCompleted,
                    "duty_session", duty.Id.ToString(), duty.EndedAt ?? duty.ExpiresAt, $"reputation:duty:{duty.Id}:completed");
            }
            else if (duty.Status is "abandoned" or "revoked")
            {
                await AppendPolicyObservationAsync(governance, duty.UserId, ReputationTracks.Moderation, ReputationReasons.DutyFailed,
                    "duty_session", duty.Id.ToString(), duty.EndedAt ?? duty.ExpiresAt, $"reputation:duty:{duty.Id}:failed");
            }
        }

        var assignments = await governance.ServiceAssignments.AsNoTracking()
            .Where(value => value.CompletedAt != null || value.FailedAt != null)
            .ToListAsync();
        foreach (var assignment in assignments)
        {
            var (completeReason, failedReason) = assignment.Track switch
            {
                ReputationTracks.Jury => (ReputationReasons.JuryCompleted, ReputationReasons.JuryFailed),
                ReputationTracks.Event => (ReputationReasons.EventReviewCompleted, ReputationReasons.EventReviewFailed),
                ReputationTracks.Moderation => (ReputationReasons.ModerationReviewCompleted, ReputationReasons.ModerationReviewFailed),
                _ => (string.Empty, string.Empty),
            };
            if (string.IsNullOrEmpty(completeReason))
                continue;
            if (assignment.CompletedAt is { } completedAt)
                await AppendPolicyObservationAsync(governance, assignment.UserId, assignment.Track, completeReason,
                    assignment.EntityType, assignment.EntityId, completedAt, $"reputation:assignment:{assignment.Id}:completed");
            else if (assignment.FailedAt is { } failedAt)
                await AppendPolicyObservationAsync(governance, assignment.UserId, assignment.Track, failedReason,
                    assignment.EntityType, assignment.EntityId, failedAt, $"reputation:assignment:{assignment.Id}:failed");
        }

        var completedEvents = await governance.EventSessions.AsNoTracking()
            .Where(value => value.Status == "completed")
            .ToListAsync();
        foreach (var session in completedEvents)
            await AppendPolicyObservationAsync(governance, session.DirectorUserId, ReputationTracks.Event,
                ReputationReasons.EventSessionCompleted, "event_session", session.Id.ToString(), session.EndedAt ?? session.ExpiresAt,
                $"reputation:event-session:{session.Id}:completed");

        var moderationReviews = await governance.ModerationReviews.AsNoTracking()
            .Join(governance.ModerationActions.AsNoTracking(), review => review.ActionId, action => action.Id,
                (review, action) => new { Review = review, Action = action })
            .ToListAsync();
        foreach (var row in moderationReviews)
        {
            (double Success, double Failure, bool Serious, string Reason) evidence = row.Review.Outcome switch
            {
                ModerationReviewOutcomes.Correct => (1.25, 0.0, false, ReputationReasons.ModerationActionCorrect),
                ModerationReviewOutcomes.ReasonableButWrong => (0.20, 0.55, false, ReputationReasons.ModerationActionMinorIssue),
                ModerationReviewOutcomes.ProceduralError => (0.10, 0.90, false, "moderation.action_procedural_error"),
                ModerationReviewOutcomes.Negligent => (0.0, 1.80, true, ReputationReasons.ModerationActionWrong),
                ModerationReviewOutcomes.Abuse => (0.0, 3.00, true, "moderation.action_abuse"),
                _ => (0.0, 0.0, false, string.Empty),
            };
            if (evidence.Success <= 0 && evidence.Failure <= 0)
                continue;
            await AppendObservationAsync(governance, new ReputationObservationInput(
                row.Action.ActorUserId,
                ReputationTracks.Moderation,
                evidence.Success,
                evidence.Failure,
                evidence.Serious,
                evidence.Reason,
                "moderation_action",
                row.Action.Id.ToString(),
                row.Review.SubmittedAt,
                "system",
                null,
                $"reputation:moderation-review:{row.Review.Id}:actor",
                JsonSerializer.Serialize(new { row.Review.Outcome, row.Review.ReviewerUserId })));
        }

        var falseReports = await governance.CourtCases.AsNoTracking()
            .Where(value => value.FalseReportAt != null)
            .Select(value => new { value.Id, value.ClaimantUserId, At = value.FalseReportAt!.Value })
            .ToListAsync();
        foreach (var item in falseReports)
            await AppendPolicyObservationAsync(governance, item.ClaimantUserId, ReputationTracks.General, ReputationReasons.FalseReport,
                "court_case", item.Id.ToString(), item.At, $"reputation:court:{item.Id}:false-report");

        await governance.SaveChangesAsync();
    }

    public async Task ReconcileQualificationsAsync()
    {
        await using var governance = governanceFactory();
        var now = DateTime.UtcNow;
        var paths = await governance.ServicePaths.ToListAsync();
        var snapshots = await governance.ReputationSnapshots.AsNoTracking().ToListAsync();
        foreach (var path in paths)
        {
            var snapshot = snapshots.SingleOrDefault(value => value.UserId == path.UserId && value.Track == path.Track);
            if (snapshot == null)
                continue;
            var completed = await governance.ServiceAssignments.AsNoTracking().CountAsync(value =>
                value.UserId == path.UserId && value.Track == path.Track && value.CompletedAt != null);
            if (path.Track == ReputationTracks.Moderation)
            {
                completed += await governance.AHelpTickets.AsNoTracking().CountAsync(value =>
                    value.ClaimedByUserId == path.UserId && value.Status == "resolved");
                completed += await governance.DutySessions.AsNoTracking().CountAsync(value =>
                    value.UserId == path.UserId && (value.Status == "completed" || value.Status == "round_ended"));
            }
            if (path.Track == ReputationTracks.Contributor)
                completed += await governance.ContributionEvents.AsNoTracking().CountAsync(value => value.UserId == path.UserId);

            var posterior = ToPosterior(snapshot);
            var eligible = ReputationPolicy.EligibleQualificationLevel(path.Track, posterior, completed);
            var qualification = await governance.Qualifications.SingleOrDefaultAsync(value => value.UserId == path.UserId && value.Track == path.Track);
            if (qualification == null)
            {
                qualification = governance.Qualifications.Add(new GovernanceQualification
                {
                    UserId = path.UserId,
                    Track = path.Track,
                    Level = 1,
                    UpdatedAt = now,
                }).Entity;
            }
            var previous = qualification.Level;
            if (eligible > qualification.Level)
            {
                qualification.Level = eligible;
                qualification.UpdatedAt = now;
                AddAudit(governance, "qualification.bayesian_promotion", null, path.UserId,
                    new { track = path.Track, from = previous, to = eligible, posterior.LowerBound, posterior.EvidenceWeight, completed });
            }
            else if (qualification.Level > 1 &&
                     posterior.LowerBound < ReputationPolicy.DemotionThreshold(qualification.Level) &&
                     now - qualification.UpdatedAt >= TimeSpan.FromDays(30))
            {
                // Hysteresis: promotion and demotion thresholds differ; at most one level may be lost per 30 days.
                qualification.Level--;
                qualification.UpdatedAt = now;
                AddAudit(governance, "qualification.bayesian_demotion", null, path.UserId,
                    new { track = path.Track, from = previous, to = qualification.Level, posterior.LowerBound, posterior.EvidenceWeight, completed });
            }
        }
        await governance.SaveChangesAsync();
    }

    private async Task<GameActivityEvidence> GetGameActivityAsync(Guid ss14UserId)
    {
        await using var game = gameFactory();
        var firstSeen = await game.Player.AsNoTracking().Where(value => value.UserId == ss14UserId)
            .Select(value => (DateTime?) value.FirstSeenTime).SingleOrDefaultAsync();
        if (firstSeen == null)
            return ReputationMath.Activity(0, 0, 0);
        var overall = await game.PlayTime.AsNoTracking()
            .Where(value => value.PlayerId == ss14UserId && value.Tracker == "Overall")
            .Select(value => (TimeSpan?) value.TimeSpent)
            .SingleOrDefaultAsync() ?? TimeSpan.Zero;
        var activeWeeks = await game.Database.SqlQuery<int>($"""
            SELECT count(DISTINCT date_trunc('week', time))::integer AS "Value"
            FROM connection_log
            WHERE user_id = {ss14UserId} AND denied IS NULL
            """).SingleAsync();
        var ageDays = Math.Max(0, (int) Math.Floor((DateTime.UtcNow - firstSeen.Value.ToUniversalTime()).TotalDays));
        return ReputationMath.Activity(overall.TotalHours, activeWeeks, ageDays);
    }

    private static async Task AppendPolicyObservationAsync(
        GovernanceDbContext governance,
        Guid userId,
        string track,
        string reason,
        string entityType,
        string entityId,
        DateTime occurredAt,
        string idempotencyKey)
    {
        var evidence = ReputationPolicy.EvidenceFor(reason);
        await AppendObservationAsync(governance, new ReputationObservationInput(
            userId, track, evidence.Success, evidence.Failure, evidence.Serious, reason,
            entityType, entityId, occurredAt, "system", null, idempotencyKey));
    }

    private static async Task AppendObservationAsync(GovernanceDbContext governance, ReputationObservationInput input)
    {
        ValidateObservation(input);
        var metadata = NormalizeJson(input.Metadata);
        await governance.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT governance.append_reputation_observation(
                {input.UserId}, {input.Track}, {input.SuccessWeight}, {input.FailureWeight}, {input.SeriousNegative},
                {input.Reason}, {input.EntityType}, {input.EntityId}, {input.OccurredAt},
                {input.CreatedByType}, {input.CreatedById}, {input.IdempotencyKey}, CAST({metadata} AS jsonb))
            """);
    }

    private static async Task UpsertSnapshotAsync(
        GovernanceDbContext governance,
        Guid userId,
        ReputationPosterior posterior,
        DateTime now)
    {
        var row = await governance.ReputationSnapshots.SingleOrDefaultAsync(value =>
            value.UserId == userId && value.Track == posterior.Track);
        if (row == null)
        {
            row = governance.ReputationSnapshots.Add(new GovernanceReputationSnapshot
            {
                UserId = userId,
                Track = posterior.Track,
            }).Entity;
        }
        row.Alpha = posterior.Alpha;
        row.Beta = posterior.Beta;
        row.Mean = posterior.Mean;
        row.LowerBound = posterior.LowerBound;
        row.EvidenceWeight = posterior.EvidenceWeight;
        row.Score = posterior.Score;
        row.CalculatedAt = now;
    }

    private static ReputationPosterior ToPosterior(GovernanceReputationSnapshot row) =>
        new(row.Track, row.Alpha, row.Beta, row.Mean, row.LowerBound, row.EvidenceWeight, row.Score);

    private static ReputationObservationValue ToValue(GovernanceReputationObservation row) =>
        new(row.OccurredAt, row.Reason, row.SuccessWeight, row.FailureWeight, row.SeriousNegative);

    private static void ValidateObservation(ReputationObservationInput input)
    {
        if (!ReputationTracks.IsTrack(input.Track))
            throw new CourtRuleException("Неизвестное направление репутации.");
        if (input.SuccessWeight < 0 || input.FailureWeight < 0 || input.SuccessWeight <= 0 && input.FailureWeight <= 0)
            throw new CourtRuleException("Репутационное наблюдение должно иметь положительный success/failure weight.");
        if (string.IsNullOrWhiteSpace(input.Reason) || string.IsNullOrWhiteSpace(input.IdempotencyKey))
            throw new CourtRuleException("Репутационное наблюдение должно иметь причину и idempotency key.");
    }

    private static string NormalizeJson(string value)
    {
        try
        {
            return JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(value) ? "{}" : value));
        }
        catch (JsonException)
        {
            throw new CourtRuleException("metadata должно быть корректным JSON.");
        }
    }

    private static void AddAudit(GovernanceDbContext governance, string eventType, string? actorId, Guid userId, object payload)
    {
        governance.AuditEvents.Add(new GovernanceAuditEvent
        {
            EventType = eventType,
            ActorType = actorId == null ? "system" : "discord_user",
            ActorId = actorId,
            EntityType = "user",
            EntityId = userId.ToString(),
            CreatedAt = DateTime.UtcNow,
            Payload = JsonSerializer.Serialize(payload),
        });
    }
}
