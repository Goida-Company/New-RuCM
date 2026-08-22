using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Npgsql;
using Robust.Shared.Network;

namespace Content.Server.Database;

public sealed record GovernanceAHelpPlayerTicketInfo(
    long Id,
    string Status,
    DateTimeOffset CreatedAt,
    string ResponderName);

public sealed record GovernanceAHelpModernTranscriptLine(
    NetUserId SenderUserId,
    string SenderName,
    string Body,
    DateTimeOffset CreatedAt);

public partial interface IServerDbManager
{
    Task<long?> SendGovernanceAHelpPlayerMessageAsync(
        NetUserId reporter,
        int roundId,
        string body,
        CancellationToken cancel = default);

    Task<GovernanceAHelpPlayerTicketInfo?> GetGovernanceAHelpPlayerTicketAsync(
        NetUserId reporter,
        int roundId,
        CancellationToken cancel = default);

    Task<IReadOnlyList<GovernanceAHelpModernTranscriptLine>> GetGovernanceAHelpPlayerTranscriptAsync(
        NetUserId reporter,
        int roundId,
        CancellationToken cancel = default);

    Task<IReadOnlyList<GovernanceAHelpModernTranscriptLine>> GetGovernanceAHelpResponderTranscriptAsync(
        long ticketId,
        NetUserId responder,
        int roundId,
        CancellationToken cancel = default);

    Task<NetUserId?> SendGovernanceAHelpResponderMessageAsync(
        long ticketId,
        NetUserId responder,
        int roundId,
        string body,
        CancellationToken cancel = default);

    Task<bool> ResolveGovernanceAHelpByReporterAsync(
        NetUserId reporter,
        int roundId,
        CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public async Task<long?> SendGovernanceAHelpPlayerMessageAsync(
        NetUserId reporter,
        int roundId,
        string body,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            roundId <= 0 || string.IsNullOrWhiteSpace(body))
            return null;

        body = body.Trim();
        if (body.Length > 3000)
            body = body[..3000];

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);

        await using (var ticketLock = new NpgsqlCommand(
                         "SELECT pg_advisory_xact_lock(hashtextextended(@reporter, @round_id))",
                         connection,
                         transaction))
        {
            ticketLock.Parameters.AddWithValue("reporter", $"rucm-ahelp:{reporter.UserId}");
            ticketLock.Parameters.AddWithValue("round_id", roundId);
            await ticketLock.ExecuteNonQueryAsync(cancel);
        }

        long? ticketId = null;
        string? status = null;
        await using (var existing = new NpgsqlCommand(
                         """
                         SELECT id, status
                         FROM governance.ahelp_tickets
                         WHERE round_id = @round_id
                           AND reporter_ss14_user_id = @reporter
                           AND status IN ('open', 'claimed', 'waiting_player', 'escalated_to_incident', 'escalated_to_court')
                         ORDER BY created_at DESC
                         LIMIT 1
                         FOR UPDATE
                         """,
                         connection,
                         transaction))
        {
            existing.Parameters.AddWithValue("round_id", roundId);
            existing.Parameters.AddWithValue("reporter", reporter.UserId);
            await using var reader = await existing.ExecuteReaderAsync(cancel);
            if (await reader.ReadAsync(cancel))
            {
                ticketId = reader.GetInt64(0);
                status = reader.GetString(1);
            }
        }

        // Court referral freezes the source transcript. The ticket remains visible until explicitly
        // resolved, but neither side may mutate the evidence package after escalation.
        if (status == "escalated_to_court")
        {
            await transaction.RollbackAsync(cancel);
            return null;
        }

