using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddContractPdfBackground : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FieldMappingsJson",
                table: "ContractTemplates",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<byte[]>(
                name: "PdfBackground",
                table: "ContractTemplates",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ContractTemplates" WHERE "PdfBackground" IS NOT NULL OR "FieldMappingsJson" <> '[]') THEN
                        RAISE EXCEPTION 'Cannot roll back while a custom contract PDF template exists';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "FieldMappingsJson",
                table: "ContractTemplates");

            migrationBuilder.DropColumn(
                name: "PdfBackground",
                table: "ContractTemplates");
        }
    }
}
