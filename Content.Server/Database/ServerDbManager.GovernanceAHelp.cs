using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._RuMC14.Governance;
using Content.Shared.CCVar;
using Npgsql;
using Robust.Shared.Network;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<long?> RecordGovernanceAHelpMessageAsync(
        NetUserId reporter,
        NetUserId sender,
        int roundId,
        string body,
        CancellationToken cancel = default);

    Task<IReadOnlyList<GovernanceAHelpTicketInfo>> GetGovernanceAHelpQueueAsync(
        NetUserId responder,
        int roundId,
        CancellationToken cancel = default);

    Task<bool> ClaimGovernanceAHelpAsync(
        long ticketId,
        NetUserId responder,
        int roundId,
        CancellationToken cancel = default);

    Task<bool> SetGovernanceAHelpStatusAsync(
        long ticketId,
        NetUserId responder,
        int roundId,
        string status,
        CancellationToken cancel = default);

    Task<NetUserId?> GetGovernanceAHelpResponderAsync(
        NetUserId reporter,
        int roundId,
        CancellationToken cancel = default);

    Task<bool> AuthorizeGovernanceAHelpChannelAsync(
        NetUserId responder,
        NetUserId reporter,
        int roundId,
        CancellationToken cancel = default);

    Task<IReadOnlyList<GovernanceAHelpTranscriptLine>> GetGovernanceAHelpTranscriptAsync(
        long ticketId,
        NetUserId responder,
        int roundId,
        CancellationToken cancel = default);

    Task<long?> OpenGovernanceExplanationAHelpAsync(
        NetUserId target,
        NetUserId responder,
        int roundId,
        string reason,
        CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public async Task<long?> RecordGovernanceAHelpMessageAsync(
        NetUserId reporter,
        NetUserId sender,
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
        await using (var existing = new NpgsqlCommand(
                         """
                         SELECT id FROM governance.ahelp_tickets
                         WHERE round_id = @round_id AND reporter_ss14_user_id = @reporter
                           AND status IN ('open', 'claimed', 'waiting_player', 'escalated_to_incident')
                         ORDER BY created_at DESC LIMIT 1
                         """,
                         connection,
                         transaction))
        {
            existing.Parameters.AddWithValue("round_id", roundId);
            existing.Parameters.AddWithValue("reporter", reporter.UserId);
            var value = await existing.ExecuteScalarAsync(cancel);
            if (value != null)
                ticketId = Convert.ToInt64(value);
        }

        var created = false;
        if (ticketId == null && reporter == sender)
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

        if (ticketId == null)
        {
            await transaction.RollbackAsync(cancel);
            return null;
        }

        await using (var message = new NpgsqlCommand(
                         """
                         INSERT INTO governance.ahelp_messages(ticket_id, sender_ss14_user_id, body)
                         VALUES (@ticket_id, @sender, @body)
                         """,
                         connection,
                         transaction))
        {
            message.Parameters.AddWithValue("ticket_id", ticketId.Value);
            message.Parameters.AddWithValue("sender", sender.UserId);
            message.Parameters.AddWithValue("body", body);
            await message.ExecuteNonQueryAsync(cancel);
        }

        await using (var audit = new NpgsqlCommand(
                         """
                         INSERT INTO governance.audit_events(
                             event_type, actor_type, actor_id, entity_type, entity_id, payload)
                         VALUES (@event_type, 'ss14_user', @actor_id, 'ahelp_ticket', @ticket_id,
                                 jsonb_build_object('round_id', @round_id))
                         """,
                         connection,
                         transaction))
        {
            audit.Parameters.AddWithValue("event_type", created ? "ahelp.created" : "ahelp.message_recorded");
            audit.Parameters.AddWithValue("actor_id", sender.UserId.ToString());
            audit.Parameters.AddWithValue("ticket_id", ticketId.Value.ToString());
            audit.Parameters.AddWithValue("round_id", roundId);
            await audit.ExecuteNonQueryAsync(cancel);
        }

        await transaction.CommitAsync(cancel);
        return ticketId;
    }

    public async Task<IReadOnlyList<GovernanceAHelpTicketInfo>> GetGovernanceAHelpQueueAsync(
        NetUserId responder,
        int roundId,
        CancellationToken cancel = default)
    {
        var result = new List<GovernanceAHelpTicketInfo>();
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase))
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
                  AND duty.round_id = @round_id AND duty.status = 'active'
                  AND duty.observer_confirmed AND duty.expires_at > now()
                  AND capability_grant.capability = 'moderation.ahelp'
                  AND capability_grant.expires_at > now() AND capability_grant.revoked_at IS NULL
                LIMIT 1
            )
            SELECT ticket.id, ticket.round_id, ticket.reporter_ss14_user_id,
                   COALESCE(player.last_seen_user_name, ticket.reporter_ss14_user_id::text),
                   ticket.summary, ticket.status,
                   ticket.created_at, COALESCE(ticket.claimed_by_user_id = actor.id, false)
            FROM governance.ahelp_tickets AS ticket
            LEFT JOIN player ON player.user_id = ticket.reporter_ss14_user_id
            CROSS JOIN actor
            WHERE ticket.round_id = @round_id
              AND (ticket.status = 'open' OR
                   (ticket.claimed_by_user_id = actor.id AND ticket.status IN ('claimed', 'waiting_player', 'escalated_to_court')))
            ORDER BY ticket.status <> 'open', ticket.created_at
            """,
            connection);
        command.Parameters.AddWithValue("responder", responder.UserId);
        command.Parameters.AddWithValue("round_id", roundId);
        await using var reader = await command.ExecuteReaderAsync(cancel);
        while (await reader.ReadAsync(cancel))
        {
            result.Add(new GovernanceAHelpTicketInfo(
                reader.GetInt64(0),
                reader.GetInt32(1),
                new NetUserId(reader.GetGuid(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                new DateTimeOffset(reader.GetDateTime(6)),
                reader.GetBoolean(7)));
        }
        return result;
    }

    public async Task<bool> ClaimGovernanceAHelpAsync(
        long ticketId,
        NetUserId responder,
        int roundId,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase))
            return false;
        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);
        await using (var ticketLock = new NpgsqlCommand(
                         "SELECT pg_advisory_xact_lock(hashtextextended('rucm-ahelp-ticket', @ticket_id))",
                         connection,
                         transaction))
        {
            ticketLock.Parameters.AddWithValue("ticket_id", ticketId);
            await ticketLock.ExecuteNonQueryAsync(cancel);
        }
        await using var command = new NpgsqlCommand(
            """
            WITH actor AS (
                SELECT users.id
                FROM governance.users AS users
                JOIN governance.duty_sessions AS duty ON duty.user_id = users.id
                JOIN governance.capability_grants AS capability_grant
                  ON capability_grant.user_id = users.id AND capability_grant.source_type = 'duty_session'
                 AND capability_grant.source_id = duty.id::text
                WHERE users.ss14_user_id = @responder
                  AND duty.round_id = @round_id AND duty.status = 'active'
                  AND duty.observer_confirmed AND duty.expires_at > now()
                  AND capability_grant.capability = 'moderation.ahelp'
                  AND capability_grant.expires_at > now() AND capability_grant.revoked_at IS NULL
                  AND NOT users.is_governance_suspended
                LIMIT 1
            ), claimed AS (
                UPDATE governance.ahelp_tickets AS ticket
                SET claimed_by_user_id = actor.id, status = 'claimed', updated_at = now()
                FROM actor
                WHERE ticket.id = @ticket_id AND ticket.round_id = @round_id
                  AND ticket.status = 'open'
                  AND ticket.reporter_ss14_user_id <> @responder
                RETURNING ticket.id AS ticket_id
            ), audited AS (
                INSERT INTO governance.audit_events(
                    event_type, actor_type, actor_id, entity_type, entity_id, payload)
                SELECT 'ahelp.claimed', 'ss14_user', @responder::text,
                       'ahelp_ticket', claimed.ticket_id::text,
                       jsonb_build_object('round_id', @round_id)
                FROM claimed
            )
            SELECT count(*) FROM claimed
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("ticket_id", ticketId);
        command.Parameters.AddWithValue("responder", responder.UserId);
        command.Parameters.AddWithValue("round_id", roundId);
        var claimed = Convert.ToInt32(await command.ExecuteScalarAsync(cancel)) == 1;
        if (claimed)
            await transaction.CommitAsync(cancel);
        else
            await transaction.RollbackAsync(cancel);
        return claimed;
    }

    public async Task<bool> SetGovernanceAHelpStatusAsync(
        long ticketId,
        NetUserId responder,
        int roundId,
        string status,
        CancellationToken cancel = default)
    {
        if (status is not ("waiting_player" or "resolved") ||
            !_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase))
            return false;
        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            WITH actor AS (
                SELECT users.id
                FROM governance.users AS users
                JOIN governance.duty_sessions AS duty ON duty.user_id = users.id
                JOIN governance.capability_grants AS capability_grant
                  ON capability_grant.user_id = users.id AND capability_grant.source_type = 'duty_session'
                 AND capability_grant.source_id = duty.id::text
                WHERE users.ss14_user_id = @responder
                  AND duty.round_id = @round_id AND duty.status = 'active'
                  AND duty.observer_confirmed AND duty.expires_at > now()
                  AND capability_grant.capability = 'moderation.ahelp'
                  AND capability_grant.expires_at > now() AND capability_grant.revoked_at IS NULL
                LIMIT 1
            ), changed AS (
                UPDATE governance.ahelp_tickets AS ticket
                SET status = @status, updated_at = now()
                FROM actor
                WHERE ticket.id = @ticket_id AND ticket.round_id = @round_id
                  AND ticket.claimed_by_user_id = actor.id
                  AND (
                      (@status = 'waiting_player' AND ticket.status IN ('claimed', 'waiting_player'))
                      OR (@status = 'resolved' AND ticket.status IN ('claimed', 'waiting_player', 'escalated_to_court')))
                RETURNING ticket.id
            ), audited AS (
                INSERT INTO governance.audit_events(
                    event_type, actor_type, actor_id, entity_type, entity_id, payload)
                SELECT 'ahelp.status_changed', 'ss14_user', @responder::text,
                       'ahelp_ticket', changed.id::text,
                       jsonb_build_object('round_id', @round_id, 'status', @status)
                FROM changed
            )
            SELECT count(*) FROM changed
            """,
            connection);
        command.Parameters.AddWithValue("ticket_id", ticketId);
        command.Parameters.AddWithValue("responder", responder.UserId);
        command.Parameters.AddWithValue("round_id", roundId);
        command.Parameters.AddWithValue("status", status);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancel)) == 1;
    }

    public async Task<NetUserId?> GetGovernanceAHelpResponderAsync(
        NetUserId reporter,
        int roundId,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase))
            return null;
        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            SELECT users.ss14_user_id
            FROM governance.ahelp_tickets AS ticket
            JOIN governance.users AS users ON users.id = ticket.claimed_by_user_id
            JOIN governance.duty_sessions AS duty
              ON duty.user_id = users.id AND duty.round_id = ticket.round_id
             AND duty.status = 'active' AND duty.observer_confirmed AND duty.expires_at > now()
            JOIN governance.capability_grants AS capability_grant
              ON capability_grant.user_id = users.id AND capability_grant.source_type = 'duty_session'
             AND capability_grant.source_id = duty.id::text AND capability_grant.capability = 'moderation.ahelp'
             AND capability_grant.expires_at > now() AND capability_grant.revoked_at IS NULL
            WHERE ticket.reporter_ss14_user_id = @reporter AND ticket.round_id = @round_id
              AND ticket.status IN ('claimed', 'waiting_player')
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("reporter", reporter.UserId);
        command.Parameters.AddWithValue("round_id", roundId);
        var result = await command.ExecuteScalarAsync(cancel);
        return result is Guid value ? new NetUserId(value) : null;
    }

    public async Task<bool> AuthorizeGovernanceAHelpChannelAsync(
        NetUserId responder,
        NetUserId reporter,
        int roundId,
        CancellationToken cancel = default)
    {
        var assigned = await GetGovernanceAHelpResponderAsync(reporter, roundId, cancel);
        return assigned == responder;
    }

    public async Task<IReadOnlyList<GovernanceAHelpTranscriptLine>> GetGovernanceAHelpTranscriptAsync(
        long ticketId,
        NetUserId responder,
        int roundId,
        CancellationToken cancel = default)
    {
        var result = new List<GovernanceAHelpTranscriptLine>();
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase))
            return result;
        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            SELECT message.sender_ss14_user_id, COALESCE(player.last_seen_user_name, 'Unknown'),
                   message.body, message.created_at
            FROM governance.ahelp_messages AS message
            JOIN governance.ahelp_tickets AS ticket ON ticket.id = message.ticket_id
            JOIN governance.users AS actor ON actor.id = ticket.claimed_by_user_id
            LEFT JOIN player ON player.user_id = message.sender_ss14_user_id
            WHERE ticket.id = @ticket_id AND ticket.round_id = @round_id
              AND actor.ss14_user_id = @responder
              AND ticket.status IN ('claimed', 'waiting_player')
            ORDER BY message.created_at, message.id
            """,
            connection);
        command.Parameters.AddWithValue("ticket_id", ticketId);
        command.Parameters.AddWithValue("responder", responder.UserId);
        command.Parameters.AddWithValue("round_id", roundId);
        await using var reader = await command.ExecuteReaderAsync(cancel);
        while (await reader.ReadAsync(cancel))
        {
            result.Add(new GovernanceAHelpTranscriptLine(
                new NetUserId(reader.GetGuid(0)),
                reader.GetString(1),
                reader.GetString(2),
                new DateTimeOffset(reader.GetDateTime(3))));
        }
        return result;
    }

    public async Task<long?> OpenGovernanceExplanationAHelpAsync(
        NetUserId target,
        NetUserId responder,
        int roundId,
        string reason,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            target == responder || roundId <= 0 || string.IsNullOrWhiteSpace(reason))
            return null;
        reason = reason.Trim();
        if (reason.Length > 512)
            return null;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);
        await using (var targetLock = new NpgsqlCommand(
                         "SELECT pg_advisory_xact_lock(hashtextextended(@target, @round_id))",
                         connection,
                         transaction))
        {
            targetLock.Parameters.AddWithValue("target", $"rucm-ahelp:{target.UserId}");
            targetLock.Parameters.AddWithValue("round_id", roundId);
            await targetLock.ExecuteNonQueryAsync(cancel);
        }

        Guid? responderId;
        await using (var actor = new NpgsqlCommand(
                         """
                         SELECT users.id
                         FROM governance.users AS users
                         JOIN governance.duty_sessions AS duty ON duty.user_id = users.id
                         JOIN governance.capability_grants AS capability_grant
                           ON capability_grant.user_id = users.id AND capability_grant.source_type = 'duty_session'
                          AND capability_grant.source_id = duty.id::text
                         WHERE users.ss14_user_id = @responder
                           AND NOT users.is_governance_suspended
                           AND duty.round_id = @round_id AND duty.status = 'active'
                           AND duty.observer_confirmed AND duty.expires_at > now()
                           AND capability_grant.capability = 'moderation.request_explanation'
                           AND capability_grant.expires_at > now() AND capability_grant.revoked_at IS NULL
                         LIMIT 1
                         """,
                         connection,
                         transaction))
        {
            actor.Parameters.AddWithValue("responder", responder.UserId);
            actor.Parameters.AddWithValue("round_id", roundId);
            responderId = await actor.ExecuteScalarAsync(cancel) as Guid?;
        }
        if (responderId == null)
        {
            await transaction.RollbackAsync(cancel);
            return null;
        }

        long? ticketId = null;
        string? status = null;
        Guid? claimedBy = null;
        await using (var existing = new NpgsqlCommand(
                         """
                         SELECT id, status, claimed_by_user_id
                         FROM governance.ahelp_tickets
                         WHERE round_id = @round_id AND reporter_ss14_user_id = @target
                           AND status IN ('open', 'claimed', 'waiting_player', 'escalated_to_incident')
                         LIMIT 1
                         """,
                         connection,
                         transaction))
        {
            existing.Parameters.AddWithValue("round_id", roundId);
            existing.Parameters.AddWithValue("target", target.UserId);
            await using var reader = await existing.ExecuteReaderAsync(cancel);
            if (await reader.ReadAsync(cancel))
            {
                ticketId = reader.GetInt64(0);
                status = reader.GetString(1);
                claimedBy = reader.IsDBNull(2) ? null : reader.GetGuid(2);
            }
        }

        if (ticketId == null)
        {
            await using var create = new NpgsqlCommand(
                """
                INSERT INTO governance.ahelp_tickets(
                    round_id, reporter_user_id, reporter_ss14_user_id, claimed_by_user_id,
                    status, summary, created_at, updated_at)
                VALUES (
                    @round_id,
                    (SELECT id FROM governance.users WHERE ss14_user_id = @target),
                    @target, @responder_id, 'claimed', @reason, now(), now())
                RETURNING id
                """,
                connection,
                transaction);
            create.Parameters.AddWithValue("round_id", roundId);
            create.Parameters.AddWithValue("target", target.UserId);
            create.Parameters.AddWithValue("responder_id", responderId.Value);
            create.Parameters.AddWithValue("reason", reason);
            ticketId = Convert.ToInt64(await create.ExecuteScalarAsync(cancel));
        }
        else if (status == "open")
        {
            await using var claim = new NpgsqlCommand(
                """
                UPDATE governance.ahelp_tickets
                SET claimed_by_user_id = @responder_id, status = 'claimed', updated_at = now()
                WHERE id = @ticket_id AND status = 'open'
                """,
                connection,
                transaction);
            claim.Parameters.AddWithValue("ticket_id", ticketId.Value);
            claim.Parameters.AddWithValue("responder_id", responderId.Value);
            await claim.ExecuteNonQueryAsync(cancel);
        }
        else if (claimedBy != responderId || status is not ("claimed" or "waiting_player"))
        {
            await transaction.RollbackAsync(cancel);
            return null;
        }

        await using (var message = new NpgsqlCommand(
                         """
                         INSERT INTO governance.ahelp_messages(ticket_id, sender_ss14_user_id, body)
                         VALUES (@ticket_id, @responder, @reason)
                         """,
                         connection,
                         transaction))
        {
            message.Parameters.AddWithValue("ticket_id", ticketId.Value);
            message.Parameters.AddWithValue("responder", responder.UserId);
            message.Parameters.AddWithValue("reason", reason);
            await message.ExecuteNonQueryAsync(cancel);
        }
        await transaction.CommitAsync(cancel);
        return ticketId;
    }
}
