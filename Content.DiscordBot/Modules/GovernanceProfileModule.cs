using Content.DiscordBot.Governance;
using Discord;
using Discord.Interactions;

namespace Content.DiscordBot.Modules;

[Group("управление", "Профиль и связи RUCM Community Governance")]
public sealed class GovernanceProfileModule(GovernanceCommunityService community) : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("профиль", "Показать гражданский рейтинг и квалификации")]
    public Task ProfileAsync(IUser? user = null) => ExecuteAsync(async () =>
    {
        var profile = await community.GetProfileAsync((user ?? Context.User).Id);
        var qualifications = string.Join("\n", profile.Qualifications.OrderBy(value => value.Key)
            .Select(value => $"• `{value.Key}`: {value.Value}"));
        await RespondAsync(embed: new EmbedBuilder().WithTitle($"Профиль управления • {profile.Name}")
            .AddField("Гражданский рейтинг", profile.Rating, true)
            .AddField("Допуск", profile.Suspended ? "Приостановлен" : "Активен", true)
            .AddField("Квалификации", qualifications).WithColor(profile.Suspended ? Color.Red : Color.Green).Build(), ephemeral: true);
    });

    [SlashCommand("друг-добавить", "Запросить или подтвердить дружескую связь для проверки конфликтов")]
    public Task AddFriendAsync(IUser user) => ExecuteAsync(async () =>
        await RespondAsync(await community.RequestFriendshipAsync(Context.User.Id, user.Id), ephemeral: true));

    [SlashCommand("друг-удалить", "Удалить дружескую связь")]
    public Task RemoveFriendAsync(IUser user) => ExecuteAsync(async () =>
    {
        await community.RemoveFriendshipAsync(Context.User.Id, user.Id);
        await RespondAsync("Связь удалена.", ephemeral: true);
    });

    private async Task ExecuteAsync(Func<Task> action)
    {
        try { await action(); }
        catch (CourtRuleException exception) { await RespondAsync(exception.Message, ephemeral: true); }
        catch (Exception exception)
        {
            await Logger.Error($"Governance profile command failed for {Context.User.Id}", exception);
            await RespondAsync("Внутренняя ошибка управления. Событие записано в журнал.", ephemeral: true);
        }
    }
}
