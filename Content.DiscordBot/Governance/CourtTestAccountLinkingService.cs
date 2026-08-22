using System.Text;
using Content.Server.Database;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

/// <summary>
/// Local-only helper for full Community Court smoke tests. It creates the same game-database
/// Discord↔SS14 relation used by the normal linking flow and can advance time-gated court stages.
/// The helper is disabled unless COURT_TEST_MODE=true.
/// </summary>
public sealed class CourtTestAccountLinkingService(
    Func<ServerDbContext> gameFactory,
    Func<GovernanceDbContext> governanceFactory,
    GovernanceCommunityService community,
    Config config)
{
    public async Task<string> LinkJurorAsync(ulong actorDiscordId, ulong targetDiscordId, string playerQuery)
    {
        EnsureTestMode();
        if (targetDiscordId > long.MaxValue)
            throw new CourtRuleException("Discord ID не поддерживается Governance.");

        playerQuery = playerQuery.Trim();
        if (playerQuery.Length == 0)
            throw new CourtRuleException("Укажите ник или SS14 UUID тестировщика.");

        await using var game = gameFactory();
        var player = await ResolvePlayerAsync(game, playerQuery);

        var linkedByPlayer = await game.RMCLinkedAccounts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.PlayerId == player.UserId);
        if (linkedByPlayer != null && linkedByPlayer.DiscordId != targetDiscordId)
        {
            throw new CourtRuleException(
                $"SS14-аккаунт «{player.LastSeenUserName}» уже привязан к другому Discord ID ({linkedByPlayer.DiscordId}). Перепривязка запрещена.");
        }

        var linkedByDiscord = await game.RMCLinkedAccounts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.DiscordId == targetDiscordId);
        if (linkedByDiscord != null && linkedByDiscord.PlayerId != player.UserId)
        {
            throw new CourtRuleException(
                $"Этот Discord-аккаунт уже привязан к другому SS14 UUID ({linkedByDiscord.PlayerId}). Перепривязка запрещена.");
        }

        // Test mode must obey the same permanent identity invariant as production linking.
        await community.ValidatePermanentLinkAsync(player.UserId, targetDiscordId);

        if (linkedByPlayer == null && linkedByDiscord == null)
        {
            var discord = await game.RMCDiscordAccounts.SingleOrDefaultAsync(value => value.Id == targetDiscordId);
            if (discord == null)
                game.RMCDiscordAccounts.Add(new RMCDiscordAccount { Id = targetDiscordId });

            game.RMCLinkedAccounts.Add(new RMCLinkedAccount
            {
                PlayerId = player.UserId,
                DiscordId = targetDiscordId,
            });
            game.RMCLinkedAccountLogs.Add(new RMCLinkedAccountLogs
            {
                PlayerId = player.UserId,
                DiscordId = targetDiscordId,
                At = DateTime.UtcNow,
            });
            await game.SaveChangesAsync();
        }

        await community.SyncPermanentLinkAsync(player.UserId, targetDiscordId);
        var profile = await community.GetProfileAsync(targetDiscordId);
        await EnsureTestJuryPathAsync(profile.UserId);
        if (!profile.Qualifications.TryGetValue("jury", out var juryLevel) || juryLevel < 1)
            await community.SetQualificationAsync(actorDiscordId, targetDiscordId, "jury", 1);

        await Logger.Info(
            $"Court test link: Discord {targetDiscordId} -> SS14 {player.UserId} ({player.LastSeenUserName}), actor {actorDiscordId}.");

        return $"Тестовая привязка подтверждена: <@{targetDiscordId}> → {player.LastSeenUserName} (`{player.UserId}`). Перепривязка запрещена; путь jury активен, допуск jury ≥ 1.";
    }

    public async Task<string> ExpireDefenseAsync(long caseId, ulong actorDiscordId)
    {
        EnsureTestMode();
        if (caseId <= 0)
            throw new CourtRuleException("Укажите корректный номер дела.");

        await using var governance = governanceFactory();
        var courtCase = await governance.CourtCases.SingleOrDefaultAsync(value => value.Id == caseId)
            ?? throw new CourtRuleException("Дело не найдено.");
        if (courtCase.Status != CourtStatuses.Defense)
            throw new CourtRuleException($"Дело №{caseId} уже не находится на стадии защиты (текущий статус: {courtCase.Status}).");

        courtCase.DefenseDeadline = DateTime.UtcNow.AddSeconds(-1);
        courtCase.Version++;
        governance.AuditEvents.Add(new GovernanceAuditEvent
        {
            EventType = "court.test_defense_expired",
            ActorType = "discord_user",
            ActorId = actorDiscordId.ToString(),
            EntityType = "court_case",
            EntityId = caseId.ToString(),
            CreatedAt = DateTime.UtcNow,
            Payload = "{\"test_mode\":true}",
        });
        await governance.SaveChangesAsync();

        await Logger.Info($"Court test mode: defense deadline expired for case {caseId} by Discord {actorDiscordId}.");
        return $"Срок защиты по делу №{caseId} завершён в тестовом режиме.";
    }

    public async Task<int> ResetPendingJuryNotificationsAsync(long caseId, ulong actorDiscordId)
    {
        EnsureTestMode();
        if (caseId <= 0)
            throw new CourtRuleException("Укажите корректный номер дела.");

        await using var governance = governanceFactory();
        if (!await governance.CourtCases.AnyAsync(value => value.Id == caseId))
            throw new CourtRuleException("Дело не найдено.");

        var invitations = await governance.Invitations
            .Where(value => value.EntityType == "court_case" &&
                            value.EntityId == caseId.ToString() &&
                            value.Purpose == "jury" &&
                            value.State == InvitationStates.Pending)
            .ToListAsync();
        foreach (var invitation in invitations)
            invitation.DiscordNotifiedAt = null;

        governance.AuditEvents.Add(new GovernanceAuditEvent
        {
            EventType = "court.test_jury_notifications_reset",
            ActorType = "discord_user",
            ActorId = actorDiscordId.ToString(),
            EntityType = "court_case",
            EntityId = caseId.ToString(),
            CreatedAt = DateTime.UtcNow,
            Payload = $"{{\"test_mode\":true,\"count\":{invitations.Count}}}",
        });
        await governance.SaveChangesAsync();

        await Logger.Info(
            $"Court test mode: reset {invitations.Count} pending jury notification(s) for case {caseId} by Discord {actorDiscordId}.");
        return invitations.Count;
    }

    public async Task<string> DiagnoseCaseAsync(long caseId)
    {
        EnsureTestMode();
        if (caseId <= 0)
            throw new CourtRuleException("Укажите корректный номер дела.");

        await using var governance = governanceFactory();
        var courtCase = await governance.CourtCases.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == caseId)
            ?? throw new CourtRuleException("Дело не найдено.");

        var jurors = await governance.Jurors.AsNoTracking()
            .Where(value => value.CaseId == caseId)
            .Join(governance.Users.AsNoTracking(),
                juror => juror.UserId,
                user => user.Id,
                (juror, user) => new { juror.UserId, juror.Active, user.DiscordUserId })
            .ToListAsync();
        var guiltVotes = await governance.GuiltVotes.AsNoTracking()
            .Where(value => value.CaseId == caseId)
            .OrderBy(value => value.SubmittedAt)
            .ToListAsync();
        var sentencingVotes = await governance.SentencingVotes.AsNoTracking()
            .Where(value => value.CaseId == caseId)
            .OrderBy(value => value.SubmittedAt)
            .ToListAsync();

        var builder = new StringBuilder();
        builder.AppendLine($"Дело №{caseId}: `{courtCase.Status}`");
        builder.AppendLine($"Коллегия: {jurors.Count}, активно: {jurors.Count(value => value.Active)}; порог: {config.CourtDecisionThreshold}/{config.CourtJurySize}");
        builder.AppendLine($"Голоса о виновности: {guiltVotes.Count}; о наказании: {sentencingVotes.Count}");
        builder.AppendLine($"Начало наказания: {(courtCase.SentencingStartedAt?.ToString("O") ?? "—")}");
        builder.AppendLine($"Итоговая мера в деле: {courtCase.SanctionType ?? "—"}");

        if (sentencingVotes.Count > 0)
        {
            builder.AppendLine("Голоса о наказании:");
            foreach (var vote in sentencingVotes)
            {
                var juror = jurors.SingleOrDefault(value => value.UserId == vote.JurorUserId);
                var discord = juror is { DiscordUserId: > 0 } ? $"<@{juror.DiscordUserId}>" : vote.JurorUserId.ToString();
                var currentStage = courtCase.SentencingStartedAt != null && vote.SubmittedAt >= courtCase.SentencingStartedAt.Value;
                builder.Append("• ").Append(discord)
                    .Append(" → `").Append(vote.SanctionType).Append('`')
                    .Append("; active=").Append(juror?.Active == true ? "да" : "нет")
                    .Append("; currentStage=").Append(currentStage ? "да" : "НЕТ")
                    .Append("; ").AppendLine(vote.SubmittedAt.ToString("O"));
            }
        }

        var text = builder.ToString();
        return text.Length <= 1900 ? text : text[..1900] + "…";
    }

    private async Task EnsureTestJuryPathAsync(Guid userId)
    {
        await using var governance = governanceFactory();
        if (await governance.ServicePaths.AnyAsync(value => value.UserId == userId && value.Track == ReputationTracks.Jury))
            return;

        var paths = await governance.ServicePaths.Where(value => value.UserId == userId).OrderBy(value => value.Slot).ToListAsync();
        var now = DateTime.UtcNow;
        var freeSlot = paths.All(value => value.Slot != 1) ? (short) 1
            : paths.All(value => value.Slot != 2) ? (short) 2
            : (short) 2;
        var row = paths.SingleOrDefault(value => value.Slot == freeSlot);
        if (row == null)
        {
            governance.ServicePaths.Add(new GovernanceServicePath
            {
                UserId = userId,
                Slot = freeSlot,
                Track = ReputationTracks.Jury,
                SelectedAt = now,
                ChangedAt = now,
            });
        }
        else
        {
            row.Track = ReputationTracks.Jury;
            row.ChangedAt = now;
        }
        await governance.SaveChangesAsync();
    }

    private void EnsureTestMode()
    {
        if (!config.CourtTestMode)
            throw new CourtRuleException("Тестовые команды суда отключены. Для локального стенда задайте COURT_TEST_MODE=true.");
    }

    private static async Task<PlayerIdentity> ResolvePlayerAsync(ServerDbContext game, string query)
    {
        if (Guid.TryParse(query, out var playerId))
        {
            return await game.Player.AsNoTracking()
                .Where(value => value.UserId == playerId)
                .Select(value => new PlayerIdentity(value.UserId, value.LastSeenUserName))
                .SingleOrDefaultAsync()
                ?? throw new CourtRuleException("Игрок с таким SS14 UUID не найден в локальной базе.");
        }

        var normalized = query.ToLower();
        var matches = await game.Player.AsNoTracking()
            .Where(value => value.LastSeenUserName.ToLower() == normalized)
            .Select(value => new PlayerIdentity(value.UserId, value.LastSeenUserName))
            .Take(3)
            .ToListAsync();
        if (matches.Count == 0)
            throw new CourtRuleException($"Игрок с ником «{query}» не найден в локальной базе.");

        var exact = matches.Where(value => value.LastSeenUserName == query).ToArray();
        if (exact.Length == 1)
            return exact[0];
        if (matches.Count == 1)
            return matches[0];

        throw new CourtRuleException("Найдено несколько игроков с таким ником. Укажите SS14 UUID.");
    }

    private sealed record PlayerIdentity(Guid UserId, string LastSeenUserName);
}
