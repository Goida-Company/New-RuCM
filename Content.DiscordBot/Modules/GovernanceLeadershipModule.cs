using Content.DiscordBot.Governance;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace Content.DiscordBot.Modules;

[Group("руководство", "Контролируемые действия руководства с обязательной причиной")]
public sealed class GovernanceLeadershipModule(
    GovernanceCommunityService community,
    GovernanceIdentityService identities,
    ReputationService reputation,
    CandidateSelectionService selection,
    CommunityCourtService court,
    CourtPunishmentService punishments,
    CourtDiscordCoordinator discord,
    ModerationTrustService moderationTrust,
    Config config) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("отменить-решение", "Отменить решение суда и откатить наказание")]
    public Task OverturnAsync(long courtCase, string reason) => ExecuteAsync(async () =>
    {
        await punishments.OverturnAsync(courtCase, Context.User.Id, reason);
        await discord.PublishLeadershipNoticeAsync(courtCase, $"Решение по делу №{courtCase} отменено руководством",
            $"Причина: {reason}\nИсполненная мера отозвана. Обычный апелляционный пересмотр не предусмотрен.", Color.Purple);
        await FollowupAsync($"Решение по делу №{courtCase} отменено; исполненная мера отозвана.", ephemeral: true);
    });

    [SlashCommand("ложная-жалоба", "Зафиксировать заведомо ложную жалобу как серьёзное репутационное событие")]
    public Task FalseReportAsync(long courtCase, string reason) => ExecuteAsync(async () =>
    {
        await community.MarkFalseReportAsync(courtCase, Context.User.Id, reason);
        await discord.PublishLeadershipNoticeAsync(courtCase, $"По делу №{courtCase} зафиксирована ложная жалоба",
            $"Причина: {reason}", Color.DarkRed);
        await FollowupAsync($"Для дела №{courtCase} зафиксирована заведомо ложная жалоба. Репутационный движок учтёт её как серьёзное наблюдение.", ephemeral: true);
    });

    [SlashCommand("квалификация", "Изменить квалификацию пользователя вручную")]
    public Task QualificationAsync(IUser user,
        [Choice("Модерация", ReputationTracks.Moderation)]
        [Choice("Присяжные", ReputationTracks.Jury)]
        [Choice("События", ReputationTracks.Event)]
        [Choice("Контрибьюторство", ReputationTracks.Contributor)] string track,
        int level) => ExecuteAsync(async () =>
    {
        await community.SetQualificationAsync(Context.User.Id, user.Id, track, checked((short) level));
        await FollowupAsync($"Квалификация `{track}` пользователя {user.Mention}: {level}.", ephemeral: true);
    });

    [SlashCommand("профиль-ss14", "Проверить Governance-профиль игрока SS14 без изменения данных")]
    public Task Ss14ProfileAsync(
        [Summary("игрок", "Игровой никнейм SS14; Discord-привязка не требуется")] string player) => ExecuteAsync(async () =>
    {
        var target = await community.RequireSs14UserByNicknameAsync(player);
        var profile = await reputation.GetProfileAsync(target.Id);
        var discordText = profile.DiscordUserId is > 0
            ? $"<@{profile.DiscordUserId}> (`{profile.DiscordUserId}`)"
            : "не привязан";
        var paths = profile.Paths.Count == 0
            ? "не выбраны"
            : string.Join("\n", profile.Paths.Select(value =>
                $"{(value.Slot == 1 ? "основной" : "дополнительный")}: `{value.Track}`"));

        await FollowupAsync(embed: new EmbedBuilder()
            .WithTitle($"Governance-профиль • {profile.Name}")
            .AddField("Governance user_id", $"`{profile.UserId}`")
            .AddField("SS14 UUID", $"`{profile.Ss14UserId}`")
            .AddField("Discord", discordText, true)
            .AddField("Общая репутация", $"{profile.General.Score}/1000", true)
            .AddField("Надёжность", $"средняя {profile.General.Mean:P1}\nнижняя 90% граница {profile.General.LowerBound:P1}", true)
            .AddField("Игровая активность",
                $"{profile.Activity.OverallHours:F0} ч • {profile.Activity.ActiveWeeks} активных нед. • аккаунту {profile.Activity.AccountAgeDays} дн.\n" +
                $"индекс {profile.Activity.ActivityIndex:P0} • вес свидетельства {profile.Activity.EvidenceWeight:F2}")
            .AddField("Пути участия", paths)
            .AddField("Допуск", profile.Suspended ? "приостановлен" : "активен", true)
            .WithColor(profile.Suspended ? Color.Red : Color.DarkBlue)
            .WithFooter("Read-only диагностика Identity / Reputation v2")
            .Build(), ephemeral: true);
    });

    [SlashCommand("диагностика-отбора", "Проверить жёсткие условия допуска пользователя в пул кандидатов")]
    public Task CandidateEligibilityAsync(
        IUser user,
        [Choice("Модерация", ReputationTracks.Moderation)]
        [Choice("Community Court", ReputationTracks.Jury)]
        [Choice("События", ReputationTracks.Event)]
        [Choice("Контрибьюторство", ReputationTracks.Contributor)] string track,
        [Summary("минимум", "Минимальная квалификация I–IV")] int minimum = 1) => ExecuteAsync(async () =>
    {
        if (minimum is < 1 or > 4)
            throw new CourtRuleException("Минимальная квалификация должна быть от I до IV.");

        var target = await community.RequireUserAsync(user.Id);
        var diagnostic = await selection.DiagnoseBaseEligibilityAsync(target.Id, track, checked((short) minimum));
        var pathText = diagnostic.PathRequirementBypassed
            ? "обойдён только тестовым режимом"
            : diagnostic.PathSelected ? "выбран" : "НЕ выбран";
        var discordText = diagnostic.DiscordRequired
            ? diagnostic.DiscordLinked ? "привязан" : "НЕ привязан"
            : "для этого направления не обязателен";

        await FollowupAsync(embed: new EmbedBuilder()
            .WithTitle($"Диагностика отбора • {user.Username}")
            .WithDescription(diagnostic.Eligible
                ? "**Базовые условия пройдены.** Пользователь может войти в пул до контекстных исключений и Thompson Sampling."
                : "**Базовые условия НЕ пройдены.** Thompson Sampling до этого пользователя не дойдёт.")
            .AddField("Направление", $"`{diagnostic.Track}`", true)
            .AddField("Квалификация", $"{diagnostic.QualificationLevel} / требуется {diagnostic.RequiredQualification}", true)
            .AddField("Путь участия", pathText, true)
            .AddField("Общий допуск", diagnostic.Suspended ? "ПРИОСТАНОВЛЕН" : "активен", true)
            .AddField("Discord", discordText, true)
            .AddField("Активный game/job ban", diagnostic.HasActiveBan ? "ДА" : "нет", true)
            .WithColor(diagnostic.Eligible ? Color.Green : Color.Orange)
            .WithFooter("Проверяются жёсткие фильтры; конфликты, cooldown, активные назначения и доступность проверяются при конкретном отборе.")
            .Build(), ephemeral: true);
    });

    [SlashCommand("симуляция-отбора", "Смоделировать реальный Thompson-отбор без создания приглашений")]
    public Task CandidateSimulationAsync(
        [Choice("Модерация", ReputationTracks.Moderation)]
        [Choice("Community Court", ReputationTracks.Jury)]
        [Choice("События", ReputationTracks.Event)]
        [Choice("Контрибьюторство", ReputationTracks.Contributor)] string track,
        [Summary("минимум", "Минимальная квалификация I–IV")] int minimum = 1,
        [Summary("итерации", "От 50 до 5000; обычно достаточно 500–1000")] int iterations = 500,
        [Summary("cooldown-часы", "Исключить недавно назначавшихся; 0–720 часов")] int cooldownHours = 0) => ExecuteAsync(async () =>
    {
        if (minimum is < 1 or > 4)
            throw new CourtRuleException("Минимальная квалификация должна быть от I до IV.");
        if (iterations is < 50 or > 5000)
            throw new CourtRuleException("Для симуляции укажите от 50 до 5000 итераций.");
        if (cooldownHours is < 0 or > 720)
            throw new CourtRuleException("Cooldown должен быть от 0 до 720 часов.");

        IReadOnlySet<ulong>? available = null;
        if (track is ReputationTracks.Moderation or ReputationTracks.Jury or ReputationTracks.Event)
            available = await GuildMembersAsync();

        var result = await selection.SimulateAsync(
            track,
            checked((short) minimum),
            iterations,
            available,
            TimeSpan.FromHours(cooldownHours));

        if (result.PoolSize == 0)
        {
            await FollowupAsync(
                $"Пул `{track}` пуст после жёстких фильтров, текущих pending/active назначений, банов" +
                (cooldownHours > 0 ? $" и cooldown {cooldownHours} ч." : "."),
                ephemeral: true);
            return;
        }

        var lines = new List<string>();
        foreach (var entry in result.Entries.Take(12))
        {
            var identity = await identities.GetIdentityAsync(entry.UserId);
            var transport = entry.DiscordUserId is > 0 ? $"<@{entry.DiscordUserId}>" : "без Discord";
            var rate = entry.Wins / (double) result.Iterations;
            var trackState = entry.TrackEvidenceWeight < 1.0
                ? $"направление: недостаточно данных, evidence {entry.TrackEvidenceWeight:F1}"
                : $"направление {entry.TrackScore}/1000, LB90 {entry.TrackLowerBound:P0}, evidence {entry.TrackEvidenceWeight:F1}";
            lines.Add(
                $"• **{identity.Name}** ({transport}) — **{rate:P1}** ({entry.Wins}/{result.Iterations}); " +
                $"квал. {entry.QualificationLevel}; {trackState}; общая {entry.GeneralScore}/1000");
        }

        var description = string.Join("\n", lines);
        if (description.Length > 3800)
            description = description[..3800] + "…";

        await FollowupAsync(embed: new EmbedBuilder()
            .WithTitle($"Симуляция отбора • {track}")
            .WithDescription(description)
            .AddField("Пул", result.PoolSize, true)
            .AddField("Итерации", result.Iterations, true)
            .AddField("Seed", result.Seed, true)
            .WithColor(result.PoolSize >= 2 ? Color.Green : Color.Orange)
            .WithFooter(result.PoolSize >= 2
                ? "Тот же Thompson Sampling, фильтр членства в Discord и текущий пул. Приглашения и назначения не создаются."
                : "В пуле только один кандидат — распределение Thompson Sampling пока неинформативно.")
            .Build(), ephemeral: true);
    });

    [SlashCommand("диагностика-привязки", "Сравнить постоянную Governance-связь с текущей игровой связью")]
    public Task DiagnoseIdentityAsync(IUser user) => ExecuteAsync(async () =>
    {
        await FollowupAsync(await identities.DiagnoseLinkAsync(user.Id), ephemeral: true);
    });

    [SlashCommand("восстановить-привязку", "Восстановить игровую таблицу строго по уже существующей постоянной Governance-связи")]
    public Task RepairIdentityAsync(IUser user) => ExecuteAsync(async () =>
    {
        await FollowupAsync(await identities.RepairGameLinkToPermanentAsync(user.Id, Context.User.Id), ephemeral: true);
    });

    [SlashCommand("вклад", "Зафиксировать подтверждённый вклад игрока в проект")]
    public Task ContributionAsync(
        [Summary("игрок", "Игровой никнейм SS14; Discord-привязка не требуется")] string player,
        [Summary("ссылка", "PR, документ, задача или другой проверяемый идентификатор")] string reference,
        [Summary("тип", "Код, локализация, карта, графика, документация, тестирование и т. п.")] string kind,
        [Summary("значимость", "0.1–3.0: масштаб полезного изменения")] double impact,
        [Summary("качество", "0.1–1.5: качество исполнения")] double quality,
        [Summary("устойчивость", "0.1–1.5: подтверждённая устойчивость результата")] double stability) => ExecuteAsync(async () =>
    {
        var target = await community.RequireSs14UserByNicknameAsync(player);
        var contribution = await reputation.RecordContributionAsync(
            target.Id,
            reference,
            kind,
            impact,
            quality,
            stability,
            DateTime.UtcNow,
            Context.User.Id);
        await FollowupAsync(
            $"Вклад №{contribution.Id} игрока **{player}** зафиксирован. " +
            "Репутация рассчитывается по значимости, качеству и устойчивости с насыщением — размер diff сам по себе очков не даёт.",
            ephemeral: true);
    });

    [SlashCommand("допуск", "Приостановить или восстановить участие во всех контурах")]
    public Task SuspensionAsync(IUser user, bool suspended, string reason) => ExecuteAsync(async () =>
    {
        await community.SetSuspendedAsync(Context.User.Id, user.Id, suspended, reason);
        await FollowupAsync($"Допуск пользователя {user.Mention}: {(suspended ? "приостановлен" : "восстановлен")}.", ephemeral: true);
    });

    [SlashCommand("аудит-действия", "Случайно назначить независимый аудит исполненного действия дежурного")]
    public Task AssignModerationAuditAsync(long action) => ExecuteAsync(async () =>
    {
        var assignment = await moderationTrust.AssignRandomReviewAsync(action);
        var reviewer = assignment.ReviewerDiscordId is > 0 ? $"<@{assignment.ReviewerDiscordId}>" : "SS14-профиль без Discord";
        await FollowupAsync(
            $"Для действия №{action} назначен независимый рецензент {reviewer}. " +
            $"Приглашение №{assignment.InvitationId} действительно до {assignment.ExpiresAt:u}.",
            ephemeral: true);
    });

    private async Task<IReadOnlySet<ulong>> GuildMembersAsync()
    {
        var members = new HashSet<ulong>();
        foreach (var discordId in await court.LinkedDiscordIdsAsync())
        {
            if (discordId == 0 || discordId > long.MaxValue)
                continue;

            try
            {
                if (await Context.Client.Rest.GetGuildUserAsync(config.Guild, discordId) != null)
                    members.Add(discordId);
            }
            catch (Discord.Net.HttpException exception) when (exception.HttpCode == System.Net.HttpStatusCode.NotFound)
            {
                // Linked account is no longer present in the configured guild.
            }
        }
        return members;
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            EnsureLeadership();
            await action();
        }
        catch (CourtRuleException exception)
        {
            await FollowupAsync(exception.Message, ephemeral: true);
        }
        catch (Exception exception)
        {
            await Logger.Error($"Leadership command failed for {Context.User.Id}", exception);
            await FollowupAsync("Внутренняя ошибка руководства. Событие записано в журнал.", ephemeral: true);
        }
    }

    private void EnsureLeadership()
    {
        if (Context.Guild.OwnerId == Context.User.Id)
            return;
        if (config.CourtLeadershipRole != 0 && Context.User is SocketGuildUser member && member.Roles.Any(value => value.Id == config.CourtLeadershipRole))
            return;
        throw new CourtRuleException("Команда доступна только владельцу сервера или настроенной роли руководства.");
    }
}
