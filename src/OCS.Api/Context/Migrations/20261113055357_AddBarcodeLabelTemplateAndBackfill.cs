using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddBarcodeLabelTemplateAndBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve existing non-empty identifiers. Lock writes while missing
            // values and the case-insensitive uniqueness index are installed.
            migrationBuilder.Sql("""
                LOCK TABLE "Products" IN SHARE ROW EXCLUSIVE MODE;
                DO $$
                DECLARE item record; candidate text;
                BEGIN
                  FOR item IN SELECT "Id" FROM "Products" WHERE "Barcode" IS NULL OR btrim("Barcode", E' \t\n\r') = '' LOOP
                    LOOP
                      candidate := 'OCS-' || upper(substr(md5(item."Id"::text || random()::text || clock_timestamp()::text), 1, 16));
                      EXIT WHEN NOT EXISTS (SELECT 1 FROM "Products" WHERE upper(btrim("Barcode", E' \t\n\r')) = candidate);
                    END LOOP;
                    UPDATE "Products" SET "Barcode" = candidate WHERE "Id" = item."Id";
                  END LOOP;
                END $$;
                CREATE UNIQUE INDEX "IX_Products_Barcode_Normalized" ON "Products" (upper(btrim("Barcode", E' \t\n\r')));
                """);
            migrationBuilder.CreateTable(
                name: "BarcodeLabelTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    WidthMm = table.Column<int>(type: "integer", nullable: false),
                    HeightMm = table.Column<int>(type: "integer", nullable: false),
                    Columns = table.Column<int>(type: "integer", nullable: false),
                    FontSizePt = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BarcodeLabelTemplates", x => x.Id);
                    table.CheckConstraint("CK_BarcodeLabelTemplates_Singleton", "\"Id\" = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Generated product identifiers remain in place to preserve printed labels.
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Products_Barcode_Normalized\";");
            migrationBuilder.DropTable(
                name: "BarcodeLabelTemplates");
        }
    }
}
