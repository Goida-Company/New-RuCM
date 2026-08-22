using System;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Npgsql;
using NpgsqlTypes;
using Robust.Shared.Network;

namespace Content.Server.Database;

public sealed record GovernanceEventExecutionAction(
    long Id,
    long SessionId,
    NetUserId ActorUserId,
    string Capability,
    string Resource,
    string Payload);

public partial interface IServerDbManager
{
    Task<int> FailUnexecutableGovernanceEventActionsAsync(
        int roundId,
        CancellationToken cancel = default);

    Task<GovernanceEventExecutionAction?> ClaimGovernanceEventActionAsync(
        int roundId,
        CancellationToken cancel = default);

    Task<bool> CompleteGovernanceEventActionAsync(
        long actionId,
        bool success,
        string? error,
        CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public async Task<int> FailUnexecutableGovernanceEventActionsAsync(
        int roundId,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) || roundId <= 0)
            return 0;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            WITH invalid AS (
                SELECT action.id,
                       action.session_id,
                       action.actor_user_id,
                       action.capability,
                       action.resource,
                       CASE
                           WHEN action.server_status = 'executing'
                                AND action.server_executed_at < now() - interval '5 minutes'
                               THEN 'Игровой сервер не подтвердил завершение действия; повторное выполнение запрещено во избежание двойного эффекта.'
                           WHEN action.status = 'denied' THEN 'Действие отклонено Governance до передачи игровому серверу.'
                           WHEN session.status <> 'active' THEN 'Сессия события уже завершена или отозвана.'
                           WHEN session.expires_at <= now() THEN 'Срок действия сессии события истёк.'
                           WHEN manifest.id IS NULL THEN 'Ресурс отсутствует в утверждённом манифесте.'
                           WHEN action.status = 'executed' AND (
                               SELECT count(*)
                               FROM governance.event_actions AS prior
                               WHERE prior.session_id = action.session_id
                                 AND prior.capability = action.capability
                                 AND prior.resource = action.resource
                                 AND prior.status = 'executed'
                                 AND prior.id <= action.id) > manifest.max_uses
                               THEN 'Лимит использований ресурса из утверждённого манифеста исчерпан.'
                           WHEN capability_grant.id IS NULL THEN 'Полномочие события отсутствует, истекло или отозвано.'
                           ELSE NULL
                       END AS error
                FROM governance.event_actions AS action
                JOIN governance.event_sessions AS session ON session.id = action.session_id
                LEFT JOIN governance.event_manifest_items AS manifest
                  ON manifest.session_id = session.id
                 AND manifest.capability = action.capability
                 AND manifest.resource = action.resource
                LEFT JOIN governance.capability_grants AS capability_grant
                  ON capability_grant.user_id = action.actor_user_id
                 AND capability_grant.source_type = 'event_session'
                 AND capability_grant.source_id = session.id::text
                 AND capability_grant.capability = action.capability
                 AND capability_grant.issued_at <= now()
                 AND capability_grant.expires_at > now()
                 AND capability_grant.revoked_at IS NULL
                 AND capability_grant.scope @> jsonb_build_object('round_id', @round_id, 'event_session_id', session.id)
                WHERE session.round_id = @round_id
                  AND (
                      (action.server_status = 'executing'
                       AND action.server_executed_at < now() - interval '5 minutes')
                      OR (action.server_status = 'pending' AND (
                          action.status = 'denied'
                          OR session.status <> 'active'
                          OR session.expires_at <= now()
                          OR manifest.id IS NULL
                          OR (manifest.id IS NOT NULL AND action.status = 'executed' AND (
                              SELECT count(*)
                              FROM governance.event_actions AS prior
                              WHERE prior.session_id = action.session_id
                                AND prior.capability = action.capability
                                AND prior.resource = action.resource
                                AND prior.status = 'executed'
                                AND prior.id <= action.id) > manifest.max_uses)
                          OR capability_grant.id IS NULL))
                  )
                FOR UPDATE OF action SKIP LOCKED
            ), changed AS (
                UPDATE governance.event_actions AS action
                SET server_status = 'failed',
                    server_executed_at = now(),
                    server_execution_error = invalid.error
                FROM invalid
                WHERE action.id = invalid.id
                  AND action.server_status IN ('pending', 'executing')
                RETURNING action.id,
                          action.session_id,
                          action.actor_user_id,
                          action.capability,
                          action.resource,
                          action.server_execution_error
            ), audited AS (
                INSERT INTO governance.audit_events(
                    event_type, actor_type, actor_id, entity_type, entity_id, payload)
                SELECT 'event.action_server_failed',
                       'ss14_server',
                       actor.ss14_user_id::text,
                       'event_action',
                       changed.id::text,
                       jsonb_build_object(
                           'event_session_id', changed.session_id,
                           'capability', changed.capability,
                           'resource', changed.resource,
                           'server_status', 'failed',
                           'error', changed.server_execution_error)
                FROM changed
                JOIN governance.users AS actor ON actor.id = changed.actor_user_id
            )
            SELECT count(*) FROM changed
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("round_id", roundId);

