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
    Task<IReadOnlyList<GovernanceDutyInvitation>> CreateGovernanceDutyInvitationsV2Async(
        int roundId,
        IReadOnlyCollection<NetUserId> onlineObservers,
        int targetResponders,
        TimeSpan invitationLifetime,
        CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    /// <summary>
    /// Reputation v2 candidate gate for the legacy transactional Duty creator.
    /// The old method still owns expiration, advisory locking, invitation creation, DutySession creation
    /// and capability issuance. This method only decides which already-eligible observers may reach it.
    /// </summary>
    public async Task<IReadOnlyList<GovernanceDutyInvitation>> CreateGovernanceDutyInvitationsV2Async(
        int roundId,
        IReadOnlyCollection<NetUserId> onlineObservers,
        int targetResponders,
        TimeSpan invitationLifetime,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            roundId <= 0 || targetResponders <= 0 || onlineObservers.Count == 0)
        {
            // Keep the old creator responsible for ending stale sessions/invitations even when there
            // is nobody to invite. Invitation responses are neutral in Reputation v2, hence zero.
            return await CreateGovernanceDutyInvitationsAsync(
                roundId,
                onlineObservers,
                targetResponders,
                invitationLifetime,
                0,
                cancel);
        }

        var onlineIds = onlineObservers.Select(value => value.UserId).ToArray();
        var roundIdText = roundId.ToString();
        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);

        int occupied;
        await using (var staffing = new NpgsqlCommand(
                         """
                         SELECT
                             (SELECT count(*) FROM governance.duty_sessions
                              WHERE round_id = @round_id AND status = 'active' AND expires_at > now())
                           + (SELECT count(*) FROM governance.invitations
                              WHERE purpose = 'moderation_duty' AND entity_id = @round_id_text
                                AND state = 'pending' AND expires_at > now())
                         """,
                         connection))
        {
            staffing.Parameters.AddWithValue("round_id", roundId);
            staffing.Parameters.AddWithValue("round_id_text", roundIdText);
            occupied = Convert.ToInt32(await staffing.ExecuteScalarAsync(cancel));
        }

        var slots = Math.Max(0, targetResponders - occupied);
        if (slots == 0)
        {
            return await CreateGovernanceDutyInvitationsAsync(
                roundId,
                Array.Empty<NetUserId>(),
                targetResponders,
                invitationLifetime,
                0,
                cancel);
        }

        var candidates = new List<DutyCandidate>();
        await using (var select = new NpgsqlCommand(
                         """
                         SELECT users.id,
                                users.ss14_user_id,
                                qualification.level,
                                COALESCE(track.alpha, 3.0),
                                COALESCE(track.beta, 3.0),
                                COALESCE(general.score, 500)
                         FROM governance.users AS users
                         JOIN governance.qualifications AS qualification
                           ON qualification.user_id = users.id
                          AND qualification.track = 'moderation'
                          AND qualification.level >= 1
                         JOIN governance.service_paths AS path
                           ON path.user_id = users.id
                          AND path.track = 'moderation'
                         LEFT JOIN governance.reputation_snapshots AS track
                           ON track.user_id = users.id
                          AND track.track = 'moderation'
                         LEFT JOIN governance.reputation_snapshots AS general
                           ON general.user_id = users.id
                          AND general.track = 'general'
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
                               WHERE duty.user_id = users.id
                                 AND duty.status = 'active'
                                 AND duty.expires_at > now()
                           )
                           AND NOT EXISTS (
                               SELECT 1 FROM governance.event_sessions AS event
                               WHERE event.director_user_id = users.id
                                 AND event.status = 'active'
                                 AND event.expires_at > now()
                           )
                           AND NOT EXISTS (
                               SELECT 1 FROM governance.service_assignments AS assignment
                               WHERE assignment.user_id = users.id
                                 AND assignment.track = 'moderation'
                                 AND assignment.assigned_at > now() - interval '24 hours'
                           )
                           AND NOT EXISTS (
                               SELECT 1 FROM governance.invitations AS invitation
                               WHERE invitation.user_id = users.id
                                 AND invitation.state = 'pending'
                                 AND invitation.expires_at > now()
                           )
                           AND NOT EXISTS (
                               SELECT 1 FROM server_ban AS ban
                               WHERE ban.player_user_id = users.ss14_user_id
                                 AND NOT ban.hidden
                                 AND (ban.expiration_time IS NULL OR ban.expiration_time > now())
                                 AND NOT EXISTS (
                                     SELECT 1 FROM server_unban AS unban
                                     WHERE unban.ban_id = ban.server_ban_id)
                           )
                           AND NOT EXISTS (
                               SELECT 1 FROM server_role_ban AS ban
                               WHERE ban.player_user_id = users.ss14_user_id
                                 AND NOT ban.hidden
                                 AND (ban.expiration_time IS NULL OR ban.expiration_time > now())
                                 AND NOT EXISTS (
                                     SELECT 1 FROM server_role_unban AS unban
                                     WHERE unban.ban_id = ban.server_role_ban_id)
                           )
                         """,
                         connection))
        {
            select.Parameters.AddWithValue(
                "online_users",
                NpgsqlDbType.Array | NpgsqlDbType.Uuid,
                onlineIds);
            select.Parameters.AddWithValue("round_id_text", roundIdText);
            await using var reader = await select.ExecuteReaderAsync(cancel);
            while (await reader.ReadAsync(cancel))
            {
                candidates.Add(new DutyCandidate(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetInt16(2),
                    reader.GetDouble(3),
                    reader.GetDouble(4),
                    reader.GetInt32(5)));
            }
        }

        if (candidates.Count == 0)
        {
            return await CreateGovernanceDutyInvitationsAsync(
                roundId,
                Array.Empty<NetUserId>(),
                targetResponders,
                invitationLifetime,
                0,
                cancel);
        }

        // Multiple requested slots are the top-k samples from one Thompson draw. This is the same
        // policy as the Discord candidate selector: Beta(track alpha,beta), then mild general and
        // qualification factors. Hard eligibility has already been applied above.
        var selected = candidates
            .Select(candidate => (Candidate: candidate, Priority: SampleDutyPriority(candidate)))
            .OrderByDescending(value => value.Priority)
            .Take(slots)
            .Select(value => new NetUserId(value.Candidate.Ss14UserId))
            .ToArray();

        return await CreateGovernanceDutyInvitationsAsync(
            roundId,
            selected,
            targetResponders,
            invitationLifetime,
            0,
            cancel);
    }

    private static double SampleDutyPriority(DutyCandidate candidate)
    {
        var thompson = SampleBeta(candidate.Alpha, candidate.Beta);
        var normalizedGeneral = Math.Clamp(candidate.GeneralScore, 0, 1000) / 1000.0;
        var generalFactor = 0.85 + 0.30 * normalizedGeneral;
        var qualificationFactor = 1.0 + 0.03 * Math.Max(0, candidate.QualificationLevel - 1);
        return thompson * generalFactor * qualificationFactor;
    }

    private static double SampleBeta(double alpha, double beta)
    {
        alpha = alpha > 0 ? alpha : 3.0;
        beta = beta > 0 ? beta : 3.0;
        var x = SampleGamma(alpha);
        var y = SampleGamma(beta);
        return x / (x + y);
    }

    private static double SampleGamma(double shape)
    {
        if (shape < 1.0)
        {
            var u = Math.Max(double.Epsilon, Random.Shared.NextDouble());
            return SampleGamma(shape + 1.0) * Math.Pow(u, 1.0 / shape);
        }

        var d = shape - 1.0 / 3.0;
        var c = 1.0 / Math.Sqrt(9.0 * d);
        while (true)
        {
            var x = StandardNormal();
            var v = 1.0 + c * x;
            if (v <= 0)
                continue;
            v = v * v * v;
            var u = Random.Shared.NextDouble();
            if (u < 1.0 - 0.0331 * x * x * x * x)
                return d * v;
            if (Math.Log(u) < 0.5 * x * x + d * (1.0 - v + Math.Log(v)))
                return d * v;
        }
    }

    private static double StandardNormal()
    {
        var u1 = Math.Max(double.Epsilon, Random.Shared.NextDouble());
        var u2 = Random.Shared.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private sealed record DutyCandidate(
        Guid GovernanceUserId,
        Guid Ss14UserId,
        short QualificationLevel,
        double Alpha,
        double Beta,
        int GeneralScore);
}
