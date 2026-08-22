using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260821020000_CourtSentencingConstraints")]
public sealed class CourtSentencingConstraints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $court$
            DECLARE
                constraint_name text;
            BEGIN
                FOR constraint_name IN
                    SELECT con.conname
                    FROM pg_constraint con
                    JOIN pg_class rel ON rel.oid = con.conrelid
                    JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
                    WHERE nsp.nspname = 'governance'
                      AND rel.relname = 'sentencing_votes'
                      AND con.contype = 'c'
                LOOP
                    EXECUTE format('ALTER TABLE governance.sentencing_votes DROP CONSTRAINT %I', constraint_name);
                END LOOP;
            END;
            $court$;

            ALTER TABLE governance.sentencing_votes
                ADD CONSTRAINT sentencing_votes_sanction_type_valid
                    CHECK (sanction_type IN ('warning', 'game_ban', 'job_ban')),
                ADD CONSTRAINT sentencing_votes_sanction_days_valid
                    CHECK (sanction_days IS NULL OR sanction_days BETWEEN 1 AND 7),
                ADD CONSTRAINT sentencing_votes_shape_valid
                    CHECK (
                        (sanction_type = 'warning' AND sanction_days IS NULL AND sanction_role IS NULL)
                        OR
                        (sanction_type = 'game_ban' AND sanction_days IS NOT NULL AND sanction_role IS NULL)
                        OR
                        (sanction_type = 'job_ban' AND sanction_days IS NOT NULL
                            AND sanction_role IS NOT NULL AND btrim(sanction_role) <> '')
                    );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE governance.sentencing_votes
                DROP CONSTRAINT IF EXISTS sentencing_votes_sanction_type_valid,
                DROP CONSTRAINT IF EXISTS sentencing_votes_sanction_days_valid,
                DROP CONSTRAINT IF EXISTS sentencing_votes_shape_valid;

            ALTER TABLE governance.sentencing_votes
                ADD CHECK (sanction_type IN ('warning', 'game_ban', 'job_ban')),
                ADD CHECK (sanction_days BETWEEN 1 AND 7),
                ADD CHECK (sanction_type <> 'warning' OR sanction_days IS NULL),
                ADD CHECK (sanction_type <> 'game_ban' OR sanction_days IS NOT NULL),
                ADD CHECK (sanction_type <> 'job_ban' OR (sanction_days IS NOT NULL AND sanction_role IS NOT NULL));
            """);
    }
}
