using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeAccountEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Freeze the v1 normalization policy here: historical migrations must not depend on live helpers.
            // EF executes this migration transactionally; the lock closes the preflight/index race.
            migrationBuilder.Sql("""
                LOCK TABLE "TAIKHOAN" IN SHARE ROW EXCLUSIVE MODE;
                CREATE TEMP TABLE ocs_auth_email_backfill ON COMMIT DROP AS
                SELECT "Id", CASE WHEN length(btrim("Email", E' \t\n\r\013\f')) <= 254
                    AND length(split_part(btrim("Email", E' \t\n\r\013\f'), '@', 1)) <= 64
                    AND btrim("Email", E' \t\n\r\013\f') COLLATE "C" ~ '^[A-Za-z0-9!#$%&''*+/=?^_`{|}~-]+(?:\.[A-Za-z0-9!#$%&''*+/=?^_`{|}~-]+)*@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$'
                THEN upper(btrim("Email", E' \t\n\r\013\f') COLLATE "C") ELSE NULL END AS normalized
                FROM "TAIKHOAN";

                DO $preflight$
                DECLARE conflict_groups integer;
                BEGIN
                    SELECT count(*) INTO conflict_groups FROM (
                        SELECT normalized FROM ocs_auth_email_backfill
                        WHERE normalized IS NOT NULL
                        GROUP BY normalized HAVING count(*) > 1
                    ) conflicts;
                    IF conflict_groups > 0 THEN
                        RAISE EXCEPTION 'Email normalization blocked: % conflict group(s). Review docs/AUTH_DATA_MIGRATION.md; no accounts were merged or deleted.', conflict_groups;
                    END IF;
                END $preflight$;

                UPDATE "TAIKHOAN" account SET "NormalizedEmail" = candidate.normalized
                FROM ocs_auth_email_backfill candidate
                WHERE account."Id" = candidate."Id"
                    AND account."NormalizedEmail" IS DISTINCT FROM candidate.normalized;
                """);

            migrationBuilder.DropIndex(
                name: "IX_TAIKHOAN_AccountType_Email",
                table: "TAIKHOAN");

            migrationBuilder.CreateIndex(
                name: "IX_TAIKHOAN_NormalizedEmail",
                table: "TAIKHOAN",
                column: "NormalizedEmail",
                unique: true,
                filter: "\"NormalizedEmail\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TAIKHOAN_NormalizedEmail",
                table: "TAIKHOAN",
                sql: "\"NormalizedEmail\" IS NOT DISTINCT FROM (CASE WHEN length(btrim(\"Email\", E' \\t\\n\\r\\013\\f')) <= 254\n    AND length(split_part(btrim(\"Email\", E' \\t\\n\\r\\013\\f'), '@', 1)) <= 64\n    AND btrim(\"Email\", E' \\t\\n\\r\\013\\f') COLLATE \"C\" ~ '^[A-Za-z0-9!#$%&''*+/=?^_`{|}~-]+(?:\\.[A-Za-z0-9!#$%&''*+/=?^_`{|}~-]+)*@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$'\nTHEN upper(btrim(\"Email\", E' \\t\\n\\r\\013\\f') COLLATE \"C\") ELSE NULL END)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TAIKHOAN_NormalizedEmail",
                table: "TAIKHOAN");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TAIKHOAN_NormalizedEmail",
                table: "TAIKHOAN");

            migrationBuilder.CreateIndex(
                name: "IX_TAIKHOAN_AccountType_Email",
                table: "TAIKHOAN",
                columns: new[] { "AccountType", "Email" },
                unique: true);
        }
    }
}
