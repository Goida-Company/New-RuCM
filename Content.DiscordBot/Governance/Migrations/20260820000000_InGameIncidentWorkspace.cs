using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260820000000_InGameIncidentWorkspace")]
public sealed class InGameIncidentWorkspace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE governance.live_incidents
                ADD COLUMN IF NOT EXISTS ahelp_ticket_id bigint
                    REFERENCES governance.ahelp_tickets(id) ON DELETE SET NULL;

            CREATE UNIQUE INDEX IF NOT EXISTS live_incidents_ahelp_ticket_idx
                ON governance.live_incidents(ahelp_ticket_id)
                WHERE ahelp_ticket_id IS NOT NULL;

            CREATE OR REPLACE FUNCTION governance.reconcile_user_identity_insert()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $governance$
            DECLARE
                existing_by_discord uuid;
                existing_by_ss14 uuid;
                existing_ss14_discord bigint;
            BEGIN
                SELECT id INTO existing_by_discord
                FROM governance.users
                WHERE discord_user_id = NEW.discord_user_id
                LIMIT 1;

                SELECT id, discord_user_id INTO existing_by_ss14, existing_ss14_discord
                FROM governance.users
                WHERE ss14_user_id = NEW.ss14_user_id
                LIMIT 1;

                IF existing_by_discord IS NOT NULL
                   AND existing_by_ss14 IS NOT NULL
                   AND existing_by_discord <> existing_by_ss14 THEN
                    RAISE EXCEPTION
                        'ambiguous governance identity rebind for Discord % and SS14 %',
                        NEW.discord_user_id,
                        NEW.ss14_user_id
                        USING ERRCODE = '23505';
                END IF;

                IF existing_by_discord IS NOT NULL AND existing_by_ss14 IS NULL THEN
                    UPDATE governance.users
                    SET ss14_user_id = NEW.ss14_user_id,
                        updated_at = now()
                    WHERE id = existing_by_discord;
                    RETURN NULL;
                END IF;

                -- In-game moderation may create an SS14-only Governance identity with an internal
                -- negative Discord id. Linking Discord later upgrades that same row instead of
                -- creating a duplicate identity.
                IF existing_by_ss14 IS NOT NULL
                   AND existing_by_discord IS NULL
                   AND existing_ss14_discord < 0
                   AND NEW.discord_user_id > 0 THEN
                    UPDATE governance.users
                    SET discord_user_id = NEW.discord_user_id,
                        updated_at = now()
                    WHERE id = existing_by_ss14;
                    RETURN NULL;
                END IF;

                RETURN NEW;
            END;
            $governance$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS governance.live_incidents_ahelp_ticket_idx;
            ALTER TABLE governance.live_incidents DROP COLUMN IF EXISTS ahelp_ticket_id;
            """);
    }
}
