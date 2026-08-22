using System.Text.Json;
using Content.Server.Database;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed record GovernanceIdentity(
    Guid GovernanceUserId,
    Guid Ss14UserId,
    long? DiscordUserId,
    string Name);

public sealed class GovernanceIdentityService(
    Func<GovernanceDbContext> governanceFactory,
    Func<ServerDbContext> gameFactory)
{
    public async Task EnsureAllSs14UsersAsync()
    {
        await using var governance = governanceFactory();
        await governance.Database.ExecuteSqlRawAsync("""
            INSERT INTO governance.users(ss14_user_id, discord_user_id, civic_rating_cache, created_at, updated_at)
            SELECT p.user_id, NULL, 500, p.first_seen_time, now()
            FROM player p
            ON CONFLICT (ss14_user_id) DO NOTHING
            """);

        var links = await governance.Database.SqlQueryRaw<CurrentGameLink>("""
            SELECT player_id AS "Ss14UserId", discord_id::bigint AS "DiscordUserId"
            FROM rmc_linked_accounts
            WHERE discord_id > 0
            """).ToListAsync();
        foreach (var link in links)
        {
            try
            {
                await AttachDiscordAsync(governance, link.Ss14UserId, link.DiscordUserId, "game_account_link", null);
            }
            catch (CourtRuleException exception)
            {
                // The game-link table may contain a stale row written by an old rebind-capable client.
                // Never let that row overwrite the permanent Governance identity.
                await Logger.Info(
                    $"[WARNING] Governance rejected inconsistent game identity Discord {link.DiscordUserId} -> SS14 {link.Ss14UserId}: {exception.Message}");
            }
        }
        await governance.SaveChangesAsync();
    }

    public async Task<GovernanceUser> RequireSs14UserAsync(Guid ss14UserId)
    {
        await using var game = gameFactory();
        var exists = await game.Player.AsNoTracking().AnyAsync(value => value.UserId == ss14UserId);
        if (!exists)
            throw new CourtRuleException("Аккаунт SS14 не найден.");

        await using var governance = governanceFactory();
        var user = await governance.Users.SingleOrDefaultAsync(value => value.Ss14UserId == ss14UserId);
        if (user == null)
        {
            var firstSeen = await game.Player.AsNoTracking().Where(value => value.UserId == ss14UserId)
                .Select(value => value.FirstSeenTime).SingleAsync();
            user = governance.Users.Add(new GovernanceUser
            {
                Id = Guid.NewGuid(),
                Ss14UserId = ss14UserId,
                DiscordUserId = null,
                CivicRatingCache = ReputationPolicy.NeutralScore,
                CreatedAt = firstSeen.ToUniversalTime(),
                UpdatedAt = DateTime.UtcNow,
            }).Entity;
            await governance.SaveChangesAsync();
        }
        return user;
    }

    public async Task<GovernanceUser> RequireSs14UserByNicknameAsync(string nickname)
    {
        nickname = nickname.Trim();
        if (nickname.Length is < 1 or > 64)
            throw new CourtRuleException("Игровой никнейм должен содержать от 1 до 64 символов.");
        var lowered = nickname.ToLowerInvariant();
        await using var game = gameFactory();
        var matches = await game.Player.AsNoTracking()
            .Where(value => value.LastSeenUserName.ToLower() == lowered)
            .Select(value => new { value.UserId, value.LastSeenUserName })
            .Take(3)
            .ToListAsync();
        if (matches.Count == 0)
            throw new CourtRuleException($"Игрок с никнеймом «{nickname}» не найден.");
        var exact = matches.Where(value => value.LastSeenUserName == nickname).ToArray();
        var selected = exact.Length == 1 ? exact[0] : matches.Count == 1 ? matches[0] :
            throw new CourtRuleException("Найдено несколько игроков с таким никнеймом. Укажите точный регистр.");
        return await RequireSs14UserAsync(selected.UserId);
    }

    public async Task ValidatePermanentLinkAsync(Guid ss14UserId, ulong discordId)
    {
        if (discordId == 0 || discordId > long.MaxValue)
            throw new CourtRuleException("Некорректный Discord ID.");

        await RequireSs14UserAsync(ss14UserId);
        await using var governance = governanceFactory();
        var user = await governance.Users.SingleAsync(value => value.Ss14UserId == ss14UserId);
        await ValidatePermanentLinkAsync(governance, user, checked((long) discordId));
    }

    public async Task SyncLinkedAccountAsync(Guid ss14UserId, ulong discordId)
    {
        if (discordId == 0 || discordId > long.MaxValue)
            throw new CourtRuleException("Некорректный Discord ID.");

        await using (var game = gameFactory())
        {
            var exactPairExists = await game.RMCLinkedAccounts.AsNoTracking().AnyAsync(value =>
                value.PlayerId == ss14UserId && value.DiscordId == discordId);
            if (!exactPairExists)
                throw new CourtRuleException("Текущая игровая привязка не соответствует запрошенной паре SS14/Discord.");
        }

        await RequireSs14UserAsync(ss14UserId);
        await using var governance = governanceFactory();
        await AttachDiscordAsync(governance, ss14UserId, checked((long) discordId), "game_account_link", discordId.ToString());
        var user = await governance.Users.SingleAsync(value => value.Ss14UserId == ss14UserId);
        await EnsureBaselineQualificationsAsync(governance, user.Id);
        await governance.SaveChangesAsync();
    }

    public async Task<Guid?> GetPermanentSs14UserIdAsync(ulong discordId)
    {
        if (discordId == 0 || discordId > long.MaxValue)
            return null;
        await using var governance = governanceFactory();
        var values = await governance.Database.SqlQuery<Guid>($"""
            SELECT ss14_user_id AS "Value"
            FROM governance.identity_bindings
            WHERE discord_user_id = {checked((long) discordId)}
            """).ToListAsync();
        return values.Count == 0 ? null : values.Single();
    }

    public async Task<string> DiagnoseLinkAsync(ulong discordId)
    {
        var permanentSs14 = await GetPermanentSs14UserIdAsync(discordId);
        await using var game = gameFactory();
        var current = await game.RMCLinkedAccounts.AsNoTracking()
            .Where(value => value.DiscordId == discordId)
            .Select(value => new { value.PlayerId, value.Player.LastSeenUserName })
            .SingleOrDefaultAsync();

        string permanentText;
        if (permanentSs14 == null)
        {
            permanentText = "не зафиксирована";
        }
        else
        {
            var permanentName = await game.Player.AsNoTracking()
                .Where(value => value.UserId == permanentSs14.Value)
                .Select(value => value.LastSeenUserName)
                .SingleOrDefaultAsync() ?? "неизвестный игрок";
            permanentText = $"{permanentName} (`{permanentSs14}`)";
        }

        var currentText = current == null
            ? "отсутствует"
            : $"{current.LastSeenUserName} (`{current.PlayerId}`)";
        var consistent = permanentSs14 == null
            ? current == null
            : current?.PlayerId == permanentSs14.Value;
        return $"Discord `{discordId}`\nПостоянная Governance-связь: {permanentText}\nТекущая игровая связь: {currentText}\nСостояние: {(consistent ? "совпадает" : "РАСХОЖДЕНИЕ")}.";
    }

    public async Task<string> RepairGameLinkToPermanentAsync(ulong discordId, ulong actorDiscordId)
    {
        var permanentSs14 = await GetPermanentSs14UserIdAsync(discordId)
            ?? throw new CourtRuleException("Для этого Discord нет постоянной Governance-привязки; создавать новую пару этой командой запрещено.");

        await using var game = gameFactory();
        await using var transaction = await game.Database.BeginTransactionAsync();
        var currentByDiscord = await game.RMCLinkedAccounts
            .Include(value => value.Player)
            .ThenInclude(value => value.Patron)
            .SingleOrDefaultAsync(value => value.DiscordId == discordId);
        if (currentByDiscord?.PlayerId == permanentSs14)
        {
            await transaction.CommitAsync();
            return $"Игровая связь уже соответствует постоянной Governance-паре: `{discordId}` → `{permanentSs14}`.";
        }

        var permanentPlayerLink = await game.RMCLinkedAccounts.AsNoTracking()
            .SingleOrDefaultAsync(value => value.PlayerId == permanentSs14);
        if (permanentPlayerLink != null && permanentPlayerLink.DiscordId != discordId)
        {
            throw new CourtRuleException(
                "В игровой БД постоянный SS14 сейчас связан с другим Discord. Автоматическое восстановление остановлено, чтобы не затронуть третью идентичность.");
        }

        var previousPlayerId = currentByDiscord?.PlayerId;
        if (currentByDiscord != null)
        {
            if (currentByDiscord.Player.Patron is { } stalePatron)
            {
                currentByDiscord.Player.Patron = null;
                game.RMCPatrons.Remove(stalePatron);
            }
            game.RMCLinkedAccounts.Remove(currentByDiscord);
        }

        if (permanentPlayerLink == null)
        {
            var discord = await game.RMCDiscordAccounts.SingleOrDefaultAsync(value => value.Id == discordId);
            if (discord == null)
                game.RMCDiscordAccounts.Add(new RMCDiscordAccount { Id = discordId });
            game.RMCLinkedAccounts.Add(new RMCLinkedAccount
            {
                PlayerId = permanentSs14,
                DiscordId = discordId,
            });
            game.RMCLinkedAccountLogs.Add(new RMCLinkedAccountLogs
            {
                PlayerId = permanentSs14,
                DiscordId = discordId,
                At = DateTime.UtcNow,
            });
        }

        await game.SaveChangesAsync();
        await transaction.CommitAsync();

        await using var governance = governanceFactory();
        var governanceUser = await governance.Users.AsNoTracking().SingleAsync(value => value.Ss14UserId == permanentSs14);
        AddAudit(governance, "identity.game_link_repaired", "discord_user", actorDiscordId.ToString(), governanceUser.Id,
            new { discord_user_id = discordId, permanent_ss14_user_id = permanentSs14, previous_game_ss14_user_id = previousPlayerId });
        await governance.SaveChangesAsync();

        return $"Игровая таблица восстановлена по постоянной Governance-паре: `{discordId}` → `{permanentSs14}`. Новая идентичность не создавалась.";
    }

    public async Task<GovernanceUser> RequireDiscordUserAsync(ulong discordId)
    {
        await using var game = gameFactory();
        var ss14UserId = await game.RMCLinkedAccounts.AsNoTracking()
            .Where(value => value.DiscordId == discordId)
            .Select(value => (Guid?) value.PlayerId)
            .SingleOrDefaultAsync();
        if (ss14UserId == null)
            throw new CourtRuleException("Discord-аккаунт не привязан к аккаунту SS14. Репутация SS14-профиля сохраняется, но Discord-функции требуют привязку.");

        await ValidatePermanentLinkAsync(ss14UserId.Value, discordId);
        await using var governance = governanceFactory();
        var user = await governance.Users.SingleAsync(value => value.Ss14UserId == ss14UserId.Value);
        await AttachDiscordAsync(governance, user.Ss14UserId, checked((long) discordId), "discord_command", discordId.ToString());
        await EnsureBaselineQualificationsAsync(governance, user.Id);
        await governance.SaveChangesAsync();
        return user;
    }

    public async Task<GovernanceIdentity> GetIdentityAsync(Guid governanceUserId)
    {
        await using var governance = governanceFactory();
        var user = await governance.Users.AsNoTracking().SingleOrDefaultAsync(value => value.Id == governanceUserId)
            ?? throw new CourtRuleException("Профиль Governance не найден.");
        await using var game = gameFactory();
        var name = await game.Player.AsNoTracking().Where(value => value.UserId == user.Ss14UserId)
            .Select(value => value.LastSeenUserName).SingleOrDefaultAsync() ?? user.Ss14UserId.ToString();
        return new GovernanceIdentity(user.Id, user.Ss14UserId, user.DiscordUserId, name);
    }

    public async Task DetachDiscordIfStaleAsync(Guid governanceUserId, string source = "reconcile")
    {
        await using var governance = governanceFactory();
        var user = await governance.Users.AsNoTracking().SingleAsync(value => value.Id == governanceUserId);
        if (user.DiscordUserId == null)
            return;
        await using var game = gameFactory();
        var stillLinked = await game.RMCLinkedAccounts.AsNoTracking().AnyAsync(value =>
            value.PlayerId == user.Ss14UserId && (long) value.DiscordId == user.DiscordUserId.Value);
        if (stillLinked)
            return;

        // A missing/mismatched game row is an operational inconsistency, not permission to rebind.
        // Permanent Governance identity is deliberately left untouched.
        throw new CourtRuleException(
            $"Постоянная связь SS14 {user.Ss14UserId} / Discord {user.DiscordUserId} расходится с игровой БД; автоматическая отвязка запрещена ({source}).");
    }

    private static async Task AttachDiscordAsync(
        GovernanceDbContext governance,
        Guid ss14UserId,
        long discordUserId,
        string source,
        string? actorId)
    {
        if (discordUserId <= 0)
            throw new CourtRuleException("Discord ID должен быть положительным snowflake.");
        var user = await governance.Users.SingleOrDefaultAsync(value => value.Ss14UserId == ss14UserId)
            ?? throw new CourtRuleException("Governance-профиль SS14 ещё не создан.");

        await ValidatePermanentLinkAsync(governance, user, discordUserId);
        if (user.DiscordUserId == discordUserId)
        {
            if (!await governance.IdentityLinks.AnyAsync(value =>
                    value.UserId == user.Id && value.DiscordUserId == discordUserId))
            {
                governance.IdentityLinks.Add(new GovernanceIdentityLink
                {
                    UserId = user.Id,
                    DiscordUserId = discordUserId,
                    LinkedAt = DateTime.UtcNow,
                    Source = source,
                    Metadata = "{}",
                });
            }
            return;
        }

        if (user.DiscordUserId != null)
            throw new CourtRuleException("Этот SS14-аккаунт уже навсегда связан с другим Discord. Перепривязка запрещена.");

        user.DiscordUserId = discordUserId;
        user.UpdatedAt = DateTime.UtcNow;
        if (!await governance.IdentityLinks.AnyAsync(value =>
                value.UserId == user.Id && value.DiscordUserId == discordUserId))
        {
            governance.IdentityLinks.Add(new GovernanceIdentityLink
            {
                UserId = user.Id,
                DiscordUserId = discordUserId,
                LinkedAt = DateTime.UtcNow,
                Source = source,
                Metadata = "{}",
            });
        }
        AddAudit(governance, "identity.discord_linked", actorId == null ? "system" : "discord_user", actorId, user.Id,
            new { discord_user_id = discordUserId, source, immutable = true });
    }

    private static async Task ValidatePermanentLinkAsync(
        GovernanceDbContext governance,
        GovernanceUser user,
        long discordUserId)
    {
        if (user.DiscordUserId is { } currentDiscord && currentDiscord != discordUserId)
            throw new CourtRuleException("Этот SS14-аккаунт уже связан с другим Discord. Перепривязка запрещена.");

        var otherCurrent = await governance.Users.AsNoTracking().SingleOrDefaultAsync(value =>
            value.DiscordUserId == discordUserId && value.Id != user.Id);
        if (otherCurrent != null)
            throw new CourtRuleException("Этот Discord уже связан с другим SS14-аккаунтом. Перепривязка запрещена.");

        var boundDiscords = await governance.Database.SqlQuery<long>($"""
            SELECT discord_user_id AS "Value"
            FROM governance.identity_bindings
            WHERE user_id = {user.Id}
            """).ToListAsync();
        if (boundDiscords.Count > 0 && boundDiscords.Single() != discordUserId)
            throw new CourtRuleException("Этот SS14-аккаунт уже имеет постоянную Discord-привязку. Перепривязка запрещена.");

        var boundUsers = await governance.Database.SqlQuery<Guid>($"""
            SELECT user_id AS "Value"
            FROM governance.identity_bindings
            WHERE discord_user_id = {discordUserId}
            """).ToListAsync();
        if (boundUsers.Count > 0 && boundUsers.Single() != user.Id)
            throw new CourtRuleException("Этот Discord уже имеет постоянную SS14-привязку. Перепривязка запрещена.");
    }

    private static async Task EnsureBaselineQualificationsAsync(GovernanceDbContext governance, Guid userId)
    {
        var selectedPaths = (await governance.ServicePaths.AsNoTracking()
                .Where(value => value.UserId == userId)
                .Select(value => value.Track)
                .ToListAsync())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var track in ReputationTracks.ServicePaths)
        {
            var qualification = await governance.Qualifications
                .SingleOrDefaultAsync(value => value.UserId == userId && value.Track == track);
            var baselineLevel = selectedPaths.Contains(track) ? (short) 1 : (short) 0;

            if (qualification == null)
            {
                governance.Qualifications.Add(new GovernanceQualification
                {
                    UserId = userId,
                    Track = track,
                    Level = baselineLevel,
                    UpdatedAt = DateTime.UtcNow,
                });
                continue;
            }

            if (baselineLevel == 0 && qualification.Level > 0)
            {
                qualification.Level = 0;
                qualification.UpdatedAt = DateTime.UtcNow;
            }
            else if (baselineLevel == 1 && qualification.Level < 1)
            {
                qualification.Level = 1;
                qualification.UpdatedAt = DateTime.UtcNow;
            }
        }
    }

    private static void AddAudit(
        GovernanceDbContext governance,
        string eventType,
        string actorType,
        string? actorId,
        Guid userId,
        object payload)
    {
        governance.AuditEvents.Add(new GovernanceAuditEvent
        {
            EventType = eventType,
            ActorType = actorType,
            ActorId = actorId,
            EntityType = "user",
            EntityId = userId.ToString(),
            CreatedAt = DateTime.UtcNow,
            Payload = JsonSerializer.Serialize(payload),
        });
    }

    private sealed record CurrentGameLink(Guid Ss14UserId, long DiscordUserId);
}
