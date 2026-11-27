using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OCS.Api.Context;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261127112000_AddReportsReadPermission")]
    public partial class AddReportsReadPermission : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "Description", "Name" },
                columnTypes: new[] { "uuid", "character varying(100)", "character varying(500)", "character varying(200)" },
                values: new object[]
                {
                    new Guid("30000000-0000-0000-0000-000000000041"),
                    "reports.read",
                    "Xem KPI, báo cáo doanh thu POS, hoa hồng ký gửi, lợi nhuận thu mua và xuất CSV.",
                    "Xem báo cáo tài chính"
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "PermissionId", "RoleId" },
                columnTypes: new[] { "uuid", "uuid" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000020"), new Guid("20000000-0000-0000-0000-000000000003") },
                    { new Guid("30000000-0000-0000-0000-000000000041"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000000041"), new Guid("20000000-0000-0000-0000-000000000003") }
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyColumnTypes: new[] { "uuid", "uuid" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000020"), new Guid("20000000-0000-0000-0000-000000000003") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyColumnTypes: new[] { "uuid", "uuid" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000041"), new Guid("20000000-0000-0000-0000-000000000001") });

            migrationBuilder.DeleteData(
                table: "RolePermissions",
                keyColumns: new[] { "PermissionId", "RoleId" },
                keyColumnTypes: new[] { "uuid", "uuid" },
                keyValues: new object[] { new Guid("30000000-0000-0000-0000-000000000041"), new Guid("20000000-0000-0000-0000-000000000003") });

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyColumnType: "uuid",
                keyValue: new Guid("30000000-0000-0000-0000-000000000041"));
        }
    }
}
