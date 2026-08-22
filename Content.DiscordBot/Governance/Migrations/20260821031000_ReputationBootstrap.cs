using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260821031000_ReputationBootstrap")]
public sealed class ReputationBootstrap : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- Every SS14 account gets a Governance identity even when Discord is not linked.
            INSERT INTO governance.users(ss14_user_id, discord_user_id, civic_rating_cache, created_at, updated_at)
            SELECT p.user_id, NULL, 500, p.first_seen_time, now()
            FROM player p
            ON CONFLICT (ss14_user_id) DO NOTHING;

            -- Imported paths are a migration convenience, not a voluntary user choice. Let the
            -- player replace them immediately after deployment instead of waiting for cooldown.
            UPDATE governance.service_paths
            SET selected_at = LEAST(selected_at, now() - interval '31 days'),
                changed_at = LEAST(changed_at, now() - interval '31 days');

            -- Existing linked accounts remain the authoritative source for the optional Discord transport.
            UPDATE governance.users u
            SET discord_user_id = linked.discord_id::bigint,
                updated_at = now()
            FROM rmc_linked_accounts linked
            WHERE linked.player_id = u.ss14_user_id
              AND linked.discord_id > 0
              AND (u.discord_user_id IS NULL OR u.discord_user_id = linked.discord_id::bigint);

            INSERT INTO governance.identity_links(user_id, discord_user_id, linked_at, source)
            SELECT u.id, linked.discord_id::bigint, now(), 'game_account_link'
            FROM governance.users u
            JOIN rmc_linked_accounts linked ON linked.player_id = u.ss14_user_id
            WHERE linked.discord_id > 0
              AND NOT EXISTS (
                  SELECT 1 FROM governance.identity_links il
                  WHERE il.user_id = u.id AND il.unlinked_at IS NULL
              )
            ON CONFLICT DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Identity/reputation history is intentionally retained on downgrade. Deleting profiles for
        // real SS14 users would also cascade immutable Governance evidence.
    }
}
