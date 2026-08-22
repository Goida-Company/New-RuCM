using Discord;

namespace Content.DiscordBot.Governance;

public static class GovernanceDiscordUi
{
    public static MessageComponent CourtThreadLauncher(long caseId) => new ComponentBuilder()
        .WithButton("Открыть панель дела", $"court-panel:{caseId}", ButtonStyle.Primary, new Emoji("⚖️"))
        .Build();

    public static MessageComponent CourtPanel(GovernanceCourtCase courtCase)
    {
        var builder = new ComponentBuilder()
            .WithButton("Обновить", $"court-panel:{courtCase.Id}", ButtonStyle.Secondary, new Emoji("🔄"), row: 0);

        switch (courtCase.Status)
        {
            case CourtStatuses.Defense:
                builder
                    .WithButton("Добавить свидетеля", $"court-witness-add:{courtCase.Id}", ButtonStyle.Secondary, new Emoji("👤"), row: 1)
                    .WithButton("Дать показание", $"court-witness-statement:{courtCase.Id}", ButtonStyle.Secondary, new Emoji("📝"), row: 1)
                    .WithButton("Закончить защиту", $"court-defense-finish:{courtCase.Id}", ButtonStyle.Success, new Emoji("✅"), row: 2);
                break;
            case CourtStatuses.Jury:
                builder
                    .WithButton("Виновен", $"court-guilt:{courtCase.Id}:{CourtVerdicts.Guilty}", ButtonStyle.Danger, new Emoji("⚠️"), row: 1)
                    .WithButton("Не виновен", $"court-guilt:{courtCase.Id}:{CourtVerdicts.NotGuilty}", ButtonStyle.Success, new Emoji("✅"), row: 1)
                    .WithButton("Недостаточно доказательств", $"court-guilt:{courtCase.Id}:{CourtVerdicts.InsufficientEvidence}", ButtonStyle.Secondary, new Emoji("❔"), row: 1);
                break;
            case CourtStatuses.Sentencing:
                builder
                    .WithButton("Предупреждение", $"court-sentence:{courtCase.Id}:{CourtSanctions.Warning}", ButtonStyle.Secondary, new Emoji("📌"), row: 1)
                    .WithButton("Бан игры", $"court-sentence:{courtCase.Id}:{CourtSanctions.GameBan}", ButtonStyle.Danger, new Emoji("⛔"), row: 1)
                    .WithButton("Бан роли", $"court-sentence:{courtCase.Id}:{CourtSanctions.JobBan}", ButtonStyle.Danger, new Emoji("🚫"), row: 1)
                    .WithButton("История", $"court-history:{courtCase.Id}", ButtonStyle.Primary, new Emoji("📚"), row: 2);
                break;
        }

        return builder.Build();
    }

    public static Embed CourtPanelEmbed(GovernanceCourtCase courtCase, Embed statusEmbed)
    {
        var description = courtCase.Status switch
        {
            CourtStatuses.Defense => "Стадия защиты. Истец и ответчик пишут обычными сообщениями прямо в треде; сообщения остальных участников удаляются. Когда обе стороны закончили обсуждение, каждая нажимает «Закончить защиту».",
            CourtStatuses.AwaitingJury => "Защита завершена. Формируется коллегия присяжных; переписка сторон закрыта.",
            CourtStatuses.Jury => "Идёт тайное голосование о виновности. Кнопки голосования доступны только действующим присяжным.",
            CourtStatuses.Sentencing => "Идёт тайное голосование о мере наказания. Перед выбором меры можно открыть историю ответчика.",
            CourtStatuses.Verdict => "Решение вынесено и ожидает исполнения.",
            CourtStatuses.Executed => "Решение исполнено.",
            CourtStatuses.Overturned => "Решение отменено руководством.",
            _ => "Текущее состояние дела.",
        };

        return statusEmbed.ToEmbedBuilder()
            .WithDescription($"{statusEmbed.Description}\n\n{description}")
            .WithFooter("RUCM Community Governance")
            .Build();
    }

    public static MessageComponent EventReviewInvite(long proposalId) => new ComponentBuilder()
        .WithButton("Принять", $"event-review-accept:{proposalId}", ButtonStyle.Success, new Emoji("✅"))
        .WithButton("Отказаться", $"event-review-decline:{proposalId}", ButtonStyle.Danger, new Emoji("✖️"))
        .WithButton("Самоотвод", $"event-review-recuse:{proposalId}", ButtonStyle.Secondary, new Emoji("↩️"))
        .Build();

    public static MessageComponent EventReviewPanel(long proposalId) => new ComponentBuilder()
        .WithButton("Одобрить", $"event-review-decision:{proposalId}:approve", ButtonStyle.Success, new Emoji("👍"))
        .WithButton("Отклонить", $"event-review-decision:{proposalId}:reject", ButtonStyle.Danger, new Emoji("👎"))
        .Build();
}
