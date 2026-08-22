using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260818010000_ModerationAppeals")]
public sealed class ModerationAppeals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS governance.moderation_appeals (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                action_id bigint NOT NULL UNIQUE REFERENCES governance.moderation_actions(id) ON DELETE CASCADE,
                appellant_user_id uuid NOT NULL REFERENCES governance.users(id),
                reason text NOT NULL,
                status text NOT NULL CHECK (status IN ('reviewing','resolved')),
                result text CHECK (result IS NULL OR result IN (
                    'correct',
                    'reasonable_but_wrong',
                    'procedural_error',
                    'negligent',
                    'abuse'
                )),
                created_at timestamptz NOT NULL DEFAULT now(),
                resolved_at timestamptz
            );

            CREATE INDEX IF NOT EXISTS moderation_appeals_status_idx
                ON governance.moderation_appeals(status, created_at);

            DO $governance$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'moderation_appeals_immutable_identity') THEN
                    CREATE OR REPLACE FUNCTION governance.guard_moderation_appeal_mutation()
                    RETURNS trigger LANGUAGE plpgsql AS $appeal$
                    BEGIN
                        IF NEW.action_id <> OLD.action_id OR
                           NEW.appellant_user_id <> OLD.appellant_user_id OR
                           NEW.reason <> OLD.reason OR
                           NEW.created_at <> OLD.created_at THEN
                            RAISE EXCEPTION 'moderation appeal identity and complaint are immutable';
                        END IF;
                        RETURN NEW;
                    END;
                    $appeal$;
                    CREATE TRIGGER moderation_appeals_immutable_identity
                    BEFORE UPDATE ON governance.moderation_appeals
                    FOR EACH ROW EXECUTE FUNCTION governance.guard_moderation_appeal_mutation();
                END IF;
            END;
            $governance$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS governance.moderation_appeals;
            DROP FUNCTION IF EXISTS governance.guard_moderation_appeal_mutation();
            """);
    }
}