        var changed = Convert.ToInt32(await command.ExecuteScalarAsync(cancel));
        await transaction.CommitAsync(cancel);
        return changed;
    }

    public async Task<GovernanceEventExecutionAction?> ClaimGovernanceEventActionAsync(
        int roundId,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) || roundId <= 0)
            return null;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            WITH candidate AS (
                SELECT action.id
                FROM governance.event_actions AS action
                JOIN governance.event_sessions AS session ON session.id = action.session_id
                JOIN governance.event_manifest_items AS manifest
                  ON manifest.session_id = session.id
                 AND manifest.capability = action.capability
                 AND manifest.resource = action.resource
                JOIN governance.capability_grants AS capability_grant
                  ON capability_grant.user_id = action.actor_user_id
                 AND capability_grant.source_type = 'event_session'
                 AND capability_grant.source_id = session.id::text
                 AND capability_grant.capability = action.capability
                WHERE action.status = 'executed'
                  AND action.server_status = 'pending'
                  AND session.round_id = @round_id
                  AND session.status = 'active'
                  AND session.expires_at > now()
                  AND capability_grant.issued_at <= now()
                  AND capability_grant.expires_at > now()
                  AND capability_grant.revoked_at IS NULL
                  AND capability_grant.scope @> jsonb_build_object('round_id', @round_id, 'event_session_id', session.id)
                  AND (
                      SELECT count(*)
                      FROM governance.event_actions AS prior
                      WHERE prior.session_id = action.session_id
                        AND prior.capability = action.capability
                        AND prior.resource = action.resource
                        AND prior.status = 'executed'
                        AND prior.id <= action.id) <= manifest.max_uses
                ORDER BY action.id
                FOR UPDATE OF action SKIP LOCKED
                LIMIT 1
            ), claimed AS (
                UPDATE governance.event_actions AS action
                SET server_status = 'executing',
                    server_executed_at = now(),
                    server_execution_error = NULL
                FROM candidate
                WHERE action.id = candidate.id
                  AND action.server_status = 'pending'
                RETURNING action.id, action.session_id, action.actor_user_id,
                          action.capability, action.resource, action.payload::text
            )
            SELECT claimed.id,
                   claimed.session_id,
                   actor.ss14_user_id,
                   claimed.capability,
                   claimed.resource,
                   claimed.payload
            FROM claimed
            JOIN governance.users AS actor ON actor.id = claimed.actor_user_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("round_id", roundId);

        GovernanceEventExecutionAction? result = null;
        await using (var reader = await command.ExecuteReaderAsync(cancel))
        {
            if (await reader.ReadAsync(cancel))
            {
                result = new GovernanceEventExecutionAction(
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    new NetUserId(reader.GetGuid(2)),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5));
            }
        }

        if (result == null)
        {
            await transaction.RollbackAsync(cancel);
            return null;
        }

        await transaction.CommitAsync(cancel);
        return result;
    }

    public async Task<bool> CompleteGovernanceEventActionAsync(
        long actionId,
        bool success,
        string? error,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) || actionId <= 0)
            return false;

        error = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
        if (error is { Length: > 1000 })
            error = error[..1000];

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            WITH changed AS (
                UPDATE governance.event_actions
                SET server_status = @server_status,
                    server_executed_at = now(),
                    server_execution_error = @error
                WHERE id = @action_id
                  AND server_status = 'executing'
                RETURNING id, session_id, actor_user_id, capability, resource
            ), audited AS (
                INSERT INTO governance.audit_events(
                    event_type, actor_type, actor_id, entity_type, entity_id, payload)
                SELECT @event_type,
                       'ss14_server',
                       actor.ss14_user_id::text,
                       'event_action',
                       changed.id::text,
                       jsonb_build_object(
                           'event_session_id', changed.session_id,
                           'capability', changed.capability,
                           'resource', changed.resource,
                           'server_status', @server_status,
                           'error', @error)
                FROM changed
                JOIN governance.users AS actor ON actor.id = changed.actor_user_id
            )
            SELECT count(*) FROM changed
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("action_id", actionId);
        command.Parameters.AddWithValue("server_status", success ? "executed" : "failed");
        command.Parameters.AddWithValue("event_type", success ? "event.action_server_executed" : "event.action_server_failed");
        command.Parameters.AddWithValue("error", NpgsqlDbType.Text, error == null ? DBNull.Value : error);

        var changed = Convert.ToInt32(await command.ExecuteScalarAsync(cancel));
        if (changed != 1)
        {
            await transaction.RollbackAsync(cancel);
            return false;
        }

        await transaction.CommitAsync(cancel);
        return true;
    }
}
