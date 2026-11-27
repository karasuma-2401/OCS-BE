using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OCS.Api.Context;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261127140000_AddSettingsAndAuditLogPermissions")]
    public partial class AddSettingsAndAuditLogPermissions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "Description", "Name" },
                columnTypes: new[] { "uuid", "character varying(100)", "character varying(500)", "character varying(200)" },
                values: new object[,]
                {
                    {
                        new Guid("30000000-0000-0000-0000-000000000042"),
                        "settings.read",
                        "Xem các tham số vận hành của cửa hàng.",
                        "Xem cấu hình hệ thống"
                    },
                    {
                        new Guid("30000000-0000-0000-0000-000000000043"),
                        "settings.update",
                        "Cập nhật thời hạn ký gửi, hoa hồng theo hạng, phí rút sớm và thời gian Auto-confirm.",
                        "Sửa cấu hình hệ thống"
                    },
                    {
                        new Guid("30000000-0000-0000-0000-000000000044"),
                        "audit_logs.read",
                        "Tra cứu lịch sử thay đổi quan trọng trong hệ thống.",
                        "Xem nhật ký quản trị"
                    }
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                columnTypes: new[] { "uuid", "uuid" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000042"), new Guid("20000000-0000-0000-0000-000000000003") },
                    { new Guid("30000000-0000-0000-0000-000000000043"), new Guid("20000000-0000-0000-0000-000000000003") },
                    { new Guid("30000000-0000-0000-0000-000000000044"), new Guid("20000000-0000-0000-0000-000000000003") }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyColumnTypes: new[] { "uuid", "uuid" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000042"), new Guid("20000000-0000-0000-0000-000000000003") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyColumnTypes: new[] { "uuid", "uuid" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000043"), new Guid("20000000-0000-0000-0000-000000000003") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyColumnTypes: new[] { "uuid", "uuid" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000044"), new Guid("20000000-0000-0000-0000-000000000003") });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyColumnType: "uuid",
                keyValue: new Guid("30000000-0000-0000-0000-000000000042"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyColumnType: "uuid",
                keyValue: new Guid("30000000-0000-0000-0000-000000000043"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyColumnType: "uuid",
                keyValue: new Guid("30000000-0000-0000-0000-000000000044"));
        }
    }
}
