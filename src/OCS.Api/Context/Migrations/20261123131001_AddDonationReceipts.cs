using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddDonationReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DonationReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiryProcessingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ContractNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RecipientOrganization = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Evidence = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    HandledByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    HandledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProductsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DonationReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DonationReceipts_EmployeeProfiles_HandledByEmployeeId",
                        column: x => x.HandledByEmployeeId,
                        principalTable: "EmployeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DonationReceipts_ExpiryProcessings_ExpiryProcessingId",
                        column: x => x.ExpiryProcessingId,
                        principalTable: "ExpiryProcessings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DonationReceipts_ExpiryProcessingId",
                table: "DonationReceipts",
                column: "ExpiryProcessingId");

            migrationBuilder.CreateIndex(
                name: "IX_DonationReceipts_HandledByEmployeeId",
                table: "DonationReceipts",
                column: "HandledByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_DonationReceipts_ReceiptNumber",
                table: "DonationReceipts",
                column: "ReceiptNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DonationReceipts");
        }
    }
}
