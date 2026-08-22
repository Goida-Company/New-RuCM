using Content.DiscordBot.Governance;
using Discord;
using Discord.Interactions;

namespace Content.DiscordBot.Modules;

[Group("суд", "Community Court RUCM")]
public sealed class CommunityCourtModule(
    CommunityCourtService court,
    CourtFilingService filing,
    CourtPunishmentService punishments,
    CourtDiscordCoordinator discord,
    CourtTestAccountLinkingService testLinks,
    Config config) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("жалоба", "Подать жалобу после завершения раунда")]
    public async Task FileCaseAsync(
        [Summary("ответчик", "Игровой никнейм ответчика в SS14; Discord-привязка не требуется")] string defendant,
        [Summary("раунд", "ID завершённого раунда")] int round,
        [Summary("описание", "Описание нарушения (20–1500 символов)")] string summary,
        [Summary("файл", "Клип или иной файл доказательства")] IAttachment? attachment = null,
        [Summary("ссылка", "Ссылка на клип или реплей")] string? evidenceUrl = null)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            EnsureEnabled();
            var evidence = attachment?.Url ?? evidenceUrl ?? string.Empty;
            var courtCase = await filing.FileByGameNicknameAsync(Context.User.Id, defendant, round, summary, evidence);
            await discord.EnsureCaseThreadAsync(courtCase);
            await FollowupAsync($"Жалоба зарегистрирована как дело №{courtCase.Id}.", ephemeral: true);
        });
    }

    [SlashCommand("защита", "Подать защиту по делу")]
    public async Task DefendAsync(
        [Summary("дело", "Номер дела")] long caseId,
        [Summary("текст", "Позиция защиты (20–3000 символов)")] string body,
        [Summary("файл", "Файл доказательства")] IAttachment? attachment = null,
        [Summary("ссылка", "Ссылка на доказательство")] string? evidenceUrl = null)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            EnsureEnabled();
            var statement = await court.SubmitDefenseAsync(caseId, Context.User.Id, body, attachment?.Url ?? evidenceUrl);
            await discord.PublishStatementAsync(caseId, statement);
            await FollowupAsync("Защита принята и опубликована в треде дела.", ephemeral: true);
        });
    }

    [SlashCommand("свидетель-добавить", "Пригласить свидетеля защиты по делу")]
    public async Task AddWitnessAsync(
        [Summary("дело", "Номер дела")] long caseId,
        [Summary("свидетель", "Привязанный Discord-пользователь")] IUser witness)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            EnsureEnabled();
            await court.AddWitnessAsync(caseId, Context.User.Id, witness.Id);
            await FollowupAsync("Свидетель добавлен. Он может отправить одно показание до окончания стадии защиты.", ephemeral: true);
        });
    }

    [SlashCommand("свидетельство", "Подать свидетельское показание")]
    public async Task WitnessStatementAsync(
        [Summary("дело", "Номер дела")] long caseId,
        [Summary("текст", "Показание (20–3000 символов)")] string body,
        [Summary("файл", "Файл доказательства")] IAttachment? attachment = null,
        [Summary("ссылка", "Ссылка на доказательство")] string? evidenceUrl = null)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            EnsureEnabled();
            var statement = await court.SubmitWitnessStatementAsync(caseId, Context.User.Id, body, attachment?.Url ?? evidenceUrl);
            await discord.PublishStatementAsync(caseId, statement);
            await FollowupAsync("Свидетельское показание принято и опубликовано.", ephemeral: true);
        });
    }

    [SlashCommand("присяжный", "Ответить на приглашение в присяжные")]
    public async Task JuryResponseAsync(
        [Summary("дело", "Номер дела")] long caseId,
        [Summary("ответ", "Принять, отказаться или взять самоотвод")]
        [Choice("Принять", InvitationStates.Accepted)]
        [Choice("Отказаться", InvitationStates.Declined)]
        [Choice("Самоотвод", InvitationStates.Recused)] string response,
        [Summary("причина", "Причина самоотвода")] string? reason = null)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            EnsureEnabled();
            var state = await court.RespondToInvitationAsync(caseId, Context.User.Id, response, reason);
            await FollowupAsync($"Ответ зафиксирован: {state}.", ephemeral: true);
        });
    }

    [SlashCommand("голос", "Тайно проголосовать о виновности")]
    public async Task VoteAsync(
        [Summary("дело", "Номер дела")] long caseId,
        [Summary("вердикт", "Ваш вариант вердикта")]
        [Choice("Виновен", CourtVerdicts.Guilty)]
        [Choice("Не виновен", CourtVerdicts.NotGuilty)]
        [Choice("Недостаточно доказательств", CourtVerdicts.InsufficientEvidence)] string verdict,
        [Summary("обоснование", "Обоснование голоса (20–1500 символов)")] string reasoning)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            EnsureEnabled();
            await court.SubmitGuiltVoteAsync(caseId, Context.User.Id, verdict, reasoning);
            await FollowupAsync("Тайный голос принят.", ephemeral: true);
        });
    }

    [SlashCommand("наказание", "Тайно проголосовать о мере наказания")]
    public async Task SentenceAsync(
        [Summary("дело", "Номер дела")] long caseId,
        [Summary("мера", "Предлагаемая мера")]
        [Choice("Предупреждение", CourtSanctions.Warning)]
        [Choice("Бан игры", CourtSanctions.GameBan)]
        [Choice("Бан роли", CourtSanctions.JobBan)] string sanction,
        [Summary("обоснование", "Обоснование меры (20–1500 символов)")] string reasoning,
        [Summary("дни", "Срок блокировки от 1 до 7 дней")] int? days = null,
        [Summary("роль", "Prototype ID роли для джоббана")] string? role = null)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            EnsureEnabled();
            short? shortDays = days == null ? null : checked((short) days.Value);
            await court.SubmitSentencingVoteAsync(caseId, Context.User.Id, sanction, shortDays, role, reasoning);
            await FollowupAsync("Тайный голос о наказании принят.", ephemeral: true);
        });
    }

    [SlashCommand("статус", "Открыть интерактивную панель дела")]
    public async Task StatusAsync([Summary("дело", "Номер дела")] long caseId)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            EnsureEnabled();
            var courtCase = await court.GetCaseAsync(caseId);
            var status = await discord.BuildStatusEmbedAsync(caseId);
            await FollowupAsync(
                embed: GovernanceDiscordUi.CourtPanelEmbed(courtCase, status),
                components: GovernanceDiscordUi.CourtPanel(courtCase),
                ephemeral: true);
        });
    }

    [SlashCommand("история", "Показать историю ответчика присяжному на стадии наказания")]
    public async Task HistoryAsync([Summary("дело", "Номер дела")] long caseId)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            EnsureEnabled();
            var history = await punishments.GetSentencingHistoryAsync(caseId, Context.User.Id);
            var text = history.Count == 0
                ? "История наказаний и публичных замечаний пуста."
                : string.Join("\n", history.Select(value =>
                    $"• <t:{new DateTimeOffset(value.CreatedAt).ToUnixTimeSeconds()}:d> **{value.Kind}** ({(value.Active ? "активно" : "завершено")}): {value.Description}"));
            if (text.Length > 3900)
                text = text[..3900] + "…";
            await FollowupAsync(embed: new EmbedBuilder().WithTitle($"История для назначения меры • дело №{caseId}")
                .WithDescription(text).WithColor(Color.DarkBlue).Build(), ephemeral: true);
        });
    }

    [SlashCommand("тест-завершить-защиту", "Локально завершить срок защиты и сразу запустить подбор присяжных")]
    [RequireOwner]
    public async Task TestExpireDefenseAsync([Summary("дело", "Номер дела")] long caseId)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            EnsureEnabled();
            var result = await testLinks.ExpireDefenseAsync(caseId, Context.User.Id);
            await discord.ProcessOnceAsync();
            await FollowupAsync($"{result} Выполнен проход планировщика: подходящим присяжным должны быть отправлены приглашения.", ephemeral: true);
        });
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (CourtRuleException exception)
        {
            await FollowupAsync(exception.Message, ephemeral: true);
        }
        catch (Exception exception)
        {
            await Logger.Error($"Community Court command failed for {Context.User.Id}", exception);
            await FollowupAsync("Внутренняя ошибка Community Court. Событие записано в журнал.", ephemeral: true);
        }
    }

    private void EnsureEnabled()
    {
        if (!config.CourtEnabled)
            throw new CourtRuleException("Community Court сейчас отключён в конфигурации бота.");
    }
}
