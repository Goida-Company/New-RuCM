using Discord.Interactions;

namespace Content.DiscordBot.Modules;

public class LinkAccountModal : IModal
{
    public string Title => "Привязка аккаунта SS14";

    [InputLabel("Код привязки SS14")]
    [RequiredInput]
    [ModalTextInput("account_code", placeholder: "Код отображается в левом верхнем углу лобби")]
    public string Code { get; set; } = string.Empty;
}
