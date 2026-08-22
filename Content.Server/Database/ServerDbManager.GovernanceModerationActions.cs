using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Npgsql;
using Robust.Shared.Network;

namespace Content.Server.Database;

public sealed record GovernanceIncidentActionInfo(
    long Id,
    long IncidentId,
    NetUserId ActorUserId,
    string ActorName,
    NetUserId TargetUserId,
    string TargetName,
    string ActionType,
    string Reason,
    int? DurationSeconds,
    string Status,
    short RequiredApprovals,
    int Approvals);

public partial interface IServerDbManager
{
    Task<GovernanceIncidentActionInfo?> ProposeGovernanceIncidentActionAsync(
        long incidentId,
        NetUserId actor,
        int roundId,
        string actionType,
        string reason,
        int? durationSeconds,
        short requiredApprovals,
        CancellationToken cancel = default);

    Task<GovernanceIncidentActionInfo?> ReviewGovernanceIncidentActionAsync(
        long actionId,
        NetUserId reviewer,
        int roundId,
        string decision,
        CancellationToken cancel = default);

    Task<IReadOnlyList<GovernanceIncidentActionInfo>> GetGovernanceIncidentActionsAsync(
        long incidentId,
        NetUserId responder,
        int roundId,
        CancellationToken cancel = default);

    Task<IReadOnlyList<GovernanceIncidentActionInfo>> GetGovernancePendingActionApprovalsAsync(
        NetUserId responder,
        int roundId,
        CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public async Task<GovernanceIncidentActionInfo?> ProposeGovernanceIncidentActionAsync(
        long incidentId,
        NetUserId actor,
        int roundId,
        string actionType,
        string reason,
        int? durationSeconds,
        short requiredApprovals,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            incidentId <= 0 || roundId <= 0 || requiredApprovals is < 1 or > 5 ||
            actionType is not ("freeze" or "round_remove" or "request_explanation" or "view_logs"))
            return null;

        reason = reason.Trim();
        if (reason.Length is < 10 or > 512)
            return null;

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
                WHERE users.ss14_user_id = @actor
                  AND NOT users.is_governance_suspended
                  AND duty.round_id = @round_id
                  AND duty.status = 'active'
                  AND duty.observer_confirmed
                  AND duty.expires_at > now()
                  AND capability_grant.capability = ('moderation.' || @action_type)
                  AND capability_grant.expires_at > now()
                  AND capability_grant.revoked_at IS NULL
                LIMIT 1
            ), incident AS (
                SELECT incident.id,
                       incident.target_user_id,
                       actor.id AS actor_id
                FROM governance.live_incidents AS incident
                JOIN governance.ahelp_tickets AS ticket ON ticket.id = incident.ahelp_ticket_id
                CROSS JOIN actor
                WHERE incident.id = @incident_id
                  AND incident.round_id = @round_id
                  AND incident.status IN ('active', 'contained')
                  AND ticket.claimed_by_user_id = actor.id
                LIMIT 1
            ), created AS (
                INSERT INTO governance.moderation_actions(
                    incident_id, actor_user_id, target_user_id, action_type,
                    reason, duration_seconds, status, required_approvals,
                    created_at, idempotency_key)
                SELECT incident.id,
                       incident.actor_id,
                       incident.target_user_id,
                       @action_type,
                       @reason,
                       @duration_seconds,
                       CASE WHEN @required_approvals = 1 THEN 'approved' ELSE 'proposed' END,
                       @required_approvals,
                       now(),
                       'ingame:' || incident.id::text || ':' || @action_type || ':' || gen_random_uuid()::text
                FROM incident
                RETURNING id, incident_id, actor_user_id, target_user_id, action_type,
                          reason, duration_seconds, status, required_approvals
            ), approval AS (
                INSERT INTO governance.moderation_approvals(
                    action_id, approver_user_id, decision, created_at)
                SELECT created.id, created.actor_user_id, 'approve', now()
                FROM created
                RETURNING action_id
            ), audited AS (
                INSERT INTO governance.audit_events(
                    event_type, actor_type, actor_id, target_type, target_id,
                    entity_type, entity_id, payload)
                SELECT 'moderation.action_proposed', 'ss14_user', @actor::text,
                       'ss14_user', target.ss14_user_id::text,
                       'moderation_action', created.id::text,
                       jsonb_build_object(
                           'round_id', @round_id,
                           'incident_id', created.incident_id,
                           'action_type', created.action_type,
                           'required_approvals', created.required_approvals,
                           'source', 'ingame_workspace')
                FROM created
                JOIN governance.users AS target ON target.id = created.target_user_id
            )
            SELECT created.id,
                   created.incident_id,
                   actor_user.ss14_user_id,
                   COALESCE(actor_player.last_seen_user_name, actor_user.ss14_user_id::text),
                   target_user.ss14_user_id,
                   COALESCE(target_player.last_seen_user_name, target_user.ss14_user_id::text),
                   created.action_type,
                   created.reason,
                   created.duration_seconds,
                   created.status,
                   created.required_approvals,
                   1
            FROM created
            JOIN governance.users AS actor_user ON actor_user.id = created.actor_user_id
            JOIN governance.users AS target_user ON target_user.id = created.target_user_id
            LEFT JOIN player AS actor_player ON actor_player.user_id = actor_user.ss14_user_id
            LEFT JOIN player AS target_player ON target_player.user_id = target_user.ss14_user_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("incident_id", incidentId);
        command.Parameters.AddWithValue("actor", actor.UserId);
        command.Parameters.AddWithValue("round_id", roundId);
        command.Parameters.AddWithValue("action_type", actionType);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("required_approvals", requiredApprovals);
        command.Parameters.AddWithValue("duration_seconds", (object?) durationSeconds ?? DBNull.Value);

