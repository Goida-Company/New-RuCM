using System;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.CCVar;
using Npgsql;
using Robust.Shared.Network;

namespace Content.Server.Database;

public sealed record GovernanceCourtEscalationInfo(long CourtCaseId, bool Created);

public partial interface IServerDbManager
{
    Task<GovernanceCourtEscalationInfo?> EscalateGovernanceIncidentToCourtAsync(
        long ticketId,
        NetUserId responder,
        int roundId,
        string reason,
        CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public async Task<GovernanceCourtEscalationInfo?> EscalateGovernanceIncidentToCourtAsync(
        long ticketId,
        NetUserId responder,
        int roundId,
        string reason,
        CancellationToken cancel = default)
    {
        if (!_cfg.GetCVar(CCVars.DatabaseEngine).Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            ticketId <= 0 || roundId <= 0)
            return null;

        reason = reason.Trim();
        if (reason.Length is < 10 or > 1500)
            return null;

        await using var connection = CreateGovernanceConnection();
        await connection.OpenAsync(cancel);
        await using var transaction = await connection.BeginTransactionAsync(cancel);

        // One court escalation per source ticket, even if the UI is clicked twice or two refreshes race.
        await using (var incidentLock = new NpgsqlCommand(
                         "SELECT pg_advisory_xact_lock(hashtextextended('rucm-incident-court', @ticket_id))",
                         connection,
                         transaction))
        {
            incidentLock.Parameters.AddWithValue("ticket_id", ticketId);
            await incidentLock.ExecuteNonQueryAsync(cancel);
        }

        Guid actorId;
        await using (var actor = new NpgsqlCommand(
                         """
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
                           AND duty.started_at <= now()
                           AND duty.expires_at > now()
                           AND capability_grant.capability = 'moderation.ahelp'
                           AND capability_grant.issued_at <= now()
                           AND capability_grant.expires_at > now()
                           AND capability_grant.revoked_at IS NULL
                           AND capability_grant.scope @> jsonb_build_object('round_id', @round_id)
                         ORDER BY capability_grant.issued_at DESC
                         LIMIT 1
                         """,
                         connection,
                         transaction))
        {
            actor.Parameters.AddWithValue("responder", responder.UserId);
            actor.Parameters.AddWithValue("round_id", roundId);
            var result = await actor.ExecuteScalarAsync(cancel);
            if (result is not Guid value)
            {
                await transaction.RollbackAsync(cancel);
                return null;
            }

            actorId = value;
        }

        // Validate and lock the claimed ticket before creating any synthetic Governance identity.
        Guid reporterSs14UserId;
        await using (var ticket = new NpgsqlCommand(
                         """
                         SELECT reporter_ss14_user_id
                         FROM governance.ahelp_tickets
                         WHERE id = @ticket_id
                           AND round_id = @round_id
                           AND claimed_by_user_id = @actor_id
                         FOR UPDATE
                         """,
                         connection,
                         transaction))
        {
            ticket.Parameters.AddWithValue("ticket_id", ticketId);
            ticket.Parameters.AddWithValue("round_id", roundId);
            ticket.Parameters.AddWithValue("actor_id", actorId);
            var result = await ticket.ExecuteScalarAsync(cancel);
            if (result is not Guid value)
            {
                await transaction.RollbackAsync(cancel);
                return null;
            }

            reporterSs14UserId = value;
        }

        // AHelp is intentionally usable without a Discord link. Create or resolve an upgradeable
        // SS14-only identity first, then bind the ticket in a separate command. Keeping these operations
        // sequential is important: PostgreSQL data-modifying CTEs share one snapshot and an UPDATE cannot
        // reliably discover a row inserted by a sibling CTE through the base table.
        Guid ensuredClaimantUserId;
        await using (var ensureReporter = new NpgsqlCommand(
                         """
                         INSERT INTO governance.users(ss14_user_id, discord_user_id, created_at, updated_at)
                         VALUES (
                             @reporter_ss14_user_id,
                             -((('x' || substr(md5(@reporter_ss14_user_id::text), 1, 15))::bit(60)::bigint) + 1),
                             now(),
                             now())
                         ON CONFLICT (ss14_user_id) DO UPDATE
                         SET updated_at = now()
                         RETURNING id
                         """,
                         connection,
                         transaction))
        {
            ensureReporter.Parameters.AddWithValue("reporter_ss14_user_id", reporterSs14UserId);
            var result = await ensureReporter.ExecuteScalarAsync(cancel);
            if (result is not Guid value)
            {
                await transaction.RollbackAsync(cancel);
                return null;
            }

            ensuredClaimantUserId = value;
        }

        await using (var bindReporter = new NpgsqlCommand(
                         """
                         UPDATE governance.ahelp_tickets
                         SET reporter_user_id = @claimant_user_id,
                             updated_at = now()
                         WHERE id = @ticket_id
                           AND round_id = @round_id
                           AND reporter_ss14_user_id = @reporter_ss14_user_id
                           AND claimed_by_user_id = @actor_id
                         """,
                         connection,
                         transaction))
        {
            bindReporter.Parameters.AddWithValue("claimant_user_id", ensuredClaimantUserId);
            bindReporter.Parameters.AddWithValue("ticket_id", ticketId);
            bindReporter.Parameters.AddWithValue("round_id", roundId);
            bindReporter.Parameters.AddWithValue("reporter_ss14_user_id", reporterSs14UserId);
            bindReporter.Parameters.AddWithValue("actor_id", actorId);
            if (await bindReporter.ExecuteNonQueryAsync(cancel) != 1)
            {
                await transaction.RollbackAsync(cancel);
                return null;
            }
        }

        long incidentId = 0;
        Guid targetUserId = Guid.Empty;
        Guid claimantUserId = Guid.Empty;
        Guid targetSs14UserId = Guid.Empty;
        Guid claimantSs14UserId = Guid.Empty;
        string incidentSummary = string.Empty;
        string incidentType = string.Empty;
        string targetName = string.Empty;
        string claimantName = string.Empty;
        string targetCharacterName = string.Empty;
        long? existingCourtCaseId = null;

        // Resolve every prerequisite explicitly instead of hiding all failures inside one giant CTE.
        await using (var source = new NpgsqlCommand(
                         """
                         SELECT live.id,
                                live.target_user_id,
                                ticket.reporter_user_id,
                                live.summary,
                                live.type,
                                live.court_case_id,
                                COALESCE(live.target_character_name, ''),
                                target.ss14_user_id,
                                COALESCE(target_player.last_seen_user_name, target.ss14_user_id::text),
                                claimant.ss14_user_id,
                                COALESCE(claimant_player.last_seen_user_name, claimant.ss14_user_id::text)
                         FROM governance.ahelp_tickets AS ticket
                         JOIN governance.live_incidents AS live ON live.ahelp_ticket_id = ticket.id
                         JOIN governance.users AS target ON target.id = live.target_user_id
                         JOIN governance.users AS claimant ON claimant.id = ticket.reporter_user_id
                         LEFT JOIN player AS target_player ON target_player.user_id = target.ss14_user_id
                         LEFT JOIN player AS claimant_player ON claimant_player.user_id = claimant.ss14_user_id
                         WHERE ticket.id = @ticket_id
                           AND ticket.round_id = @round_id
                           AND ticket.claimed_by_user_id = @actor_id
                           AND live.round_id = @round_id
                           AND live.status IN ('active', 'contained', 'escalated_to_court')
                         LIMIT 1
                         FOR UPDATE OF ticket, live
                         """,
                         connection,
                         transaction))
        {
            source.Parameters.AddWithValue("ticket_id", ticketId);
            source.Parameters.AddWithValue("round_id", roundId);
            source.Parameters.AddWithValue("actor_id", actorId);

            await using var reader = await source.ExecuteReaderAsync(cancel);
            if (!await reader.ReadAsync(cancel))
            {
                await reader.DisposeAsync();
                await transaction.RollbackAsync(cancel);
                return null;
            }

            incidentId = reader.GetInt64(0);
            targetUserId = reader.GetGuid(1);
            claimantUserId = reader.GetGuid(2);
            incidentSummary = reader.GetString(3);
            incidentType = reader.GetString(4);
            existingCourtCaseId = reader.IsDBNull(5) ? null : reader.GetInt64(5);
            targetCharacterName = reader.GetString(6);
            targetSs14UserId = reader.GetGuid(7);
            targetName = reader.GetString(8);
            claimantSs14UserId = reader.GetGuid(9);
            claimantName = reader.GetString(10);
        }

        if (claimantUserId == targetUserId)
        {
            await transaction.RollbackAsync(cancel);
            return null;
        }

        // Idempotent retry: a case may already have been created before a client refresh/retry.
        if (existingCourtCaseId is { } existingId)
        {
            await using var restoreState = new NpgsqlCommand(
                """
                UPDATE governance.live_incidents
                SET status = 'escalated_to_court', reporter_user_id = @claimant_user_id
                WHERE id = @incident_id;

                UPDATE governance.ahelp_tickets
                SET status = 'escalated_to_court', updated_at = now()
                WHERE id = @ticket_id;
                """,
                connection,
                transaction);
            restoreState.Parameters.AddWithValue("claimant_user_id", claimantUserId);
            restoreState.Parameters.AddWithValue("incident_id", incidentId);
            restoreState.Parameters.AddWithValue("ticket_id", ticketId);
            await restoreState.ExecuteNonQueryAsync(cancel);
            await transaction.CommitAsync(cancel);
            return new GovernanceCourtEscalationInfo(existingId, false);
        }

        var characterPart = string.IsNullOrWhiteSpace(targetCharacterName)
            ? string.Empty
            : $" • персонаж: {targetCharacterName}";
        var caseSummary =
            $"LiveIncident #{incidentId} ({incidentType})\n" +
            $"Заявитель: {claimantName} • SS14 {claimantSs14UserId}\n" +
            $"Ответчик: {targetName}{characterPart} • SS14 {targetSs14UserId}\n" +
            $"{incidentSummary}\n\n" +
            $"Передано дежурным в Community Court: {reason}";
        if (caseSummary.Length > 1500)
            caseSummary = caseSummary[..1500];

        long courtCaseId;
        await using (var createCase = new NpgsqlCommand(
                         """
                         INSERT INTO governance.court_cases(
                             claimant_user_id, defendant_user_id, round_id, summary,
                             status, filed_at, defense_deadline, version)
                         VALUES (
                             @claimant_user_id, @target_user_id, @round_id, @summary,
                             'defense', now(), now() + interval '48 hours', 0)
                         RETURNING id
                         """,
                         connection,
                         transaction))
        {
            createCase.Parameters.AddWithValue("claimant_user_id", claimantUserId);
            createCase.Parameters.AddWithValue("target_user_id", targetUserId);
            createCase.Parameters.AddWithValue("round_id", roundId);
            createCase.Parameters.AddWithValue("summary", caseSummary);
            courtCaseId = Convert.ToInt64(await createCase.ExecuteScalarAsync(cancel));
        }

        var complaintBody = $"{incidentSummary}\n\nОснование передачи в суд: {reason}";
        if (complaintBody.Length > 3000)
            complaintBody = complaintBody[..3000];
        var evidenceReference =
            $"RUCM Governance: LiveIncident #{incidentId}, AHelp #{ticketId}, ответчик {targetName}" +
            (string.IsNullOrWhiteSpace(targetCharacterName) ? string.Empty : $" / {targetCharacterName}") +
            $" (SS14 {targetSs14UserId}).";

        await using (var complaint = new NpgsqlCommand(
                         """
                         INSERT INTO governance.court_statements(
                             case_id, author_user_id, kind, body, evidence_reference, created_at)
                         VALUES (@case_id, @claimant_user_id, 'complaint', @body, @evidence, now())
                         """,
                         connection,
                         transaction))
        {
            complaint.Parameters.AddWithValue("case_id", courtCaseId);
            complaint.Parameters.AddWithValue("claimant_user_id", claimantUserId);
            complaint.Parameters.AddWithValue("body", complaintBody);
            complaint.Parameters.AddWithValue("evidence", evidenceReference);
            await complaint.ExecuteNonQueryAsync(cancel);
        }

        await using (var participants = new NpgsqlCommand(
                         """
                         INSERT INTO governance.court_participants(case_id, user_id, role, added_at)
                         VALUES
                             (@case_id, @claimant_user_id, 'claimant', now()),
                             (@case_id, @target_user_id, 'defendant', now())
                         ON CONFLICT (case_id, user_id) DO NOTHING
                         """,
                         connection,
                         transaction))
        {
            participants.Parameters.AddWithValue("case_id", courtCaseId);
            participants.Parameters.AddWithValue("claimant_user_id", claimantUserId);
            participants.Parameters.AddWithValue("target_user_id", targetUserId);
            await participants.ExecuteNonQueryAsync(cancel);
        }

        await using (var link = new NpgsqlCommand(
                         """
                         UPDATE governance.live_incidents
                         SET status = 'escalated_to_court',
                             reporter_user_id = @claimant_user_id,
                             court_case_id = @case_id
                         WHERE id = @incident_id;

                         UPDATE governance.ahelp_tickets
                         SET status = 'escalated_to_court', updated_at = now()
                         WHERE id = @ticket_id;
                         """,
                         connection,
                         transaction))
        {
            link.Parameters.AddWithValue("claimant_user_id", claimantUserId);
            link.Parameters.AddWithValue("case_id", courtCaseId);
            link.Parameters.AddWithValue("incident_id", incidentId);
            link.Parameters.AddWithValue("ticket_id", ticketId);
            await link.ExecuteNonQueryAsync(cancel);
        }

        await using (var audit = new NpgsqlCommand(
                         """
                         INSERT INTO governance.audit_events(
                             event_type, actor_type, actor_id, target_type, target_id,
                             entity_type, entity_id, payload)
                         VALUES (
                             'incident.escalated_to_court', 'ss14_user', @responder,
                             'ss14_user', @target,
                             'court_case', @case_id,
                             jsonb_build_object(
                                 'round_id', @round_id,
                                 'ticket_id', @ticket_id,
                                 'incident_id', @incident_id,
                                 'reason', @reason,
                                 'claimant_ss14_user_id', @claimant,
                                 'target_name', @target_name,
                                 'target_character_name', @target_character_name))
                         """,
                         connection,
                         transaction))
        {
            audit.Parameters.AddWithValue("responder", responder.UserId.ToString());
            audit.Parameters.AddWithValue("target", targetSs14UserId.ToString());
            audit.Parameters.AddWithValue("case_id", courtCaseId.ToString());
            audit.Parameters.AddWithValue("round_id", roundId);
            audit.Parameters.AddWithValue("ticket_id", ticketId);
            audit.Parameters.AddWithValue("incident_id", incidentId);
            audit.Parameters.AddWithValue("reason", reason);
            audit.Parameters.AddWithValue("claimant", claimantSs14UserId);
            audit.Parameters.AddWithValue("target_name", targetName);
            audit.Parameters.AddWithValue("target_character_name", targetCharacterName);
            await audit.ExecuteNonQueryAsync(cancel);
        }

        await transaction.CommitAsync(cancel);
        return new GovernanceCourtEscalationInfo(courtCaseId, true);
    }
}
