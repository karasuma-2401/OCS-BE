using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddContractTemplateManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "TemplateVersion",
                table: "ContractDocuments",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.CreateTable(
                name: "ContractTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrganizationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HeaderText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FooterText = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DefaultTerms = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractTemplates", x => x.Id);
                    table.CheckConstraint("CK_ContractTemplates_Singleton", "\"Id\" = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ContractTemplates") OR EXISTS (SELECT 1 FROM "ContractDocuments" WHERE length("TemplateVersion") > 30) THEN
                        RAISE EXCEPTION 'Cannot roll back while contract template settings or versioned documents exist';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "ContractTemplates");

            migrationBuilder.AlterColumn<string>(
                name: "TemplateVersion",
                table: "ContractDocuments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);
        }
    }
}
