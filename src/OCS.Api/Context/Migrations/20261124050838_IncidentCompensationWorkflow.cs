using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class IncidentCompensationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "PayoutDetails" WHERE "ProductId" IS NOT NULL GROUP BY "ProductId", "Type" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Cannot add payout uniqueness: duplicate product/type payout details exist.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "PayoutDetails" WHERE "IncidentReportId" IS NOT NULL GROUP BY "IncidentReportId" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Cannot add payout uniqueness: an incident is already attached to multiple payouts.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "FinancialTransactions" WHERE "PayoutId" IS NOT NULL GROUP BY "PayoutId" HAVING COUNT(*) > 1) THEN
                        RAISE EXCEPTION 'Cannot add payout transaction uniqueness: duplicate payout transactions exist.';
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_FinancialTransactions_PayoutId",
                table: "FinancialTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductStatusHistories_NewStatus",
                table: "ProductStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductStatusHistories_PreviousStatus",
                table: "ProductStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Enums",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payouts_Status",
                table: "Payouts");

            migrationBuilder.DropIndex(
                name: "IX_PayoutDetails_IncidentReportId",
                table: "PayoutDetails");

            migrationBuilder.DropIndex(
                name: "IX_PayoutDetails_ProductId",
                table: "PayoutDetails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayoutDetails_Type",
                table: "PayoutDetails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IncidentReports_Enums",
                table: "IncidentReports");

            migrationBuilder.AlterColumn<Guid>(
                name: "ApprovedByEmployeeId",
                table: "Payouts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "Payouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "Payouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByEmployeeId",
                table: "Payouts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CustomerConfirmedAt",
                table: "Payouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TransferredAt",
                table: "Payouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ApprovedByEmployeeId",
                table: "IncidentReports",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "IncidentReports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "IncidentReports",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EvidenceUrls",
                table: "IncidentReports",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "IncidentReports",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "Description", "Name" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000036"), "incidents.read", "Tra cứu biên bản sự cố sản phẩm.", "Xem biên bản sự cố" },
                    { new Guid("30000000-0000-0000-0000-000000000037"), "incidents.create", "Ghi nhận sản phẩm hư hỏng hoặc thất lạc.", "Lập biên bản sự cố" },
                    { new Guid("30000000-0000-0000-0000-000000000038"), "incidents.review", "Phê duyệt bồi thường hoặc yêu cầu kiểm tra lại.", "Duyệt biên bản sự cố" },
                    { new Guid("30000000-0000-0000-0000-000000000039"), "settlements.read", "Xem bảng kê và trạng thái giải ngân.", "Xem quyết toán" },
                    { new Guid("30000000-0000-0000-0000-000000000040"), "settlements.manage", "Tạo và gửi lệnh chi trả cho người ký gửi.", "Quản lý quyết toán" }
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000036"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000000037"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000000038"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000000039"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000000040"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000000036"), new Guid("20000000-0000-0000-0000-000000000002") },
                    { new Guid("30000000-0000-0000-0000-000000000037"), new Guid("20000000-0000-0000-0000-000000000002") },
                    { new Guid("30000000-0000-0000-0000-000000000036"), new Guid("20000000-0000-0000-0000-000000000003") },
                    { new Guid("30000000-0000-0000-0000-000000000037"), new Guid("20000000-0000-0000-0000-000000000003") },
                    { new Guid("30000000-0000-0000-0000-000000000038"), new Guid("20000000-0000-0000-0000-000000000003") },
                    { new Guid("30000000-0000-0000-0000-000000000039"), new Guid("20000000-0000-0000-0000-000000000003") },
                    { new Guid("30000000-0000-0000-0000-000000000040"), new Guid("20000000-0000-0000-0000-000000000003") }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductStatusHistories_NewStatus",
                table: "ProductStatusHistories",
                sql: "\"NewStatus\" IN ('PendingProcessing', 'OnSale', 'Sold', 'Expired', 'Donated', 'Returned', 'EarlyWithdrawn', 'Compensated', 'Cancelled', 'IncidentPending')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductStatusHistories_PreviousStatus",
                table: "ProductStatusHistories",
                sql: "\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN ('PendingProcessing', 'OnSale', 'Sold', 'Expired', 'Donated', 'Returned', 'EarlyWithdrawn', 'Compensated', 'Cancelled', 'IncidentPending')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Enums",
                table: "Products",
                sql: "\"Status\" IN (\r\n    'PendingProcessing', 'OnSale', 'Sold',\r\n    'Expired', 'Donated', 'Returned',\r\n    'EarlyWithdrawn', 'Compensated', 'Cancelled', 'IncidentPending'\r\n)\r\nAND (\r\n    \"AgreedBrandTier\" IS NULL\r\n    OR \"AgreedBrandTier\" IN (\r\n        'NoBrand', 'LocalBrand',\r\n        'Commercial', 'Luxury'\r\n    )\r\n)");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_CreatedByEmployeeId",
                table: "Payouts",
                column: "CreatedByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_PayoutId",
                table: "FinancialTransactions",
                column: "PayoutId",
                unique: true,
                filter: "\"PayoutId\" IS NOT NULL");

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

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayoutDetails_Type",
                table: "PayoutDetails",
                sql: "\"Type\" IN ('Commission', 'Compensation', 'ServiceFee', 'SaleProceeds')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncidentReports_Enums",
                table: "IncidentReports",
                sql: "\"Type\" IN ('Lost', 'Damaged', 'TagSwapped') AND \"Status\" IN ('Pending', 'Approved', 'Compensated', 'NeedsRecheck')");

            migrationBuilder.AddForeignKey(
                name: "FK_Payouts_EmployeeProfiles_CreatedByEmployeeId",
                table: "Payouts",
                column: "CreatedByEmployeeId",
                principalTable: "EmployeeProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinancialTransactions_PayoutId",
                table: "FinancialTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Payouts_EmployeeProfiles_CreatedByEmployeeId",
                table: "Payouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductStatusHistories_NewStatus",
                table: "ProductStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductStatusHistories_PreviousStatus",
                table: "ProductStatusHistories");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Enums",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Payouts_CreatedByEmployeeId",
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

            migrationBuilder.DropCheckConstraint(
                name: "CK_PayoutDetails_Type",
                table: "PayoutDetails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IncidentReports_Enums",
                table: "IncidentReports");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000036"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000037"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000038"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000039"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000040"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000036"), new Guid("20000000-0000-0000-0000-000000000002") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000037"), new Guid("20000000-0000-0000-0000-000000000002") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000036"), new Guid("20000000-0000-0000-0000-000000000003") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000037"), new Guid("20000000-0000-0000-0000-000000000003") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000038"), new Guid("20000000-0000-0000-0000-000000000003") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000039"), new Guid("20000000-0000-0000-0000-000000000003") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000040"), new Guid("20000000-0000-0000-0000-000000000003") });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000036"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000037"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000038"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000039"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000040"));

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "CreatedByEmployeeId",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "CustomerConfirmedAt",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "TransferredAt",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "IncidentReports");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "IncidentReports");

            migrationBuilder.DropColumn(
                name: "EvidenceUrls",
                table: "IncidentReports");

            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "IncidentReports");

            migrationBuilder.AlterColumn<Guid>(
                name: "ApprovedByEmployeeId",
                table: "Payouts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ApprovedByEmployeeId",
                table: "IncidentReports",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductStatusHistories_NewStatus",
                table: "ProductStatusHistories",
                sql: "\"NewStatus\" IN ('PendingProcessing', 'OnSale', 'Sold', 'Expired', 'Donated', 'Returned', 'EarlyWithdrawn', 'Compensated', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductStatusHistories_PreviousStatus",
                table: "ProductStatusHistories",
                sql: "\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN ('PendingProcessing', 'OnSale', 'Sold', 'Expired', 'Donated', 'Returned', 'EarlyWithdrawn', 'Compensated', 'Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Enums",
                table: "Products",
                sql: "\"Status\" IN (\r\n    'PendingProcessing', 'OnSale', 'Sold',\r\n    'Expired', 'Donated', 'Returned',\r\n    'EarlyWithdrawn', 'Compensated', 'Cancelled'\r\n)\r\nAND (\r\n    \"AgreedBrandTier\" IS NULL\r\n    OR \"AgreedBrandTier\" IN (\r\n        'NoBrand', 'LocalBrand',\r\n        'Commercial', 'Luxury'\r\n    )\r\n)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payouts_Status",
                table: "Payouts",
                sql: "\"Status\" IN ('Pending', 'Transferred', 'Completed')");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutDetails_IncidentReportId",
                table: "PayoutDetails",
                column: "IncidentReportId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_PayoutId",
                table: "FinancialTransactions",
                column: "PayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutDetails_ProductId",
                table: "PayoutDetails",
                column: "ProductId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PayoutDetails_Type",
                table: "PayoutDetails",
                sql: "\"Type\" IN ('Commission', 'Compensation', 'ServiceFee')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IncidentReports_Enums",
                table: "IncidentReports",
                sql: "\"Type\" IN ('Lost', 'Damaged', 'TagSwapped') AND \"Status\" IN ('Pending', 'Approved', 'Compensated')");
        }
    }
}
