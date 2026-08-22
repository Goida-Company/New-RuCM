using System.Data;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed record CourtDefenseCompletionResult(
    bool ClaimantConfirmed,
    bool DefendantConfirmed,
    bool Transitioned);

/// <summary>
/// Bridges Governance conversations to Discord without making Discord authoritative.
/// AHelp is mirrored one-way from the game database. Court parties may talk directly
/// in their public case thread during the defense stage; every accepted message is
/// persisted as immutable Court material before it is considered part of the case.
/// </summary>
public sealed class GovernanceDiscordConversationCoordinator
{
    private readonly DiscordSocketClient _client;
    private readonly Func<GovernanceDbContext> _governanceFactory;
    private readonly Config _config;

    public GovernanceDiscordConversationCoordinator(
        DiscordSocketClient client,
        Func<GovernanceDbContext> governanceFactory,
        Config config)
    {
        _client = client;
        _governanceFactory = governanceFactory;
        _config = config;
        _client.MessageReceived += HandleMessageAsync;
        _client.Ready += ConfigureCourtThreadPermissionsAsync;
    }

    public async Task RunSchedulerAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(5);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_client.ConnectionState == Discord.ConnectionState.Connected)
                {
                    // CommandHandler keeps Governance channels read-only by default. Court is the
                    // deliberate exception: members may send in an existing case thread, while this
                    // coordinator immediately rejects everyone except the two parties.
                    await ConfigureCourtThreadPermissionsAsync();
                    await SyncAHelpsAsync(cancellationToken);
                }
            }
            catch (Exception exception)
            {
                await Logger.Error("Governance Discord conversation scheduler iteration failed", exception);
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    public async Task<CourtDefenseCompletionResult> ConfirmDefenseCompleteAsync(long caseId, ulong discordId)
    {
        if (!_config.CourtEnabled)
            throw new CourtRuleException("Community Court сейчас отключён в конфигурации бота.");
        if (discordId == 0 || discordId > long.MaxValue)
            throw new CourtRuleException("Некорректный Discord ID.");

        await using var governance = _governanceFactory();
        await using var transaction = await governance.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        _ = await governance.Database.SqlQuery<long>($"""
            SELECT id AS "Value"
            FROM governance.court_cases
            WHERE id = {caseId}
            FOR UPDATE
            """).SingleOrDefaultAsync();

        var courtCase = await governance.CourtCases.SingleOrDefaultAsync(value => value.Id == caseId)
            ?? throw new CourtRuleException("Дело не найдено.");
        if (courtCase.Status != CourtStatuses.Defense)
            throw new CourtRuleException("Стадия защиты по этому делу уже завершена.");
        if (courtCase.DefenseDeadline <= DateTime.UtcNow)
            throw new CourtRuleException("Срок стадии защиты уже истёк.");

        var userId = await governance.Users.AsNoTracking()
            .Where(value => value.DiscordUserId == checked((long) discordId))
            .Select(value => (Guid?) value.Id)
            .SingleOrDefaultAsync()
            ?? throw new CourtRuleException("Discord-аккаунт не связан с Governance-профилем.");

        var isClaimant = userId == courtCase.ClaimantUserId;
        var isDefendant = userId == courtCase.DefendantUserId;
        if (!isClaimant && !isDefendant)
            throw new CourtRuleException("Завершение защиты могут подтвердить только истец и ответчик.");

        var now = DateTime.UtcNow;
        var inserted = await governance.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO governance.court_defense_confirmations(case_id, user_id, confirmed_at)
            VALUES ({caseId}, {userId}, {now})
            ON CONFLICT (case_id, user_id) DO NOTHING
            """);
        if (inserted > 0)
        {
            governance.AuditEvents.Add(new GovernanceAuditEvent
            {
                EventType = "court.defense_finish_confirmed",
                ActorType = "discord_user",
                ActorId = discordId.ToString(),
                EntityType = "court_case",
                EntityId = caseId.ToString(),
                CreatedAt = now,
                Payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    role = isClaimant ? "claimant" : "defendant",
                }),
            });
        }

        var confirmed = (await governance.Database.SqlQuery<Guid>($"""
            SELECT user_id AS "Value"
            FROM governance.court_defense_confirmations
            WHERE case_id = {caseId}
              AND user_id IN ({courtCase.ClaimantUserId}, {courtCase.DefendantUserId})
            """).ToListAsync()).ToHashSet();

        var claimantConfirmed = confirmed.Contains(courtCase.ClaimantUserId);
        var defendantConfirmed = confirmed.Contains(courtCase.DefendantUserId);
        var transitioned = claimantConfirmed && defendantConfirmed;
        if (transitioned)
        {
            courtCase.Status = CourtStatuses.AwaitingJury;
            courtCase.Version++;
            governance.AuditEvents.Add(new GovernanceAuditEvent
            {
                EventType = "court.defense_completed_by_parties",
                ActorType = "system",
                EntityType = "court_case",
                EntityId = caseId.ToString(),
                CreatedAt = now,
                Payload = "{\"claimant_confirmed\":true,\"defendant_confirmed\":true}",
            });
        }

        await governance.SaveChangesAsync();
        await transaction.CommitAsync();
        return new CourtDefenseCompletionResult(claimantConfirmed, defendantConfirmed, transitioned);
    }

    public async Task LockDefenseThreadAsync(long caseId)
    {
        await using var governance = _governanceFactory();
        var threadId = await governance.CourtCases.AsNoTracking()
            .Where(value => value.Id == caseId)
            .Select(value => value.DiscordThreadId)
            .SingleOrDefaultAsync();
        if (threadId is not > 0 || _client.GetChannel(checked((ulong) threadId.Value)) is not SocketThreadChannel thread)
            return;

        try
        {
            await thread.SendMessageAsync(embed: new EmbedBuilder()
                .WithTitle("Стадия защиты завершена")
                .WithDescription("Истец и ответчик подтвердили завершение защиты. Переписка сторон закрыта; дело переходит к формированию коллегии присяжных.")
                .WithColor(Color.DarkBlue)
                .WithCurrentTimestamp()
                .Build());
            await thread.ModifyAsync(properties => properties.Locked = true);
        }
        catch (Exception exception)
        {
            await Logger.Error($"Could not lock Court thread after defense completion for case {caseId}", exception);
        }
    }

    private async Task HandleMessageAsync(SocketMessage rawMessage)
    {
        if (rawMessage is not SocketUserMessage message || message.Channel is not SocketThreadChannel thread)
            return;
        if (_client.CurrentUser != null && message.Author.Id == _client.CurrentUser.Id)
            return;

        try
        {
            await using var governance = _governanceFactory();
            var courtCase = await governance.Database.SqlQueryRaw<CourtThreadRow>("""
                SELECT court.id AS "CaseId",
                       court.claimant_user_id AS "ClaimantUserId",
                       court.defendant_user_id AS "DefendantUserId",
                       claimant.discord_user_id AS "ClaimantDiscordId",
                       defendant.discord_user_id AS "DefendantDiscordId",
                       court.status AS "Status",
                       court.defense_deadline AS "DefenseDeadline",
                       EXISTS (
                           SELECT 1
                           FROM governance.court_defense_confirmations AS confirmation
                           WHERE confirmation.case_id = court.id
                             AND confirmation.user_id = court.claimant_user_id) AS "ClaimantConfirmed",
                       EXISTS (
                           SELECT 1
                           FROM governance.court_defense_confirmations AS confirmation
                           WHERE confirmation.case_id = court.id
                             AND confirmation.user_id = court.defendant_user_id) AS "DefendantConfirmed"
                FROM governance.court_cases AS court
                JOIN governance.users AS claimant ON claimant.id = court.claimant_user_id
                JOIN governance.users AS defendant ON defendant.id = court.defendant_user_id
                WHERE court.discord_thread_id = @thread_id
                LIMIT 1
                """, new Npgsql.NpgsqlParameter("thread_id", checked((long) thread.Id)))
                .SingleOrDefaultAsync();

            if (courtCase == null)
            {
                var ahelp = await governance.AHelpTickets.AsNoTracking()
                    .AnyAsync(value => value.DiscordThreadId == checked((long) thread.Id));
                if (ahelp)
                    await DeleteUnauthorizedMessageAsync(message);
                return;
            }

            var authorId = checked((long) message.Author.Id);
            var isClaimant = courtCase.ClaimantDiscordId == authorId;
            var isDefendant = courtCase.DefendantDiscordId == authorId;
            var authorAlreadyFinished = isClaimant && courtCase.ClaimantConfirmed ||
                                        isDefendant && courtCase.DefendantConfirmed;
            var discussionOpen = courtCase.Status == CourtStatuses.Defense &&
                                 courtCase.DefenseDeadline > DateTime.UtcNow &&
                                 !authorAlreadyFinished;
            if ((!isClaimant && !isDefendant) || !discussionOpen)
            {
                await DeleteUnauthorizedMessageAsync(message);
                return;
            }

            var body = message.Content.Trim();
            var attachments = message.Attachments.Select(value => value.Url).ToArray();
            if (body.Length == 0 && attachments.Length == 0)
            {
                await DeleteUnauthorizedMessageAsync(message);
                return;
            }
            if (body.Length == 0)
                body = "Вложение Discord без текстового комментария.";

            var evidence = attachments.Length == 0 ? null : string.Join('\n', attachments);
            var authorUserId = isClaimant ? courtCase.ClaimantUserId : courtCase.DefendantUserId;
            governance.CourtStatements.Add(new GovernanceCourtStatement
            {
                CaseId = courtCase.CaseId,
                AuthorUserId = authorUserId,
                Kind = isClaimant ? "claimant_discussion" : "defendant_discussion",
                Body = body,
                EvidenceReference = evidence,
                CreatedAt = message.Timestamp.UtcDateTime,
            });
            governance.AuditEvents.Add(new GovernanceAuditEvent
            {
                EventType = "court.party_message_recorded",
                ActorType = "discord_user",
                ActorId = message.Author.Id.ToString(),
                EntityType = "court_case",
                EntityId = courtCase.CaseId.ToString(),
                CreatedAt = DateTime.UtcNow,
                Payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    role = isClaimant ? "claimant" : "defendant",
                    discord_message_id = message.Id,
                    attachments = attachments.Length,
                }),
            });
            await governance.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            await Logger.Error($"Could not process Governance thread message {message.Id}", exception);
            try
            {
                await message.DeleteAsync();
            }
            catch
            {
                // Preserve the original error; deletion is only a fail-closed cleanup attempt.
            }
        }
    }

    private async Task SyncAHelpsAsync(CancellationToken cancellationToken)
    {
        await using var governance = _governanceFactory();
        var pending = await governance.Database.SqlQueryRaw<AHelpSyncRow>("""
            SELECT ticket.id AS "TicketId",
                   ticket.discord_thread_id AS "ThreadId",
                   ticket.round_id AS "RoundId",
                   ticket.status AS "Status",
                   ticket.summary AS "Summary",
                   COALESCE(player.last_seen_user_name, ticket.reporter_ss14_user_id::text) AS "ReporterName",
                   sync.status_message_id AS "StatusMessageId",
                   COALESCE(sync.last_message_id, 0) AS "LastMessageId",
                   sync.last_status AS "LastStatus"
            FROM governance.ahelp_tickets AS ticket
            LEFT JOIN player ON player.user_id = ticket.reporter_ss14_user_id
            LEFT JOIN governance.ahelp_discord_sync AS sync ON sync.ticket_id = ticket.id
            WHERE ticket.discord_thread_id IS NOT NULL
              AND (
                    sync.ticket_id IS NULL
                    OR sync.last_status IS DISTINCT FROM ticket.status
                    OR EXISTS (
                        SELECT 1
                        FROM governance.ahelp_messages AS message
                        WHERE message.ticket_id = ticket.id
                          AND message.id > COALESCE(sync.last_message_id, 0)))
            ORDER BY ticket.id
            """).ToListAsync(cancellationToken);

        foreach (var row in pending)
        {
            try
            {
                await SyncAHelpAsync(row, cancellationToken);
            }
            catch (Exception exception)
            {
                await Logger.Error($"Could not synchronize AHelp {row.TicketId} to Discord", exception);
            }
        }
    }

    private async Task SyncAHelpAsync(AHelpSyncRow row, CancellationToken cancellationToken)
    {
        if (_client.GetChannel(checked((ulong) row.ThreadId)) is not SocketThreadChannel thread)
            return;

        IUserMessage? statusMessage = null;
        if (row.StatusMessageId is > 0)
            statusMessage = await thread.GetMessageAsync(checked((ulong) row.StatusMessageId.Value)) as IUserMessage;

        var statusEmbed = BuildAHelpStatusEmbed(row);
        if (statusMessage == null)
        {
            statusMessage = await thread.SendMessageAsync(embed: statusEmbed);
        }
        else if (!string.Equals(row.LastStatus, row.Status, StringComparison.Ordinal))
        {
            await statusMessage.ModifyAsync(properties => properties.Embed = statusEmbed);
        }

        await MarkAHelpSyncAsync(row.TicketId, statusMessage.Id, row.LastMessageId, row.Status, cancellationToken);

        await using var governance = _governanceFactory();
        var messages = await governance.Database.SqlQueryRaw<AHelpMessageRow>("""
            SELECT message.id AS "Id",
                   COALESCE(player.last_seen_user_name, message.sender_ss14_user_id::text) AS "SenderName",
                   message.body AS "Body",
                   message.created_at AS "CreatedAt",
                   (message.sender_ss14_user_id = ticket.reporter_ss14_user_id) AS "FromReporter"
            FROM governance.ahelp_messages AS message
            JOIN governance.ahelp_tickets AS ticket ON ticket.id = message.ticket_id
            LEFT JOIN player ON player.user_id = message.sender_ss14_user_id
            WHERE message.ticket_id = @ticket_id
              AND message.id > @last_message_id
            ORDER BY message.id
            """,
            new Npgsql.NpgsqlParameter("ticket_id", row.TicketId),
            new Npgsql.NpgsqlParameter("last_message_id", row.LastMessageId))
            .ToListAsync(cancellationToken);

        var lastMessageId = row.LastMessageId;
        foreach (var message in messages)
        {
            await thread.SendMessageAsync(embed: new EmbedBuilder()
                .WithAuthor($"{(message.FromReporter ? "Игрок" : "Дежурный")} · {EscapeDiscord(message.SenderName)}")
                .WithDescription(EscapeDiscord(message.Body))
                .WithColor(message.FromReporter ? Color.Gold : Color.Blue)
                .WithTimestamp(new DateTimeOffset(message.CreatedAt))
                .Build());
            lastMessageId = message.Id;
            await MarkAHelpSyncAsync(row.TicketId, statusMessage.Id, lastMessageId, row.Status, cancellationToken);
        }
    }

    private async Task MarkAHelpSyncAsync(
        long ticketId,
        ulong statusMessageId,
        long lastMessageId,
        string status,
        CancellationToken cancellationToken)
    {
        await using var governance = _governanceFactory();
        await governance.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO governance.ahelp_discord_sync(
                ticket_id, status_message_id, last_message_id, last_status, updated_at)
            VALUES ({ticketId}, {checked((long) statusMessageId)}, {lastMessageId}, {status}, now())
            ON CONFLICT (ticket_id) DO UPDATE
            SET status_message_id = EXCLUDED.status_message_id,
                last_message_id = GREATEST(governance.ahelp_discord_sync.last_message_id, EXCLUDED.last_message_id),
                last_status = EXCLUDED.last_status,
                updated_at = now()
            """, cancellationToken);
    }

    private async Task ConfigureCourtThreadPermissionsAsync()
    {
        if (!_config.CourtEnabled || _config.CourtChannel == 0)
            return;
        var guild = _client.GetGuild(_config.Guild);
        if (guild == null || _client.GetChannel(_config.CourtChannel) is not SocketGuildChannel channel)
            return;

        var everyone = guild.EveryoneRole;
        var everyoneCurrent = channel.GetPermissionOverwrite(everyone) ?? OverwritePermissions.InheritAll;
        var everyoneCourt = everyoneCurrent.Modify(
            sendMessages: PermValue.Deny,
            createPublicThreads: PermValue.Deny,
            createPrivateThreads: PermValue.Deny,
            sendMessagesInThreads: PermValue.Allow);
        if (!everyoneCurrent.Equals(everyoneCourt))
            await channel.AddPermissionOverwriteAsync(everyone, everyoneCourt);

        var bot = guild.CurrentUser;
        var botCurrent = channel.GetPermissionOverwrite(bot) ?? OverwritePermissions.InheritAll;
        var botCourt = botCurrent.Modify(
            sendMessages: PermValue.Allow,
            embedLinks: PermValue.Allow,
            attachFiles: PermValue.Allow,
            manageMessages: PermValue.Allow,
            manageThreads: PermValue.Allow,
            createPublicThreads: PermValue.Allow,
            createPrivateThreads: PermValue.Allow,
            sendMessagesInThreads: PermValue.Allow);
        if (!botCurrent.Equals(botCourt))
            await channel.AddPermissionOverwriteAsync(bot, botCourt);
    }

    private static async Task DeleteUnauthorizedMessageAsync(SocketUserMessage message)
    {
        try
        {
            await message.DeleteAsync();
        }
        catch (Exception exception)
        {
            await Logger.Error($"Could not remove unauthorized Governance thread message {message.Id}", exception);
        }
    }

    private static Embed BuildAHelpStatusEmbed(AHelpSyncRow row)
    {
        var (status, color) = row.Status switch
        {
            "open" => ("Открыт", Color.Gold),
            "claimed" => ("В работе", Color.Blue),
            "waiting_player" => ("Ожидается ответ игрока", Color.Orange),
            "escalated_to_incident" => ("Передан в инцидент", Color.DarkOrange),
            "escalated_to_court" => ("Передан в Community Court", Color.DarkPurple),
            "resolved" => ("Закрыт", Color.Green),
            _ => (row.Status, Color.DarkGrey),
        };

        return new EmbedBuilder()
            .WithTitle($"AHelp №{row.TicketId} · раунд {row.RoundId}")
            .WithDescription(EscapeDiscord(row.Summary))
            .AddField("Заявитель", EscapeDiscord(row.ReporterName), true)
            .AddField("Статус", status, true)
            .WithColor(color)
            .WithCurrentTimestamp()
            .Build();
    }

    private static string EscapeDiscord(string value)
    {
        return value.Replace("@", "@\u200B", StringComparison.Ordinal)
            .Replace("`", "ˋ", StringComparison.Ordinal);
    }

    private sealed record AHelpSyncRow(
        long TicketId,
        long ThreadId,
        int RoundId,
        string Status,
        string Summary,
        string ReporterName,
        long? StatusMessageId,
        long LastMessageId,
        string? LastStatus);

    private sealed record AHelpMessageRow(
        long Id,
        string SenderName,
        string Body,
        DateTime CreatedAt,
        bool FromReporter);

    private sealed record CourtThreadRow(
        long CaseId,
        Guid ClaimantUserId,
        Guid DefendantUserId,
        long? ClaimantDiscordId,
        long? DefendantDiscordId,
        string Status,
        DateTime DefenseDeadline,
        bool ClaimantConfirmed,
        bool DefendantConfirmed);
}
