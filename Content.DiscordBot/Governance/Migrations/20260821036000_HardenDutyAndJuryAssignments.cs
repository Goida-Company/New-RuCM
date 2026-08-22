using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260821036000_HardenDutyAndJuryAssignments")]
public sealed class HardenDutyAndJuryAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- Existing active Duty sessions must not survive a real departure from the moderation path.
            -- Audit first, then revoke grants, then close the Duty session so every subsequent server
            -- authorization fails closed immediately.
            INSERT INTO governance.audit_events(
                event_type, actor_type, entity_type, entity_id, payload)
            SELECT 'moderation.duty_revoked_path_removed',
                   'system',
                   'duty_session',
                   duty.id::text,
                   jsonb_build_object(
                       'user_id', duty.user_id,
                       'reason', 'moderation service path is not selected',
                       'source', 'migration_backfill')
            FROM governance.duty_sessions AS duty
            WHERE duty.status = 'active'
              AND NOT EXISTS (
                  SELECT 1
                  FROM governance.service_paths AS path
                  WHERE path.user_id = duty.user_id
                    AND path.track = 'moderation');

            UPDATE governance.capability_grants AS capability_grant
            SET revoked_at = COALESCE(capability_grant.revoked_at, now())
            WHERE capability_grant.source_type = 'duty_session'
              AND capability_grant.revoked_at IS NULL
              AND EXISTS (
                  SELECT 1
                  FROM governance.duty_sessions AS duty
                  WHERE duty.id::text = capability_grant.source_id
                    AND duty.user_id = capability_grant.user_id
                    AND duty.status = 'active'
                    AND NOT EXISTS (
                        SELECT 1
                        FROM governance.service_paths AS path
                        WHERE path.user_id = duty.user_id
                          AND path.track = 'moderation'));

            UPDATE governance.duty_sessions AS duty
            SET status = 'revoked',
                ended_at = COALESCE(duty.ended_at, now()),
                version = duty.version + 1
            WHERE duty.status = 'active'
              AND NOT EXISTS (
                  SELECT 1
                  FROM governance.service_paths AS path
                  WHERE path.user_id = duty.user_id
                    AND path.track = 'moderation');

            -- Replace the immediate path trigger with a deferred invariant. This matters when a user
            -- only swaps primary/secondary slots: the final transaction still contains moderation and
            -- must not revoke an otherwise valid Duty session.
            CREATE OR REPLACE FUNCTION governance.demote_moderation_qualification_after_path_change()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $governance$
            DECLARE
                affected_user uuid;
            BEGIN
                affected_user := OLD.user_id;
                IF OLD.track <> 'moderation' THEN
                    RETURN NULL;
                END IF;

                -- Evaluate the final transaction state. If moderation still exists in another slot,
                -- this was only a slot move and no authority is revoked.
                IF EXISTS (
                    SELECT 1
                    FROM governance.service_paths AS path
                    WHERE path.user_id = affected_user
                      AND path.track = 'moderation') THEN
                    RETURN NULL;
                END IF;

                INSERT INTO governance.audit_events(
                    event_type, actor_type, entity_type, entity_id, payload)
                SELECT 'moderation.duty_revoked_path_removed',
                       'system',
                       'duty_session',
                       duty.id::text,
                       jsonb_build_object(
                           'user_id', affected_user,
                           'reason', 'moderation service path removed')
                FROM governance.duty_sessions AS duty
                WHERE duty.user_id = affected_user
                  AND duty.status = 'active';

                UPDATE governance.capability_grants AS capability_grant
                SET revoked_at = COALESCE(capability_grant.revoked_at, now())
                WHERE capability_grant.user_id = affected_user
                  AND capability_grant.source_type = 'duty_session'
                  AND capability_grant.revoked_at IS NULL
                  AND EXISTS (
                      SELECT 1
                      FROM governance.duty_sessions AS duty
                      WHERE duty.id::text = capability_grant.source_id
                        AND duty.user_id = affected_user
                        AND duty.status = 'active');

                UPDATE governance.duty_sessions
                SET status = 'revoked',
                    ended_at = COALESCE(ended_at, now()),
                    version = version + 1
                WHERE user_id = affected_user
                  AND status = 'active';

                UPDATE governance.qualifications
                SET level = 0,
                    updated_at = now()
                WHERE user_id = affected_user
                  AND track = 'moderation'
                  AND level > 0;

                RETURN NULL;
            END;
            $governance$;

            DROP TRIGGER IF EXISTS governance_moderation_path_demotes_qualification ON governance.service_paths;
            CREATE CONSTRAINT TRIGGER governance_moderation_path_demotes_qualification
            AFTER DELETE OR UPDATE ON governance.service_paths
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW
            EXECUTE FUNCTION governance.demote_moderation_qualification_after_path_change();

            -- Older code created a jury service assignment at invitation time. Never-accepted open
            -- assignments are not obligations and must not consume cooldown or become failure evidence.
            DELETE FROM governance.service_assignments AS assignment
            USING governance.jurors AS juror, governance.invitations AS invitation
            WHERE assignment.user_id = juror.user_id
              AND assignment.track = 'jury'
              AND assignment.entity_type = 'court_case'
              AND assignment.entity_id = juror.case_id::text
              AND invitation.id = juror.invitation_id
              AND invitation.state <> 'accepted'
              AND assignment.completed_at IS NULL
              AND assignment.failed_at IS NULL;

            -- Preserve accepted obligations created by older deployments if the assignment row is
            -- missing for any reason. The application now creates this row exactly on acceptance.
            INSERT INTO governance.service_assignments(
                user_id, track, entity_type, entity_id, assigned_at)
            SELECT juror.user_id,
                   'jury',
                   'court_case',
                   juror.case_id::text,
                   COALESCE(invitation.responded_at, juror.assigned_at)
            FROM governance.jurors AS juror
            JOIN governance.invitations AS invitation ON invitation.id = juror.invitation_id
            WHERE invitation.state = 'accepted'
              AND NOT EXISTS (
                  SELECT 1
                  FROM governance.service_assignments AS assignment
                  WHERE assignment.user_id = juror.user_id
                    AND assignment.track = 'jury'
                    AND assignment.entity_type = 'court_case'
                    AND assignment.entity_id = juror.case_id::text);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS governance_moderation_path_demotes_qualification ON governance.service_paths;

            CREATE OR REPLACE FUNCTION governance.demote_moderation_qualification_after_path_change()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $governance$
            DECLARE
                affected_user uuid;
            BEGIN
                affected_user := OLD.user_id;
                IF OLD.track <> 'moderation' THEN
                    RETURN NULL;
                END IF;

                IF TG_OP = 'UPDATE' AND NEW.track = 'moderation' THEN
                    RETURN NULL;
                END IF;

                UPDATE governance.qualifications
                SET level = 0,
                    updated_at = now()
                WHERE user_id = affected_user
                  AND track = 'moderation'
                  AND level > 0;
                RETURN NULL;
            END;
            $governance$;

            CREATE TRIGGER governance_moderation_path_demotes_qualification
            AFTER DELETE OR UPDATE OF track ON governance.service_paths
            FOR EACH ROW
            EXECUTE FUNCTION governance.demote_moderation_qualification_after_path_change();
            """);
    }
}
