using System.Text.Json;
using Content.DiscordBot;
using Content.DiscordBot.Governance;
using Content.Server.Database;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var client = new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents =
        GatewayIntents.Guilds |
        GatewayIntents.GuildMessages |
        GatewayIntents.MessageContent,
});
client.Log += Logger.Log;
var seedBoostyTiers = args.Contains("--seed-boosty-tiers");
var listBoostyTiers = args.Contains("--list-boosty-tiers");
var listTestPatrons = args.Contains("--list-test-patrons");
var grantTestTierIndex = Array.IndexOf(args, "--grant-test-tier");
var migrateOnly = args.Contains("--migrate-only");
var governanceDoctor = args.Contains("--governance-doctor");
var environmentFileIndex = Array.IndexOf(args, "--env-file");
if (environmentFileIndex >= 0)
{
    if (args.Length <= environmentFileIndex + 1)
        throw new ArgumentException("Usage: --env-file <path>");
    ConfigurationLoader.LoadEnvironmentFile(args[environmentFileIndex + 1]);
}

string? token = null;
string? connectionString = null;
var guild = 0UL;
var config = new Config();
if (File.Exists("config.json"))
{
    config = await JsonSerializer.DeserializeAsync<Config>(File.OpenRead("config.json")) ?? new Config();
    token = config.Token;
    connectionString = config.DatabaseString;
    guild = config.Guild;
}

ConfigurationLoader.ApplyEnvironment(config, ref token, ref connectionString, ref guild);

if (string.IsNullOrWhiteSpace(connectionString))
    throw new ArgumentException("No database connection string found.");

ServerDbContext CreateConfiguredDatabase()
{
    var postgresBuilder = new DbContextOptionsBuilder<PostgresServerDbContext>();
    postgresBuilder.UseNpgsql(connectionString);
    return new PostgresServerDbContext(postgresBuilder.Options);
}

GovernanceDbContext CreateGovernanceDatabase()
{
    var builder = new DbContextOptionsBuilder<GovernanceDbContext>();
    builder.UseNpgsql(connectionString);
    return new GovernanceDbContext(builder.Options);
}

async Task WithConfiguredDatabase(Func<ServerDbContext, Task> action)
{
    await using var db = CreateConfiguredDatabase();
    await action(db);
}

if (seedBoostyTiers)
{
    await WithConfiguredDatabase(BoostyTierSeeder.Seed);
    Console.WriteLine("Boosty sponsor tiers seeded.");
    return;
}

if (listBoostyTiers)
{
    await WithConfiguredDatabase(BoostyTierSeeder.PrintTiers);
    return;
}

if (listTestPatrons)
{
    await WithConfiguredDatabase(BoostyTierSeeder.PrintPatrons);
    return;
}

if (grantTestTierIndex >= 0)
{
    if (args.Length <= grantTestTierIndex + 2)
        throw new ArgumentException("Usage: --grant-test-tier <player-name-or-user-id> <tier-name>");

    var playerNameOrId = args[grantTestTierIndex + 1];
    var tierName = args[grantTestTierIndex + 2];
    await WithConfiguredDatabase(db => BoostyTierSeeder.GrantTestTier(db, playerNameOrId, tierName));
    Console.WriteLine($"Granted '{tierName}' to '{playerNameOrId}'.");
    return;
}

await using (var governance = CreateGovernanceDatabase())
    await governance.Database.MigrateAsync();

if (migrateOnly)
{
    Console.WriteLine("Governance migrations applied successfully.");
    return;
}

if (governanceDoctor)
{
    await using var governance = CreateGovernanceDatabase();
    var requiredTables = new HashSet<string>(StringComparer.Ordinal)
    {
        "users", "identity_links", "identity_bindings", "service_paths", "rating_entries", "reputation_observations",
        "reputation_snapshots", "game_activity_snapshots", "contribution_events", "qualifications", "conflicts",
        "invitations", "court_cases", "court_participants", "court_statements", "jurors", "guilt_votes",
        "sentencing_votes", "friendships", "service_assignments", "punishment_executions", "duty_sessions",
        "capability_grants", "ahelp_tickets", "ahelp_messages", "ahelp_discord_sync", "court_defense_confirmations",
        "court_thread_message_sync", "live_incidents", "incident_actions", "incident_action_approvals",
        "event_proposals", "event_manifests", "event_reviews", "event_sessions", "event_actions",
        "moderation_reviews", "audit_events",
    };
    var connection = governance.Database.GetDbConnection();
    await connection.OpenAsync();
    await using (var command = connection.CreateCommand())
    {
        command.CommandText = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'governance'
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var actual = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
            actual.Add(reader.GetString(0));
        var missing = requiredTables.Where(table => !actual.Contains(table)).OrderBy(table => table).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Governance doctor failed: missing tables: {string.Join(", ", missing)}");
    }

    var doctorIdentities = new GovernanceIdentityService(CreateGovernanceDatabase, CreateConfiguredDatabase);
    var doctorReputation = new ReputationService(CreateGovernanceDatabase, CreateConfiguredDatabase);
    var doctorSelection = new CandidateSelectionService(CreateGovernanceDatabase, CreateConfiguredDatabase, doctorReputation, config);
    _ = await doctorSelection.SelectAsync("jury", 1, "doctor", "read-only", 1, [], null, TimeSpan.Zero);
    Console.WriteLine($"Governance doctor OK: {requiredTables.Count} workflow/reputation tables, immutable Identity v2, Bayesian evidence, AHelp, Court, event execution and game activity contracts.");
    return;
}

