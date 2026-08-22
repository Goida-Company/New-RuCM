using System.Data.Common;
using Content.Server.Database;
using Microsoft.EntityFrameworkCore;

namespace Content.DiscordBot.Governance;

/// <summary>
/// Builds the immutable source package for a court case escalated from an in-game LiveIncident.
/// The public complaint evidence is the complete Governance AHelp transcript. Administrative
/// history is collected separately so the court coordinator can present it as service material.
/// </summary>
public sealed class CourtSourceMaterialService(
    Func<GovernanceDbContext> governanceFactory,
    Func<ServerDbContext> gameFactory)
{
    public async Task<IReadOnlyList<long>> CasesNeedingMaterialsAsync()
    {
        await using var governance = governanceFactory();
        var connection = governance.Database.GetDbConnection();
        await EnsureOpenAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT court.id
            FROM governance.court_cases AS court
            JOIN governance.live_incidents AS incident ON incident.court_case_id = court.id
            WHERE court.discord_thread_id IS NOT NULL
              AND court.materials_published_at IS NULL
            ORDER BY court.id
            """;

        var result = new List<long>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetInt64(0));
        return result;
    }

    public async Task MarkMaterialsPublishedAsync(long caseId)
    {
        await using var governance = governanceFactory();
        var connection = governance.Database.GetDbConnection();
        await EnsureOpenAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE governance.court_cases
            SET materials_published_at = COALESCE(materials_published_at, now()),
                version = version + 1
            WHERE id = @case_id
            """;
        AddParameter(command, "case_id", caseId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<CourtSourceMaterial?> GetAsync(long caseId)
    {
        long incidentId;
        long ticketId;
        Guid claimantId;
        Guid defendantId;
        string characterName;
        var transcriptRows = new List<(DateTime CreatedAt, Guid Sender, string Body)>();

        await using (var governance = governanceFactory())
        {
            var connection = governance.Database.GetDbConnection();
            await EnsureOpenAsync(connection);

            await using (var source = connection.CreateCommand())
            {
                source.CommandText = """
                    SELECT incident.id,
                           incident.ahelp_ticket_id,
                           ticket.reporter_ss14_user_id,
                           defendant.ss14_user_id,
                           COALESCE(incident.target_character_name, '')
                    FROM governance.live_incidents AS incident
                    JOIN governance.ahelp_tickets AS ticket ON ticket.id = incident.ahelp_ticket_id
                    JOIN governance.users AS defendant ON defendant.id = incident.target_user_id
                    WHERE incident.court_case_id = @case_id
                    LIMIT 1
                    """;
                AddParameter(source, "case_id", caseId);
                await using var reader = await source.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return null;

                incidentId = reader.GetInt64(0);
                ticketId = reader.GetInt64(1);
                claimantId = reader.GetGuid(2);
                defendantId = reader.GetGuid(3);
                characterName = reader.GetString(4);
            }

            await using (var transcriptCommand = connection.CreateCommand())
            {
                transcriptCommand.CommandText = """
                    SELECT created_at, sender_ss14_user_id, body
                    FROM governance.ahelp_messages
                    WHERE ticket_id = @ticket_id
                    ORDER BY created_at, id
                    """;
                AddParameter(transcriptCommand, "ticket_id", ticketId);
                await using var reader = await transcriptCommand.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    transcriptRows.Add((reader.GetDateTime(0), reader.GetGuid(1), reader.GetString(2)));
            }
        }

        var ids = transcriptRows.Select(value => value.Sender)
            .Append(claimantId)
            .Append(defendantId)
            .Distinct()
            .ToArray();

        await using var game = gameFactory();
        var names = await game.Player.AsNoTracking()
            .Where(player => ids.Contains(player.UserId))
            .ToDictionaryAsync(player => player.UserId, player => player.LastSeenUserName);

        var claimantName = names.GetValueOrDefault(claimantId, claimantId.ToString());
        var defendantName = names.GetValueOrDefault(defendantId, defendantId.ToString());
        if (string.IsNullOrWhiteSpace(characterName))
            characterName = defendantName;

        var transcript = transcriptRows.Select(value => new CourtTranscriptEntry(
            value.CreatedAt,
            value.Sender,
            names.GetValueOrDefault(value.Sender, value.Sender.ToString()),
            value.Body,
            value.Sender != claimantId)).ToArray();

        var history = new List<CourtPlayerHistoryEntry>();

        var notes = await game.AdminNotes.AsNoTracking()
            .Where(note => note.PlayerUserId == defendantId && !note.Deleted)
            .OrderBy(note => note.CreatedAt)
            .Select(note => new { note.CreatedAt, note.Message, note.Severity, note.Secret })
            .ToListAsync();
        history.AddRange(notes.Select(note => new CourtPlayerHistoryEntry(
            note.CreatedAt,
            note.Secret ? $"Заметка {note.Severity} • служебная" : $"Заметка {note.Severity}",
            note.Message)));

        var watchlists = await game.AdminWatchlists.AsNoTracking()
            .Where(note => note.PlayerUserId == defendantId && !note.Deleted)
            .OrderBy(note => note.CreatedAt)
            .Select(note => new { note.CreatedAt, note.Message })
            .ToListAsync();
        history.AddRange(watchlists.Select(note => new CourtPlayerHistoryEntry(
            note.CreatedAt,
            "Watchlist",
            note.Message)));

        var bans = await game.Ban.AsNoTracking()
            .Where(ban => ban.PlayerUserId == defendantId && !ban.Hidden)
            .OrderBy(ban => ban.BanTime)
            .Select(ban => new { ban.BanTime, ban.Reason, ban.Severity, ban.ExpirationTime })
            .ToListAsync();
        history.AddRange(bans.Select(ban => new CourtPlayerHistoryEntry(
            ban.BanTime,
            $"Бан {ban.Severity}" + (ban.ExpirationTime == null ? " • бессрочный" : $" • до {ban.ExpirationTime:yyyy-MM-dd HH:mm} UTC"),
            ban.Reason)));

        var roleBans = await game.RoleBan.AsNoTracking()
            .Where(ban => ban.PlayerUserId == defendantId && !ban.Hidden)
            .OrderBy(ban => ban.BanTime)
            .Select(ban => new { ban.BanTime, ban.Reason, ban.Severity, ban.RoleId, ban.ExpirationTime })
            .ToListAsync();
        history.AddRange(roleBans.Select(ban => new CourtPlayerHistoryEntry(
            ban.BanTime,
            $"JobBan {ban.RoleId} • {ban.Severity}" + (ban.ExpirationTime == null ? " • бессрочный" : $" • до {ban.ExpirationTime:yyyy-MM-dd HH:mm} UTC"),
            ban.Reason)));

        return new CourtSourceMaterial(
            incidentId,
            ticketId,
            claimantId,
            claimantName,
            defendantId,
            defendantName,
            characterName,
            transcript,
            history.OrderBy(value => value.CreatedAt).ToArray());
    }

    private static async Task EnsureOpenAsync(DbConnection connection)
    {
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
