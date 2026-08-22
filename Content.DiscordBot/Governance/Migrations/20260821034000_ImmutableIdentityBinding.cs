using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.DiscordBot.Governance.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20260821034000_ImmutableIdentityBinding")]
public sealed class ImmutableIdentityBinding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS governance.identity_bindings (
                user_id uuid PRIMARY KEY REFERENCES governance.users(id) ON DELETE RESTRICT,
                ss14_user_id uuid NOT NULL UNIQUE,
                discord_user_id bigint NOT NULL UNIQUE,
                bound_at timestamptz NOT NULL DEFAULT now(),
                source text NOT NULL DEFAULT 'migration'
            );

            -- The Governance profile is authoritative here. If the game-link table was changed by
            -- an older rebind-capable client but Governance rejected it, do not bless that failed
            -- attempt as a new permanent identity.
            INSERT INTO governance.identity_bindings(user_id, ss14_user_id, discord_user_id, bound_at, source)
            SELECT id, ss14_user_id, discord_user_id, COALESCE(created_at, now()), 'governance_user'
            FROM governance.users
            WHERE discord_user_id IS NOT NULL AND discord_user_id > 0
            ON CONFLICT DO NOTHING;

            CREATE OR REPLACE FUNCTION governance.enforce_immutable_user_identity()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $governance$
            DECLARE
                target_user_id uuid;
                bound_discord bigint;
                bound_user_id uuid;
                current_discord bigint;
                existing_by_discord uuid;
            BEGIN
                IF TG_OP = 'UPDATE' AND OLD.ss14_user_id IS DISTINCT FROM NEW.ss14_user_id THEN
                    RAISE EXCEPTION 'SS14 identity is immutable for Governance user %', OLD.id
                        USING ERRCODE = '23505';
                END IF;

                IF TG_OP = 'INSERT' THEN
                    -- Compatibility with old INSERT ... ON CONFLICT synchronization used by Court.
                    -- Existing SS14 profiles are authoritative: ignore any Discord proposed by that
                    -- legacy upsert. First-time links are performed through GovernanceIdentityService.
                    SELECT id, discord_user_id
                    INTO target_user_id, current_discord
                    FROM governance.users
                    WHERE ss14_user_id = NEW.ss14_user_id
                    LIMIT 1;

                    IF target_user_id IS NOT NULL THEN
                        NEW.discord_user_id := current_discord;
                        RETURN NEW;
                    END IF;

                    target_user_id := NEW.id;
                ELSE
                    target_user_id := OLD.id;
                END IF;

                IF NEW.discord_user_id IS NULL THEN
                    RETURN NEW;
                END IF;

                SELECT discord_user_id INTO bound_discord
                FROM governance.identity_bindings
                WHERE user_id = target_user_id;

                IF bound_discord IS NOT NULL AND bound_discord <> NEW.discord_user_id THEN
                    RAISE EXCEPTION 'SS14 % is permanently bound to another Discord account', NEW.ss14_user_id
                        USING ERRCODE = '23505';
                END IF;

                SELECT user_id INTO bound_user_id
                FROM governance.identity_bindings
                WHERE discord_user_id = NEW.discord_user_id;

                IF bound_user_id IS NOT NULL AND bound_user_id <> target_user_id THEN
                    RAISE EXCEPTION 'Discord % is permanently bound to another SS14 account', NEW.discord_user_id
                        USING ERRCODE = '23505';
                END IF;

                SELECT id INTO existing_by_discord
                FROM governance.users
                WHERE discord_user_id = NEW.discord_user_id
                  AND id <> target_user_id
                LIMIT 1;

                IF existing_by_discord IS NOT NULL THEN
                    RAISE EXCEPTION 'Discord % is already attached to another Governance profile', NEW.discord_user_id
                        USING ERRCODE = '23505';
                END IF;

                SELECT discord_user_id INTO current_discord
                FROM governance.users
                WHERE id = target_user_id;

                IF current_discord IS NOT NULL
                   AND current_discord <> NEW.discord_user_id THEN
                    RAISE EXCEPTION 'Governance profile % is already attached to another Discord account', target_user_id
                        USING ERRCODE = '23505';
                END IF;

                RETURN NEW;
            END;
            $governance$;

            CREATE OR REPLACE FUNCTION governance.remember_immutable_user_identity()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $governance$
            BEGIN
                IF NEW.discord_user_id IS NULL THEN
                    RETURN NEW;
                END IF;

                INSERT INTO governance.identity_bindings(user_id, ss14_user_id, discord_user_id, bound_at, source)
                VALUES (NEW.id, NEW.ss14_user_id, NEW.discord_user_id, now(), 'users_trigger')
                ON CONFLICT (user_id) DO NOTHING;
                RETURN NEW;
            END;
            $governance$;

            CREATE OR REPLACE FUNCTION governance.prevent_identity_binding_mutation()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $governance$
            BEGIN
                RAISE EXCEPTION 'Governance identity bindings are immutable'
                    USING ERRCODE = '55000';
            END;
            $governance$;

            DROP TRIGGER IF EXISTS governance_users_identity_rebind ON governance.users;
            DROP TRIGGER IF EXISTS governance_users_identity_immutable_insert ON governance.users;
            DROP TRIGGER IF EXISTS governance_users_identity_immutable_update ON governance.users;
            DROP TRIGGER IF EXISTS governance_users_identity_remember_insert ON governance.users;
            DROP TRIGGER IF EXISTS governance_users_identity_remember_update ON governance.users;
            DROP TRIGGER IF EXISTS governance_identity_bindings_immutable ON governance.identity_bindings;

            CREATE TRIGGER governance_users_identity_immutable_insert
            BEFORE INSERT ON governance.users
            FOR EACH ROW
            EXECUTE FUNCTION governance.enforce_immutable_user_identity();

            CREATE TRIGGER governance_users_identity_immutable_update
            BEFORE UPDATE OF ss14_user_id, discord_user_id ON governance.users
            FOR EACH ROW
            EXECUTE FUNCTION governance.enforce_immutable_user_identity();

            CREATE TRIGGER governance_users_identity_remember_insert
            AFTER INSERT ON governance.users
            FOR EACH ROW
            EXECUTE FUNCTION governance.remember_immutable_user_identity();

            CREATE TRIGGER governance_users_identity_remember_update
            AFTER UPDATE OF discord_user_id ON governance.users
            FOR EACH ROW
            WHEN (NEW.discord_user_id IS NOT NULL)
            EXECUTE FUNCTION governance.remember_immutable_user_identity();

            CREATE TRIGGER governance_identity_bindings_immutable
            BEFORE UPDATE OR DELETE ON governance.identity_bindings
            FOR EACH ROW
            EXECUTE FUNCTION governance.prevent_identity_binding_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS governance_identity_bindings_immutable ON governance.identity_bindings;
            DROP TRIGGER IF EXISTS governance_users_identity_remember_update ON governance.users;
            DROP TRIGGER IF EXISTS governance_users_identity_remember_insert ON governance.users;
            DROP TRIGGER IF EXISTS governance_users_identity_immutable_update ON governance.users;
            DROP TRIGGER IF EXISTS governance_users_identity_immutable_insert ON governance.users;
            DROP FUNCTION IF EXISTS governance.prevent_identity_binding_mutation();
            DROP FUNCTION IF EXISTS governance.remember_immutable_user_identity();
            DROP FUNCTION IF EXISTS governance.enforce_immutable_user_identity();
            DROP TABLE IF EXISTS governance.identity_bindings;
            """);
    }
}
