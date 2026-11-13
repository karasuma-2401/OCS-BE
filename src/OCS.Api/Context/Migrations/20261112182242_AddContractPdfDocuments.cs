using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddContractPdfDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContractDocuments",
                columns: table => new
                {
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    Terms = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: true),
                    TemplateVersion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractDocuments", x => x.ContractId);
                    table.ForeignKey(
                        name: "FK_ContractDocuments_ConsignmentContracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "ConsignmentContracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ContractDocuments") THEN
                        RAISE EXCEPTION 'Cannot roll back while saved contract PDFs exist';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "ContractDocuments");
        }
    }
}
