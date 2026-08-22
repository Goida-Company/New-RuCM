using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260817010000_FullGovernance")]
public sealed class FullGovernance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE governance.court_cases ADD COLUMN IF NOT EXISTS overturned_at timestamptz;
            ALTER TABLE governance.court_cases ADD COLUMN IF NOT EXISTS overturn_reason text;
            ALTER TABLE governance.court_cases ADD COLUMN IF NOT EXISTS false_report_at timestamptz;
            ALTER TABLE governance.punishment_executions ADD COLUMN IF NOT EXISTS reverted_at timestamptz;

            CREATE TABLE IF NOT EXISTS governance.court_participants (
                case_id bigint NOT NULL REFERENCES governance.court_cases(id) ON DELETE CASCADE,
                user_id uuid NOT NULL REFERENCES governance.users(id),
                role text NOT NULL CHECK (role IN ('claimant', 'defendant', 'witness')),
                added_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (case_id, user_id)
            );

            INSERT INTO governance.court_participants(case_id, user_id, role, added_at)
            SELECT id, claimant_user_id, 'claimant', filed_at FROM governance.court_cases
            ON CONFLICT DO NOTHING;
            INSERT INTO governance.court_participants(case_id, user_id, role, added_at)
            SELECT id, defendant_user_id, 'defendant', filed_at FROM governance.court_cases
            ON CONFLICT DO NOTHING;

            CREATE TABLE IF NOT EXISTS governance.friendships (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES governance.users(id),
                friend_user_id uuid NOT NULL REFERENCES governance.users(id),
                requested_by_user_id uuid NOT NULL REFERENCES governance.users(id),
                created_at timestamptz NOT NULL DEFAULT now(),
                confirmed_at timestamptz,
                UNIQUE (user_id, friend_user_id),
                CHECK (user_id < friend_user_id),
                CHECK (user_id <> friend_user_id)
            );

            CREATE TABLE IF NOT EXISTS governance.service_assignments (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES governance.users(id),
                track text NOT NULL CHECK (track IN ('jury', 'moderation', 'event')),
                entity_type text NOT NULL,
                entity_id text NOT NULL,
                assigned_at timestamptz NOT NULL DEFAULT now(),
                completed_at timestamptz,
                failed_at timestamptz,
                UNIQUE (user_id, track, entity_type, entity_id)
            );
            CREATE INDEX IF NOT EXISTS service_assignments_recent_idx
                ON governance.service_assignments(user_id, track, assigned_at DESC);

            CREATE TABLE IF NOT EXISTS governance.leadership_overrides (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                entity_type text NOT NULL,
                entity_id text NOT NULL,
                action text NOT NULL,
                reason text NOT NULL,
                actor_discord_id bigint NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS governance.ahelp_tickets (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                round_id integer NOT NULL,
                reporter_user_id uuid NOT NULL REFERENCES governance.users(id),
                target_user_id uuid REFERENCES governance.users(id),
                claimed_by_user_id uuid REFERENCES governance.users(id),
                status text NOT NULL CHECK (status IN ('open','claimed','waiting_player','resolved','escalated_to_incident','escalated_to_court')),
                summary text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                updated_at timestamptz NOT NULL DEFAULT now(),
                discord_thread_id bigint
            );
            CREATE INDEX IF NOT EXISTS ahelp_open_idx ON governance.ahelp_tickets(status, created_at);

            CREATE TABLE IF NOT EXISTS governance.live_incidents (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                round_id integer NOT NULL,
                target_user_id uuid NOT NULL REFERENCES governance.users(id),
                reporter_user_id uuid REFERENCES governance.users(id),
                created_by_user_id uuid NOT NULL REFERENCES governance.users(id),
                type text NOT NULL,
                summary text NOT NULL,
                status text NOT NULL CHECK (status IN ('active','contained','closed','escalated_to_court')),
                created_at timestamptz NOT NULL DEFAULT now(),
                closed_at timestamptz,
                court_case_id bigint REFERENCES governance.court_cases(id)
            );

            CREATE TABLE IF NOT EXISTS governance.moderation_actions (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                incident_id bigint NOT NULL REFERENCES governance.live_incidents(id),
                actor_user_id uuid NOT NULL REFERENCES governance.users(id),
                target_user_id uuid NOT NULL REFERENCES governance.users(id),
                action_type text NOT NULL CHECK (action_type IN ('freeze','round_remove','request_explanation','view_logs')),
                reason text NOT NULL,
                duration_seconds integer,
                status text NOT NULL CHECK (status IN ('proposed','approved','rejected','executed','expired')),
                required_approvals smallint NOT NULL CHECK (required_approvals BETWEEN 1 AND 5),
                created_at timestamptz NOT NULL DEFAULT now(),
                executed_at timestamptz,
                idempotency_key text NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS governance.moderation_approvals (
                action_id bigint NOT NULL REFERENCES governance.moderation_actions(id) ON DELETE CASCADE,
                approver_user_id uuid NOT NULL REFERENCES governance.users(id),
                decision text NOT NULL CHECK (decision IN ('approve','reject','more_information')),
                created_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (action_id, approver_user_id)
            );

            CREATE TABLE IF NOT EXISTS governance.event_proposals (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                owner_user_id uuid NOT NULL REFERENCES governance.users(id),
                title text NOT NULL,
                description text NOT NULL,
                duration_minutes integer NOT NULL CHECK (duration_minutes BETWEEN 10 AND 480),
                manifest jsonb NOT NULL DEFAULT '[]'::jsonb,
                status text NOT NULL CHECK (status IN ('review','approved','rejected','active','completed','aborted')),
                created_at timestamptz NOT NULL DEFAULT now(),
                review_deadline timestamptz NOT NULL,
                discord_thread_id bigint
            );
            ALTER TABLE governance.event_proposals ADD COLUMN IF NOT EXISTS manifest jsonb NOT NULL DEFAULT '[]'::jsonb;

            CREATE TABLE IF NOT EXISTS governance.event_reviews (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                proposal_id bigint NOT NULL REFERENCES governance.event_proposals(id) ON DELETE CASCADE,
                reviewer_user_id uuid NOT NULL REFERENCES governance.users(id),
                decision text NOT NULL CHECK (decision IN ('approve','reject')),
                reasoning text NOT NULL,
                submitted_at timestamptz NOT NULL DEFAULT now(),
                UNIQUE (proposal_id, reviewer_user_id)
            );

            CREATE TABLE IF NOT EXISTS governance.event_sessions (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                proposal_id bigint NOT NULL UNIQUE REFERENCES governance.event_proposals(id),
                director_user_id uuid NOT NULL REFERENCES governance.users(id),
                round_id integer,
                status text NOT NULL CHECK (status IN ('active','completed','aborted','revoked')),
                started_at timestamptz NOT NULL,
                expires_at timestamptz NOT NULL,
                ended_at timestamptz,
                CHECK (expires_at > started_at)
            );

            CREATE TABLE IF NOT EXISTS governance.event_manifest_items (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                session_id bigint NOT NULL REFERENCES governance.event_sessions(id) ON DELETE CASCADE,
                capability text NOT NULL,
                resource text NOT NULL,
                max_uses integer NOT NULL CHECK (max_uses >= 0),
                used_count integer NOT NULL DEFAULT 0 CHECK (used_count >= 0),
                UNIQUE (session_id, capability, resource)
            );

            CREATE TABLE IF NOT EXISTS governance.event_actions (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                session_id bigint NOT NULL REFERENCES governance.event_sessions(id),
                actor_user_id uuid NOT NULL REFERENCES governance.users(id),
                capability text NOT NULL,
                resource text NOT NULL,
                status text NOT NULL CHECK (status IN ('allowed','denied','executed')),
                created_at timestamptz NOT NULL DEFAULT now(),
                payload jsonb NOT NULL DEFAULT '{}'::jsonb
            );

            DO $governance$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'moderation_approvals_immutable') THEN
                    CREATE TRIGGER moderation_approvals_immutable BEFORE UPDATE OR DELETE ON governance.moderation_approvals
                    FOR EACH ROW EXECUTE FUNCTION governance.reject_immutable_mutation();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'event_reviews_immutable') THEN
                    CREATE TRIGGER event_reviews_immutable BEFORE UPDATE OR DELETE ON governance.event_reviews
                    FOR EACH ROW EXECUTE FUNCTION governance.reject_immutable_mutation();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'leadership_overrides_immutable') THEN
                    CREATE TRIGGER leadership_overrides_immutable BEFORE UPDATE OR DELETE ON governance.leadership_overrides
                    FOR EACH ROW EXECUTE FUNCTION governance.reject_immutable_mutation();
                END IF;
            END;
            $governance$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS governance.event_actions;
            DROP TABLE IF EXISTS governance.event_manifest_items;
            DROP TABLE IF EXISTS governance.event_sessions;
            DROP TABLE IF EXISTS governance.event_reviews;
            DROP TABLE IF EXISTS governance.event_proposals;
            DROP TABLE IF EXISTS governance.moderation_approvals;
            DROP TABLE IF EXISTS governance.moderation_actions;
            DROP TABLE IF EXISTS governance.live_incidents;
            DROP TABLE IF EXISTS governance.ahelp_tickets;
            DROP TABLE IF EXISTS governance.leadership_overrides;
            DROP TABLE IF EXISTS governance.service_assignments;
            DROP TABLE IF EXISTS governance.friendships;
            DROP TABLE IF EXISTS governance.court_participants;
            """);
    }
}
