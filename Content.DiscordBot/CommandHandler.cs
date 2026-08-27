using System.Collections.Immutable;
using System.Reflection;
using Content.DiscordBot.Modules;
using Content.DiscordBot.Governance;
using Content.Server.Database;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot;

public sealed class CommandHandler(
    DiscordSocketClient client,
    CommandService commands,
    InteractionService interaction,
    Func<ServerDbContext> databaseFactory,
    GovernanceIdentityService identities,
    DiscordGuildMemberCache guildMembers,
    IServiceProvider services,
    ulong guild)
{
    private ImmutableDictionary<ulong, RMCPatronTier>? _patronTiers;
    private ImmutableArray<RMCPatronTier> _tierPriority;
    private Task? _refreshPatronsTask;

    private sealed record LinkedPatronSnapshot(Guid PlayerId, ulong DiscordId, string PlayerName);
    private sealed record PatronRefreshDecision(
        Guid PlayerId,
        ulong DiscordId,
        string PlayerName,
        string? DiscordUsername,
        int? TierId,
        string? TierName);

    public int Running = 1;

    public async Task InstallCommandsAsync()
    {
        await using var db = databaseFactory();
        var patronTiers = await db.RMCPatronTiers.ToListAsync();
        _tierPriority = [..patronTiers.OrderBy(t => t.Priority)];
        _patronTiers = patronTiers.ToImmutableDictionary(t => t.DiscordRole, t => t);

        client.MessageReceived += HandleCommandAsync;
        client.ButtonExecuted += HandleButtonAsync;
        client.ModalSubmitted += HandleModalAsync;
        client.InteractionCreated += HandleInteractionAsync;
        client.Ready += RegisterInteractionsAsync;
        await commands.AddModulesAsync(Assembly.GetEntryAssembly(), services);
        await interaction.AddModulesAsync(Assembly.GetEntryAssembly(), services);

        interaction.AddModalInfo<LinkAccountModal>();

        _refreshPatronsTask = Task.Run(async () => await RefreshPatrons());
    }

    private async Task RegisterInteractionsAsync()
    {
        await interaction.RegisterCommandsToGuildAsync(guild, true);
        await ConfigureGovernanceChannelPermissionsAsync();
        await Logger.Info($"Registered Discord interactions in guild {guild}.");
    }

    private async Task ConfigureGovernanceChannelPermissionsAsync()
    {
        if (services.GetService(typeof(Config)) is not Config config)
            return;

        var socketGuild = client.GetGuild(guild);
        if (socketGuild == null)
            return;

        var channelIds = new[] { config.CourtChannel, config.GovernanceChannel }
            .Where(value => value != 0)
            .Distinct()
            .ToArray();

        foreach (var channelId in channelIds)
        {
            if (client.GetChannel(channelId) is not SocketGuildChannel channel)
                continue;

            try
            {
                // Court threads are the deliberate exception to the read-only Governance ACL:
                // Discord must allow thread messages so claimant/defendant can speak there.
                // GovernanceDiscordConversationCoordinator still deletes messages from everyone
                // except the two parties while the defense stage is open.
                var allowCourtThreadMessages = channelId == config.CourtChannel;
                var everyone = socketGuild.EveryoneRole;
                var everyoneCurrent = channel.GetPermissionOverwrite(everyone) ?? OverwritePermissions.InheritAll;
                var everyoneReadOnly = everyoneCurrent.Modify(
                    sendMessages: PermValue.Deny,
                    createPublicThreads: PermValue.Deny,
                    createPrivateThreads: PermValue.Deny,
                    sendMessagesInThreads: allowCourtThreadMessages ? PermValue.Allow : PermValue.Deny);
                if (!everyoneCurrent.Equals(everyoneReadOnly))
                    await channel.AddPermissionOverwriteAsync(everyone, everyoneReadOnly);

                var bot = socketGuild.CurrentUser;
                var botCurrent = channel.GetPermissionOverwrite(bot) ?? OverwritePermissions.InheritAll;
                var botWritable = botCurrent.Modify(
                    sendMessages: PermValue.Allow,
                    embedLinks: PermValue.Allow,
                    attachFiles: PermValue.Allow,
                    manageThreads: PermValue.Allow,
                    createPublicThreads: PermValue.Allow,
                    createPrivateThreads: PermValue.Allow,
                    sendMessagesInThreads: PermValue.Allow);
                if (!botCurrent.Equals(botWritable))
                    await channel.AddPermissionOverwriteAsync(bot, botWritable);

                var mode = allowCourtThreadMessages
                    ? "read-only outside threads; court thread messages enabled"
                    : "read-only for regular members";
                await Logger.Info($"Governance Discord channel '{channel.Name}' ({channel.Id}) configured {mode}.");
            }
            catch (Exception exception)
            {
                await Logger.Error($"Could not configure Governance permissions for channel {channelId}", exception);
            }
        }
    }

    private async Task HandleInteractionAsync(SocketInteraction socketInteraction)
    {
        var context = new SocketInteractionContext(client, socketInteraction);
        var result = await interaction.ExecuteCommandAsync(context, services);
        if (!result.IsSuccess && result.Error != InteractionCommandError.UnknownCommand)
            await Logger.Info($"Interaction failed for {socketInteraction.User.Id}: {result.ErrorReason}");
    }

    private async Task HandleCommandAsync(SocketMessage messageParam)
    {
        // Governance parent channels are read-only. Court threads are intentionally writable at
        // the Discord permission layer and are filtered by GovernanceDiscordConversationCoordinator.
        var message = messageParam as SocketUserMessage;
        if (message == null || message.Author.IsBot)
            return;

        var argPos = 0;
        if (!(message.HasCharPrefix('!', ref argPos) ||
            message.HasMentionPrefix(client.CurrentUser, ref argPos)))
            return;

        var context = new SocketCommandContext(client, message);
        var result = await commands.ExecuteAsync(context, argPos, null);
        if (!result.IsSuccess)
        {
            var reason = result.ErrorReason ?? "неизвестная ошибка команды";
            await Logger.Info($"Command '{message.Content}' failed for {message.Author.Username}: {reason}");

            if (result.Error != CommandError.UnknownCommand)
                await context.Channel.SendMessageAsync($"Команда не выполнена: {reason}");
        }
    }

    private async Task HandleButtonAsync(SocketMessageComponent component)
    {
        switch (component.Data.CustomId)
        {
            case "link-ss14-account":
                await component.RespondWithModalAsync<LinkAccountModal>("link-ss14-account");
                break;
        }
    }

    private async Task HandleModalAsync(SocketModal modal)
    {
        await using var db = databaseFactory();
        switch (modal.Data.CustomId)
        {
            case "link-ss14-account":
                if (modal.GuildId is not { } guildId)
                    break;

                var codeStr = modal.Data.Components.First(c => c.CustomId == "account_code").Value.Trim();
                if (string.IsNullOrWhiteSpace(codeStr))
                    break;

                await modal.DeferAsync(true);
                if (!Guid.TryParse(codeStr, out var code))
                {
                    await modal.FollowupAsync(
                        $"`{codeStr}` — некорректный код привязки. Получите новый код в лобби игры и повторите попытку.",
                        ephemeral: true);
                    break;
                }

                var authorId = modal.User.Id;
                var discord = await db.RMCDiscordAccounts
                    .Include(d => d.LinkedAccount)
                    .ThenInclude(l => l.Player)
                    .FirstOrDefaultAsync(a => a.Id == authorId);
                var codes = await db.RMCLinkingCodes
                    .Include(l => l.Player)
                    .FirstOrDefaultAsync(p => p.Code == code);

                if (codes == null)
                {
                    await modal.FollowupAsync(
                        "Код привязки не найден. Зайдите на игровой сервер, получите новый код в лобби и повторите попытку.",
                        ephemeral: true);
                    break;
                }

                if (codes.CreationTime < DateTime.UtcNow.Subtract(TimeSpan.FromDays(1)))
                {
                    await modal.FollowupAsync(
                        "Срок действия кода привязки истёк. Получите новый код в лобби игры.",
                        ephemeral: true);
                    break;
                }

                var targetPlayerId = codes.Player.UserId;
                if (discord?.LinkedAccount is { } currentDiscordLink && currentDiscordLink.PlayerId != targetPlayerId)
                {
                    await modal.FollowupAsync(
                        $"Ваш Discord уже связан с SS14-аккаунтом **{currentDiscordLink.Player.LastSeenUserName}**. Перепривязка запрещена.",
                        ephemeral: true);
                    break;
                }

                var currentPlayerLink = await db.RMCLinkedAccounts.AsNoTracking()
                    .SingleOrDefaultAsync(value => value.PlayerId == targetPlayerId);
                if (currentPlayerLink != null && currentPlayerLink.DiscordId != authorId)
                {
                    await modal.FollowupAsync(
                        "Этот SS14-аккаунт уже связан с другим Discord. Перепривязка запрещена.",
                        ephemeral: true);
                    break;
                }

                try
                {
                    // Permanent Governance identity is checked before any game-database mutation.
                    await identities.ValidatePermanentLinkAsync(targetPlayerId, authorId);
                }
                catch (CourtRuleException exception)
                {
                    await modal.FollowupAsync(exception.Message, ephemeral: true);
                    break;
                }

                var createdLink = false;
                if (discord?.LinkedAccount == null)
                {
                    discord ??= db.RMCDiscordAccounts.Add(new RMCDiscordAccount { Id = authorId }).Entity;
                    discord.LinkedAccount = db.RMCLinkedAccounts.Add(new RMCLinkedAccount { Discord = discord }).Entity;
                    discord.LinkedAccount.Player = codes.Player;
                    createdLink = true;
                }

                var memberLookup = await guildMembers.LookupAsync(authorId);
                RMCPatronTier? selectedTier = null;
                if (memberLookup.IsDefinitive)
                {
                    var roles = memberLookup.User?.RoleIds.ToArray() ?? [];
                    selectedTier = await db.RMCPatronTiers
                        .Where(t => roles.Contains(t.DiscordRole))
                        .OrderBy(t => t.Priority)
                        .FirstOrDefaultAsync();
                }
                else
                {
                    await Logger.Info(
                        $"[WARNING] Skipping patron update while linking discord id {authorId} " +
                        $"to player id {targetPlayerId}: Discord lookup was not definitive.");
                }

                if (createdLink)
                {
                    db.RMCLinkedAccountLogs.Add(new RMCLinkedAccountLogs
                    {
                        Discord = discord!,
                        Player = discord!.LinkedAccount.Player,
                    });
                }

                await using (var transaction = await db.Database.BeginTransactionAsync())
                {
                    db.ChangeTracker.DetectChanges();
                    await db.SaveChangesAsync();
                    if (memberLookup.IsDefinitive)
                    {
                        if (selectedTier == null)
                            await RMCPatronPersistence.RemoveAsync(db, targetPlayerId);
                        else
                            await RMCPatronPersistence.SetTierAsync(db, targetPlayerId, selectedTier.Id);
                    }
                    await transaction.CommitAsync();
                }
                try
                {
                    await identities.SyncLinkedAccountAsync(targetPlayerId, authorId);
                }
                catch (CourtRuleException exception)
                {
                    await Logger.Error(
                        $"Governance identity synchronization rejected game link Discord {authorId} -> SS14 {targetPlayerId}",
                        exception);
                    await modal.FollowupAsync(
                        "Игровая связь сохранена, но проверка Governance обнаружила конфликт постоянной идентичности. Обратитесь к руководству; автоматическая перепривязка не выполнялась.",
                        ephemeral: true);
                    break;
                }

                var msg = $"SS14-аккаунт **{codes.Player.LastSeenUserName}** успешно связан с вашим Discord. Эта связь постоянная и не может быть перепривязана к другому аккаунту.";
                if (selectedTier != null)
                    msg += $" Уровень поддержки: **{selectedTier.Name}**.";

                await modal.FollowupAsync(msg, ephemeral: true);
                break;
        }
    }

    private async Task RefreshPatrons()
    {
        while (Interlocked.CompareExchange(ref Running, 1, 1) == 1)
        {
            try
            {
                List<LinkedPatronSnapshot> patrons;
                await using (var readDb = databaseFactory())
                {
                    patrons = await readDb.RMCLinkedAccounts
                        .AsNoTracking()
                        .Select(linked => new LinkedPatronSnapshot(
                            linked.PlayerId,
                            linked.DiscordId,
                            linked.Player.LastSeenUserName))
                        .ToListAsync();
                }

                var decisions = new List<PatronRefreshDecision>();
                foreach (var linked in patrons)
                {
                    try
                    {
                        var lookup = await guildMembers.LookupAsync(linked.DiscordId);
                        if (!lookup.IsDefinitive)
                        {
                            await Logger.Info(
                                $"[WARNING] Skipping patron refresh for discord id {linked.DiscordId} " +
                                $"and player id {linked.PlayerId}: Discord lookup was not definitive.");
                            continue;
                        }

                        var user = lookup.User;
                        if (user == null)
                        {
                            decisions.Add(new PatronRefreshDecision(
                                linked.PlayerId,
                                linked.DiscordId,
                                linked.PlayerName,
                                null,
                                null,
                                null));
                            continue;
                        }

                        var tier = _tierPriority.FirstOrDefault(value => user.RoleIds.Contains(value.DiscordRole));
                        decisions.Add(new PatronRefreshDecision(
                            linked.PlayerId,
                            linked.DiscordId,
                            linked.PlayerName,
                            user.Username,
                            tier?.Id,
                            tier?.Name));
                    }
                    catch (Exception e)
                    {
                        await Logger.Error($"Error updating patron with discord id {linked.DiscordId} and player id {linked.PlayerId}", e);
                    }
                }

                var changes = new List<PatronRefreshDecision>();
                await using (var writeDb = databaseFactory())
                await using (var transaction = await writeDb.Database.BeginTransactionAsync())
                {
                    foreach (var decision in decisions)
                    {
                        var changed = decision.TierId == null
                            ? await RMCPatronPersistence.RemoveAsync(writeDb, decision.PlayerId)
                            : await RMCPatronPersistence.SetTierAsync(
                                writeDb,
                                decision.PlayerId,
                                decision.TierId.Value);
                        if (changed)
                            changes.Add(decision);
                    }

                    await transaction.CommitAsync();
                }

                foreach (var change in changes)
                {
                    if (change.TierId != null)
                    {
                        await Logger.Info(
                            $"Updated patron {change.DiscordUsername}:{change.DiscordId}:{change.PlayerName} " +
                            $"with tier {change.TierName}");
                    }
                    else if (change.DiscordUsername == null)
                    {
                        await Logger.Info($"Removed patron {change.DiscordId}:{change.PlayerName}");
                    }
                    else
                    {
                        await Logger.Info(
                            $"Removed patron {change.DiscordUsername}:{change.DiscordId}:{change.PlayerName}");
                    }
                }
            }
            catch (Exception e)
            {
                await Logger.Error("Error refreshing patrons", e);
            }

            await Task.Delay(60000);
        }
    }
}
