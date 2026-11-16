using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddBuyoutAutoApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Buyouts_Approver",
                table: "Buyouts");

            migrationBuilder.AddColumn<decimal>(
                name: "AppliedApprovalThreshold",
                table: "Buyouts",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovalConfigurationVersion",
                table: "Buyouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovalDecidedAt",
                table: "Buyouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalDecisionReason",
                table: "Buyouts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAutoApproved",
                table: "Buyouts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Buyouts_Approver",
                table: "Buyouts",
                sql: "\"Status\" <> 'Approved'\r\nOR \"ApprovedByEmployeeId\" IS NOT NULL OR \"IsAutoApproved\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Buyouts_Approver",
                table: "Buyouts");

            migrationBuilder.DropColumn(
                name: "AppliedApprovalThreshold",
                table: "Buyouts");

            migrationBuilder.DropColumn(
                name: "ApprovalConfigurationVersion",
                table: "Buyouts");

            migrationBuilder.DropColumn(
                name: "ApprovalDecidedAt",
                table: "Buyouts");

            migrationBuilder.DropColumn(
                name: "ApprovalDecisionReason",
                table: "Buyouts");

            migrationBuilder.DropColumn(
                name: "IsAutoApproved",
                table: "Buyouts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Buyouts_Approver",
                table: "Buyouts",
                sql: "\"Status\" <> 'Approved'\r\nOR \"ApprovedByEmployeeId\" IS NOT NULL");
        }
    }
}
