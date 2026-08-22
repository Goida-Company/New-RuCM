using Content.DiscordBot.Governance;
using Discord;
using Discord.Interactions;

namespace Content.DiscordBot.Modules;

/// <summary>
/// Discord surface for moderation trust and independent post-action review only.
/// Live AHelp handling, incident creation, containment and quorum decisions are intentionally
/// in-game Governance workflows so Discord cannot become a second moderation control plane.
/// </summary>
[Group("дежурство", "Доверие и независимый аудит дежурств")]
public sealed class ModerationGovernanceModule(
    ModerationTrustService moderationTrust) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("доверие", "Показать Moderation Trust пользователя")]
    public Task TrustAsync(IUser? user = null) => ExecuteAsync(async () =>
    {
        var target = user ?? Context.User;
        var profile = await moderationTrust.GetProfileAsync(target.Id);
        var embed = new EmbedBuilder()
            .WithTitle($"Moderation Trust • {target.Username}")
            .AddField("Итог", $"{profile.TrustScore}/1000", true)
            .AddField("Уверенность", $"{profile.Confidence}%", true)
            .AddField("Точность решений", $"{profile.DecisionAccuracy}%", true)
            .AddField("Процедурность", $"{profile.ProceduralScore}%", true)
            .AddField("Надёжность", $"{profile.ReliabilityScore}%", true)
            .AddField("Проверено действий", profile.ReviewedActions, true)
            .AddField("Дежурства", $"{profile.CompletedDuties} успешно / {profile.FailedDuties} сорвано", true)
            .AddField("Серьёзные вмешательства", profile.SeriousInterventions, true)
            .WithColor(profile.TrustScore >= 800 ? Color.Green : profile.TrustScore >= 600 ? Color.Gold : Color.Orange)
            .Build();
        await FollowupAsync(embed: embed, ephemeral: true);
    });

    [SlashCommand("аудит-ответ", "Ответить на приглашение проверить действие дежурного")]
    public Task AuditInvitationAsync(
        long action,
        [Choice("Принять", "accepted")][Choice("Отказаться", "declined")][Choice("Самоотвод", "recused")] string response,
        string? reason = null) => ExecuteAsync(async () =>
    {
        var state = await moderationTrust.RespondToInvitationAsync(action, Context.User.Id, response, reason);
        await FollowupAsync($"Ответ на приглашение по действию №{action}: `{state}`.", ephemeral: true);
    });

    [SlashCommand("аудит-материалы", "Показать материалы назначенного независимого аудита")]
    public Task AuditMaterialsAsync(long action) => ExecuteAsync(async () =>
    {
        var packet = await moderationTrust.GetReviewPacketAsync(action, Context.User.Id);
        var embed = new EmbedBuilder()
            .WithTitle($"Независимый аудит • действие №{packet.ActionId}")
            .AddField("Раунд", packet.RoundId, true)
            .AddField("Инцидент", $"#{packet.IncidentId} • {packet.IncidentType}", true)
            .AddField("Тип действия", packet.ActionType, true)
            .AddField("Кворум", $"{packet.Approvals}/{packet.RequiredApprovals}; отклонений {packet.Rejections}", true)
            .AddField("Причина дежурного", packet.Reason)
            .AddField("Контекст инцидента", packet.IncidentSummary)
            .AddField("Выполнено", packet.ExecutedAt?.ToString("u") ?? "нет данных", true)
            .AddField("Передано в суд", packet.EscalatedToCourt ? "да" : "нет", true)
            .WithColor(Color.Blue)
            .Build();
        await FollowupAsync(embed: embed, ephemeral: true);
    });

    [SlashCommand("аудит", "Отправить независимую оценку действия дежурного")]
    public Task AuditAsync(
        long action,
        [Choice("Корректно", "correct")]
        [Choice("Разумно, но ошибочно", "reasonable_but_wrong")]
        [Choice("Процедурная ошибка", "procedural_error")]
        [Choice("Небрежность", "negligent")]
        [Choice("Злоупотребление", "abuse")] string outcome,
        string reasoning) => ExecuteAsync(async () =>
    {
        await moderationTrust.SubmitReviewAsync(action, Context.User.Id, outcome, reasoning);
        await FollowupAsync($"Независимый аудит действия №{action} сохранён: `{outcome}`.", ephemeral: true);
    });

    private async Task ExecuteAsync(Func<Task> action)
    {
        await DeferAsync(ephemeral: true);
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
            await Logger.Error($"Moderation governance command failed for {Context.User.Id}", exception);
            await FollowupAsync("Внутренняя ошибка дежурства. Событие записано в журнал.", ephemeral: true);
        }
    }
}
