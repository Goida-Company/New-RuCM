using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260822040000_ReputationSupportIdempotencyCompatibility")]
public sealed class ReputationSupportIdempotencyCompatibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- AHelp observations created before support was retired are immutable and use the
            -- support track. The scheduler now emits the same source event on moderation while
            -- intentionally preserving the original idempotency key. Treat that one historical
            -- track transition as the same observation; all other semantic key collisions remain
            -- hard failures.
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
                    -- Compatibility for immutable AHelp evidence written before the support path
                    -- was folded into moderation. RefreshUserAsync already folds these legacy
                    -- support rows into the moderation posterior, so inserting a second row would
                    -- double-count the same resolved AHelp.
                    IF result.user_id = p_user_id
                       AND result.track = 'support'
                       AND p_track = 'moderation'
                       AND result.success_weight = p_success_weight
                       AND result.failure_weight = p_failure_weight
                       AND result.serious_negative = p_serious_negative
                       AND result.reason = p_reason
                       AND p_reason = 'support.ahelp_resolved'
                       AND result.entity_type = p_entity_type
                       AND result.entity_id = p_entity_id
                       AND p_entity_type = 'ahelp_ticket'
                       AND p_idempotency_key LIKE 'reputation:ahelp:%:resolved' THEN
                        RETURN result;
                    END IF;

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
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
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
            """);
    }
}
