using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260821010000_AHelpCourtActiveTicket")]
public sealed class AHelpCourtActiveTicket : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS governance.ahelp_one_active_reporter_idx;
            CREATE UNIQUE INDEX ahelp_one_active_reporter_idx
                ON governance.ahelp_tickets(round_id, reporter_ss14_user_id)
                WHERE status IN ('open', 'claimed', 'waiting_player', 'escalated_to_incident', 'escalated_to_court');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS governance.ahelp_one_active_reporter_idx;
            CREATE UNIQUE INDEX ahelp_one_active_reporter_idx
                ON governance.ahelp_tickets(round_id, reporter_ss14_user_id)
                WHERE status IN ('open', 'claimed', 'waiting_player', 'escalated_to_incident');
            """);
    }
}
