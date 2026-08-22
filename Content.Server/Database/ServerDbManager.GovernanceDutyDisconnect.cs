using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Npgsql;
using Robust.Shared.Network;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<IReadOnlyList<NetUserId>> GetActiveGovernanceDutyUsersAsync(
        int roundId,
        CancellationToken cancel = default);

    Task<bool> AbandonGovernanceDutyAsync(
        NetUserId userId,
        int roundId,
        CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public async Task<IReadOnlyList<NetUserId>> GetActiveGovernanceDutyUsersAsync(
        int roundId,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) || roundId <= 0)
            return Array.Empty<NetUserId>();

        var result = new List<NetUserId>();
        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            SELECT users.ss14_user_id
            FROM governance.duty_sessions AS duty
            JOIN governance.users AS users ON users.id = duty.user_id
            WHERE duty.round_id = @round_id
              AND duty.status = 'active'
              AND duty.expires_at > now()
            """,
            connection);
        command.Parameters.AddWithValue("round_id", roundId);
        await using var reader = await command.ExecuteReaderAsync(cancel);
        while (await reader.ReadAsync(cancel))
            result.Add(new NetUserId(reader.GetGuid(0)));

        return result;
    }

    public async Task<bool> AbandonGovernanceDutyAsync(
        NetUserId userId,
        int roundId,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) || roundId <= 0)
            return false;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);

        // Serialize with invitation/staffing creation so a freed slot is observed atomically by the next pass.
        await using (var dutyLock = new NpgsqlCommand(
                         "SELECT pg_advisory_xact_lock(hashtext('rucm-governance-duty'), @round_id)",
                         connection,
                         transaction))
        {
            dutyLock.Parameters.AddWithValue("round_id", roundId);
            await dutyLock.ExecuteNonQueryAsync(cancel);
        }

        await using var command = new NpgsqlCommand(
            """
            WITH ended AS (
                UPDATE governance.duty_sessions AS duty
                SET status = 'abandoned',
                    ended_at = now(),
                    version = version + 1
                FROM governance.users AS users
                WHERE duty.user_id = users.id
                  AND users.ss14_user_id = @ss14_user_id
                  AND duty.round_id = @round_id
                  AND duty.status = 'active'
                  AND duty.expires_at > now()
                RETURNING duty.id, duty.user_id
            ), assignment_failed AS (
                UPDATE governance.service_assignments AS assignment
                SET failed_at = COALESCE(assignment.failed_at, now())
                FROM ended AS duty
                WHERE assignment.user_id = duty.user_id
                  AND assignment.track = 'moderation'
                  AND assignment.entity_type = 'round'
                  AND assignment.entity_id = @round_id_text
                  AND assignment.completed_at IS NULL
                  AND assignment.failed_at IS NULL
                RETURNING assignment.id
            ), revoked AS (
                UPDATE governance.capability_grants AS capability_grant
                SET revoked_at = now()
                FROM ended AS duty
                WHERE capability_grant.source_type = 'duty_session'
                  AND capability_grant.source_id = duty.id::text
                  AND capability_grant.revoked_at IS NULL
                RETURNING capability_grant.id, capability_grant.source_id, capability_grant.capability
            ), requeued AS (
                UPDATE governance.ahelp_tickets AS ticket
                SET claimed_by_user_id = NULL,
                    status = 'open',
                    updated_at = now()
                FROM ended AS duty
                WHERE ticket.round_id = @round_id
                  AND ticket.claimed_by_user_id = duty.user_id
                  AND ticket.status IN ('claimed', 'waiting_player')
                RETURNING ticket.id
            ), duty_audited AS (
                INSERT INTO governance.audit_events(
                    event_type, actor_type, actor_id, entity_type, entity_id, payload)
                SELECT 'duty.abandoned', 'ss14_server', duty.user_id::text,
                       'duty_session', duty.id::text,
                       jsonb_build_object('round_id', @round_id, 'reason', 'disconnect_timeout')
                FROM ended AS duty
                RETURNING id
            ), capability_audited AS (
                INSERT INTO governance.audit_events(
                    event_type, actor_type, entity_type, entity_id, payload)
                SELECT 'capability.revoked', 'ss14_server', 'capability_grant', revoked.id::text,
                       jsonb_build_object(
                           'source_type', 'duty_session',
                           'source_id', revoked.source_id,
                           'capability', revoked.capability,
                           'reason', 'duty_abandoned')
                FROM revoked
                RETURNING id
            ), ahelp_audited AS (
                INSERT INTO governance.audit_events(
                    event_type, actor_type, actor_id, entity_type, entity_id, payload)
                SELECT 'ahelp.requeued', 'ss14_server', duty.user_id::text,
                       'ahelp_ticket', requeued.id::text,
                       jsonb_build_object('round_id', @round_id, 'reason', 'responder_disconnected')
                FROM requeued
                CROSS JOIN ended AS duty
                RETURNING id
            )
            SELECT EXISTS(SELECT 1 FROM ended);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("ss14_user_id", userId.UserId);
        command.Parameters.AddWithValue("round_id", roundId);
        command.Parameters.AddWithValue("round_id_text", roundId.ToString());

        var abandoned = Convert.ToBoolean(await command.ExecuteScalarAsync(cancel));
        await transaction.CommitAsync(cancel);
        return abandoned;
    }
}
