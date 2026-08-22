using Content.DiscordBot.Governance;
using Discord.Interactions;

namespace Content.DiscordBot.Modules;

[Group("тест", "Локальные инструменты тестирования Governance")]
public sealed class CourtTestInteractionModule(
    CourtTestAccountLinkingService testLinks,
    CourtDiscordCoordinator coordinator) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("суд-переотправить-приглашения", "Переотправить ожидающие приглашения присяжным с актуальными кнопками")]
    [RequireOwner]
    public async Task ResendJuryInvitationsAsync(long caseId)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            var count = await testLinks.ResetPendingJuryNotificationsAsync(caseId, Context.User.Id);
            await coordinator.ProcessOnceAsync();
            await FollowupAsync(
                count == 0
                    ? $"По делу №{caseId} нет ожидающих приглашений присяжным."
                    : $"Переотправлено ожидающих приглашений по делу №{caseId}: {count}.",
                ephemeral: true);
        }
        catch (CourtRuleException exception)
        {
            await FollowupAsync(exception.Message, ephemeral: true);
        }
        catch (Exception exception)
        {
            await Logger.Error($"Court test jury invitation resend failed for {Context.User.Id}", exception);
            await FollowupAsync("Не удалось переотправить приглашения. Ошибка записана в журнал Discord-бота.", ephemeral: true);
        }
    }

    [SlashCommand("суд-диагностика", "Показать фактическое состояние коллегии и голосов по делу")]
    [RequireOwner]
    public async Task DiagnoseCourtAsync(long caseId)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            await FollowupAsync(await testLinks.DiagnoseCaseAsync(caseId), ephemeral: true);
        }
        catch (CourtRuleException exception)
        {
            await FollowupAsync(exception.Message, ephemeral: true);
        }
        catch (Exception exception)
        {
            await Logger.Error($"Court test diagnostics failed for {Context.User.Id}", exception);
            await FollowupAsync("Не удалось получить диагностику дела. Ошибка записана в журнал Discord-бота.", ephemeral: true);
        }
    }
}
