using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddBuyoutWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidAt",
                table: "Buyouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaidByEmployeeId",
                table: "Buyouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Buyouts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Buyouts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "BuyoutInspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Size = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ConditionPercentage = table.Column<int>(type: "integer", nullable: false),
                    BrandId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuyoutInspections", x => x.Id);
                    table.CheckConstraint("CK_BuyoutInspections_Condition", "\"ConditionPercentage\" BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_BuyoutInspections_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuyoutInspections_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuyoutInspections_CustomerProfiles_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "CustomerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuyoutInspections_EmployeeProfiles_InspectedByEmployeeId",
                        column: x => x.InspectedByEmployeeId,
                        principalTable: "EmployeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BuyoutProposalItems",
                columns: table => new
                {
                    BuyoutId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListedPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuyoutProposalItems", x => x.BuyoutId);
                    table.CheckConstraint("CK_BuyoutProposalItems_Price", "\"ListedPrice\" >= 0");
                    table.ForeignKey(
                        name: "FK_BuyoutProposalItems_BuyoutInspections_InspectionId",
                        column: x => x.InspectionId,
                        principalTable: "BuyoutInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BuyoutProposalItems_Buyouts_BuyoutId",
                        column: x => x.BuyoutId,
                        principalTable: "Buyouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Buyouts_PaidByEmployeeId",
                table: "Buyouts",
                column: "PaidByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_BuyoutInspections_BrandId",
                table: "BuyoutInspections",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_BuyoutInspections_CategoryId",
                table: "BuyoutInspections",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BuyoutInspections_CustomerId",
                table: "BuyoutInspections",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_BuyoutInspections_InspectedByEmployeeId",
                table: "BuyoutInspections",
                column: "InspectedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_BuyoutProposalItems_InspectionId",
                table: "BuyoutProposalItems",
                column: "InspectionId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.AddForeignKey(
                name: "FK_Buyouts_EmployeeProfiles_PaidByEmployeeId",
                table: "Buyouts",
                column: "PaidByEmployeeId",
                principalTable: "EmployeeProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Buyouts_EmployeeProfiles_PaidByEmployeeId",
                table: "Buyouts");

            migrationBuilder.DropTable(
                name: "BuyoutProposalItems");

            migrationBuilder.DropTable(
                name: "BuyoutInspections");

            migrationBuilder.DropIndex(
                name: "IX_Buyouts_PaidByEmployeeId",
                table: "Buyouts");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Buyouts");

            migrationBuilder.DropColumn(
                name: "PaidByEmployeeId",
                table: "Buyouts");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "Buyouts");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Buyouts");
        }
    }
}
