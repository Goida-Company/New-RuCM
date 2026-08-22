using System;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Npgsql;
using Robust.Shared.Network;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    Task<bool> IsGovernanceRoundRemovedAsync(
        NetUserId userId,
        int roundId,
        CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    /// <summary>
    /// Checks the authoritative Governance ledger rather than process memory so an executed
    /// round_remove remains enforceable after Content.Server restarts during the same round.
    /// </summary>
    public async Task<bool> IsGovernanceRoundRemovedAsync(
        NetUserId userId,
        int roundId,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) || roundId <= 0)
            return false;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM governance.moderation_actions AS action
                JOIN governance.live_incidents AS incident ON incident.id = action.incident_id
                JOIN governance.users AS target ON target.id = action.target_user_id
                WHERE target.ss14_user_id = @user_id
                  AND incident.round_id = @round_id
                  AND action.action_type = 'round_remove'
                  AND action.status = 'executed'
            )
            """,
            connection);
        command.Parameters.AddWithValue("user_id", userId.UserId);
        command.Parameters.AddWithValue("round_id", roundId);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancel));
    }
}
