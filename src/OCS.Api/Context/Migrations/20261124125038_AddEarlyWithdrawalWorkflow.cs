using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddEarlyWithdrawalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EarlyWithdrawals_CustomerId",
                table: "EarlyWithdrawals");

            migrationBuilder.AddColumn<bool>(
                name: "IsReservedForWithdrawal",
                table: "Products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "ProcessedByEmployeeId",
                table: "EarlyWithdrawals",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "EarlyWithdrawals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionNote",
                table: "EarlyWithdrawals",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HandedOverAt",
                table: "EarlyWithdrawals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HandedOverByEmployeeId",
                table: "EarlyWithdrawals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandoverEvidence",
                table: "EarlyWithdrawals",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidAt",
                table: "EarlyWithdrawals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaidByEmployeeId",
                table: "EarlyWithdrawals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "EarlyWithdrawals",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RequestedAt",
                table: "EarlyWithdrawals",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "EarlyWithdrawals",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "EarlyWithdrawals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "PenaltyRate",
                table: "EarlyWithdrawalItems",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "EarlyWithdrawalFeeRate",
                table: "ConsignmentContracts",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.Sql("UPDATE \"EarlyWithdrawals\" SET \"RequestedAt\" = \"CreatedAt\" WHERE \"Status\" = 'Legacy';");

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "Description", "Name" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000001001"), "withdrawals.read", "Xem yêu cầu rút hàng sớm.", "Xem yêu cầu rút sớm" },
                    { new Guid("30000000-0000-0000-0000-000000001002"), "withdrawals.approve", "Duyệt và từ chối yêu cầu rút sớm.", "Duyệt rút sớm" },
                    { new Guid("30000000-0000-0000-0000-000000001003"), "withdrawals.payment", "Ghi nhận thu đủ phí rút sớm.", "Thu phí rút sớm" },
                    { new Guid("30000000-0000-0000-0000-000000001004"), "withdrawals.handover", "Xác nhận giao hàng rút sớm.", "Giao hàng rút sớm" }
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000001001"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000001002"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000001003"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000001004"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000001001"), new Guid("20000000-0000-0000-0000-000000000002") },
                    { new Guid("30000000-0000-0000-0000-000000001002"), new Guid("20000000-0000-0000-0000-000000000002") },
                    { new Guid("30000000-0000-0000-0000-000000001003"), new Guid("20000000-0000-0000-0000-000000000002") },
                    { new Guid("30000000-0000-0000-0000-000000001004"), new Guid("20000000-0000-0000-0000-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_EarlyWithdrawals_CustomerId_RequestedAt",
                table: "EarlyWithdrawals",
                columns: new[] { "CustomerId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EarlyWithdrawals_HandedOverByEmployeeId",
                table: "EarlyWithdrawals",
                column: "HandedOverByEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EarlyWithdrawals_PaidByEmployeeId",
                table: "EarlyWithdrawals",
                column: "PaidByEmployeeId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EarlyWithdrawals_Status",
                table: "EarlyWithdrawals",
                sql: "\"Status\" IN ('Pending', 'Approved', 'Rejected', 'Cancelled', 'Completed', 'Legacy')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EarlyWithdrawalItems_Rate",
                table: "EarlyWithdrawalItems",
                sql: "\"PenaltyRate\" BETWEEN 0 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ConsignmentContracts_WithdrawalFeeRate",
                table: "ConsignmentContracts",
                sql: "\"EarlyWithdrawalFeeRate\" IS NULL OR \"EarlyWithdrawalFeeRate\" BETWEEN 0 AND 100");

            migrationBuilder.AddForeignKey(
                name: "FK_EarlyWithdrawals_EmployeeProfiles_HandedOverByEmployeeId",
                table: "EarlyWithdrawals",
                column: "HandedOverByEmployeeId",
                principalTable: "EmployeeProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EarlyWithdrawals_EmployeeProfiles_PaidByEmployeeId",
                table: "EarlyWithdrawals",
                column: "PaidByEmployeeId",
                principalTable: "EmployeeProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EarlyWithdrawals_EmployeeProfiles_HandedOverByEmployeeId",
                table: "EarlyWithdrawals");

            migrationBuilder.DropForeignKey(
                name: "FK_EarlyWithdrawals_EmployeeProfiles_PaidByEmployeeId",
                table: "EarlyWithdrawals");

            migrationBuilder.DropIndex(
                name: "IX_EarlyWithdrawals_CustomerId_RequestedAt",
                table: "EarlyWithdrawals");

            migrationBuilder.DropIndex(
                name: "IX_EarlyWithdrawals_HandedOverByEmployeeId",
                table: "EarlyWithdrawals");

            migrationBuilder.DropIndex(
                name: "IX_EarlyWithdrawals_PaidByEmployeeId",
                table: "EarlyWithdrawals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EarlyWithdrawals_Status",
                table: "EarlyWithdrawals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EarlyWithdrawalItems_Rate",
                table: "EarlyWithdrawalItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ConsignmentContracts_WithdrawalFeeRate",
                table: "ConsignmentContracts");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000001001"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000001002"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000001003"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000001004"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000001001"), new Guid("20000000-0000-0000-0000-000000000002") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000001002"), new Guid("20000000-0000-0000-0000-000000000002") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000001003"), new Guid("20000000-0000-0000-0000-000000000002") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000001004"), new Guid("20000000-0000-0000-0000-000000000002") });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000001001"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000001002"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000001003"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000001004"));

            migrationBuilder.DropColumn(
                name: "IsReservedForWithdrawal",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "EarlyWithdrawals");

            migrationBuilder.DropColumn(
                name: "DecisionNote",
                table: "EarlyWithdrawals");

            migrationBuilder.DropColumn(
                name: "HandedOverAt",
                table: "EarlyWithdrawals");

            migrationBuilder.DropColumn(
                name: "HandedOverByEmployeeId",
                table: "EarlyWithdrawals");

            migrationBuilder.DropColumn(
                name: "HandoverEvidence",
                table: "EarlyWithdrawals");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "EarlyWithdrawals");

            migrationBuilder.DropColumn(
                name: "PaidByEmployeeId",
                table: "EarlyWithdrawals");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "EarlyWithdrawals");

            migrationBuilder.DropColumn(
                name: "RequestedAt",
                table: "EarlyWithdrawals");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "EarlyWithdrawals");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "EarlyWithdrawals");

            migrationBuilder.DropColumn(
                name: "PenaltyRate",
                table: "EarlyWithdrawalItems");

            migrationBuilder.DropColumn(
                name: "EarlyWithdrawalFeeRate",
                table: "ConsignmentContracts");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProcessedByEmployeeId",
                table: "EarlyWithdrawals",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EarlyWithdrawals_CustomerId",
                table: "EarlyWithdrawals",
                column: "CustomerId");
        }
    }
}
