using Content.DiscordBot.Governance;
using Discord.Interactions;

namespace Content.DiscordBot.Modules;

public sealed class EventReviewUiInteractionModule(EventGovernanceService events)
    : InteractionModuleBase<SocketInteractionContext>
{
    [ComponentInteraction("event-review-accept:*")]
    public Task AcceptAsync(string proposalId) => HandleInvitationAsync(proposalId, InvitationStates.Accepted, null);

    [ComponentInteraction("event-review-decline:*")]
    public Task DeclineAsync(string proposalId) => HandleInvitationAsync(proposalId, InvitationStates.Declined, null);

    [ComponentInteraction("event-review-recuse:*")]
    public async Task RecuseAsync(string proposalIdText)
    {
        var proposalId = ParseProposalId(proposalIdText);
        await RespondWithModalAsync<EventReviewRecusalModal>($"event-review-recuse-submit:{proposalId}");
    }

    [ModalInteraction("event-review-recuse-submit:*")]
    public Task RecuseSubmitAsync(string proposalId, EventReviewRecusalModal modal) =>
        HandleInvitationAsync(proposalId, InvitationStates.Recused, modal.Reason);

    [ComponentInteraction("event-review-decision:*:*")]
    public async Task ReviewDecisionAsync(string proposalIdText, string decision)
    {
        var proposalId = ParseProposalId(proposalIdText);
        if (decision is not ("approve" or "reject"))
            throw new CourtRuleException("Неизвестный вариант рецензии.");
        await RespondWithModalAsync<EventReviewDecisionModal>($"event-review-decision-submit:{proposalId}:{decision}");
    }

    [ModalInteraction("event-review-decision-submit:*:*")]
    public async Task ReviewDecisionSubmitAsync(string proposalIdText, string decision, EventReviewDecisionModal modal)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            var proposalId = ParseProposalId(proposalIdText);
            var result = await events.ReviewAsync(proposalId, Context.User.Id, decision, modal.Reasoning);
            var decisionText = decision == "approve" ? "одобрена" : "отклонена";
            await FollowupAsync(
                $"Рецензия {decisionText}. Текущий результат: {result.Approvals} за / {result.Rejections} против; статус `{result.Status}`.",
                ephemeral: true);
        }
        catch (CourtRuleException exception)
        {
            await FollowupAsync(exception.Message, ephemeral: true);
        }
        catch (Exception exception)
        {
            await Logger.Error($"Event review decision UI failed for {Context.User.Id}", exception);
            await FollowupAsync("Не удалось сохранить рецензию. Ошибка записана в журнал Discord-бота.", ephemeral: true);
        }
    }

    private async Task HandleInvitationAsync(string proposalIdText, string response, string? reason)
    {
        await DeferAsync(ephemeral: true);
        try
        {
            var proposalId = ParseProposalId(proposalIdText);
            var state = await events.RespondToReviewInvitationAsync(proposalId, Context.User.Id, response, reason);
            if (state == InvitationStates.Accepted)
            {
                await FollowupAsync(
                    $"Вы приняли рецензирование заявки №{proposalId}. Теперь выберите итог рецензии.",
                    components: GovernanceDiscordUi.EventReviewPanel(proposalId),
                    ephemeral: true);
                return;
            }

            var text = state switch
            {
                InvitationStates.Declined => $"Вы отказались от рецензирования заявки №{proposalId}.",
                InvitationStates.Recused => $"Самоотвод от рецензирования заявки №{proposalId} зафиксирован.",
                _ => $"Ответ по заявке №{proposalId} зафиксирован: {state}.",
            };
            await FollowupAsync(text, ephemeral: true);
        }
        catch (CourtRuleException exception)
        {
            await FollowupAsync(exception.Message, ephemeral: true);
        }
        catch (Exception exception)
        {
            await Logger.Error($"Event review invitation UI failed for {Context.User.Id}", exception);
            await FollowupAsync("Не удалось обработать приглашение. Ошибка записана в журнал Discord-бота.", ephemeral: true);
        }
    }

    private static long ParseProposalId(string value)
    {
        if (!long.TryParse(value, out var proposalId) || proposalId <= 0)
            throw new CourtRuleException("Некорректный номер заявки события.");
        return proposalId;
    }
}
