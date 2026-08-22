using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260817000000_CommunityCourt")]
public sealed class CommunityCourt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE SCHEMA IF NOT EXISTS governance;

            CREATE TABLE IF NOT EXISTS governance.users (
                id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                ss14_user_id uuid NOT NULL UNIQUE,
                discord_user_id bigint NOT NULL UNIQUE,
                civic_rating_cache integer NOT NULL DEFAULT 0,
                is_governance_suspended boolean NOT NULL DEFAULT false,
                created_at timestamptz NOT NULL DEFAULT now(),
                updated_at timestamptz NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS governance.qualifications (
                user_id uuid NOT NULL REFERENCES governance.users(id) ON DELETE CASCADE,
                track text NOT NULL CHECK (track IN ('jury', 'moderation', 'event')),
                level smallint NOT NULL DEFAULT 0 CHECK (level BETWEEN 0 AND 4),
                updated_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, track)
            );

            CREATE TABLE IF NOT EXISTS governance.rating_entries (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES governance.users(id),
                amount integer NOT NULL,
                reason text NOT NULL,
                entity_type text NOT NULL,
                entity_id text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by_type text NOT NULL,
                created_by_id text,
                idempotency_key text NOT NULL UNIQUE,
                metadata jsonb NOT NULL DEFAULT '{}'::jsonb
            );

            CREATE TABLE IF NOT EXISTS governance.conflicts (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES governance.users(id),
                related_user_id uuid REFERENCES governance.users(id),
                entity_type text,
                entity_id text,
                reason text NOT NULL,
                starts_at timestamptz NOT NULL DEFAULT now(),
                ends_at timestamptz,
                created_by_type text NOT NULL,
                created_by_id text,
                CHECK (related_user_id IS NOT NULL OR (entity_type IS NOT NULL AND entity_id IS NOT NULL))
            );

            CREATE TABLE IF NOT EXISTS governance.invitations (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES governance.users(id),
                entity_type text NOT NULL,
                entity_id text NOT NULL,
                purpose text NOT NULL,
                state text NOT NULL CHECK (state IN ('pending', 'accepted', 'declined', 'recused', 'expired', 'cancelled', 'failed')),
                created_at timestamptz NOT NULL DEFAULT now(),
                expires_at timestamptz NOT NULL,
                responded_at timestamptz,
                recusal_reason text,
                idempotency_key text NOT NULL UNIQUE,
                version integer NOT NULL DEFAULT 0,
                discord_notified_at timestamptz
            );

            ALTER TABLE governance.invitations
                ADD COLUMN IF NOT EXISTS discord_notified_at timestamptz;

            CREATE INDEX IF NOT EXISTS invitations_due_idx
                ON governance.invitations(expires_at) WHERE state = 'pending';

            CREATE TABLE IF NOT EXISTS governance.court_cases (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                claimant_user_id uuid NOT NULL REFERENCES governance.users(id),
                defendant_user_id uuid NOT NULL REFERENCES governance.users(id),
                round_id integer NOT NULL,
                summary text NOT NULL,
                status text NOT NULL,
                filed_at timestamptz NOT NULL,
                defense_deadline timestamptz NOT NULL,
                guilt_started_at timestamptz,
                guilt_deadline timestamptz,
                sentencing_started_at timestamptz,
                sentencing_deadline timestamptz,
                verdict text,
                sanction_type text,
                sanction_days smallint CHECK (sanction_days BETWEEN 1 AND 7),
                sanction_role text,
                executed_at timestamptz,
                execution_reference text,
                version integer NOT NULL DEFAULT 0,
                discord_thread_id bigint,
                verdict_message_id bigint,
                published_at timestamptz,
                CHECK (claimant_user_id <> defendant_user_id),
                CHECK (sanction_type IS DISTINCT FROM 'game_ban' OR sanction_days IS NOT NULL),
                CHECK (sanction_type IS DISTINCT FROM 'job_ban' OR (sanction_days IS NOT NULL AND sanction_role IS NOT NULL))
            );

            ALTER TABLE governance.court_cases
                ADD COLUMN IF NOT EXISTS discord_thread_id bigint,
                ADD COLUMN IF NOT EXISTS verdict_message_id bigint,
                ADD COLUMN IF NOT EXISTS published_at timestamptz;

            CREATE TABLE IF NOT EXISTS governance.court_statements (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                case_id bigint NOT NULL REFERENCES governance.court_cases(id) ON DELETE CASCADE,
                author_user_id uuid NOT NULL REFERENCES governance.users(id),
                kind text NOT NULL CHECK (kind IN ('complaint', 'defense', 'witness')),
                body text NOT NULL,
                evidence_reference text,
                created_at timestamptz NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS governance.jurors (
                case_id bigint NOT NULL REFERENCES governance.court_cases(id) ON DELETE CASCADE,
                user_id uuid NOT NULL REFERENCES governance.users(id),
                invitation_id bigint NOT NULL UNIQUE REFERENCES governance.invitations(id),
                active boolean NOT NULL DEFAULT false,
                assigned_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (case_id, user_id)
            );

            CREATE TABLE IF NOT EXISTS governance.guilt_votes (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                case_id bigint NOT NULL REFERENCES governance.court_cases(id) ON DELETE CASCADE,
                juror_user_id uuid NOT NULL REFERENCES governance.users(id),
                verdict text NOT NULL CHECK (verdict IN ('guilty', 'not_guilty', 'insufficient_evidence')),
                reasoning text NOT NULL,
                submitted_at timestamptz NOT NULL DEFAULT now(),
                idempotency_key text NOT NULL UNIQUE,
                UNIQUE (case_id, juror_user_id)
            );

            CREATE TABLE IF NOT EXISTS governance.sentencing_votes (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                case_id bigint NOT NULL REFERENCES governance.court_cases(id) ON DELETE CASCADE,
                juror_user_id uuid NOT NULL REFERENCES governance.users(id),
                sanction_type text NOT NULL CHECK (sanction_type IN ('warning', 'game_ban', 'job_ban')),
                sanction_days smallint CHECK (sanction_days BETWEEN 1 AND 7),
                sanction_role text,
                reasoning text NOT NULL,
                submitted_at timestamptz NOT NULL DEFAULT now(),
                idempotency_key text NOT NULL UNIQUE,
                UNIQUE (case_id, juror_user_id),
                CHECK (sanction_type <> 'warning' OR sanction_days IS NULL),
                CHECK (sanction_type <> 'game_ban' OR sanction_days IS NOT NULL),
                CHECK (sanction_type <> 'job_ban' OR (sanction_days IS NOT NULL AND sanction_role IS NOT NULL))
            );

            CREATE TABLE IF NOT EXISTS governance.duty_sessions (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES governance.users(id),
                round_id integer NOT NULL,
                started_at timestamptz NOT NULL,
                expires_at timestamptz NOT NULL,
                ended_at timestamptz,
                status text NOT NULL CHECK (status IN ('active', 'completed', 'abandoned', 'revoked', 'round_ended')),
                qualification_at_start smallint NOT NULL CHECK (qualification_at_start BETWEEN 1 AND 4),
                observer_confirmed boolean NOT NULL,
                idempotency_key text NOT NULL UNIQUE,
                version integer NOT NULL DEFAULT 0,
                CHECK (expires_at > started_at),
                CHECK (observer_confirmed)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS one_active_duty_per_user_idx
                ON governance.duty_sessions(user_id) WHERE status = 'active';

            CREATE TABLE IF NOT EXISTS governance.capability_grants (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES governance.users(id),
                capability text NOT NULL,
                source_type text NOT NULL,
                source_id text NOT NULL,
                scope jsonb NOT NULL,
                issued_at timestamptz NOT NULL,
                expires_at timestamptz NOT NULL,
                revoked_at timestamptz,
                idempotency_key text NOT NULL UNIQUE,
                CHECK (expires_at > issued_at)
            );

            CREATE INDEX IF NOT EXISTS capability_check_idx ON governance.capability_grants(
                user_id, capability, source_type, source_id, expires_at
            ) WHERE revoked_at IS NULL;

            CREATE TABLE IF NOT EXISTS governance.punishment_executions (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                case_id bigint NOT NULL UNIQUE REFERENCES governance.court_cases(id),
                sanction_type text NOT NULL CHECK (sanction_type IN ('warning', 'game_ban', 'job_ban')),
                external_reference text NOT NULL,
                executed_at timestamptz NOT NULL DEFAULT now(),
                idempotency_key text NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS governance.audit_events (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                event_type text NOT NULL,
                actor_type text NOT NULL,
                actor_id text,
                target_type text,
                target_id text,
                entity_type text NOT NULL,
                entity_id text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                payload jsonb NOT NULL DEFAULT '{}'::jsonb
            );

            CREATE OR REPLACE FUNCTION governance.reject_immutable_mutation()
            RETURNS trigger LANGUAGE plpgsql AS $court$
            BEGIN
                RAISE EXCEPTION '% is immutable', TG_TABLE_SCHEMA || '.' || TG_TABLE_NAME;
            END;
            $court$;

            DO $court$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'rating_entries_immutable') THEN
                    CREATE TRIGGER rating_entries_immutable BEFORE UPDATE OR DELETE ON governance.rating_entries
                    FOR EACH ROW EXECUTE FUNCTION governance.reject_immutable_mutation();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'audit_events_immutable') THEN
                    CREATE TRIGGER audit_events_immutable BEFORE UPDATE OR DELETE ON governance.audit_events
                    FOR EACH ROW EXECUTE FUNCTION governance.reject_immutable_mutation();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'guilt_votes_immutable') THEN
                    CREATE TRIGGER guilt_votes_immutable BEFORE UPDATE OR DELETE ON governance.guilt_votes
                    FOR EACH ROW EXECUTE FUNCTION governance.reject_immutable_mutation();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'sentencing_votes_immutable') THEN
                    CREATE TRIGGER sentencing_votes_immutable BEFORE UPDATE OR DELETE ON governance.sentencing_votes
                    FOR EACH ROW EXECUTE FUNCTION governance.reject_immutable_mutation();
                END IF;
            END;
            $court$;

            CREATE OR REPLACE FUNCTION governance.append_rating_entry(
                p_user_id uuid,
                p_amount integer,
                p_reason text,
                p_entity_type text,
                p_entity_id text,
                p_created_by_type text,
                p_created_by_id text,
                p_idempotency_key text,
                p_metadata jsonb DEFAULT '{}'::jsonb
            ) RETURNS governance.rating_entries
            LANGUAGE plpgsql AS $court$
            DECLARE
                result governance.rating_entries;
            BEGIN
                PERFORM 1 FROM governance.users WHERE id = p_user_id FOR UPDATE;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'governance user not found';
                END IF;
                SELECT * INTO result FROM governance.rating_entries WHERE idempotency_key = p_idempotency_key;
                IF FOUND THEN
                    IF result.user_id <> p_user_id OR result.amount <> p_amount OR result.reason <> p_reason THEN
                        RAISE EXCEPTION 'idempotency key reused for different rating mutation';
                    END IF;
                    RETURN result;
                END IF;
                INSERT INTO governance.rating_entries(
                    user_id, amount, reason, entity_type, entity_id,
                    created_by_type, created_by_id, idempotency_key, metadata
                ) VALUES (
                    p_user_id, p_amount, p_reason, p_entity_type, p_entity_id,
                    p_created_by_type, p_created_by_id, p_idempotency_key, p_metadata
                ) RETURNING * INTO result;
                UPDATE governance.users SET civic_rating_cache = civic_rating_cache + p_amount, updated_at = now()
                WHERE id = p_user_id;
                RETURN result;
            END;
            $court$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS governance CASCADE;");
    }
}
