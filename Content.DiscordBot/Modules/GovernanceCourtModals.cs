using Discord;
using Discord.Interactions;

namespace Content.DiscordBot.Modules;

public sealed class CourtDefenseModal : IModal
{
    public string Title => "Защита по делу";

    [InputLabel("Позиция защиты")]
    [ModalTextInput("body", TextInputStyle.Paragraph, "Опишите позицию защиты…", 20, 3000)]
    public string Body { get; set; } = string.Empty;

    [InputLabel("Ссылка на доказательство")]
    [RequiredInput(false)]
    [ModalTextInput("evidence", TextInputStyle.Short, "Необязательно: ссылка на клип, файл или реплей", 0, 1000)]
    public string? Evidence { get; set; }
}

public sealed class CourtWitnessStatementModal : IModal
{
    public string Title => "Показание свидетеля";

    [InputLabel("Показание")]
    [ModalTextInput("body", TextInputStyle.Paragraph, "Опишите известные вам обстоятельства…", 20, 3000)]
    public string Body { get; set; } = string.Empty;

    [InputLabel("Ссылка на доказательство")]
    [RequiredInput(false)]
    [ModalTextInput("evidence", TextInputStyle.Short, "Необязательно", 0, 1000)]
    public string? Evidence { get; set; }
}

public sealed class CourtAddWitnessModal : IModal
{
    public string Title => "Добавить свидетеля";

    [InputLabel("Discord ID или упоминание")]
    [ModalTextInput("discord_user", TextInputStyle.Short, "Например: 123456789012345678 или @Пользователь", 2, 100)]
    public string DiscordUser { get; set; } = string.Empty;
}

public sealed class CourtVoteReasonModal : IModal
{
    public string Title => "Тайный голос";

    [InputLabel("Обоснование")]
    [ModalTextInput("reasoning", TextInputStyle.Paragraph, "Почему вы выбрали этот вариант?", 20, 1500)]
    public string Reasoning { get; set; } = string.Empty;
}

public sealed class CourtWarningSentenceModal : IModal
{
    public string Title => "Предупреждение";

    [InputLabel("Обоснование меры")]
    [ModalTextInput("reasoning", TextInputStyle.Paragraph, "Почему достаточно предупреждения?", 20, 1500)]
    public string Reasoning { get; set; } = string.Empty;
}

public sealed class CourtGameBanSentenceModal : IModal
{
    public string Title => "Бан игры";

    [InputLabel("Срок в днях")]
    [ModalTextInput("days", TextInputStyle.Short, "От 1 до 7", 1, 1)]
    public string Days { get; set; } = string.Empty;

    [InputLabel("Обоснование меры")]
    [ModalTextInput("reasoning", TextInputStyle.Paragraph, "Обоснуйте срок и необходимость блокировки", 20, 1500)]
    public string Reasoning { get; set; } = string.Empty;
}

public sealed class CourtJobBanSentenceModal : IModal
{
    public string Title => "Бан роли";

    [InputLabel("Prototype ID роли")]
    [ModalTextInput("role", TextInputStyle.Short, "Например: CMJobCommandingOfficer", 1, 100)]
    public string Role { get; set; } = string.Empty;

    [InputLabel("Срок в днях")]
    [ModalTextInput("days", TextInputStyle.Short, "От 1 до 7", 1, 1)]
    public string Days { get; set; } = string.Empty;

    [InputLabel("Обоснование меры")]
    [ModalTextInput("reasoning", TextInputStyle.Paragraph, "Обоснуйте срок и выбор роли", 20, 1500)]
    public string Reasoning { get; set; } = string.Empty;
}

public sealed class CourtRecusalModal : IModal
{
    public string Title => "Самоотвод";

    [InputLabel("Причина самоотвода")]
    [ModalTextInput("reason", TextInputStyle.Paragraph, "Кратко укажите конфликт интересов или другую причину", 3, 500)]
    public string Reason { get; set; } = string.Empty;
}
