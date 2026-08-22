using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260821000000_EventActionServerExecution")]
public sealed class EventActionServerExecution : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE governance.event_actions
                ADD COLUMN IF NOT EXISTS server_status text,
                ADD COLUMN IF NOT EXISTS server_executed_at timestamptz,
                ADD COLUMN IF NOT EXISTS server_execution_error text;

            UPDATE governance.event_actions
            SET server_status = CASE WHEN status = 'denied' THEN 'failed' ELSE 'executed' END,
                server_executed_at = COALESCE(server_executed_at, created_at)
            WHERE server_status IS NULL;

            ALTER TABLE governance.event_actions
                ALTER COLUMN server_status SET DEFAULT 'pending',
                ALTER COLUMN server_status SET NOT NULL;

            DO $governance$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conname = 'event_actions_server_status_check'
                      AND conrelid = 'governance.event_actions'::regclass) THEN
                    ALTER TABLE governance.event_actions
                        ADD CONSTRAINT event_actions_server_status_check
                        CHECK (server_status IN ('pending', 'executing', 'executed', 'failed'));
                END IF;
            END;
            $governance$;

            CREATE INDEX IF NOT EXISTS event_actions_server_pending_idx
                ON governance.event_actions(server_status, id)
                WHERE status = 'executed' AND server_status = 'pending';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS governance.event_actions_server_pending_idx;
            ALTER TABLE governance.event_actions
                DROP CONSTRAINT IF EXISTS event_actions_server_status_check,
                DROP COLUMN IF EXISTS server_execution_error,
                DROP COLUMN IF EXISTS server_executed_at,
                DROP COLUMN IF EXISTS server_status;
            """);
    }
}
