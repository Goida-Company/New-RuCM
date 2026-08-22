using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260819000000_GovernanceIdentityRebind")]
public sealed class GovernanceIdentityRebind : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION governance.reconcile_user_identity_insert()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $governance$
            DECLARE
                existing_by_discord uuid;
                existing_by_ss14 uuid;
            BEGIN
                SELECT id INTO existing_by_discord
                FROM governance.users
                WHERE discord_user_id = NEW.discord_user_id
                LIMIT 1;

                SELECT id INTO existing_by_ss14
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

                RETURN NEW;
            END;
            $governance$;

            DROP TRIGGER IF EXISTS governance_users_identity_rebind ON governance.users;
            CREATE TRIGGER governance_users_identity_rebind
            BEFORE INSERT ON governance.users
            FOR EACH ROW
            EXECUTE FUNCTION governance.reconcile_user_identity_insert();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS governance_users_identity_rebind ON governance.users;
            DROP FUNCTION IF EXISTS governance.reconcile_user_identity_insert();
            """);
    }
}
