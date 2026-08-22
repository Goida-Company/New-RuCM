using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260821032000_DisableLegacyLinearRating")]
public sealed class DisableLegacyLinearRating : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- Keep the old immutable rating ledger callable for backward compatibility while old
            -- workflows are phased out. It is no longer allowed to mutate the authoritative score.
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
            LANGUAGE plpgsql AS $legacy_rating$
            DECLARE result governance.rating_entries;
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
                RETURN result;
            END;
            $legacy_rating$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
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
            LANGUAGE plpgsql AS $legacy_rating$
            DECLARE result governance.rating_entries;
            BEGIN
                PERFORM 1 FROM governance.users WHERE id = p_user_id FOR UPDATE;
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'governance user not found';
                END IF;
                SELECT * INTO result FROM governance.rating_entries WHERE idempotency_key = p_idempotency_key;
                IF FOUND THEN RETURN result; END IF;
                INSERT INTO governance.rating_entries(
                    user_id, amount, reason, entity_type, entity_id,
                    created_by_type, created_by_id, idempotency_key, metadata
                ) VALUES (
                    p_user_id, p_amount, p_reason, p_entity_type, p_entity_id,
                    p_created_by_type, p_created_by_id, p_idempotency_key, p_metadata
                ) RETURNING * INTO result;
                UPDATE governance.users
                SET civic_rating_cache = civic_rating_cache + p_amount, updated_at = now()
                WHERE id = p_user_id;
                RETURN result;
            END;
            $legacy_rating$;
            """);
    }
}
