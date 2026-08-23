using System.Text.Json;
using Discord;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

public sealed class CourtSchedulerCoordinator(
    DiscordSocketClient client,
    CourtDiscordCoordinator discord,
    Func<GovernanceDbContext> governanceFactory,
    Config config)
{
    private const string DefenseRecoveredEvent = "court.defense_skipped_no_discord_recovered";
    private const string JurySearchNotifiedEvent = "court.jury_search_notified";

    public async Task RunSchedulerAsync(CancellationToken cancellationToken)
    {
        var delay = TimeSpan.FromSeconds(Math.Clamp(config.CourtSchedulerSeconds, 10, 3600));
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync();
            }
            catch (Exception exception)
            {
                await Logger.Error("Community Court scheduler iteration failed", exception);
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    public async Task ProcessOnceAsync()
    {
        if (!config.CourtEnabled)
            return;
        if (client.ConnectionState != ConnectionState.Connected)
            return;

        // Notify cases that are already waiting before the normal pass starts selecting candidates.
        await PublishJurySearchNoticesAsync();

        // The normal pass synchronizes linked accounts first, so recovery below uses fresh Discord identity data.
        await discord.ProcessOnceAsync();

        var recovered = await RecoverUnreachableDefendantsAsync();
        await PublishJurySearchNoticesAsync();

        // Recovered cases have just moved to AwaitingJury; select and notify candidates immediately.
        if (recovered > 0)
            await discord.ProcessOnceAsync();
    }

    private async Task<int> RecoverUnreachableDefendantsAsync()
    {
        var now = DateTime.UtcNow;
        await using var governance = governanceFactory();
        await using var transaction = await governance.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var stuckCases = await governance.CourtCases
            .Join(
                governance.Users,
                courtCase => courtCase.DefendantUserId,
                user => user.Id,
                (courtCase, user) => new { CourtCase = courtCase, Defendant = user })
            .Where(value =>
                value.CourtCase.Status == CourtStatuses.Defense &&
                (!value.Defendant.DiscordUserId.HasValue || value.Defendant.DiscordUserId <= 0))
            .Select(value => value.CourtCase)
            .ToListAsync();

        foreach (var courtCase in stuckCases)
        {
            courtCase.Status = CourtStatuses.AwaitingJury;
            courtCase.DefenseDeadline = now;
            courtCase.Version++;
            governance.AuditEvents.Add(new GovernanceAuditEvent
            {
                EventType = DefenseRecoveredEvent,
                ActorType = "system",
                EntityType = "court_case",
                EntityId = courtCase.Id.ToString(),
                CreatedAt = now,
                Payload = JsonSerializer.Serialize(new
                {
                    reason = "defendant_has_no_discord",
                    recovered_status = CourtStatuses.AwaitingJury,
                }),
            });
        }

        await governance.SaveChangesAsync();
        await transaction.CommitAsync();
        return stuckCases.Count;
    }

    private async Task PublishJurySearchNoticesAsync()
    {
        await using var governance = governanceFactory();
        var notified = (await governance.AuditEvents.AsNoTracking()
                .Where(value =>
                    value.EventType == JurySearchNotifiedEvent &&
                    value.EntityType == "court_case")
                .Select(value => value.EntityId)
                .ToListAsync())
            .ToHashSet(StringComparer.Ordinal);

        var candidates = await governance.CourtCases.AsNoTracking()
            .Where(value =>
                value.Status == CourtStatuses.AwaitingJury ||
                value.Status == CourtStatuses.Jury)
            .OrderBy(value => value.Id)
            .ToListAsync();

        foreach (var courtCase in candidates)
        {
            var entityId = courtCase.Id.ToString();
            if (notified.Contains(entityId))
                continue;

            var defendantDiscordId = await governance.Users.AsNoTracking()
                .Where(value => value.Id == courtCase.DefendantUserId)
                .Select(value => value.DiscordUserId)
                .SingleOrDefaultAsync();
            var defendantHasNoDiscord = defendantDiscordId == null || defendantDiscordId <= 0;

            var thread = await discord.EnsureCaseThreadAsync(courtCase);
            var description = defendantHasNoDiscord
                ? "Ответчик не привязал Discord к SS14, поэтому стадия защиты пропущена. Система начала формирование коллегии присяжных и рассылает приглашения подходящим кандидатам."
                : "Стадия защиты завершена. Система начала формирование коллегии присяжных и рассылает приглашения подходящим кандидатам.";

            await thread.SendMessageAsync(embed: new EmbedBuilder()
                .WithTitle("Начат поиск присяжных")
                .WithDescription(description)
                .WithColor(Color.DarkOrange)
                .WithCurrentTimestamp()
                .Build());

            await MarkJurySearchNotifiedAsync(courtCase.Id);
            notified.Add(entityId);
        }
    }

    private async Task MarkJurySearchNotifiedAsync(long caseId)
    {
        await using var governance = governanceFactory();
        var entityId = caseId.ToString();
        if (await governance.AuditEvents.AnyAsync(value =>
                value.EventType == JurySearchNotifiedEvent &&
                value.EntityType == "court_case" &&
                value.EntityId == entityId))
            return;

        governance.AuditEvents.Add(new GovernanceAuditEvent
        {
            EventType = JurySearchNotifiedEvent,
            ActorType = "system",
            EntityType = "court_case",
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow,
            Payload = "{}",
        });
        await governance.SaveChangesAsync();
    }
}
