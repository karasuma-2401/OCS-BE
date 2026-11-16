using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddProductImageContentAndCustomerVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Content",
                table: "ProductImages",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "ProductImages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // PostgreSQL already supplies the system column xmin on every table.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ProductImages" WHERE "Content" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot roll back while product image data exists';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "Content",
                table: "ProductImages");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "ProductImages");

        }
    }
}
