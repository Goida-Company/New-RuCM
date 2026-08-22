using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260818000000_ModerationTrust")]
public sealed class ModerationTrust : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS governance.moderation_reviews (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                action_id bigint NOT NULL REFERENCES governance.moderation_actions(id) ON DELETE CASCADE,
                reviewer_user_id uuid NOT NULL REFERENCES governance.users(id),
                outcome text NOT NULL CHECK (outcome IN (
                    'correct',
                    'reasonable_but_wrong',
                    'procedural_error',
                    'negligent',
                    'abuse'
                )),
                reasoning text NOT NULL,
                submitted_at timestamptz NOT NULL DEFAULT now(),
                idempotency_key text NOT NULL UNIQUE,
                UNIQUE (action_id, reviewer_user_id)
            );

            CREATE INDEX IF NOT EXISTS moderation_reviews_action_idx
                ON governance.moderation_reviews(action_id, submitted_at DESC);
            CREATE INDEX IF NOT EXISTS moderation_reviews_reviewer_idx
                ON governance.moderation_reviews(reviewer_user_id, submitted_at DESC);
            CREATE INDEX IF NOT EXISTS moderation_actions_actor_review_idx
                ON governance.moderation_actions(actor_user_id, status, executed_at DESC);

            DO $governance$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'moderation_reviews_immutable') THEN
                    CREATE TRIGGER moderation_reviews_immutable
                    BEFORE UPDATE OR DELETE ON governance.moderation_reviews
                    FOR EACH ROW EXECUTE FUNCTION governance.reject_immutable_mutation();
                END IF;
            END;
            $governance$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS governance.moderation_actions_actor_review_idx;
            DROP TABLE IF EXISTS governance.moderation_reviews;
            """);
    }
}
