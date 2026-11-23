using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddExpiryProcessingItemEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Evidence",
                table: "ExpiryProcessingItems",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HandledAt",
                table: "ExpiryProcessingItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HandledByEmployeeId",
                table: "ExpiryProcessingItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "ExpiryProcessingItems",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientOrganization",
                table: "ExpiryProcessingItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpiryProcessingItems_HandledByEmployeeId",
                table: "ExpiryProcessingItems",
                column: "HandledByEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpiryProcessingItems_EmployeeProfiles_HandledByEmployeeId",
                table: "ExpiryProcessingItems",
                column: "HandledByEmployeeId",
                principalTable: "EmployeeProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpiryProcessingItems_EmployeeProfiles_HandledByEmployeeId",
                table: "ExpiryProcessingItems");

            migrationBuilder.DropIndex(
                name: "IX_ExpiryProcessingItems_HandledByEmployeeId",
                table: "ExpiryProcessingItems");

            migrationBuilder.DropColumn(
                name: "Evidence",
                table: "ExpiryProcessingItems");

            migrationBuilder.DropColumn(
                name: "HandledAt",
                table: "ExpiryProcessingItems");

            migrationBuilder.DropColumn(
                name: "HandledByEmployeeId",
                table: "ExpiryProcessingItems");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "ExpiryProcessingItems");

            migrationBuilder.DropColumn(
                name: "RecipientOrganization",
                table: "ExpiryProcessingItems");
        }
    }
}
