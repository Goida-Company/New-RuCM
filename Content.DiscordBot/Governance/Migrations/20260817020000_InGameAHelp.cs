using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260817020000_InGameAHelp")]
public sealed class InGameAHelp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE governance.ahelp_tickets
                ALTER COLUMN reporter_user_id DROP NOT NULL;
            ALTER TABLE governance.ahelp_tickets
                ADD COLUMN IF NOT EXISTS reporter_ss14_user_id uuid;

            UPDATE governance.ahelp_tickets AS ticket
            SET reporter_ss14_user_id = users.ss14_user_id
            FROM governance.users AS users
            WHERE ticket.reporter_user_id = users.id
              AND ticket.reporter_ss14_user_id IS NULL;

            ALTER TABLE governance.ahelp_tickets
                ALTER COLUMN reporter_ss14_user_id SET NOT NULL;

            CREATE INDEX IF NOT EXISTS ahelp_reporter_round_idx
                ON governance.ahelp_tickets(reporter_ss14_user_id, round_id, created_at DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS ahelp_one_active_reporter_idx
                ON governance.ahelp_tickets(round_id, reporter_ss14_user_id)
                WHERE status IN ('open', 'claimed', 'waiting_player', 'escalated_to_incident');

            CREATE TABLE IF NOT EXISTS governance.ahelp_messages (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                ticket_id bigint NOT NULL REFERENCES governance.ahelp_tickets(id) ON DELETE CASCADE,
                sender_ss14_user_id uuid NOT NULL,
                body text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS ahelp_messages_ticket_idx
                ON governance.ahelp_messages(ticket_id, created_at, id);

            DO $governance$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'ahelp_messages_immutable') THEN
                    CREATE TRIGGER ahelp_messages_immutable BEFORE UPDATE OR DELETE ON governance.ahelp_messages
                    FOR EACH ROW EXECUTE FUNCTION governance.reject_immutable_mutation();
                END IF;
            END;
            $governance$;

            WITH eligible AS (
                SELECT duty.id AS duty_id, duty.user_id, duty.round_id, duty.expires_at
                FROM governance.duty_sessions AS duty
                JOIN governance.qualifications AS qualification
                  ON qualification.user_id = duty.user_id
                 AND qualification.track = 'moderation'
                 AND qualification.level >= 1
                WHERE duty.status = 'active' AND duty.expires_at > now()
            )
            INSERT INTO governance.capability_grants(
                user_id, capability, source_type, source_id, scope,
                issued_at, expires_at, idempotency_key)
            SELECT user_id, 'moderation.ahelp', 'duty_session', duty_id::text,
                   jsonb_build_object('round_id', round_id), now(), expires_at,
                   'moderation-duty:' || duty_id::text || ':moderation.ahelp'
            FROM eligible
            ON CONFLICT (idempotency_key) DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM governance.capability_grants WHERE capability = 'moderation.ahelp';
            DROP TABLE IF EXISTS governance.ahelp_messages;
            DROP INDEX IF EXISTS governance.ahelp_one_active_reporter_idx;
            DROP INDEX IF EXISTS governance.ahelp_reporter_round_idx;
            ALTER TABLE governance.ahelp_tickets DROP COLUMN IF EXISTS reporter_ss14_user_id;
            ALTER TABLE governance.ahelp_tickets ALTER COLUMN reporter_user_id SET NOT NULL;
            """);
    }
}
