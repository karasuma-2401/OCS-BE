using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AllowStaffInventoryOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000027"),
                column: "Description",
                value: "Đưa sản phẩm đủ điều kiện lên bán, không cần duyệt kho.");

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[] { new Guid("30000000-0000-0000-0000-000000000027"), new Guid("20000000-0000-0000-0000-000000000002") });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000027"), new Guid("20000000-0000-0000-0000-000000000002") });

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("30000000-0000-0000-0000-000000000027"),
                column: "Description",
                value: "Đưa sản phẩm đã duyệt lên bán.");
        }
    }
}