        var created = false;
        if (ticketId == null)
        {
            await using var create = new NpgsqlCommand(
                """
                INSERT INTO governance.ahelp_tickets(
                    round_id, reporter_user_id, reporter_ss14_user_id, status,
                    summary, created_at, updated_at)
                VALUES (
                    @round_id,
                    (SELECT id FROM governance.users WHERE ss14_user_id = @reporter),
                    @reporter, 'open', @summary, now(), now())
                RETURNING id
                """,
                connection,
                transaction);
            create.Parameters.AddWithValue("round_id", roundId);
            create.Parameters.AddWithValue("reporter", reporter.UserId);
            create.Parameters.AddWithValue("summary", body);
            ticketId = Convert.ToInt64(await create.ExecuteScalarAsync(cancel));
            created = true;
        }
        else if (status == "waiting_player")
        {
            await using var activate = new NpgsqlCommand(
                """
                UPDATE governance.ahelp_tickets
                SET status = 'claimed', updated_at = now()
                WHERE id = @ticket_id AND status = 'waiting_player'
                """,
                connection,
                transaction);
            activate.Parameters.AddWithValue("ticket_id", ticketId.Value);
            await activate.ExecuteNonQueryAsync(cancel);
        }
        else
        {
            await using var touch = new NpgsqlCommand(
                "UPDATE governance.ahelp_tickets SET updated_at = now() WHERE id = @ticket_id",
                connection,
                transaction);
            touch.Parameters.AddWithValue("ticket_id", ticketId.Value);
            await touch.ExecuteNonQueryAsync(cancel);
        }

        await using (var message = new NpgsqlCommand(
                         """
                         INSERT INTO governance.ahelp_messages(ticket_id, sender_ss14_user_id, body)
                         VALUES (@ticket_id, @reporter, @body)
                         """,
                         connection,
                         transaction))
        {
            message.Parameters.AddWithValue("ticket_id", ticketId.Value);
            message.Parameters.AddWithValue("reporter", reporter.UserId);
            message.Parameters.AddWithValue("body", body);
            await message.ExecuteNonQueryAsync(cancel);
        }

        await using (var audit = new NpgsqlCommand(
                         """
                         INSERT INTO governance.audit_events(
                             event_type, actor_type, actor_id, entity_type, entity_id, payload)
                         VALUES (@event_type, 'ss14_user', @actor_id, 'ahelp_ticket', @ticket_id,
                                 jsonb_build_object('round_id', @round_id, 'source', 'governance_ui'))
                         """,
                         connection,
                         transaction))
        {
            audit.Parameters.AddWithValue("event_type", created ? "ahelp.created" : "ahelp.player_message");
            audit.Parameters.AddWithValue("actor_id", reporter.UserId.ToString());
            audit.Parameters.AddWithValue("ticket_id", ticketId.Value.ToString());
            audit.Parameters.AddWithValue("round_id", roundId);
            await audit.ExecuteNonQueryAsync(cancel);
        }

