using System;
using System.Collections.Generic;
using System.Linq;
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
    Task<IReadOnlyList<GovernanceJuryInvitation>> GetPendingGovernanceJuryInvitationsAsync(
        IReadOnlyCollection<NetUserId> onlineUsers,
        CancellationToken cancel = default);

    Task<GovernanceDutyResponse> RespondGovernanceJuryInvitationAsync(
        long invitationId,
        NetUserId userId,
        GovernanceDutyInvitationChoice choice,
        int acceptReward,
        int declinePenalty,
        int expiryPenalty,
        CancellationToken cancel = default);

    Task<IReadOnlyList<GovernanceDutyInvitation>> CreateGovernanceDutyInvitationsAsync(
        int roundId,
        IReadOnlyCollection<NetUserId> onlineObservers,
        int targetResponders,
        TimeSpan invitationLifetime,
        int expiryPenalty,
        CancellationToken cancel = default);

    Task<GovernanceDutyResponse> RespondGovernanceDutyInvitationAsync(
        long invitationId,
        NetUserId userId,
        int roundId,
        GovernanceDutyInvitationChoice choice,
        TimeSpan sessionLifetime,
        int acceptReward,
        int declinePenalty,
        int expiryPenalty,
        CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public async Task<IReadOnlyList<GovernanceJuryInvitation>> GetPendingGovernanceJuryInvitationsAsync(
        IReadOnlyCollection<NetUserId> onlineUsers,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            onlineUsers.Count == 0)
        {
            return Array.Empty<GovernanceJuryInvitation>();
        }

        var onlineIds = onlineUsers.Select(user => user.UserId).ToArray();
        var result = new List<GovernanceJuryInvitation>();
        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var command = new NpgsqlCommand(
            """
            SELECT invitation.id, users.ss14_user_id, invitation.entity_id,
                   invitation.expires_at
            FROM governance.invitations AS invitation
            JOIN governance.users AS users ON users.id = invitation.user_id
            WHERE invitation.purpose = 'jury'
              AND invitation.entity_type = 'court_case'
              AND invitation.state = 'pending'
              AND invitation.expires_at > now()
              AND users.ss14_user_id = ANY(@online_users)
              AND NOT users.is_governance_suspended
            ORDER BY invitation.created_at
            """,
            connection);
        command.Parameters.AddWithValue(
            "online_users",
            NpgsqlDbType.Array | NpgsqlDbType.Uuid,
            onlineIds);
        await using var reader = await command.ExecuteReaderAsync(cancel);
        while (await reader.ReadAsync(cancel))
        {
            result.Add(new GovernanceJuryInvitation(
                reader.GetInt64(0),
                new NetUserId(reader.GetGuid(1)),
                reader.GetString(2),
                new DateTimeOffset(reader.GetDateTime(3))));
        }

        return result;
    }

    public async Task<GovernanceDutyResponse> RespondGovernanceJuryInvitationAsync(
        long invitationId,
        NetUserId userId,
        GovernanceDutyInvitationChoice choice,
        int acceptReward,
        int declinePenalty,
        int expiryPenalty,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase))
            return new GovernanceDutyResponse(GovernanceDutyResponseStatus.Invalid, 0);

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);
        await using (var invitationLock = new NpgsqlCommand(
                         "SELECT pg_advisory_xact_lock(hashtextextended('rucm-jury-invitation', @id))",
                         connection,
                         transaction))
        {
            invitationLock.Parameters.AddWithValue("id", invitationId);
            await invitationLock.ExecuteNonQueryAsync(cancel);
        }

        Guid governanceUserId;
        string state;
        DateTime expiresAt;
        int rating;
        var found = false;
        await using (var select = new NpgsqlCommand(
                         """
                         SELECT invitation.user_id, invitation.state, invitation.expires_at,
                                users.civic_rating_cache
                         FROM governance.invitations AS invitation
                         JOIN governance.users AS users ON users.id = invitation.user_id
                         WHERE invitation.id = @invitation_id
                           AND invitation.purpose = 'jury'
                           AND invitation.entity_type = 'court_case'
                           AND users.ss14_user_id = @ss14_user_id
                           AND NOT users.is_governance_suspended
                         FOR UPDATE OF invitation, users
                         """,
                         connection,
                         transaction))
        {
            select.Parameters.AddWithValue("invitation_id", invitationId);
            select.Parameters.AddWithValue("ss14_user_id", userId.UserId);
            await using var reader = await select.ExecuteReaderAsync(cancel);
            if (await reader.ReadAsync(cancel))
            {
                governanceUserId = reader.GetGuid(0);
                state = reader.GetString(1);
                expiresAt = reader.GetDateTime(2);
                rating = reader.GetInt32(3);
                found = true;
            }
            else
            {
                governanceUserId = Guid.Empty;
                state = string.Empty;
                expiresAt = default;
                rating = 0;
            }
        }

        if (!found)
            return new GovernanceDutyResponse(GovernanceDutyResponseStatus.Invalid, 0);

        if (state != "pending")
        {
            await transaction.RollbackAsync(cancel);
            return new GovernanceDutyResponse(GovernanceDutyResponseStatus.AlreadyHandled, rating);
        }

        if (expiresAt <= DateTime.UtcNow)
        {
            await SetInvitationStateAsync(connection, transaction, invitationId, "expired", cancel);
            rating = await AppendDutyRatingAsync(
                connection,
                transaction,
                governanceUserId,
                -expiryPenalty,
                "jury_invite_expired",
                invitationId,
                cancel);
            await AppendDutyAuditAsync(
                connection,
                transaction,
                "invitation.expired",
                governanceUserId,
                "invitation",
                invitationId.ToString(),
                new { purpose = "jury", penalty = expiryPenalty },
                cancel);
            await transaction.CommitAsync(cancel);
            return new GovernanceDutyResponse(GovernanceDutyResponseStatus.Expired, rating);
        }

        var targetState = choice switch
        {
            GovernanceDutyInvitationChoice.Accept => "accepted",
            GovernanceDutyInvitationChoice.Decline => "declined",
            _ => "recused",
        };
        await SetInvitationStateAsync(connection, transaction, invitationId, targetState, cancel);
        if (choice == GovernanceDutyInvitationChoice.Accept)
        {
            rating = await AppendDutyRatingAsync(
                connection,
                transaction,
                governanceUserId,
                acceptReward,
                "jury_invite_accept",
                invitationId,
                cancel);
        }
        else if (choice == GovernanceDutyInvitationChoice.Decline)
        {
            rating = await AppendDutyRatingAsync(
                connection,
                transaction,
                governanceUserId,
                -declinePenalty,
                "jury_invite_decline",
                invitationId,
                cancel);
        }

        await AppendDutyAuditAsync(
            connection,
            transaction,
            $"invitation.{targetState}",
            governanceUserId,
            "invitation",
            invitationId.ToString(),
            new
            {
                purpose = "jury",
                reward = choice == GovernanceDutyInvitationChoice.Accept ? acceptReward : 0,
                penalty = choice == GovernanceDutyInvitationChoice.Decline ? declinePenalty : 0,
            },
            cancel);
        await transaction.CommitAsync(cancel);
        return new GovernanceDutyResponse(
            choice switch
            {
                GovernanceDutyInvitationChoice.Accept => GovernanceDutyResponseStatus.Accepted,
                GovernanceDutyInvitationChoice.Decline => GovernanceDutyResponseStatus.Declined,
                _ => GovernanceDutyResponseStatus.Recused,
            },
            rating);
    }

    public async Task<IReadOnlyList<GovernanceDutyInvitation>> CreateGovernanceDutyInvitationsAsync(
        int roundId,
        IReadOnlyCollection<NetUserId> onlineObservers,
        int targetResponders,
        TimeSpan invitationLifetime,
        int expiryPenalty,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            roundId <= 0 || targetResponders <= 0)
        {
            return Array.Empty<GovernanceDutyInvitation>();
        }

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);

        await using (var dutyLock = new NpgsqlCommand(
                         "SELECT pg_advisory_xact_lock(hashtext('rucm-governance-duty'), @round_id)",
                         connection,
                         transaction))
        {
            dutyLock.Parameters.AddWithValue("round_id", roundId);
            await dutyLock.ExecuteNonQueryAsync(cancel);
        }

        // A new round invalidates every old invitation. A natural timeout also ends a duty
        // session and revokes its capabilities before staffing is calculated.
        await using (var closeOld = new NpgsqlCommand(
                         """
                         WITH ended AS (
                             UPDATE governance.duty_sessions
                             SET status = CASE
                                     WHEN round_id <> @round_id THEN 'round_ended'
                                     ELSE 'completed'
                                 END,
                                 ended_at = now(),
                                 version = version + 1
                             WHERE status = 'active'
                               AND (round_id <> @round_id OR expires_at <= now())
                             RETURNING id, user_id, round_id, status
                         ), duty_audited AS (
                             INSERT INTO governance.audit_events(
                                 event_type, actor_type, entity_type, entity_id, payload)
                             SELECT 'duty.ended', 'ss14_server', 'duty_session', id::text,
                                    jsonb_build_object('round_id', round_id, 'status', status)
                             FROM ended
                         ), revoked AS (
                             UPDATE governance.capability_grants AS capability_grant
                             SET revoked_at = now()
                             FROM ended AS duty
                             WHERE capability_grant.source_type = 'duty_session'
                               AND capability_grant.source_id = duty.id::text
                               AND capability_grant.revoked_at IS NULL
                             RETURNING capability_grant.id, capability_grant.source_id, capability_grant.capability
                         ), capability_audited AS (
                             INSERT INTO governance.audit_events(
                                 event_type, actor_type, entity_type, entity_id, payload)
                             SELECT 'capability.revoked', 'ss14_server', 'capability_grant', id::text,
                                    jsonb_build_object(
                                        'source_type', 'duty_session',
                                        'source_id', source_id,
                                        'capability', capability)
                             FROM revoked
                         )
                         SELECT count(*) FROM ended;

                         UPDATE governance.invitations
                         SET state = 'cancelled', responded_at = now(), version = version + 1
                         WHERE purpose = 'moderation_duty'
                           AND state = 'pending'
                           AND entity_id <> @round_id_text;
                         """,
                         connection,
                         transaction))
        {
            closeOld.Parameters.AddWithValue("round_id", roundId);
            closeOld.Parameters.AddWithValue("round_id_text", roundId.ToString());
            await closeOld.ExecuteNonQueryAsync(cancel);
        }

        var expired = new List<(long Id, Guid UserId)>();
        await using (var expire = new NpgsqlCommand(
                         """
                         UPDATE governance.invitations
                         SET state = 'expired', responded_at = now(), version = version + 1
                         WHERE purpose = 'moderation_duty'
                           AND state = 'pending'
                           AND expires_at <= now()
                         RETURNING id, user_id
                         """,
                         connection,
                         transaction))
        await using (var reader = await expire.ExecuteReaderAsync(cancel))
        {
            while (await reader.ReadAsync(cancel))
                expired.Add((reader.GetInt64(0), reader.GetGuid(1)));
        }

        foreach (var invitation in expired)
        {
            await AppendDutyRatingAsync(
                connection,
                transaction,
                invitation.UserId,
                -expiryPenalty,
                "moderation_invite_expired",
                invitation.Id,
                cancel);
            await AppendDutyAuditAsync(
                connection,
                transaction,
                "invitation.expired",
                invitation.UserId,
                "invitation",
                invitation.Id.ToString(),
                new { penalty = expiryPenalty },
                cancel);
        }

        if (onlineObservers.Count == 0)
        {
            await transaction.CommitAsync(cancel);
            return Array.Empty<GovernanceDutyInvitation>();
        }

        var onlineIds = new Guid[onlineObservers.Count];
        var index = 0;
        foreach (var observer in onlineObservers)
            onlineIds[index++] = observer.UserId;

        // Keep identity rows in sync with the game's authoritative Discord linkage.
        await using (var syncUsers = new NpgsqlCommand(
                         """
                         INSERT INTO governance.users(ss14_user_id, discord_user_id)
                         SELECT linked.player_id, linked.discord_id
                         FROM rmc_linked_accounts AS linked
                         WHERE linked.player_id = ANY(@online_users)
                         ON CONFLICT (ss14_user_id) DO UPDATE
                         SET discord_user_id = excluded.discord_user_id, updated_at = now();

                         INSERT INTO governance.qualifications(user_id, track, level)
                         SELECT users.id, 'moderation', 0
                         FROM governance.users AS users
                         WHERE users.ss14_user_id = ANY(@online_users)
                         ON CONFLICT (user_id, track) DO NOTHING;
                         """,
                         connection,
                         transaction))
        {
            syncUsers.Parameters.AddWithValue(
                "online_users",
                NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                onlineIds);
            await syncUsers.ExecuteNonQueryAsync(cancel);
        }

        int occupied;
        await using (var staffing = new NpgsqlCommand(
                         """
                         SELECT
                             (SELECT count(*) FROM governance.duty_sessions
                              WHERE round_id = @round_id AND status = 'active'
                                AND expires_at > now())
                           + (SELECT count(*) FROM governance.invitations
                              WHERE purpose = 'moderation_duty' AND entity_id = @round_id_text
                                AND state = 'pending' AND expires_at > now())
                         """,
                         connection,
                         transaction))
        {
            staffing.Parameters.AddWithValue("round_id", roundId);
            staffing.Parameters.AddWithValue("round_id_text", roundId.ToString());
            occupied = Convert.ToInt32(await staffing.ExecuteScalarAsync(cancel));
        }

        var slots = Math.Max(0, targetResponders - occupied);
        if (slots == 0)
        {
            await transaction.CommitAsync(cancel);
            return Array.Empty<GovernanceDutyInvitation>();
        }

        var candidates = new List<(Guid GovernanceId, Guid Ss14Id)>();
        await using (var select = new NpgsqlCommand(
                         """
                         SELECT users.id, users.ss14_user_id
                         FROM governance.users AS users
                         JOIN governance.qualifications AS qualification
                           ON qualification.user_id = users.id
                          AND qualification.track = 'moderation'
                          AND qualification.level >= 1
                         WHERE users.ss14_user_id = ANY(@online_users)
                           AND NOT users.is_governance_suspended
                           AND NOT EXISTS (
                               SELECT 1 FROM governance.conflicts AS conflict
                               WHERE conflict.user_id = users.id
                                 AND (conflict.ends_at IS NULL OR conflict.ends_at > now())
                                 AND (conflict.entity_type IS NULL OR
                                      (conflict.entity_type = 'round' AND conflict.entity_id = @round_id_text))
                           )
                           AND NOT EXISTS (
                               SELECT 1 FROM governance.duty_sessions AS duty
                               WHERE duty.user_id = users.id AND duty.status = 'active'
                           )
                           AND NOT EXISTS (
                               SELECT 1 FROM governance.event_sessions AS event
                               WHERE event.director_user_id = users.id AND event.status = 'active' AND event.expires_at > now()
                           )
                           AND NOT EXISTS (
                               SELECT 1 FROM governance.service_assignments AS assignment
                               WHERE assignment.user_id = users.id AND assignment.track = 'moderation'
                                 AND assignment.assigned_at > now() - interval '24 hours'
                           )
                           AND NOT EXISTS (
                               SELECT 1 FROM governance.invitations AS invitation
                               WHERE invitation.user_id = users.id
                                 AND invitation.state = 'pending' AND invitation.expires_at > now()
                           )
                           AND NOT EXISTS (
                               SELECT 1 FROM server_ban AS ban
                               WHERE ban.player_user_id = users.ss14_user_id AND NOT ban.hidden
                                 AND (ban.expiration_time IS NULL OR ban.expiration_time > now())
                                 AND NOT EXISTS (SELECT 1 FROM server_unban AS unban WHERE unban.ban_id = ban.server_ban_id)
                           )
                           AND NOT EXISTS (
                               SELECT 1 FROM server_role_ban AS ban
                               WHERE ban.player_user_id = users.ss14_user_id AND NOT ban.hidden
                                 AND (ban.expiration_time IS NULL OR ban.expiration_time > now())
                                 AND NOT EXISTS (SELECT 1 FROM server_role_unban AS unban WHERE unban.ban_id = ban.server_role_ban_id)
                           )
                         ORDER BY random()
                         LIMIT @slots
                         FOR UPDATE OF users SKIP LOCKED
                         """,
                         connection,
                         transaction))
        {
            select.Parameters.AddWithValue(
                "online_users",
                NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                onlineIds);
            select.Parameters.AddWithValue("round_id_text", roundId.ToString());
            select.Parameters.AddWithValue("slots", slots);
            await using var reader = await select.ExecuteReaderAsync(cancel);
            while (await reader.ReadAsync(cancel))
                candidates.Add((reader.GetGuid(0), reader.GetGuid(1)));
        }

        var result = new List<GovernanceDutyInvitation>(candidates.Count);
        foreach (var candidate in candidates)
        {
            long invitationId;
            DateTime expiresAt;
            await using (var insert = new NpgsqlCommand(
                             """
                             INSERT INTO governance.invitations(
                                 user_id, entity_type, entity_id, purpose, state,
                                 expires_at, idempotency_key)
                             VALUES (
                                 @user_id, 'round', @round_id, 'moderation_duty', 'pending',
                                 now() + @lifetime, @idempotency_key)
                             RETURNING id, expires_at
                             """,
                             connection,
                             transaction))
            {
                insert.Parameters.AddWithValue("user_id", candidate.GovernanceId);
                insert.Parameters.AddWithValue("round_id", roundId.ToString());
                insert.Parameters.AddWithValue("lifetime", invitationLifetime);
                insert.Parameters.AddWithValue(
                    "idempotency_key",
                    $"moderation-duty-invite:{roundId}:{candidate.Ss14Id}");
                await using var reader = await insert.ExecuteReaderAsync(cancel);
                await reader.ReadAsync(cancel);
                invitationId = reader.GetInt64(0);
                expiresAt = reader.GetDateTime(1);
            }

            await AppendDutyAuditAsync(
                connection,
                transaction,
                "invitation.created",
                candidate.GovernanceId,
                "invitation",
                invitationId.ToString(),
                new { purpose = "moderation_duty", round_id = roundId },
                cancel);
            result.Add(new GovernanceDutyInvitation(
                invitationId,
                new NetUserId(candidate.Ss14Id),
                roundId,
                new DateTimeOffset(expiresAt)));
        }

        await transaction.CommitAsync(cancel);
        return result;
    }

    public async Task<GovernanceDutyResponse> RespondGovernanceDutyInvitationAsync(
        long invitationId,
        NetUserId userId,
        int roundId,
        GovernanceDutyInvitationChoice choice,
        TimeSpan sessionLifetime,
        int acceptReward,
        int declinePenalty,
        int expiryPenalty,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase))
            return new GovernanceDutyResponse(GovernanceDutyResponseStatus.Invalid, 0);

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);

        await using (var invitationLock = new NpgsqlCommand(
                         "SELECT pg_advisory_xact_lock(hashtextextended('rucm-duty-invitation', @invitation_id))",
                         connection,
                         transaction))
        {
            invitationLock.Parameters.AddWithValue("invitation_id", invitationId);
            await invitationLock.ExecuteNonQueryAsync(cancel);
        }

        Guid governanceUserId;
        string state;
        DateTime expiresAt;
        short qualification;
        int rating;
        var found = false;
        await using (var select = new NpgsqlCommand(
                         """
                         SELECT invitation.user_id, invitation.state, invitation.expires_at,
                                qualification.level, users.civic_rating_cache
                         FROM governance.invitations AS invitation
                         JOIN governance.users AS users ON users.id = invitation.user_id
                         JOIN governance.qualifications AS qualification
                           ON qualification.user_id = users.id AND qualification.track = 'moderation'
                         WHERE invitation.id = @invitation_id
                           AND invitation.purpose = 'moderation_duty'
                           AND invitation.entity_id = @round_id
                           AND users.ss14_user_id = @ss14_user_id
                           AND NOT users.is_governance_suspended
                           AND qualification.level >= 1
                         FOR UPDATE OF invitation, users
                         """,
                         connection,
                         transaction))
        {
            select.Parameters.AddWithValue("invitation_id", invitationId);
            select.Parameters.AddWithValue("round_id", roundId.ToString());
            select.Parameters.AddWithValue("ss14_user_id", userId.UserId);
            await using var reader = await select.ExecuteReaderAsync(cancel);
            if (await reader.ReadAsync(cancel))
            {
                governanceUserId = reader.GetGuid(0);
                state = reader.GetString(1);
                expiresAt = reader.GetDateTime(2);
                qualification = reader.GetInt16(3);
                rating = reader.GetInt32(4);
                found = true;
            }
            else
            {
                governanceUserId = Guid.Empty;
                state = string.Empty;
                expiresAt = default;
                qualification = 0;
                rating = 0;
            }
        }

        if (!found)
            return new GovernanceDutyResponse(GovernanceDutyResponseStatus.Invalid, 0);

        if (state != "pending")
        {
            await transaction.RollbackAsync(cancel);
            return new GovernanceDutyResponse(GovernanceDutyResponseStatus.AlreadyHandled, rating);
        }

        if (expiresAt <= DateTime.UtcNow)
        {
            await SetInvitationStateAsync(connection, transaction, invitationId, "expired", cancel);
            rating = await AppendDutyRatingAsync(
                connection,
                transaction,
                governanceUserId,
                -expiryPenalty,
                "moderation_invite_expired",
                invitationId,
                cancel);
            await AppendDutyAuditAsync(
                connection,
                transaction,
                "invitation.expired",
                governanceUserId,
                "invitation",
                invitationId.ToString(),
                new { penalty = expiryPenalty },
                cancel);
            await transaction.CommitAsync(cancel);
            return new GovernanceDutyResponse(GovernanceDutyResponseStatus.Expired, rating);
        }

        if (choice is GovernanceDutyInvitationChoice.Decline or GovernanceDutyInvitationChoice.Recuse)
        {
            var stateValue = choice == GovernanceDutyInvitationChoice.Decline ? "declined" : "recused";
            await SetInvitationStateAsync(connection, transaction, invitationId, stateValue, cancel);
            if (choice == GovernanceDutyInvitationChoice.Decline)
            {
                rating = await AppendDutyRatingAsync(
                    connection,
                    transaction,
                    governanceUserId,
                    -declinePenalty,
                    "moderation_invite_decline",
                    invitationId,
                    cancel);
            }

            await AppendDutyAuditAsync(
                connection,
                transaction,
                $"invitation.{stateValue}",
                governanceUserId,
                "invitation",
                invitationId.ToString(),
                new { penalty = choice == GovernanceDutyInvitationChoice.Decline ? declinePenalty : 0 },
                cancel);
            await transaction.CommitAsync(cancel);
            return new GovernanceDutyResponse(
                choice == GovernanceDutyInvitationChoice.Decline
                    ? GovernanceDutyResponseStatus.Declined
                    : GovernanceDutyResponseStatus.Recused,
                rating);
        }

        await SetInvitationStateAsync(connection, transaction, invitationId, "accepted", cancel);
        rating = await AppendDutyRatingAsync(
            connection,
            transaction,
            governanceUserId,
            acceptReward,
            "moderation_duty_accept",
            invitationId,
            cancel);

        long dutyId;
        DateTime dutyExpiresAt;
        await using (var createDuty = new NpgsqlCommand(
                         """
                         INSERT INTO governance.duty_sessions(
                             user_id, round_id, started_at, expires_at, status,
                             qualification_at_start, observer_confirmed, idempotency_key)
                         VALUES (
                             @user_id, @round_id, now(), now() + @lifetime, 'active',
                             @qualification, true, @idempotency_key)
                         RETURNING id, expires_at
                         """,
                         connection,
                         transaction))
        {
            createDuty.Parameters.AddWithValue("user_id", governanceUserId);
            createDuty.Parameters.AddWithValue("round_id", roundId);
            createDuty.Parameters.AddWithValue("lifetime", sessionLifetime);
            createDuty.Parameters.AddWithValue("qualification", qualification);
            createDuty.Parameters.AddWithValue("idempotency_key", $"moderation-duty:{roundId}:{userId.UserId}");
            await using var reader = await createDuty.ExecuteReaderAsync(cancel);
            await reader.ReadAsync(cancel);
            dutyId = reader.GetInt64(0);
            dutyExpiresAt = reader.GetDateTime(1);
        }

        // Rotation cooldown starts only after the candidate actually accepts and begins service.
        // Declines, recuses, delivery failures and expired invitations must not consume the 24h slot.
        await using (var assignment = new NpgsqlCommand(
                         """
                         INSERT INTO governance.service_assignments(
                             user_id, track, entity_type, entity_id, assigned_at)
                         VALUES (@user_id, 'moderation', 'round', @round_id, now())
                         ON CONFLICT (user_id, track, entity_type, entity_id) DO NOTHING
                         """,
                         connection,
                         transaction))
        {
            assignment.Parameters.AddWithValue("user_id", governanceUserId);
            assignment.Parameters.AddWithValue("round_id", roundId.ToString());
            await assignment.ExecuteNonQueryAsync(cancel);
        }

        await using (var capability = new NpgsqlCommand(
                         """
                         WITH available(capability, minimum_qualification) AS (
                             VALUES
                                 ('moderation.ahelp', 1),
                                 ('moderation.freeze', 1),
                                 ('moderation.request_explanation', 1),
                                 ('moderation.view_logs', 1),
                                 ('moderation.round_remove', 2)
                         ), issued AS (
                             INSERT INTO governance.capability_grants(
                                 user_id, capability, source_type, source_id, scope,
                                 issued_at, expires_at, idempotency_key)
                             SELECT @user_id, available.capability, 'duty_session', @source_id,
                                    jsonb_build_object('round_id', @round_id), now(), @expires_at,
                                    'moderation-duty:' || @source_id || ':' || available.capability
                             FROM available
                             WHERE @qualification >= available.minimum_qualification
                             RETURNING id, capability
                         )
                         INSERT INTO governance.audit_events(
                             event_type, actor_type, actor_id, entity_type, entity_id, payload)
                         SELECT 'capability.issued', 'ss14_server', @user_id::text,
                                'capability_grant', issued.id::text,
                                jsonb_build_object('source_type', 'duty_session', 'source_id', @source_id,
                                                   'capability', issued.capability, 'round_id', @round_id)
                         FROM issued
                         """,
                         connection,
                         transaction))
        {
            capability.Parameters.AddWithValue("user_id", governanceUserId);
            capability.Parameters.AddWithValue("source_id", dutyId.ToString());
            capability.Parameters.AddWithValue("round_id", roundId);
            capability.Parameters.AddWithValue("expires_at", dutyExpiresAt);
            capability.Parameters.AddWithValue("qualification", qualification);
            await capability.ExecuteNonQueryAsync(cancel);
        }

        await AppendDutyAuditAsync(
            connection,
            transaction,
            "invitation.accepted",
            governanceUserId,
            "invitation",
            invitationId.ToString(),
            new { reward = acceptReward, round_id = roundId },
            cancel);
        await AppendDutyAuditAsync(
            connection,
            transaction,
            "duty.started",
            governanceUserId,
            "duty_session",
            dutyId.ToString(),
            new { round_id = roundId, qualification },
            cancel);

        await transaction.CommitAsync(cancel);
        var duty = new GovernanceDutySession(
            dutyId,
            governanceUserId,
            userId,
            roundId,
            new DateTimeOffset(dutyExpiresAt));
        return new GovernanceDutyResponse(GovernanceDutyResponseStatus.Accepted, rating, duty);
    }

    private static async Task SetInvitationStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long invitationId,
        string state,
        CancellationToken cancel)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE governance.invitations
            SET state = @state, responded_at = now(), version = version + 1
            WHERE id = @invitation_id AND state = 'pending'
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("invitation_id", invitationId);
        await command.ExecuteNonQueryAsync(cancel);
    }

    private static async Task<int> AppendDutyRatingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid governanceUserId,
        int amount,
        string reason,
        long invitationId,
        CancellationToken cancel)
    {
        await using (var command = new NpgsqlCommand(
            """
            SELECT governance.append_rating_entry(
                @user_id, @amount, @reason, 'invitation', @entity_id,
                'ss14_server', NULL, @idempotency_key,
                jsonb_build_object('invitation_id', @invitation_id));

            INSERT INTO governance.audit_events(
                event_type, actor_type, actor_id, entity_type, entity_id, payload)
            VALUES (
                'rating.changed', 'ss14_server', @user_id::text,
                'invitation', @entity_id,
                jsonb_build_object('amount', @amount, 'reason', @reason));
            """,
            connection,
            transaction))
        {
            command.Parameters.AddWithValue("user_id", governanceUserId);
            command.Parameters.AddWithValue("amount", amount);
            command.Parameters.AddWithValue("reason", reason);
            command.Parameters.AddWithValue("entity_id", invitationId.ToString());
            command.Parameters.AddWithValue("invitation_id", invitationId);
            command.Parameters.AddWithValue("idempotency_key", $"moderation-duty-invitation:{invitationId}:{reason}");
            await command.ExecuteNonQueryAsync(cancel);
        }

        await using var ratingCommand = new NpgsqlCommand(
            "SELECT civic_rating_cache FROM governance.users WHERE id = @user_id",
            connection,
            transaction);
        ratingCommand.Parameters.AddWithValue("user_id", governanceUserId);
        var result = await ratingCommand.ExecuteScalarAsync(cancel);
        return Convert.ToInt32(result);
    }

    private static async Task AppendDutyAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string eventType,
        Guid governanceUserId,
        string entityType,
        string entityId,
        object payload,
        CancellationToken cancel)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO governance.audit_events(
                event_type, actor_type, actor_id, entity_type, entity_id, payload)
            VALUES (
                @event_type, 'ss14_server', @actor_id, @entity_type, @entity_id,
                CAST(@payload AS jsonb))
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("actor_id", governanceUserId.ToString());
        command.Parameters.AddWithValue("entity_type", entityType);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("payload", System.Text.Json.JsonSerializer.Serialize(payload));
        await command.ExecuteNonQueryAsync(cancel);
    }
}