        GovernanceIncidentActionInfo? result = null;
        await using (var reader = await command.ExecuteReaderAsync(cancel))
        {
            if (await reader.ReadAsync(cancel))
                result = ReadIncidentAction(reader);
        }

        if (result == null)
        {
            await transaction.RollbackAsync(cancel);
            return null;
        }

        await transaction.CommitAsync(cancel);
        return result;
    }

    public async Task<GovernanceIncidentActionInfo?> ReviewGovernanceIncidentActionAsync(
        long actionId,
        NetUserId reviewer,
        int roundId,
        string decision,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            actionId <= 0 || roundId <= 0 || decision is not ("approve" or "reject"))
            return null;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);

        await using var command = new NpgsqlCommand(
            """
            WITH reviewer AS (
                SELECT users.id
                FROM governance.users AS users
                JOIN governance.duty_sessions AS duty ON duty.user_id = users.id
                JOIN governance.capability_grants AS capability_grant
                  ON capability_grant.user_id = users.id
                 AND capability_grant.source_type = 'duty_session'
                 AND capability_grant.source_id = duty.id::text
                JOIN governance.moderation_actions AS action
                  ON action.id = @action_id
                 AND capability_grant.capability = ('moderation.' || action.action_type)
                JOIN governance.live_incidents AS incident ON incident.id = action.incident_id
                WHERE users.ss14_user_id = @reviewer
                  AND NOT users.is_governance_suspended
                  AND duty.round_id = @round_id
                  AND duty.status = 'active'
                  AND duty.observer_confirmed
                  AND duty.expires_at > now()
                  AND capability_grant.expires_at > now()
                  AND capability_grant.revoked_at IS NULL
                  AND incident.round_id = @round_id
                  AND incident.status IN ('active', 'contained')
                  AND action.status = 'proposed'
                  AND action.actor_user_id <> users.id
                  AND NOT EXISTS (
                      SELECT 1 FROM governance.moderation_approvals AS old
                      WHERE old.action_id = action.id AND old.approver_user_id = users.id)
                LIMIT 1
            ), vote AS (
                INSERT INTO governance.moderation_approvals(
                    action_id, approver_user_id, decision, created_at)
                SELECT @action_id, reviewer.id, @decision, now()
                FROM reviewer
                RETURNING action_id
            ), counts AS (
                SELECT action.id,
                       count(*) FILTER (WHERE approval.decision = 'approve')::integer AS approvals,
                       count(*) FILTER (WHERE approval.decision = 'reject')::integer AS rejections
                FROM governance.moderation_actions AS action
                JOIN vote ON vote.action_id = action.id
                LEFT JOIN governance.moderation_approvals AS approval ON approval.action_id = action.id
                GROUP BY action.id
            ), changed AS (
                UPDATE governance.moderation_actions AS action
                SET status = CASE
                    WHEN counts.rejections > 0 THEN 'rejected'
                    WHEN counts.approvals >= action.required_approvals THEN 'approved'
                    ELSE 'proposed'
                END
                FROM counts
                WHERE action.id = counts.id
                RETURNING action.id, action.incident_id, action.actor_user_id, action.target_user_id,
                          action.action_type, action.reason, action.duration_seconds,
                          action.status, action.required_approvals, counts.approvals
            ), audited AS (
                INSERT INTO governance.audit_events(
                    event_type, actor_type, actor_id, entity_type, entity_id, payload)
                SELECT 'moderation.action_reviewed', 'ss14_user', @reviewer::text,
                       'moderation_action', changed.id::text,
                       jsonb_build_object(
                           'round_id', @round_id,
                           'decision', @decision,
                           'status', changed.status,
                           'approvals', changed.approvals,
                           'required_approvals', changed.required_approvals,
                           'source', 'ingame_workspace')
                FROM changed
            )
            SELECT changed.id,
                   changed.incident_id,
                   actor_user.ss14_user_id,
                   COALESCE(actor_player.last_seen_user_name, actor_user.ss14_user_id::text),
                   target_user.ss14_user_id,
                   COALESCE(target_player.last_seen_user_name, target_user.ss14_user_id::text),
                   changed.action_type,
                   changed.reason,
                   changed.duration_seconds,
                   changed.status,
                   changed.required_approvals,
                   changed.approvals
            FROM changed
            JOIN governance.users AS actor_user ON actor_user.id = changed.actor_user_id
            JOIN governance.users AS target_user ON target_user.id = changed.target_user_id
            LEFT JOIN player AS actor_player ON actor_player.user_id = actor_user.ss14_user_id
            LEFT JOIN player AS target_player ON target_player.user_id = target_user.ss14_user_id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("action_id", actionId);
        command.Parameters.AddWithValue("reviewer", reviewer.UserId);
        command.Parameters.AddWithValue("round_id", roundId);
        command.Parameters.AddWithValue("decision", decision);

        GovernanceIncidentActionInfo? result = null;
        await using (var reader = await command.ExecuteReaderAsync(cancel))
        {
            if (await reader.ReadAsync(cancel))
                result = ReadIncidentAction(reader);
        }

        if (result == null)
        {
            await transaction.RollbackAsync(cancel);
            return null;
        }

        await transaction.CommitAsync(cancel);
        return result;
    }

    public async Task<IReadOnlyList<GovernanceIncidentActionInfo>> GetGovernanceIncidentActionsAsync(
        long incidentId,
        NetUserId responder,
        int roundId,
        CancellationToken cancel = default)
    {
        var result = new List<GovernanceIncidentActionInfo>();
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            incidentId <= 0 || roundId <= 0)
            return result;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            WITH responder AS (
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
            SELECT action.id,
                   action.incident_id,
                   actor_user.ss14_user_id,
                   COALESCE(actor_player.last_seen_user_name, actor_user.ss14_user_id::text),
                   target_user.ss14_user_id,
                   COALESCE(target_player.last_seen_user_name, target_user.ss14_user_id::text),
                   action.action_type,
                   action.reason,
                   action.duration_seconds,
                   action.status,
                   action.required_approvals,
                   count(approval.approver_user_id) FILTER (WHERE approval.decision = 'approve')::integer
            FROM governance.moderation_actions AS action
            JOIN governance.live_incidents AS incident ON incident.id = action.incident_id
            JOIN governance.ahelp_tickets AS ticket ON ticket.id = incident.ahelp_ticket_id
            JOIN responder ON responder.id = ticket.claimed_by_user_id
            JOIN governance.users AS actor_user ON actor_user.id = action.actor_user_id
            JOIN governance.users AS target_user ON target_user.id = action.target_user_id
            LEFT JOIN player AS actor_player ON actor_player.user_id = actor_user.ss14_user_id
            LEFT JOIN player AS target_player ON target_player.user_id = target_user.ss14_user_id
            LEFT JOIN governance.moderation_approvals AS approval ON approval.action_id = action.id
            WHERE incident.id = @incident_id
              AND incident.round_id = @round_id
            GROUP BY action.id, action.incident_id, actor_user.ss14_user_id, actor_player.last_seen_user_name,
                     target_user.ss14_user_id, target_player.last_seen_user_name,
                     action.action_type, action.reason, action.duration_seconds,
                     action.status, action.required_approvals
            ORDER BY action.created_at DESC
            LIMIT 20
            """,
            connection);
        command.Parameters.AddWithValue("incident_id", incidentId);
        command.Parameters.AddWithValue("responder", responder.UserId);
        command.Parameters.AddWithValue("round_id", roundId);

        await using var reader = await command.ExecuteReaderAsync(cancel);
        while (await reader.ReadAsync(cancel))
            result.Add(ReadIncidentAction(reader));
        return result;
    }

    public async Task<IReadOnlyList<GovernanceIncidentActionInfo>> GetGovernancePendingActionApprovalsAsync(
        NetUserId responder,
        int roundId,
        CancellationToken cancel = default)
    {
        var result = new List<GovernanceIncidentActionInfo>();
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) || roundId <= 0)
            return result;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            WITH responder AS (
                SELECT users.id, duty.id AS duty_id
                FROM governance.users AS users
                JOIN governance.duty_sessions AS duty ON duty.user_id = users.id
                WHERE users.ss14_user_id = @responder
                  AND NOT users.is_governance_suspended
                  AND duty.round_id = @round_id
                  AND duty.status = 'active'
                  AND duty.observer_confirmed
                  AND duty.expires_at > now()
                LIMIT 1
            )
            SELECT action.id,
                   action.incident_id,
                   actor_user.ss14_user_id,
                   COALESCE(actor_player.last_seen_user_name, actor_user.ss14_user_id::text),
                   target_user.ss14_user_id,
                   COALESCE(target_player.last_seen_user_name, target_user.ss14_user_id::text),
                   action.action_type,
                   action.reason,
                   action.duration_seconds,
                   action.status,
                   action.required_approvals,
                   count(approval.approver_user_id) FILTER (WHERE approval.decision = 'approve')::integer
            FROM governance.moderation_actions AS action
            JOIN governance.live_incidents AS incident ON incident.id = action.incident_id
            CROSS JOIN responder
            JOIN governance.users AS actor_user ON actor_user.id = action.actor_user_id
            JOIN governance.users AS target_user ON target_user.id = action.target_user_id
            JOIN governance.capability_grants AS capability_grant
              ON capability_grant.user_id = responder.id
             AND capability_grant.source_type = 'duty_session'
             AND capability_grant.source_id = responder.duty_id::text
             AND capability_grant.capability = ('moderation.' || action.action_type)
             AND capability_grant.expires_at > now()
             AND capability_grant.revoked_at IS NULL
            LEFT JOIN player AS actor_player ON actor_player.user_id = actor_user.ss14_user_id
            LEFT JOIN player AS target_player ON target_player.user_id = target_user.ss14_user_id
            LEFT JOIN governance.moderation_approvals AS approval ON approval.action_id = action.id
            WHERE incident.round_id = @round_id
              AND incident.status IN ('active', 'contained')
              AND action.status = 'proposed'
              AND action.actor_user_id <> responder.id
              AND NOT EXISTS (
                  SELECT 1 FROM governance.moderation_approvals AS mine
                  WHERE mine.action_id = action.id AND mine.approver_user_id = responder.id)
            GROUP BY action.id, action.incident_id, actor_user.ss14_user_id, actor_player.last_seen_user_name,
                     target_user.ss14_user_id, target_player.last_seen_user_name,
                     action.action_type, action.reason, action.duration_seconds,
                     action.status, action.required_approvals
            ORDER BY action.created_at
            LIMIT 20
            """,
            connection);
        command.Parameters.AddWithValue("responder", responder.UserId);
        command.Parameters.AddWithValue("round_id", roundId);

        await using var reader = await command.ExecuteReaderAsync(cancel);
        while (await reader.ReadAsync(cancel))
            result.Add(ReadIncidentAction(reader));
        return result;
    }

    private static GovernanceIncidentActionInfo ReadIncidentAction(NpgsqlDataReader reader)
    {
        return new GovernanceIncidentActionInfo(
            reader.GetInt64(0),
            reader.GetInt64(1),
            new NetUserId(reader.GetGuid(2)),
            reader.GetString(3),
            new NetUserId(reader.GetGuid(4)),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8),
            reader.GetString(9),
            reader.GetInt16(10),
            reader.GetInt32(11));
    }
}
