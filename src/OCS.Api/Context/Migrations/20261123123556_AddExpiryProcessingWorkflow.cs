using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddExpiryProcessingWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ProcessedByEmployeeId",
                table: "ExpiryProcessings",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CollectionDeadline",
                table: "ExpiryProcessings",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "Evidence",
                table: "ExpiryProcessings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "ExpiryProcessings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientOrganization",
                table: "ExpiryProcessings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UnclaimedApprovedAt",
                table: "ExpiryProcessings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UnclaimedApprovedByEmployeeId",
                table: "ExpiryProcessings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnclaimedBasis",
                table: "ExpiryProcessings",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "Description", "Name" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000032"), "expiry.read", "Xem phiếu xử lý hàng ký gửi hết hạn.", "Xem hàng hết hạn" },
                    { new Guid("30000000-0000-0000-0000-000000000033"), "expiry.return", "Xác nhận bàn giao hàng cho người ký gửi.", "Giao trả hàng hết hạn" },
                    { new Guid("30000000-0000-0000-0000-000000000034"), "expiry.donate", "Xác nhận bàn giao cho tổ chức nhận.", "Bàn giao hàng quyên góp" },
                    { new Guid("30000000-0000-0000-0000-000000000035"), "expiry.mark-unclaimed", "Duyệt chuyển hàng không nhận sang quyên góp.", "Duyệt hàng không nhận" }
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000032"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000000033"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000000034"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000000035"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000000032"), new Guid("20000000-0000-0000-0000-000000000002") },
                    { new Guid("30000000-0000-0000-0000-000000000033"), new Guid("20000000-0000-0000-0000-000000000002") },
                    { new Guid("30000000-0000-0000-0000-000000000034"), new Guid("20000000-0000-0000-0000-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiryProcessings_UnclaimedApprovedByEmployeeId",
                table: "ExpiryProcessings",
                column: "UnclaimedApprovedByEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_ExpiryProcessings_EmployeeProfiles_UnclaimedApprovedByEmplo~",
                table: "ExpiryProcessings",
                column: "UnclaimedApprovedByEmployeeId",
                principalTable: "EmployeeProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExpiryProcessings_EmployeeProfiles_UnclaimedApprovedByEmplo~",
                table: "ExpiryProcessings");

            migrationBuilder.DropIndex(
                name: "IX_ExpiryProcessings_UnclaimedApprovedByEmployeeId",
                table: "ExpiryProcessings");

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000032"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000033"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000034"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000035"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000032"), new Guid("20000000-0000-0000-0000-000000000002") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000033"), new Guid("20000000-0000-0000-0000-000000000002") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000034"), new Guid("20000000-0000-0000-0000-000000000002") });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000032"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000033"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000034"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000035"));

            migrationBuilder.DropColumn(
                name: "Evidence",
                table: "ExpiryProcessings");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "ExpiryProcessings");

            migrationBuilder.DropColumn(
                name: "RecipientOrganization",
                table: "ExpiryProcessings");

            migrationBuilder.DropColumn(
                name: "UnclaimedApprovedAt",
                table: "ExpiryProcessings");

            migrationBuilder.DropColumn(
                name: "UnclaimedApprovedByEmployeeId",
                table: "ExpiryProcessings");

            migrationBuilder.DropColumn(
                name: "UnclaimedBasis",
                table: "ExpiryProcessings");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProcessedByEmployeeId",
                table: "ExpiryProcessings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CollectionDeadline",
                table: "ExpiryProcessings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
