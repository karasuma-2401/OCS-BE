using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddContractSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "SignatureContent",
                table: "ContractDocuments",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureContentType",
                table: "ContractDocuments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ContractDocuments" WHERE "SignatureContent" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot roll back while saved signature images exist';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "SignatureContent",
                table: "ContractDocuments");

            migrationBuilder.DropColumn(
                name: "SignatureContentType",
                table: "ContractDocuments");
        }
    }
}
