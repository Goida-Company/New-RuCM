using System;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Npgsql;
using Robust.Shared.Network;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<long?> CreateGovernanceDutyTeleportIncidentAsync(
        NetUserId responder,
        NetUserId target,
        int roundId,
        CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public async Task<long?> CreateGovernanceDutyTeleportIncidentAsync(
        NetUserId responder,
        NetUserId target,
        int roundId,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            roundId <= 0 || responder == target)
        {
            return null;
        }

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);

        // A target may never have linked Discord. Keep the same SS14-only identity strategy used by
        // live AHelp incidents so the incident can still point at the authoritative SS14 account.
        await using (var ensureTarget = new NpgsqlCommand(
                         """
                         INSERT INTO governance.users(ss14_user_id, discord_user_id, created_at, updated_at)
                         VALUES (
                             @target,
                             -((('x' || substr(md5(@target::text), 1, 15))::bit(60)::bigint) + 1),
                             now(), now())
                         ON CONFLICT (ss14_user_id) DO NOTHING
                         """,
                         connection,
                         transaction))
        {
            ensureTarget.Parameters.AddWithValue("target", target.UserId);
            await ensureTarget.ExecuteNonQueryAsync(cancel);
        }

        await using var command = new NpgsqlCommand(
            """
            WITH actor AS (
                SELECT users.id, duty.id AS duty_id
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
                  AND duty.started_at <= now()
                  AND duty.expires_at > now()
                  AND capability_grant.capability = 'moderation.ahelp'
                  AND capability_grant.issued_at <= now()
                  AND capability_grant.expires_at > now()
                  AND capability_grant.revoked_at IS NULL
                  AND capability_grant.scope @> jsonb_build_object('round_id', @round_id)
                ORDER BY capability_grant.issued_at DESC
                LIMIT 1
            ), target_user AS (
                SELECT id, ss14_user_id
                FROM governance.users
                WHERE ss14_user_id = @target
                LIMIT 1
            ), created AS (
                INSERT INTO governance.live_incidents(
                    round_id,
                    target_user_id,
                    reporter_user_id,
                    created_by_user_id,
                    type,
                    summary,
                    status,
                    created_at)
                SELECT
                    @round_id,
                    target_user.id,
                    NULL,
                    actor.id,
                    'duty_teleport_player_to_self',
                    'Дежурный телепортировал игрока к себе для оперативного разбирательства.',
                    'active',
                    now()
                FROM actor, target_user
                RETURNING id, target_user_id, created_by_user_id
            ), audited AS (
                INSERT INTO governance.audit_events(
                    event_type,
                    actor_type,
                    actor_id,
                    target_type,
                    target_id,
                    entity_type,
                    entity_id,
                    payload)
                SELECT
                    'incident.created_from_duty_teleport',
                    'ss14_user',
                    @responder::text,
                    'ss14_user',
                    target_user.ss14_user_id::text,
                    'live_incident',
                    created.id::text,
                    jsonb_build_object(
                        'round_id', @round_id,
                        'duty_id', actor.duty_id,
                        'operation', 'teleport_player_to_self')
                FROM created, actor, target_user
            )
            SELECT id FROM created
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("responder", responder.UserId);
        command.Parameters.AddWithValue("target", target.UserId);
        command.Parameters.AddWithValue("round_id", roundId);

        var result = await command.ExecuteScalarAsync(cancel);
        if (result == null || result == DBNull.Value)
        {
            await transaction.RollbackAsync(cancel);
            return null;
        }

        await transaction.CommitAsync(cancel);
        return Convert.ToInt64(result);
    }
}
