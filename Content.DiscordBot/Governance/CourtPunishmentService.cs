using System.Text.Json;
using Content.Server.Database;
using Content.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed record CourtHistoryItem(DateTime CreatedAt, string Kind, string Description, bool Active);

public sealed class CourtPunishmentService(
    Func<GovernanceDbContext> governanceFactory,
    Func<ServerDbContext> gameFactory)
{
    public async Task ExecutePendingAsync()
    {
        await using var governance = governanceFactory();
        var cases = await governance.CourtCases.AsNoTracking()
            .Where(value => value.Status == CourtStatuses.Verdict && value.Verdict == CourtVerdicts.Guilty)
            .Where(value => !governance.PunishmentExecutions.Any(execution => execution.CaseId == value.Id))
            .ToListAsync();
        foreach (var courtCase in cases)
            await ExecuteAsync(courtCase.Id);
    }

    public async Task ExecuteAsync(long caseId)
    {
        await using var governance = governanceFactory();
        var courtCase = await governance.CourtCases.SingleAsync(value => value.Id == caseId);
        if (courtCase.Status is CourtStatuses.Executed or CourtStatuses.Overturned)
            return;
        if (courtCase.Status != CourtStatuses.Verdict || courtCase.Verdict != CourtVerdicts.Guilty || courtCase.SanctionType == null)
            throw new CourtRuleException("Исполнить можно только вступившее в силу решение о виновности.");
        if (await governance.PunishmentExecutions.AnyAsync(value => value.CaseId == caseId))
            return;

        var defendant = await governance.Users.AsNoTracking().SingleAsync(value => value.Id == courtCase.DefendantUserId);
        var marker = $"[Community Court #{caseId}]";
        var now = DateTime.UtcNow;
        string externalReference;
        await using (var game = gameFactory())
        {
            switch (courtCase.SanctionType)
            {
                case CourtSanctions.Warning:
                {
                    var note = await game.AdminNotes.SingleOrDefaultAsync(value => value.PlayerUserId == defendant.Ss14UserId && value.Message.StartsWith(marker));
                    if (note == null)
                    {
                        note = game.AdminNotes.Add(new AdminNote
                        {
                            RoundId = courtCase.RoundId,
                            PlayerUserId = defendant.Ss14UserId,
                            PlaytimeAtNote = TimeSpan.Zero,
                            Message = $"{marker} Предупреждение по решению присяжных. {courtCase.Summary}",
                            Severity = NoteSeverity.Minor,
                            CreatedAt = now,
                            LastEditedAt = now,
                            Secret = false,
                            Deleted = false,
                        }).Entity;
                        await game.SaveChangesAsync();
                    }
                    externalReference = $"admin_note:{note.Id}";
                    break;
                }
                case CourtSanctions.GameBan:
                {
                    var ban = await game.Ban.SingleOrDefaultAsync(value => value.PlayerUserId == defendant.Ss14UserId && value.Reason.StartsWith(marker));
                    if (ban == null)
                    {
                        ban = game.Ban.Add(new ServerBan
                        {
                            RoundId = courtCase.RoundId,
                            PlayerUserId = defendant.Ss14UserId,
                            PlaytimeAtNote = TimeSpan.Zero,
                            BanTime = now,
                            ExpirationTime = now.AddDays(courtCase.SanctionDays!.Value),
                            Reason = $"{marker} Решение присяжных: {courtCase.Summary}",
                            Severity = NoteSeverity.Medium,
                            AutoDelete = false,
                            Hidden = false,
                        }).Entity;
                        await game.SaveChangesAsync();
                    }
                    externalReference = $"server_ban:{ban.Id}";
                    break;
                }
                case CourtSanctions.JobBan:
                {
                    var ban = await game.RoleBan.SingleOrDefaultAsync(value => value.PlayerUserId == defendant.Ss14UserId && value.Reason.StartsWith(marker));
                    if (ban == null)
                    {
                        ban = game.RoleBan.Add(new ServerRoleBan
                        {
                            RoundId = courtCase.RoundId,
                            PlayerUserId = defendant.Ss14UserId,
                            PlaytimeAtNote = TimeSpan.Zero,
                            BanTime = now,
                            ExpirationTime = now.AddDays(courtCase.SanctionDays!.Value),
                            Reason = $"{marker} Решение присяжных: {courtCase.Summary}",
                            Severity = NoteSeverity.Medium,
                            Hidden = false,
                            RoleId = courtCase.SanctionRole!,
                        }).Entity;
                        await game.SaveChangesAsync();
                    }
                    externalReference = $"server_role_ban:{ban.Id}";
                    break;
                }
                default:
                    throw new CourtRuleException("Неизвестная мера наказания.");
            }
        }

        governance.PunishmentExecutions.Add(new GovernancePunishmentExecution
        {
            CaseId = caseId,
            SanctionType = courtCase.SanctionType,
            ExternalReference = externalReference,
            ExecutedAt = now,
            IdempotencyKey = $"court:{caseId}:execute",
        });
        courtCase.Status = CourtStatuses.Executed;
        courtCase.ExecutedAt = now;
        courtCase.ExecutionReference = externalReference;
        courtCase.Version++;
        AddAudit(governance, "court.punishment_executed", "system", null, caseId,
            new { sanction = courtCase.SanctionType, external_reference = externalReference });
        await governance.SaveChangesAsync();
    }

    public async Task OverturnAsync(long caseId, ulong actorDiscordId, string reason)
    {
        reason = reason.Trim();
        if (reason.Length is < 20 or > 1500)
            throw new CourtRuleException("Причина отмены должна содержать от 20 до 1500 символов.");
        await using var governance = governanceFactory();
        var courtCase = await governance.CourtCases.SingleOrDefaultAsync(value => value.Id == caseId)
            ?? throw new CourtRuleException("Дело не найдено.");
        if (courtCase.Status == CourtStatuses.Overturned)
            return;
        if (courtCase.Status is not (CourtStatuses.Verdict or CourtStatuses.Executed))
            throw new CourtRuleException("Отменить можно только вынесенное решение.");

        var now = DateTime.UtcNow;
        var execution = await governance.PunishmentExecutions.SingleOrDefaultAsync(value => value.CaseId == caseId);
        if (execution is { RevertedAt: null })
        {
            var reference = execution.ExternalReference.Split(':', 2);
            if (reference.Length == 2 && int.TryParse(reference[1], out var id))
            {
                await using var game = gameFactory();
                switch (reference[0])
                {
                    case "server_ban" when !await game.Unban.AnyAsync(value => value.BanId == id):
                        game.Unban.Add(new ServerUnban { BanId = id, UnbanTime = now });
                        break;
                    case "server_role_ban" when !await game.RoleUnban.AnyAsync(value => value.BanId == id):
                        game.RoleUnban.Add(new ServerRoleUnban { BanId = id, UnbanTime = now });
                        break;
                    case "admin_note":
                        var note = await game.AdminNotes.SingleOrDefaultAsync(value => value.Id == id);
                        if (note != null)
                        {
                            note.Deleted = true;
                            note.DeletedAt = now;
                        }
                        break;
                }
                await game.SaveChangesAsync();
            }
            execution.RevertedAt = now;
        }

        courtCase.Status = CourtStatuses.Overturned;
        courtCase.OverturnedAt = now;
        courtCase.OverturnReason = reason;
        courtCase.Version++;
        governance.LeadershipOverrides.Add(new GovernanceLeadershipOverride
        {
            EntityType = "court_case",
            EntityId = caseId.ToString(),
            Action = "overturn",
            Reason = reason,
            ActorDiscordId = checked((long) actorDiscordId),
            CreatedAt = now,
        });
        AddAudit(governance, "leadership.court_overturned", "discord_user", actorDiscordId.ToString(), caseId, new { reason });
        await governance.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<CourtHistoryItem>> GetSentencingHistoryAsync(long caseId, ulong jurorDiscordId)
    {
        await using var governance = governanceFactory();
        var juror = await governance.Users.AsNoTracking().SingleOrDefaultAsync(value => value.DiscordUserId == checked((long) jurorDiscordId))
            ?? throw new CourtRuleException("Discord-аккаунт не привязан к аккаунту SS14.");
        var courtCase = await governance.CourtCases.AsNoTracking().SingleOrDefaultAsync(value => value.Id == caseId)
            ?? throw new CourtRuleException("Дело не найдено.");
        if (courtCase.Status != CourtStatuses.Sentencing ||
            !await governance.Jurors.AnyAsync(value => value.CaseId == caseId && value.UserId == juror.Id && value.Active))
            throw new CourtRuleException("История доступна только действующим присяжным на стадии назначения меры.");
        var defendant = await governance.Users.AsNoTracking().SingleAsync(value => value.Id == courtCase.DefendantUserId);
        var result = new List<CourtHistoryItem>();
        await using var game = gameFactory();
        result.AddRange(await game.AdminNotes.AsNoTracking()
            .Where(value => value.PlayerUserId == defendant.Ss14UserId && !value.Deleted && !value.Secret)
            .OrderByDescending(value => value.CreatedAt).Take(10)
            .Select(value => new CourtHistoryItem(value.CreatedAt, "Замечание", value.Message, true)).ToListAsync());
        result.AddRange(await game.Ban.AsNoTracking()
            .Where(value => value.PlayerUserId == defendant.Ss14UserId && !value.Hidden)
            .OrderByDescending(value => value.BanTime).Take(10)
            .Select(value => new CourtHistoryItem(value.BanTime, "Бан игры", value.Reason,
                value.Unban == null && (value.ExpirationTime == null || value.ExpirationTime > DateTime.UtcNow))).ToListAsync());
        result.AddRange(await game.RoleBan.AsNoTracking()
            .Where(value => value.PlayerUserId == defendant.Ss14UserId && !value.Hidden)
            .OrderByDescending(value => value.BanTime).Take(10)
            .Select(value => new CourtHistoryItem(value.BanTime, "Бан роли " + value.RoleId, value.Reason,
                value.Unban == null && (value.ExpirationTime == null || value.ExpirationTime > DateTime.UtcNow))).ToListAsync());
        return result.OrderByDescending(value => value.CreatedAt).Take(20).ToArray();
    }

    private static void AddAudit(GovernanceDbContext db, string eventType, string actorType, string? actorId, long caseId, object payload)
    {
        db.AuditEvents.Add(new GovernanceAuditEvent
        {
            EventType = eventType,
            ActorType = actorType,
            ActorId = actorId,
            EntityType = "court_case",
            EntityId = caseId.ToString(),
            CreatedAt = DateTime.UtcNow,
            Payload = JsonSerializer.Serialize(payload),
        });
    }
}
