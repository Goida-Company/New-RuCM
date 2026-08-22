using Content.DiscordBot.Governance;
using Discord;
using Discord.Interactions;

namespace Content.DiscordBot.Modules;

public sealed class CourtUiInteractionModule(
    CommunityCourtService court,
    CourtPunishmentService punishments,
    CourtDiscordCoordinator discord,
    GovernanceDiscordConversationCoordinator conversations,
    Config config) : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction("court-panel:*")]
    public async Task PanelAsync(string caseIdText)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            var caseId = ParseCaseId(caseIdText);
            await FollowupPanelAsync(caseId);
        });
    }

    [ComponentInteraction("court-defense:*")]
    public async Task DefenseAsync(string caseIdText)
    {
        EnsureEnabled();
        var caseId = ParseCaseId(caseIdText);
        await RespondWithModalAsync<CourtDefenseModal>($"court-defense-submit:{caseId}");
    }

    [ModalInteraction("court-defense-submit:*")]
    public async Task DefenseSubmitAsync(string caseIdText, CourtDefenseModal modal)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            var caseId = ParseCaseId(caseIdText);
            var statement = await court.SubmitDefenseAsync(caseId, Context.User.Id, modal.Body, EmptyToNull(modal.Evidence));
            await discord.PublishStatementAsync(caseId, statement);
            await FollowupAsync("Защита принята и опубликована в материалах дела.", ephemeral: true);
            await FollowupPanelAsync(caseId);
        });
    }

    [ComponentInteraction("court-defense-finish:*")]
    public async Task DefenseFinishAsync(string caseIdText)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            var caseId = ParseCaseId(caseIdText);
            var result = await conversations.ConfirmDefenseCompleteAsync(caseId, Context.User.Id);
            if (result.Transitioned)
            {
                await conversations.LockDefenseThreadAsync(caseId);
                await FollowupAsync(
                    "Обе стороны подтвердили завершение защиты. Переписка закрыта, дело переходит к формированию коллегии присяжных.",
                    ephemeral: true);
            }
            else
            {
                var waitingFor = result.ClaimantConfirmed && !result.DefendantConfirmed
                    ? "ответчика"
                    : "истца";
                await FollowupAsync(
                    $"Ваше подтверждение сохранено. Для завершения стадии требуется подтверждение {waitingFor}.",
                    ephemeral: true);
            }
            await FollowupPanelAsync(caseId);
        });
    }

    [ComponentInteraction("court-witness-add:*")]
    public async Task AddWitnessAsync(string caseIdText)
    {
        EnsureEnabled();
        var caseId = ParseCaseId(caseIdText);
        await RespondWithModalAsync<CourtAddWitnessModal>($"court-witness-add-submit:{caseId}");
    }

    [ModalInteraction("court-witness-add-submit:*")]
    public async Task AddWitnessSubmitAsync(string caseIdText, CourtAddWitnessModal modal)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            var caseId = ParseCaseId(caseIdText);
            var witnessId = ParseDiscordUserId(modal.DiscordUser);
            await court.AddWitnessAsync(caseId, Context.User.Id, witnessId);
            await FollowupAsync($"Свидетель <@{witnessId}> добавлен к делу №{caseId}.", ephemeral: true);
            await FollowupPanelAsync(caseId);
        });
    }

    [ComponentInteraction("court-witness-statement:*")]
    public async Task WitnessStatementAsync(string caseIdText)
    {
        EnsureEnabled();
        var caseId = ParseCaseId(caseIdText);
        await RespondWithModalAsync<CourtWitnessStatementModal>($"court-witness-statement-submit:{caseId}");
    }

    [ModalInteraction("court-witness-statement-submit:*")]
    public async Task WitnessStatementSubmitAsync(string caseIdText, CourtWitnessStatementModal modal)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            var caseId = ParseCaseId(caseIdText);
            var statement = await court.SubmitWitnessStatementAsync(
                caseId,
                Context.User.Id,
                modal.Body,
                EmptyToNull(modal.Evidence));
            await discord.PublishStatementAsync(caseId, statement);
            await FollowupAsync("Показание принято и опубликовано в материалах дела.", ephemeral: true);
            await FollowupPanelAsync(caseId);
        });
    }

    [ComponentInteraction("court-guilt:*:*")]
    public async Task GuiltAsync(string caseIdText, string verdict)
    {
        EnsureEnabled();
        var caseId = ParseCaseId(caseIdText);
        if (verdict is not (CourtVerdicts.Guilty or CourtVerdicts.NotGuilty or CourtVerdicts.InsufficientEvidence))
            throw new CourtRuleException("Неизвестный вариант вердикта.");
        await RespondWithModalAsync<CourtVoteReasonModal>($"court-guilt-submit:{caseId}:{verdict}");
    }

    [ModalInteraction("court-guilt-submit:*:*")]
    public async Task GuiltSubmitAsync(string caseIdText, string verdict, CourtVoteReasonModal modal)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            var caseId = ParseCaseId(caseIdText);
            await court.SubmitGuiltVoteAsync(caseId, Context.User.Id, verdict, modal.Reasoning);
            await FollowupAsync("Тайный голос принят.", ephemeral: true);
            await FollowupPanelAsync(caseId);
        });
    }

    [ComponentInteraction("court-sentence:*:*")]
    public async Task SentenceAsync(string caseIdText, string sanction)
    {
        EnsureEnabled();
        var caseId = ParseCaseId(caseIdText);
        switch (sanction)
        {
            case CourtSanctions.Warning:
                await RespondWithModalAsync<CourtWarningSentenceModal>($"court-sentence-warning-submit:{caseId}");
                break;
            case CourtSanctions.GameBan:
                await RespondWithModalAsync<CourtGameBanSentenceModal>($"court-sentence-gameban-submit:{caseId}");
                break;
            case CourtSanctions.JobBan:
                await RespondWithModalAsync<CourtJobBanSentenceModal>($"court-sentence-jobban-submit:{caseId}");
                break;
            default:
                throw new CourtRuleException("Неизвестный тип наказания.");
        }
    }

    [ModalInteraction("court-sentence-warning-submit:*")]
    public Task WarningSubmitAsync(string caseIdText, CourtWarningSentenceModal modal) =>
        SubmitSentenceAsync(caseIdText, CourtSanctions.Warning, null, null, modal.Reasoning);

    [ModalInteraction("court-sentence-gameban-submit:*")]
    public Task GameBanSubmitAsync(string caseIdText, CourtGameBanSentenceModal modal) =>
        SubmitSentenceAsync(caseIdText, CourtSanctions.GameBan, ParseDays(modal.Days), null, modal.Reasoning);

    [ModalInteraction("court-sentence-jobban-submit:*")]
    public Task JobBanSubmitAsync(string caseIdText, CourtJobBanSentenceModal modal) =>
        SubmitSentenceAsync(caseIdText, CourtSanctions.JobBan, ParseDays(modal.Days), modal.Role, modal.Reasoning);

    [ComponentInteraction("court-history:*")]
    public async Task HistoryAsync(string caseIdText)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            var caseId = ParseCaseId(caseIdText);
            var history = await punishments.GetSentencingHistoryAsync(caseId, Context.User.Id);
            var text = history.Count == 0
                ? "История наказаний и публичных замечаний пуста."
                : string.Join("\n", history.Select(value =>
                    $"• <t:{new DateTimeOffset(value.CreatedAt).ToUnixTimeSeconds()}:d> **{value.Kind}** " +
                    $"({(value.Active ? "активно" : "завершено")}): {value.Description}"));
            if (text.Length > 3900)
                text = text[..3900] + "…";
            await FollowupAsync(embed: new EmbedBuilder()
                .WithTitle($"История для назначения меры • дело №{caseId}")
                .WithDescription(text)
                .WithColor(Color.DarkBlue)
                .Build(), ephemeral: true);
        });
    }

    private async Task SubmitSentenceAsync(
        string caseIdText,
        string sanction,
        short? days,
        string? role,
        string reasoning)
    {
        await DeferAsync(ephemeral: true);
        await ExecuteAsync(async () =>
        {
            var caseId = ParseCaseId(caseIdText);
            await court.SubmitSentencingVoteAsync(caseId, Context.User.Id, sanction, days, role, reasoning);
            await FollowupAsync("Тайный голос о мере наказания принят.", ephemeral: true);
            await FollowupPanelAsync(caseId);
        });
    }

    private async Task FollowupPanelAsync(long caseId)
    {
        EnsureEnabled();
        var courtCase = await court.GetCaseAsync(caseId);
        var status = await discord.BuildStatusEmbedAsync(caseId);
        await FollowupAsync(
            embed: GovernanceDiscordUi.CourtPanelEmbed(courtCase, status),
            components: GovernanceDiscordUi.CourtPanel(courtCase),
            ephemeral: true);
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            EnsureEnabled();
            await action();
        }
        catch (CourtRuleException exception)
        {
            await FollowupAsync(exception.Message, ephemeral: true);
        }
        catch (Exception exception)
        {
            await Logger.Error($"Community Court UI failed for {Context.User.Id}", exception);
            await FollowupAsync("Не удалось выполнить действие Community Court. Ошибка записана в журнал Discord-бота.", ephemeral: true);
        }
    }

    private void EnsureEnabled()
    {
        if (!config.CourtEnabled)
            throw new CourtRuleException("Community Court сейчас отключён в конфигурации бота.");
    }

    private static long ParseCaseId(string value)
    {
        if (!long.TryParse(value, out var caseId) || caseId <= 0)
            throw new CourtRuleException("Некорректный номер дела.");
        return caseId;
    }

    private static ulong ParseDiscordUserId(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("<@", StringComparison.Ordinal) && normalized.EndsWith('>'))
            normalized = normalized[2..^1].TrimStart('!');
        if (!ulong.TryParse(normalized, out var discordId) || discordId == 0)
            throw new CourtRuleException("Укажите Discord ID пользователя или корректное упоминание вида @Пользователь.");
        return discordId;
    }

    private static short ParseDays(string value)
    {
        return short.TryParse(value.Trim(), out var days) ? days : (short) 0;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
