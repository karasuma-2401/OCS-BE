using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OCS.Api.Context;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20261115190000_AddContractTermsAndEmployeeSnapshots")]
    public partial class AddContractTermsAndEmployeeSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TermsSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(6000)",
                maxLength: 6000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateTitleSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrganizationNameSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeaderTextSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FooterTextSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployeeNameSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployeeTitleSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployeePhoneSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TermsSnapshot", table: "ConsignmentContracts");
            migrationBuilder.DropColumn(name: "TemplateTitleSnapshot", table: "ConsignmentContracts");
            migrationBuilder.DropColumn(name: "OrganizationNameSnapshot", table: "ConsignmentContracts");
            migrationBuilder.DropColumn(name: "HeaderTextSnapshot", table: "ConsignmentContracts");
            migrationBuilder.DropColumn(name: "FooterTextSnapshot", table: "ConsignmentContracts");
            migrationBuilder.DropColumn(name: "EmployeeNameSnapshot", table: "ConsignmentContracts");
            migrationBuilder.DropColumn(name: "EmployeeTitleSnapshot", table: "ConsignmentContracts");
            migrationBuilder.DropColumn(name: "EmployeePhoneSnapshot", table: "ConsignmentContracts");
        }
    }
}
