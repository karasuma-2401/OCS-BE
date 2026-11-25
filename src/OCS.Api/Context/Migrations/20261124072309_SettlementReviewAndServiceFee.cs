using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class SettlementReviewAndServiceFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Payouts_Status",
                table: "Payouts");

            migrationBuilder.DropIndex(
                name: "IX_PayoutDetails_IncidentReportId",
                table: "PayoutDetails");

            migrationBuilder.DropIndex(
                name: "IX_PayoutDetails_ProductId_Type",
                table: "PayoutDetails");

            migrationBuilder.AddColumn<string>(
                name: "BankAccountHolder",
                table: "Payouts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "Payouts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankCode",
                table: "Payouts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAt",
                table: "Payouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByEmployeeId",
                table: "Payouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Payouts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReleased",
                table: "PayoutDetails",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("""
                UPDATE "Payouts" AS p SET
                    "BankCode" = c."BankCodeSnapshot",
                    "BankAccountNumber" = c."BankAccountNumberSnapshot",
                    "BankAccountHolder" = c."BankAccountHolderSnapshot"
                FROM "ConsignmentContracts" AS c
                WHERE p."ConsignmentContractId" = c."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_RejectedByEmployeeId",
                table: "Payouts",
                column: "RejectedByEmployeeId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payouts_Status",
                table: "Payouts",
                sql: "\"Status\" IN ('Pending', 'Approved', 'Transferred', 'Completed', 'Rejected')");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutDetails_IncidentReportId",
                table: "PayoutDetails",
                column: "IncidentReportId",
                unique: true,
                filter: "\"IncidentReportId\" IS NOT NULL AND NOT \"IsReleased\"");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutDetails_ProductId_Type",
                table: "PayoutDetails",
                columns: new[] { "ProductId", "Type" },
                unique: true,
                filter: "\"ProductId\" IS NOT NULL AND NOT \"IsReleased\"");

            migrationBuilder.AddForeignKey(
                name: "FK_Payouts_EmployeeProfiles_RejectedByEmployeeId",
                table: "Payouts",
                column: "RejectedByEmployeeId",
                principalTable: "EmployeeProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payouts_EmployeeProfiles_RejectedByEmployeeId",
                table: "Payouts");

            migrationBuilder.DropIndex(
                name: "IX_Payouts_RejectedByEmployeeId",
                table: "Payouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payouts_Status",
                table: "Payouts");

            migrationBuilder.DropIndex(
                name: "IX_PayoutDetails_IncidentReportId",
                table: "PayoutDetails");

            migrationBuilder.DropIndex(
                name: "IX_PayoutDetails_ProductId_Type",
                table: "PayoutDetails");

            migrationBuilder.DropColumn(
                name: "BankAccountHolder",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "BankCode",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "RejectedByEmployeeId",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "IsReleased",
                table: "PayoutDetails");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payouts_Status",
                table: "Payouts",
                sql: "\"Status\" IN ('Pending', 'Approved', 'Transferred', 'Completed')");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutDetails_IncidentReportId",
                table: "PayoutDetails",
                column: "IncidentReportId",
                unique: true,
                filter: "\"IncidentReportId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutDetails_ProductId_Type",
                table: "PayoutDetails",
                columns: new[] { "ProductId", "Type" },
                unique: true,
                filter: "\"ProductId\" IS NOT NULL");
        }
    }
}
