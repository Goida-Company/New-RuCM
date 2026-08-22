using Npgsql;

namespace Content.DiscordBot.Governance;

public sealed class CourtInstanceLock : IAsyncDisposable
{
    private const long LockId = 0x5255434D434F5552;
    private readonly NpgsqlConnection _connection;

    private CourtInstanceLock(NpgsqlConnection connection)
    {
        _connection = connection;
    }

    public static async Task<CourtInstanceLock> AcquireAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@lock_id)", connection);
        command.Parameters.AddWithValue("lock_id", LockId);
        if (await command.ExecuteScalarAsync() is not true)
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException(
                "Another Content.DiscordBot instance owns the Community Court lock. " +
                "Do not run two Discord processes with the same bot token.");
        }

        return new CourtInstanceLock(connection);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection.State == System.Data.ConnectionState.Open)
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@lock_id)", _connection);
            command.Parameters.AddWithValue("lock_id", LockId);
            await command.ExecuteNonQueryAsync();
        }
        await _connection.DisposeAsync();
    }
}
