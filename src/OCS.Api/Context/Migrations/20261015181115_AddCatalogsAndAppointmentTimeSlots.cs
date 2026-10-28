using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogsAndAppointmentTimeSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DANHMUC",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DANHMUC", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KHOLUU",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KHOLUU", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KHUNGGIOHEN",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    MaxCustomers = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KHUNGGIOHEN", x => x.Id);
                    table.CheckConstraint("CK_KHUNGGIOHEN_MaxCustomers", "\"MaxCustomers\" > 0");
                    table.CheckConstraint("CK_KHUNGGIOHEN_TimeRange", "\"EndTime\" > \"StartTime\"");
                });

            migrationBuilder.CreateTable(
                name: "THUONGHIEU",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DefaultCommissionRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_THUONGHIEU", x => x.Id);
                    table.CheckConstraint("CK_THUONGHIEU_CommissionRate", "\"DefaultCommissionRate\" BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_THUONGHIEU_Tier", "\"Tier\" IN (\r\n    'NoBrand',\r\n    'LocalBrand',\r\n    'Commercial',\r\n    'Luxury'\r\n)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DANHMUC_Name",
                table: "DANHMUC",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KHOLUU_Code",
                table: "KHOLUU",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KHUNGGIOHEN_StartTime_EndTime",
                table: "KHUNGGIOHEN",
                columns: new[] { "StartTime", "EndTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_THUONGHIEU_Name",
                table: "THUONGHIEU",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DANHMUC");

            migrationBuilder.DropTable(
                name: "KHOLUU");

            migrationBuilder.DropTable(
                name: "KHUNGGIOHEN");

            migrationBuilder.DropTable(
                name: "THUONGHIEU");
        }
    }
}
