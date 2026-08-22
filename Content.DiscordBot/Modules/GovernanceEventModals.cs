using Discord;
using Discord.Interactions;

namespace Content.DiscordBot.Modules;

public sealed class EventReviewRecusalModal : IModal
{
    public string Title => "Самоотвод от рецензии";

    [InputLabel("Причина самоотвода")]
    [ModalTextInput("reason", TextInputStyle.Paragraph, "Кратко укажите конфликт интересов или другую причину", 3, 500)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class EventReviewDecisionModal : IModal
{
    public string Title => "Рецензия события";

    [InputLabel("Обоснование решения")]
    [ModalTextInput("reasoning", TextInputStyle.Paragraph, "Обоснуйте одобрение или отклонение заявки", 20, 1500)]
    public string Reasoning { get; set; } = string.Empty;
}
