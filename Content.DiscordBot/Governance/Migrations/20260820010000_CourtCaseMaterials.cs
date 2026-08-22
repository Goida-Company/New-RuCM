using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260820010000_CourtCaseMaterials")]
public sealed class CourtCaseMaterials : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE governance.live_incidents
                ADD COLUMN IF NOT EXISTS target_character_name text;

            ALTER TABLE governance.court_cases
                ADD COLUMN IF NOT EXISTS materials_published_at timestamptz;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE governance.court_cases DROP COLUMN IF EXISTS materials_published_at;
            ALTER TABLE governance.live_incidents DROP COLUMN IF EXISTS target_character_name;
            """);
    }
}