        await transaction.CommitAsync(cancel);
        return ticketId;
    }

    public async Task<GovernanceAHelpPlayerTicketInfo?> GetGovernanceAHelpPlayerTicketAsync(
        NetUserId reporter,
        int roundId,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) || roundId <= 0)
            return null;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            SELECT ticket.id,
                   ticket.status,
                   ticket.created_at,
                   COALESCE(responder_player.last_seen_user_name, '')
            FROM governance.ahelp_tickets AS ticket
            LEFT JOIN governance.users AS responder ON responder.id = ticket.claimed_by_user_id
            LEFT JOIN player AS responder_player ON responder_player.user_id = responder.ss14_user_id
            WHERE ticket.round_id = @round_id
              AND ticket.reporter_ss14_user_id = @reporter
              AND ticket.status IN ('open', 'claimed', 'waiting_player', 'escalated_to_incident', 'escalated_to_court')
            ORDER BY ticket.created_at DESC
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("round_id", roundId);
        command.Parameters.AddWithValue("reporter", reporter.UserId);
        await using var reader = await command.ExecuteReaderAsync(cancel);
        if (!await reader.ReadAsync(cancel))
            return null;

        return new GovernanceAHelpPlayerTicketInfo(
            reader.GetInt64(0),
            reader.GetString(1),
            new DateTimeOffset(reader.GetDateTime(2)),
            reader.GetString(3));
    }

    public async Task<IReadOnlyList<GovernanceAHelpModernTranscriptLine>> GetGovernanceAHelpPlayerTranscriptAsync(
        NetUserId reporter,
        int roundId,
        CancellationToken cancel = default)
    {
        var result = new List<GovernanceAHelpModernTranscriptLine>();
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) || roundId <= 0)
            return result;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            SELECT message.sender_ss14_user_id,
                   COALESCE(sender.last_seen_user_name, message.sender_ss14_user_id::text),
                   message.body,
                   message.created_at
            FROM governance.ahelp_messages AS message
            JOIN governance.ahelp_tickets AS ticket ON ticket.id = message.ticket_id
            LEFT JOIN player AS sender ON sender.user_id = message.sender_ss14_user_id
            WHERE ticket.round_id = @round_id
              AND ticket.reporter_ss14_user_id = @reporter
              AND ticket.status IN ('open', 'claimed', 'waiting_player', 'escalated_to_incident', 'escalated_to_court')
            ORDER BY message.created_at, message.id
            """,
            connection);
        command.Parameters.AddWithValue("round_id", roundId);
        command.Parameters.AddWithValue("reporter", reporter.UserId);
        await using var reader = await command.ExecuteReaderAsync(cancel);
        while (await reader.ReadAsync(cancel))
        {
            result.Add(new GovernanceAHelpModernTranscriptLine(
                new NetUserId(reader.GetGuid(0)),
                reader.GetString(1),
                reader.GetString(2),
                new DateTimeOffset(reader.GetDateTime(3))));
        }

        return result;
    }

    public async Task<IReadOnlyList<GovernanceAHelpModernTranscriptLine>> GetGovernanceAHelpResponderTranscriptAsync(
        long ticketId,
        NetUserId responder,
        int roundId,
        CancellationToken cancel = default)
    {
        var result = new List<GovernanceAHelpModernTranscriptLine>();
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) || roundId <= 0)
            return result;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            WITH actor AS (
                SELECT users.id
                FROM governance.users AS users
                JOIN governance.duty_sessions AS duty ON duty.user_id = users.id
                JOIN governance.capability_grants AS capability_grant
                  ON capability_grant.user_id = users.id
                 AND capability_grant.source_type = 'duty_session'
                 AND capability_grant.source_id = duty.id::text
                WHERE users.ss14_user_id = @responder
                  AND NOT users.is_governance_suspended
                  AND duty.round_id = @round_id
                  AND duty.status = 'active'
                  AND duty.observer_confirmed
                  AND duty.expires_at > now()
                  AND capability_grant.capability = 'moderation.ahelp'
                  AND capability_grant.expires_at > now()
                  AND capability_grant.revoked_at IS NULL
                LIMIT 1
            )
            SELECT message.sender_ss14_user_id,
                   COALESCE(sender.last_seen_user_name, message.sender_ss14_user_id::text),
                   message.body,
                   message.created_at
            FROM governance.ahelp_messages AS message
            JOIN governance.ahelp_tickets AS ticket ON ticket.id = message.ticket_id
            JOIN actor ON actor.id = ticket.claimed_by_user_id
            LEFT JOIN player AS sender ON sender.user_id = message.sender_ss14_user_id
            WHERE ticket.id = @ticket_id
              AND ticket.round_id = @round_id
              AND ticket.status IN ('claimed', 'waiting_player', 'escalated_to_court')
            ORDER BY message.created_at, message.id
            """,
            connection);
        command.Parameters.AddWithValue("ticket_id", ticketId);
        command.Parameters.AddWithValue("responder", responder.UserId);
        command.Parameters.AddWithValue("round_id", roundId);
        await using var reader = await command.ExecuteReaderAsync(cancel);
        while (await reader.ReadAsync(cancel))
        {
            result.Add(new GovernanceAHelpModernTranscriptLine(
                new NetUserId(reader.GetGuid(0)),
                reader.GetString(1),
                reader.GetString(2),
                new DateTimeOffset(reader.GetDateTime(3))));
        }

        return result;
    }

    public async Task<NetUserId?> SendGovernanceAHelpResponderMessageAsync(
        long ticketId,
        NetUserId responder,
        int roundId,
        string body,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            roundId <= 0 || string.IsNullOrWhiteSpace(body))
            return null;

        body = body.Trim();
        if (body.Length > 3000)
            body = body[..3000];

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);

        await using var command = new NpgsqlCommand(
            """
            WITH actor AS (
                SELECT users.id
                FROM governance.users AS users
                JOIN governance.duty_sessions AS duty ON duty.user_id = users.id
                JOIN governance.capability_grants AS capability_grant
                  ON capability_grant.user_id = users.id
                 AND capability_grant.source_type = 'duty_session'
                 AND capability_grant.source_id = duty.id::text
                WHERE users.ss14_user_id = @responder
                  AND NOT users.is_governance_suspended
                  AND duty.round_id = @round_id
                  AND duty.status = 'active'
                  AND duty.observer_confirmed
                  AND duty.expires_at > now()
                  AND capability_grant.capability = 'moderation.ahelp'
                  AND capability_grant.expires_at > now()
                  AND capability_grant.revoked_at IS NULL
                LIMIT 1
            ), updated AS (
                UPDATE governance.ahelp_tickets AS ticket
                SET status = 'claimed', updated_at = now()
                FROM actor
                WHERE ticket.id = @ticket_id
                  AND ticket.round_id = @round_id
                  AND ticket.claimed_by_user_id = actor.id
                  AND ticket.status IN ('claimed', 'waiting_player')
                RETURNING ticket.reporter_ss14_user_id
            ), inserted AS (
                INSERT INTO governance.ahelp_messages(ticket_id, sender_ss14_user_id, body)
                SELECT @ticket_id, @responder, @body
                FROM updated
                RETURNING id
            ), audited AS (
                INSERT INTO governance.audit_events(
                    event_type, actor_type, actor_id, entity_type, entity_id, payload)
                SELECT 'ahelp.responder_message', 'ss14_user', @responder::text,
                       'ahelp_ticket', @ticket_id::text,
                       jsonb_build_object('round_id', @round_id, 'source', 'governance_ui')
                FROM inserted
            )
            SELECT reporter_ss14_user_id FROM updated
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("ticket_id", ticketId);
        command.Parameters.AddWithValue("responder", responder.UserId);
        command.Parameters.AddWithValue("round_id", roundId);
        command.Parameters.AddWithValue("body", body);

        var result = await command.ExecuteScalarAsync(cancel);
        if (result is not Guid reporterId)
        {
            await transaction.RollbackAsync(cancel);
            return null;
        }

        await transaction.CommitAsync(cancel);
        return new NetUserId(reporterId);
    }

    public async Task<bool> ResolveGovernanceAHelpByReporterAsync(
        NetUserId reporter,
        int roundId,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) || roundId <= 0)
            return false;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            WITH changed AS (
                UPDATE governance.ahelp_tickets
                SET status = 'resolved', updated_at = now()
                WHERE round_id = @round_id
                  AND reporter_ss14_user_id = @reporter
                  AND status IN ('open', 'claimed', 'waiting_player', 'escalated_to_court')
                RETURNING id
            ), audited AS (
                INSERT INTO governance.audit_events(
                    event_type, actor_type, actor_id, entity_type, entity_id, payload)
                SELECT 'ahelp.resolved_by_reporter', 'ss14_user', @reporter::text,
                       'ahelp_ticket', changed.id::text,
                       jsonb_build_object('round_id', @round_id)
                FROM changed
            )
            SELECT count(*) FROM changed
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("round_id", roundId);
        command.Parameters.AddWithValue("reporter", reporter.UserId);
        var changed = Convert.ToInt32(await command.ExecuteScalarAsync(cancel)) > 0;
        if (changed)
            await transaction.CommitAsync(cancel);
        else
            await transaction.RollbackAsync(cancel);
        return changed;
    }
}
