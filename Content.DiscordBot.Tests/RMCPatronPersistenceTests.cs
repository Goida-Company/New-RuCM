using Content.Server.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Content.DiscordBot.Tests;

[TestFixture]
public sealed class RMCPatronPersistenceTests
{
    private SqliteConnection _connection = default!;
    private DbContextOptions<SqliteServerDbContext> _options = default!;

    [SetUp]
    public async Task SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        await using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE rmc_patrons (
                player_id TEXT NOT NULL PRIMARY KEY,
                tier_id INTEGER NOT NULL,
                ghost_color INTEGER NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
        _options = new DbContextOptionsBuilder<SqliteServerDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    [TearDown]
    public async Task TearDown()
    {
        await _connection.DisposeAsync();
    }

    [Test]
    public async Task SetTierInsertsMissingPatron()
    {
        var playerId = Guid.NewGuid();
        await using var db = new SqliteServerDbContext(_options);

        Assert.That(await RMCPatronPersistence.SetTierAsync(db, playerId, 3), Is.True);

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT tier_id FROM rmc_patrons WHERE player_id = $playerId";
        command.Parameters.AddWithValue("$playerId", playerId);
        Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo(3));
    }

    [Test]
    public async Task SetTierReturnsFalseWhenTierIsUnchanged()
    {
        var playerId = Guid.NewGuid();
        await using var db = new SqliteServerDbContext(_options);

        Assert.That(await RMCPatronPersistence.SetTierAsync(db, playerId, 3), Is.True);
        Assert.That(await RMCPatronPersistence.SetTierAsync(db, playerId, 3), Is.False);

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM rmc_patrons";
        Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo(1L));
    }

    [Test]
    public async Task SetTierUpdatesExistingPatronWithoutAddingRow()
    {
        var playerId = Guid.NewGuid();
        await using var db = new SqliteServerDbContext(_options);

        Assert.That(await RMCPatronPersistence.SetTierAsync(db, playerId, 3), Is.True);
        Assert.That(await RMCPatronPersistence.SetTierAsync(db, playerId, 7), Is.True);

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT count(*), max(tier_id) FROM rmc_patrons";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetInt32(0), Is.EqualTo(1));
            Assert.That(reader.GetInt32(1), Is.EqualTo(7));
        });
    }

    [Test]
    public async Task RemoveReturnsFalseWhenPatronDoesNotExist()
    {
        await using var db = new SqliteServerDbContext(_options);

        Assert.That(await RMCPatronPersistence.RemoveAsync(db, Guid.NewGuid()), Is.False);
    }

    [Test]
    public async Task RemoveDeletesExistingPatron()
    {
        var playerId = Guid.NewGuid();
        await using var db = new SqliteServerDbContext(_options);

        Assert.That(await RMCPatronPersistence.SetTierAsync(db, playerId, 3), Is.True);
        Assert.That(await RMCPatronPersistence.RemoveAsync(db, playerId), Is.True);

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM rmc_patrons";
        Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo(0L));
    }
}
