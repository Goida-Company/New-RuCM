using Content.DiscordBot.Governance;
using Discord;
using Discord.Interactions;

namespace Content.DiscordBot.Modules;

[Group("аккаунт", "Привязка аккаунта SS14")]
public sealed class AccountLinkingInteractionModule(CourtTestAccountLinkingService testLinks)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("панель", "Создать панель привязки аккаунта")]
    [RequireOwner]
    public Task CreatePanelAsync()
    {
        var component = new ComponentBuilder()
            .WithButton("Привязать аккаунт SS14", "link-ss14-account")
            .Build();
        return RespondAsync("Привяжите аккаунт SS14 с помощью кнопки ниже.", components: component);
    }

    [SlashCommand("тест-привязать", "Локально связать Discord тестировщика с SS14 и допустить его в присяжные")]
    [RequireOwner]
    public async Task TestLinkAsync(IUser user, string player)
    {
        try
        {
            var result = await testLinks.LinkJurorAsync(Context.User.Id, user.Id, player);
            await RespondAsync(result, ephemeral: true);
        }
        catch (CourtRuleException exception)
        {
            await RespondAsync(exception.Message, ephemeral: true);
        }
        catch (Exception exception)
        {
            await Logger.Error($"Court test account linking failed for Discord {user.Id}", exception);
            await RespondAsync("Не удалось создать тестовую привязку. Ошибка записана в журнал Discord-бота.", ephemeral: true);
        }
    }
}
