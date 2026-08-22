using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260821033000_NormalizeLegacySyntheticDiscord")]
public sealed class NormalizeLegacySyntheticDiscord : Migration
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
                -- Compatibility with older game-server code that represented an SS14-only identity
                -- as a deterministic negative bigint. Reputation v2 represents absence honestly.
                IF NEW.discord_user_id IS NOT NULL AND NEW.discord_user_id <= 0 THEN
                    NEW.discord_user_id := NULL;
                END IF;

                IF NEW.discord_user_id IS NOT NULL THEN
                    SELECT id INTO existing_by_discord
                    FROM governance.users
                    WHERE discord_user_id = NEW.discord_user_id
                    LIMIT 1;
                END IF;

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
                    RAISE EXCEPTION
                        'Discord % is linked to a different SS14 Governance profile',
                        NEW.discord_user_id
                        USING ERRCODE = '23505';
                END IF;

                RETURN NEW;
            END;
            $governance$;

            UPDATE governance.users SET discord_user_id = NULL, updated_at = now()
            WHERE discord_user_id <= 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keeping NULL for SS14-only identities is safe even if this compatibility migration is rolled back.
    }
}
