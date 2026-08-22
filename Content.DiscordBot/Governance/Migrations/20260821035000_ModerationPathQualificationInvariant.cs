using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260821035000_ModerationPathQualificationInvariant")]
public sealed class ModerationPathQualificationInvariant : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- A moderation qualification is usable only while moderation is one of the player's
            -- explicitly selected service paths. This closes the server-side Duty bypass where an
            -- old/manual qualification could remain active after the player left the path.
            UPDATE governance.qualifications AS qualification
            SET level = 0,
                updated_at = now()
            WHERE qualification.track = 'moderation'
              AND qualification.level > 0
              AND NOT EXISTS (
                  SELECT 1
                  FROM governance.service_paths AS path
                  WHERE path.user_id = qualification.user_id
                    AND path.track = 'moderation');

            CREATE OR REPLACE FUNCTION governance.require_moderation_path_for_qualification()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $governance$
            BEGIN
                IF NEW.track = 'moderation' AND NEW.level > 0 AND NOT EXISTS (
                    SELECT 1
                    FROM governance.service_paths AS path
                    WHERE path.user_id = NEW.user_id
                      AND path.track = 'moderation') THEN
                    RAISE EXCEPTION 'moderation qualification requires selected moderation service path for user %', NEW.user_id
                        USING ERRCODE = '23514';
                END IF;
                RETURN NEW;
            END;
            $governance$;

            CREATE OR REPLACE FUNCTION governance.demote_moderation_qualification_after_path_change()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $governance$
            DECLARE
                affected_user uuid;
            BEGIN
                affected_user := OLD.user_id;
                IF OLD.track <> 'moderation' THEN
                    RETURN COALESCE(NEW, OLD);
                END IF;

                IF TG_OP = 'UPDATE' AND NEW.track = 'moderation' THEN
                    RETURN NEW;
                END IF;

                UPDATE governance.qualifications
                SET level = 0,
                    updated_at = now()
                WHERE user_id = affected_user
                  AND track = 'moderation'
                  AND level > 0;
                RETURN COALESCE(NEW, OLD);
            END;
            $governance$;

            DROP TRIGGER IF EXISTS governance_moderation_qualification_requires_path ON governance.qualifications;
            CREATE CONSTRAINT TRIGGER governance_moderation_qualification_requires_path
            AFTER INSERT OR UPDATE OF user_id, track, level ON governance.qualifications
            DEFERRABLE INITIALLY DEFERRED
            FOR EACH ROW
            EXECUTE FUNCTION governance.require_moderation_path_for_qualification();

            DROP TRIGGER IF EXISTS governance_moderation_path_demotes_qualification ON governance.service_paths;
            CREATE TRIGGER governance_moderation_path_demotes_qualification
            AFTER DELETE OR UPDATE OF track ON governance.service_paths
            FOR EACH ROW
            EXECUTE FUNCTION governance.demote_moderation_qualification_after_path_change();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS governance_moderation_path_demotes_qualification ON governance.service_paths;
            DROP TRIGGER IF EXISTS governance_moderation_qualification_requires_path ON governance.qualifications;
            DROP FUNCTION IF EXISTS governance.demote_moderation_qualification_after_path_change();
            DROP FUNCTION IF EXISTS governance.require_moderation_path_for_qualification();
            """);
    }
}
