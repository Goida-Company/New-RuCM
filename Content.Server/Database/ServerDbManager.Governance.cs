using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._RuMC14.Governance;
using Content.Shared.CCVar;
using Npgsql;
using NpgsqlTypes;
using Robust.Shared.Network;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<GovernanceDutySession?> GetGovernanceDutySessionAsync(
        NetUserId userId,
        CancellationToken cancel = default);

    Task<GovernanceAuthorization?> AuthorizeGovernanceCapabilityAsync(
        NetUserId userId,
        int roundId,
        string capability,
        CancellationToken cancel = default);

    Task AppendGovernanceAuditAsync(
        string eventType,
        NetUserId actor,
        NetUserId? target,
        string entityType,
        string entityId,
        object payload,
        CancellationToken cancel = default);

    Task<GovernanceModerationActionAuthorization?> AuthorizeGovernanceModerationActionAsync(
        NetUserId actor,
        NetUserId target,
        int roundId,
        long actionId,
        string actionType,
        CancellationToken cancel = default);

    Task CompleteGovernanceModerationActionAsync(long actionId, CancellationToken cancel = default);

    Task<int> GetGovernanceOpenAHelpCountAsync(CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    private NpgsqlConnection CreateGovernanceConnection()
    {
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = _cfg.GetCVar(CCVars.DatabasePgHost),
            Port = _cfg.GetCVar(CCVars.DatabasePgPort),
            Database = _cfg.GetCVar(CCVars.DatabasePgDatabase),
            Username = _cfg.GetCVar(CCVars.DatabasePgUsername),
            Password = _cfg.GetCVar(CCVars.DatabasePgPassword),
            ApplicationName = "RussianCM Governance",
        };

        return new NpgsqlConnection(connectionString.ConnectionString);
    }

    public async Task<GovernanceDutySession?> GetGovernanceDutySessionAsync(
        NetUserId userId,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase))
            return null;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            SELECT ds.id, u.id, ds.round_id, ds.expires_at
            FROM governance.users AS u
            JOIN governance.duty_sessions AS ds ON ds.user_id = u.id
            WHERE u.ss14_user_id = @user_id
              AND NOT u.is_governance_suspended
              AND ds.status = 'active'
              AND ds.observer_confirmed
              AND ds.started_at <= now()
              AND ds.expires_at > now()
            ORDER BY ds.started_at DESC
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("user_id", userId.UserId);

        await using var reader = await command.ExecuteReaderAsync(cancel);
        if (!await reader.ReadAsync(cancel))
            return null;

        return new GovernanceDutySession(
            reader.GetInt64(0),
            reader.GetGuid(1),
            userId,
            reader.GetInt32(2),
            new DateTimeOffset(reader.GetDateTime(3)));
    }

    public async Task<GovernanceAuthorization?> AuthorizeGovernanceCapabilityAsync(
        NetUserId userId,
        int roundId,
        string capability,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase))
            return null;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            SELECT ds.id, u.id, ds.expires_at, cg.expires_at
            FROM governance.users AS u
            JOIN governance.duty_sessions AS ds ON ds.user_id = u.id
            JOIN governance.capability_grants AS cg
              ON cg.user_id = u.id
             AND cg.source_type = 'duty_session'
             AND cg.source_id = ds.id::text
            WHERE u.ss14_user_id = @user_id
              AND NOT u.is_governance_suspended
              AND ds.round_id = @round_id
              AND ds.status = 'active'
              AND ds.observer_confirmed
              AND ds.started_at <= now()
              AND ds.expires_at > now()
              AND cg.capability = @capability
              AND cg.issued_at <= now()
              AND cg.expires_at > now()
              AND cg.revoked_at IS NULL
              AND cg.scope @> jsonb_build_object('round_id', @round_id)
            ORDER BY cg.issued_at DESC
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("user_id", userId.UserId);
        command.Parameters.AddWithValue("round_id", roundId);
        command.Parameters.AddWithValue("capability", capability);

        await using var reader = await command.ExecuteReaderAsync(cancel);
        if (!await reader.ReadAsync(cancel))
            return null;

        var duty = new GovernanceDutySession(
            reader.GetInt64(0),
            reader.GetGuid(1),
            userId,
            roundId,
            new DateTimeOffset(reader.GetDateTime(2)));
        return new GovernanceAuthorization(
            duty,
            capability,
            new DateTimeOffset(reader.GetDateTime(3)));
    }

    public async Task AppendGovernanceAuditAsync(
        string eventType,
        NetUserId actor,
        NetUserId? target,
        string entityType,
        string entityId,
        object payload,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase))
            return;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO governance.audit_events(
                event_type, actor_type, actor_id, target_type, target_id,
                entity_type, entity_id, payload)
            VALUES (
                @event_type, 'ss14_server', @actor_id, @target_type, @target_id,
                @entity_type, @entity_id, CAST(@payload AS jsonb))
            """,
            connection);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("actor_id", actor.UserId.ToString());
        command.Parameters.AddWithValue(
            "target_type",
            NpgsqlDbType.Text,
            target == null ? DBNull.Value : "ss14_user");
        command.Parameters.AddWithValue(
            "target_id",
            NpgsqlDbType.Text,
            target == null ? DBNull.Value : target.Value.UserId.ToString());
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(payload));
        await command.ExecuteNonQueryAsync(cancel);
    }

    public async Task<GovernanceModerationActionAuthorization?> AuthorizeGovernanceModerationActionAsync(
        NetUserId actor,
        NetUserId target,
        int roundId,
        long actionId,
        string actionType,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase))
            return null;
        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            SELECT action.id, incident.id, action.action_type
            FROM governance.moderation_actions AS action
            JOIN governance.live_incidents AS incident ON incident.id = action.incident_id
            JOIN governance.users AS executor_user ON executor_user.ss14_user_id = @actor_id
            JOIN governance.users AS target_user ON target_user.id = action.target_user_id
            WHERE action.id = @action_id
              AND action.action_type = @action_type
              AND action.status = 'approved'
              AND incident.status IN ('active', 'contained')
              AND incident.round_id = @round_id
              AND target_user.ss14_user_id = @target_id
              AND EXISTS (
                  SELECT 1
                  FROM governance.moderation_approvals AS mine
                  WHERE mine.action_id = action.id
                    AND mine.approver_user_id = executor_user.id
                    AND mine.decision = 'approve')
              AND (SELECT count(*) FROM governance.moderation_approvals AS approval
                   WHERE approval.action_id = action.id AND approval.decision = 'approve') >= action.required_approvals
            """,
            connection);
        command.Parameters.AddWithValue("action_id", actionId);
        command.Parameters.AddWithValue("action_type", actionType);
        command.Parameters.AddWithValue("round_id", roundId);
        command.Parameters.AddWithValue("actor_id", actor.UserId);
        command.Parameters.AddWithValue("target_id", target.UserId);
        await using var reader = await command.ExecuteReaderAsync(cancel);
        return await reader.ReadAsync(cancel)
            ? new GovernanceModerationActionAuthorization(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2))
            : null;
    }

    public async Task CompleteGovernanceModerationActionAsync(long actionId, CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase))
            return;
        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            "UPDATE governance.moderation_actions SET status = 'executed', executed_at = now() WHERE id = @id AND status = 'approved'",
            connection);
        command.Parameters.AddWithValue("id", actionId);
        await command.ExecuteNonQueryAsync(cancel);
    }

    public async Task<int> GetGovernanceOpenAHelpCountAsync(CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase))
            return 0;
        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM governance.ahelp_tickets WHERE status IN ('open', 'waiting_player')",
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancel));
    }
}
