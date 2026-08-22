using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260821037000_ServicePathQualificationInvariant")]
public sealed class ServicePathQualificationInvariant : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- Qualification I-IV is meaningful only for an explicitly selected service path.
            -- Preserve reputation/history, but normalize the current authorization level.
            UPDATE governance.qualifications AS qualification
            SET level = 0,
                updated_at = now()
            WHERE qualification.track IN ('support', 'moderation', 'jury', 'event', 'contributor')
              AND qualification.level > 0
              AND NOT EXISTS (
                  SELECT 1
                  FROM governance.service_paths AS path
                  WHERE path.user_id = qualification.user_id
                    AND path.track = qualification.track);

            -- A selected path always starts at least at qualification I.
            UPDATE governance.qualifications AS qualification
            SET level = 1,
                updated_at = now()
            WHERE qualification.track IN ('support', 'moderation', 'jury', 'event', 'contributor')
              AND qualification.level < 1
              AND EXISTS (
                  SELECT 1
                  FROM governance.service_paths AS path
                  WHERE path.user_id = qualification.user_id
                    AND path.track = qualification.track);

            CREATE OR REPLACE FUNCTION governance.require_service_path_for_qualification()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $governance$
            BEGIN
                IF NEW.track IN ('support', 'moderation', 'jury', 'event', 'contributor')
                   AND NEW.level > 0
                   AND NOT EXISTS (
                       SELECT 1
                       FROM governance.service_paths AS path
                       WHERE path.user_id = NEW.user_id
                         AND path.track = NEW.track) THEN
                    RAISE EXCEPTION 'qualification % requires selected service path for user %', NEW.track, NEW.user_id
                        USING ERRCODE = '23514';
                END IF;
                RETURN NULL;
            END;
            $governance$;

            DROP TRIGGER IF EXISTS governance_qualification_requires_service_path ON governance.qualifications;
            CREATE CONSTRAINT TRIGGER governance_qualification_requires_service_path
            AFTER INSERT OR UPDATE OF user_id, track, level ON governance.qualifications
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW
            EXECUTE FUNCTION governance.require_service_path_for_qualification();

            -- Path slot swaps may temporarily remove a track during the transaction. Evaluate the
            -- final transaction state so moving primary <-> secondary never destroys qualification.
            CREATE OR REPLACE FUNCTION governance.demote_qualification_after_service_path_change()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $governance$
            DECLARE
                affected_user uuid;
                affected_track text;
            BEGIN
                affected_user := OLD.user_id;
                affected_track := OLD.track;

                IF affected_track NOT IN ('support', 'moderation', 'jury', 'event', 'contributor') THEN
                    RETURN NULL;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM governance.service_paths AS path
                    WHERE path.user_id = affected_user
                      AND path.track = affected_track) THEN
                    RETURN NULL;
                END IF;

                UPDATE governance.qualifications
                SET level = 0,
                    updated_at = now()
                WHERE user_id = affected_user
                  AND track = affected_track
                  AND level > 0;

                RETURN NULL;
            END;
            $governance$;

            DROP TRIGGER IF EXISTS governance_service_path_demotes_qualification ON governance.service_paths;
            CREATE CONSTRAINT TRIGGER governance_service_path_demotes_qualification
            AFTER DELETE OR UPDATE ON governance.service_paths
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW
            EXECUTE FUNCTION governance.demote_qualification_after_service_path_change();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS governance_service_path_demotes_qualification ON governance.service_paths;
            DROP TRIGGER IF EXISTS governance_qualification_requires_service_path ON governance.qualifications;
            DROP FUNCTION IF EXISTS governance.demote_qualification_after_service_path_change();
            DROP FUNCTION IF EXISTS governance.require_service_path_for_qualification();
            """);
    }
}
