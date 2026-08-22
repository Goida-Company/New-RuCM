using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260821030000_ReputationIdentityV2")]
public sealed class ReputationIdentityV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- SS14 is the authoritative Governance identity. Discord is an optional transport.
            ALTER TABLE governance.users ALTER COLUMN discord_user_id DROP NOT NULL;
            UPDATE governance.users SET discord_user_id = NULL, updated_at = now()
            WHERE discord_user_id <= 0;
            ALTER TABLE governance.users DROP CONSTRAINT IF EXISTS users_discord_user_id_key;
            DROP INDEX IF EXISTS governance."IX_users_DiscordUserId";
            DROP INDEX IF EXISTS governance.ix_users_discord_user_id;
            CREATE UNIQUE INDEX IF NOT EXISTS users_discord_user_id_unique_idx
                ON governance.users(discord_user_id)
                WHERE discord_user_id IS NOT NULL;
            ALTER TABLE governance.users ALTER COLUMN civic_rating_cache SET DEFAULT 500;

            -- Keep a durable history when Discord is linked, unlinked or rebound.
            CREATE TABLE IF NOT EXISTS governance.identity_links (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES governance.users(id) ON DELETE CASCADE,
                discord_user_id bigint NOT NULL,
                linked_at timestamptz NOT NULL DEFAULT now(),
                unlinked_at timestamptz,
                source text NOT NULL,
                metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
                CHECK (discord_user_id > 0),
                CHECK (unlinked_at IS NULL OR unlinked_at >= linked_at)
            );
            CREATE UNIQUE INDEX IF NOT EXISTS identity_links_one_current_user_idx
                ON governance.identity_links(user_id) WHERE unlinked_at IS NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS identity_links_one_current_discord_idx
                ON governance.identity_links(discord_user_id) WHERE unlinked_at IS NULL;
            INSERT INTO governance.identity_links(user_id, discord_user_id, linked_at, source)
            SELECT id, discord_user_id, created_at, 'migration'
            FROM governance.users
            WHERE discord_user_id IS NOT NULL AND discord_user_id > 0
            ON CONFLICT DO NOTHING;

            -- Paths are voluntary specialisations. A user may keep at most two active paths.
            CREATE TABLE IF NOT EXISTS governance.service_paths (
                user_id uuid NOT NULL REFERENCES governance.users(id) ON DELETE CASCADE,
                slot smallint NOT NULL CHECK (slot BETWEEN 1 AND 2),
                track text NOT NULL CHECK (track IN ('support','moderation','jury','event','contributor')),
                selected_at timestamptz NOT NULL DEFAULT now(),
                changed_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, slot),
                UNIQUE (user_id, track)
            );

            -- Existing installations keep up to two strongest existing tracks as their initial paths.
            INSERT INTO governance.service_paths(user_id, slot, track, selected_at, changed_at)
            SELECT user_id, ordinal::smallint, track, now(), now()
            FROM (
                SELECT q.user_id,
                       q.track,
                       row_number() OVER (
                           PARTITION BY q.user_id
                           ORDER BY q.level DESC,
                                    CASE q.track WHEN 'moderation' THEN 1 WHEN 'jury' THEN 2 ELSE 3 END,
                                    q.track) AS ordinal
                FROM governance.qualifications q
                WHERE q.level > 0 AND q.track IN ('moderation','jury','event')
            ) ranked
            WHERE ordinal <= 2
            ON CONFLICT DO NOTHING;

            -- Expand qualification/assignment vocabulary without changing existing rows.
            DO $governance$
            DECLARE constraint_name text;
            BEGIN
                FOR constraint_name IN
                    SELECT conname FROM pg_constraint
                    WHERE conrelid = 'governance.qualifications'::regclass
                      AND contype = 'c'
                      AND pg_get_constraintdef(oid) ILIKE '%track%'
                LOOP
                    EXECUTE format('ALTER TABLE governance.qualifications DROP CONSTRAINT %I', constraint_name);
                END LOOP;
                ALTER TABLE governance.qualifications
                    ADD CONSTRAINT qualifications_track_valid
                    CHECK (track IN ('support','moderation','jury','event','contributor'));

                FOR constraint_name IN
                    SELECT conname FROM pg_constraint
                    WHERE conrelid = 'governance.service_assignments'::regclass
                      AND contype = 'c'
                      AND pg_get_constraintdef(oid) ILIKE '%track%'
                LOOP
                    EXECUTE format('ALTER TABLE governance.service_assignments DROP CONSTRAINT %I', constraint_name);
                END LOOP;
                ALTER TABLE governance.service_assignments
                    ADD CONSTRAINT service_assignments_track_valid
                    CHECK (track IN ('support','moderation','jury','event','contributor'));
            END;
            $governance$;

            -- Immutable statistical evidence. We keep alpha/beta evidence rather than mutable points.
            CREATE TABLE IF NOT EXISTS governance.reputation_observations (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES governance.users(id) ON DELETE CASCADE,
                track text NOT NULL CHECK (track IN ('general','support','moderation','jury','event','contributor')),
                success_weight double precision NOT NULL DEFAULT 0 CHECK (success_weight >= 0),
                failure_weight double precision NOT NULL DEFAULT 0 CHECK (failure_weight >= 0),
                serious_negative boolean NOT NULL DEFAULT false,
                reason text NOT NULL,
                entity_type text NOT NULL,
                entity_id text NOT NULL,
                occurred_at timestamptz NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by_type text NOT NULL,
                created_by_id text,
                idempotency_key text NOT NULL UNIQUE,
                metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
                CHECK (success_weight > 0 OR failure_weight > 0)
            );
            CREATE INDEX IF NOT EXISTS reputation_observations_user_track_time_idx
                ON governance.reputation_observations(user_id, track, occurred_at DESC);

            -- Cached posterior. Source evidence remains immutable and is always reproducible.
            CREATE TABLE IF NOT EXISTS governance.reputation_snapshots (
                user_id uuid NOT NULL REFERENCES governance.users(id) ON DELETE CASCADE,
                track text NOT NULL CHECK (track IN ('general','support','moderation','jury','event','contributor')),
                alpha double precision NOT NULL,
                beta double precision NOT NULL,
                mean double precision NOT NULL,
                lower_bound double precision NOT NULL,
                evidence_weight double precision NOT NULL,
                score integer NOT NULL CHECK (score BETWEEN 0 AND 1000),
                calculated_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, track),
                CHECK (alpha > 0 AND beta > 0),
                CHECK (mean BETWEEN 0 AND 1),
                CHECK (lower_bound BETWEEN 0 AND 1),
                CHECK (evidence_weight >= 0)
            );

            CREATE TABLE IF NOT EXISTS governance.game_activity_snapshots (
                user_id uuid PRIMARY KEY REFERENCES governance.users(id) ON DELETE CASCADE,
                overall_hours double precision NOT NULL DEFAULT 0 CHECK (overall_hours >= 0),
                active_weeks integer NOT NULL DEFAULT 0 CHECK (active_weeks >= 0),
                account_age_days integer NOT NULL DEFAULT 0 CHECK (account_age_days >= 0),
                activity_index double precision NOT NULL DEFAULT 0 CHECK (activity_index BETWEEN 0 AND 1),
                evidence_weight double precision NOT NULL DEFAULT 0 CHECK (evidence_weight >= 0),
                calculated_at timestamptz NOT NULL DEFAULT now()
            );

            -- Contributor evidence is intentionally semantic: impact/quality/stability, not line count.
            CREATE TABLE IF NOT EXISTS governance.contribution_events (
                id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES governance.users(id) ON DELETE CASCADE,
                reference text NOT NULL,
                contribution_kind text NOT NULL,
                impact double precision NOT NULL CHECK (impact BETWEEN 0 AND 3),
                quality double precision NOT NULL CHECK (quality BETWEEN 0 AND 1.5),
                stability double precision NOT NULL CHECK (stability BETWEEN 0 AND 1.5),
                occurred_at timestamptz NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by_discord_id bigint,
                idempotency_key text NOT NULL UNIQUE,
                metadata jsonb NOT NULL DEFAULT '{}'::jsonb
            );
            CREATE INDEX IF NOT EXISTS contribution_events_user_time_idx
                ON governance.contribution_events(user_id, occurred_at DESC);

            -- Preserve meaningful legacy history while intentionally dropping invitation farming.
            INSERT INTO governance.reputation_observations(
                user_id, track, success_weight, failure_weight, serious_negative, reason,
                entity_type, entity_id, occurred_at, created_at, created_by_type, created_by_id,
                idempotency_key, metadata)
            SELECT r.user_id,
                   CASE
                       WHEN r.reason LIKE 'jury_%' THEN 'jury'
                       WHEN r.reason LIKE 'event_%' THEN 'event'
                       WHEN r.reason LIKE 'moderation_%' THEN 'moderation'
                       ELSE 'general'
                   END,
                   CASE WHEN r.amount > 0 THEN LEAST(r.amount / 10.0, 3.0) ELSE 0 END,
                   CASE WHEN r.amount < 0 THEN LEAST(abs(r.amount) / 10.0, 5.0) ELSE 0 END,
                   r.reason IN ('false_report','jury_duty_failed','event_review_failed','moderation_review_failed'),
                   'legacy:' || r.reason,
                   r.entity_type,
                   r.entity_id,
                   r.created_at,
                   r.created_at,
                   r.created_by_type,
                   r.created_by_id,
                   'legacy-rating:' || r.id,
                   r.metadata
            FROM governance.rating_entries r
            WHERE r.reason NOT LIKE '%invite_accepted'
              AND r.reason NOT LIKE '%invite_declined'
              AND r.reason NOT LIKE '%invite_expired'
              AND r.reason NOT LIKE '%accept_reward_rollback'
              AND r.amount <> 0
            ON CONFLICT (idempotency_key) DO NOTHING;

            UPDATE governance.users SET civic_rating_cache = 500, updated_at = now();

            CREATE OR REPLACE FUNCTION governance.append_reputation_observation(
                p_user_id uuid,
                p_track text,
                p_success_weight double precision,
                p_failure_weight double precision,
                p_serious_negative boolean,
                p_reason text,
                p_entity_type text,
                p_entity_id text,
                p_occurred_at timestamptz,
                p_created_by_type text,
                p_created_by_id text,
                p_idempotency_key text,
                p_metadata jsonb DEFAULT '{}'::jsonb
            ) RETURNS governance.reputation_observations
            LANGUAGE plpgsql AS $reputation$
            DECLARE result governance.reputation_observations;
            BEGIN
                PERFORM 1 FROM governance.users WHERE id = p_user_id FOR UPDATE;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'governance user not found';
                END IF;
                SELECT * INTO result
                FROM governance.reputation_observations
                WHERE idempotency_key = p_idempotency_key;
                IF FOUND THEN
                    IF result.user_id <> p_user_id OR result.track <> p_track OR
                       result.success_weight <> p_success_weight OR result.failure_weight <> p_failure_weight OR
                       result.reason <> p_reason THEN
                        RAISE EXCEPTION 'idempotency key reused for different reputation observation';
                    END IF;
                    RETURN result;
                END IF;
                INSERT INTO governance.reputation_observations(
                    user_id, track, success_weight, failure_weight, serious_negative, reason,
                    entity_type, entity_id, occurred_at, created_by_type, created_by_id,
                    idempotency_key, metadata)
                VALUES (
                    p_user_id, p_track, p_success_weight, p_failure_weight, p_serious_negative, p_reason,
                    p_entity_type, p_entity_id, p_occurred_at, p_created_by_type, p_created_by_id,
                    p_idempotency_key, p_metadata)
                RETURNING * INTO result;
                RETURN result;
            END;
            $reputation$;

            DO $governance$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'reputation_observations_immutable') THEN
                    CREATE TRIGGER reputation_observations_immutable
                    BEFORE UPDATE OR DELETE ON governance.reputation_observations
                    FOR EACH ROW EXECUTE FUNCTION governance.reject_immutable_mutation();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'contribution_events_immutable') THEN
                    CREATE TRIGGER contribution_events_immutable
                    BEFORE UPDATE OR DELETE ON governance.contribution_events
                    FOR EACH ROW EXECUTE FUNCTION governance.reject_immutable_mutation();
                END IF;
            END;
            $governance$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP FUNCTION IF EXISTS governance.append_reputation_observation(
                uuid,text,double precision,double precision,boolean,text,text,text,timestamptz,text,text,text,jsonb);
            DROP TABLE IF EXISTS governance.contribution_events;
            DROP TABLE IF EXISTS governance.game_activity_snapshots;
            DROP TABLE IF EXISTS governance.reputation_snapshots;
            DROP TABLE IF EXISTS governance.reputation_observations;
            DROP TABLE IF EXISTS governance.service_paths;
            DROP TABLE IF EXISTS governance.identity_links;
            DROP INDEX IF EXISTS governance.users_discord_user_id_unique_idx;
            ALTER TABLE governance.users ALTER COLUMN civic_rating_cache SET DEFAULT 0;
            """);
    }
}