if (string.IsNullOrWhiteSpace(token))
    throw new ArgumentException("No token found.");

if (guild == 0)
    throw new ArgumentException("No Discord guild found.");

config.Guild = guild;
if (config.CourtEnabled && config.CourtChannel == 0)
    throw new ArgumentException("Community Court is enabled but CourtChannel is not configured.");

await using CourtInstanceLock? courtInstanceLock = config.CourtEnabled
    ? await CourtInstanceLock.AcquireAsync(connectionString)
    : null;

var identities = new GovernanceIdentityService(CreateGovernanceDatabase, CreateConfiguredDatabase);
var reputation = new ReputationService(CreateGovernanceDatabase, CreateConfiguredDatabase);
var reputationHistory = new ReputationHistoryService(CreateGovernanceDatabase);
var selection = new CandidateSelectionService(CreateGovernanceDatabase, CreateConfiguredDatabase, reputation, config);
var courtPolicy = CourtPolicy.FromConfig(config);
var court = new CommunityCourtService(
    CreateGovernanceDatabase,
    CreateConfiguredDatabase,
    courtPolicy,
    selection);
var courtFiling = new CourtFilingService(identities, CreateGovernanceDatabase, CreateConfiguredDatabase, courtPolicy);
var courtMaterials = new CourtSourceMaterialService(CreateGovernanceDatabase, CreateConfiguredDatabase);
var community = new GovernanceCommunityService(identities, CreateGovernanceDatabase, CreateConfiguredDatabase);
var courtTestLinks = new CourtTestAccountLinkingService(CreateConfiguredDatabase, CreateGovernanceDatabase, community, config);
var punishments = new CourtPunishmentService(CreateGovernanceDatabase, CreateConfiguredDatabase);
var moderation = new ModerationGovernanceService(CreateGovernanceDatabase, CreateConfiguredDatabase, community);
var moderationTrust = new ModerationTrustService(CreateGovernanceDatabase, community, selection, config);
var events = new EventGovernanceService(CreateGovernanceDatabase, community, selection, config);
var eventStatus = new EventGovernanceStatusService(CreateGovernanceDatabase);
var guildMembers = new DiscordGuildMemberCache(client, config.Guild);
var coordinator = new CourtDiscordCoordinator(client, court, courtMaterials, punishments, events, moderation, config, guildMembers);
var conversations = new GovernanceDiscordConversationCoordinator(client, CreateGovernanceDatabase, config);
var moderationTrustCoordinator = new ModerationTrustCoordinator(client, moderationTrust, court, config, guildMembers);
var reputationCoordinator = new ReputationCoordinator(identities, reputation, config);
var services = new ServiceCollection()
    .AddSingleton(client)
    .AddSingleton(config)
    .AddSingleton<Func<GovernanceDbContext>>(_ => CreateGovernanceDatabase)
    .AddSingleton<Func<ServerDbContext>>(_ => CreateConfiguredDatabase)
    .AddSingleton(guildMembers)
    .AddSingleton(identities)
    .AddSingleton(reputation)
    .AddSingleton(reputationHistory)
    .AddSingleton(selection)
    .AddSingleton(court)
    .AddSingleton(courtFiling)
    .AddSingleton(courtMaterials)
    .AddSingleton(community)
    .AddSingleton(courtTestLinks)
    .AddSingleton(punishments)
    .AddSingleton(moderation)
    .AddSingleton(moderationTrust)
    .AddSingleton(events)
    .AddSingleton(eventStatus)
    .AddSingleton(coordinator)
    .AddSingleton(conversations)
    .AddSingleton(moderationTrustCoordinator)
    .AddSingleton(reputationCoordinator)
    .BuildServiceProvider();

await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();

var interaction = new InteractionService(client);
var handler = new CommandHandler(
    client,
    new CommandService(),
    interaction,
    CreateConfiguredDatabase,
    identities,
    guildMembers,
    services,
    guild);

using var shutdown = new CancellationTokenSource();

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    Interlocked.Decrement(ref handler.Running);
    shutdown.Cancel();
};

await handler.InstallCommandsAsync();
var scheduler = Task.Run(() => coordinator.RunSchedulerAsync(shutdown.Token));
var conversationScheduler = Task.Run(() => conversations.RunSchedulerAsync(shutdown.Token));
var moderationTrustScheduler = Task.Run(() => moderationTrustCoordinator.RunSchedulerAsync(shutdown.Token));
var reputationScheduler = Task.Run(() => reputationCoordinator.RunSchedulerAsync(shutdown.Token));

try
{
    await Task.Delay(Timeout.Infinite, shutdown.Token);
}
catch (OperationCanceledException)
{
    // Normal process shutdown.
}

await client.StopAsync();
await services.DisposeAsync();
try
{
    await Task.WhenAll(scheduler, conversationScheduler, moderationTrustScheduler, reputationScheduler);
}
catch (OperationCanceledException)
{
    // Normal scheduler shutdown.
}