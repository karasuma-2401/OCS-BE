using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddPayoutDisputeWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisputeReason",
                table: "Payouts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisputeResolutionNote",
                table: "Payouts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DisputeResolvedAt",
                table: "Payouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DisputeResolvedByEmployeeId",
                table: "Payouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisputeStatus",
                table: "Payouts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DisputedAt",
                table: "Payouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_DisputeResolvedByEmployeeId",
                table: "Payouts",
                column: "DisputeResolvedByEmployeeId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payouts_DisputeStatus",
                table: "Payouts",
                sql: "\"DisputeStatus\" IS NULL OR \"DisputeStatus\" IN ('Open', 'Resolved', 'Rejected')");

            migrationBuilder.AddForeignKey(
                name: "FK_Payouts_EmployeeProfiles_DisputeResolvedByEmployeeId",
                table: "Payouts",
                column: "DisputeResolvedByEmployeeId",
                principalTable: "EmployeeProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payouts_EmployeeProfiles_DisputeResolvedByEmployeeId",
                table: "Payouts");

            migrationBuilder.DropIndex(
                name: "IX_Payouts_DisputeResolvedByEmployeeId",
                table: "Payouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payouts_DisputeStatus",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "DisputeReason",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "DisputeResolutionNote",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "DisputeResolvedAt",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "DisputeResolvedByEmployeeId",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "DisputeStatus",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "DisputedAt",
                table: "Payouts");
        }
    }
}
