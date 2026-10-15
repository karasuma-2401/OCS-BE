using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OCS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccountsAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QUYEN",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QUYEN", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TAIKHOAN",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    IdentityNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PhoneVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TAIKHOAN", x => x.Id);
                    table.CheckConstraint("CK_TAIKHOAN_AccountType", "\"AccountType\" IN ('Customer', 'Internal')");
                    table.CheckConstraint("CK_TAIKHOAN_ActiveInternalPassword", "\"AccountType\" <> 'Internal'\r\nOR \"Status\" <> 'Active'\r\nOR (\r\n    \"PasswordHash\" IS NOT NULL\r\n    AND length(btrim(\"PasswordHash\")) > 0\r\n)");
                    table.CheckConstraint("CK_TAIKHOAN_InternalUsername", "\"AccountType\" <> 'Internal'\r\nOR (\r\n    \"Username\" IS NOT NULL\r\n    AND length(btrim(\"Username\")) > 0\r\n)");
                    table.CheckConstraint("CK_TAIKHOAN_Status", "\"Status\" IN (\r\n    'PendingActivation',\r\n    'Active',\r\n    'Locked',\r\n    'Deactivated'\r\n)");
                    table.ForeignKey(
                        name: "FK_TAIKHOAN_TAIKHOAN_CreatedByAccountId",
                        column: x => x.CreatedByAccountId,
                        principalTable: "TAIKHOAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VAITRO",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VAITRO", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KHACHHANG",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BankCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BankAccountNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BankAccountHolderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KHACHHANG", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KHACHHANG_TAIKHOAN_Id",
                        column: x => x.Id,
                        principalTable: "TAIKHOAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NHANVIEN",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobTitle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    HiredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NHANVIEN", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NHANVIEN_TAIKHOAN_Id",
                        column: x => x.Id,
                        principalTable: "TAIKHOAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NHATKYHETHONG",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Changes = table.Column<string>(type: "jsonb", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NHATKYHETHONG", x => x.Id);
                    table.CheckConstraint("CK_NHATKYHETHONG_ActorType", "\"ActorType\" IN ('User', 'System', 'Anonymous')");
                    table.CheckConstraint("CK_NHATKYHETHONG_UserActor", "\"ActorType\" <> 'User'\r\nOR \"ActorAccountId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_NHATKYHETHONG_TAIKHOAN_ActorAccountId",
                        column: x => x.ActorAccountId,
                        principalTable: "TAIKHOAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PHANQUYEN",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PHANQUYEN", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_PHANQUYEN_QUYEN_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "QUYEN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PHANQUYEN_VAITRO_RoleId",
                        column: x => x.RoleId,
                        principalTable: "VAITRO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TAIKHOAN_VAITRO",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TAIKHOAN_VAITRO", x => new { x.AccountId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_TAIKHOAN_VAITRO_TAIKHOAN_AccountId",
                        column: x => x.AccountId,
                        principalTable: "TAIKHOAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TAIKHOAN_VAITRO_VAITRO_RoleId",
                        column: x => x.RoleId,
                        principalTable: "VAITRO",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "NHATKYHETHONG",
                columns: new[] { "Id", "Action", "ActorAccountId", "ActorType", "Changes", "CorrelationId", "EntityId", "EntityType", "IpAddress", "OccurredAt", "Reason", "RecordedAt", "Source" },
                values: new object[] { new Guid("40000000-0000-0000-0000-000000000001"), "DemoDataSeeded", null, "System", "{\"Dataset\":{\"old\":null,\"new\":\"AccountsDemoV1\"}}", null, "10000000-0000-0000-0000-000000000001", "Account", null, new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Bản ghi minh họa thuộc bộ dữ liệu demo; không phải sự kiện người dùng thực tế.", new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "DemoSeed" });

            migrationBuilder.InsertData(
                table: "QUYEN",
                columns: new[] { "Id", "Code", "Description", "Name" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000001"), "customers.read", "Tra cứu hồ sơ phục vụ tiếp nhận tại cửa hàng.", "Xem hồ sơ khách" },
                    { new Guid("30000000-0000-0000-0000-000000000002"), "accounts.create", "Quản lý cấp tài khoản cho nhân viên.", "Cấp tài khoản nội bộ" },
                    { new Guid("30000000-0000-0000-0000-000000000003"), "appointments.create", "Khách hàng tạo lịch hẹn ký gửi của mình trên mini app.", "Đặt lịch ký gửi" }
                });

            migrationBuilder.InsertData(
                table: "TAIKHOAN",
                columns: new[] { "Id", "AccountType", "ActivatedAt", "CreatedAt", "CreatedByAccountId", "Email", "FullName", "IdentityNumber", "PasswordHash", "PhoneNumber", "PhoneVerifiedAt", "Status", "UpdatedAt", "Username" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), "Internal", null, new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "manager.demo@example.com", "Quản lý demo", null, null, "0900000001", null, "PendingActivation", null, "manager.demo" },
                    { new Guid("10000000-0000-0000-0000-000000000003"), "Customer", null, new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "customer.demo@example.com", "Khách ký gửi demo", null, null, "0900000003", null, "PendingActivation", null, null }
                });

            migrationBuilder.InsertData(
                table: "VAITRO",
                columns: new[] { "Id", "Code", "Description", "Name" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000001"), "Manager", "Vai trò mẫu cho tài khoản quản lý nội bộ.", "Quản lý" },
                    { new Guid("20000000-0000-0000-0000-000000000002"), "Staff", "Vai trò mẫu cho nhân viên sử dụng POS.", "Nhân viên" },
                    { new Guid("20000000-0000-0000-0000-000000000003"), "Admin", "Vai trò quản trị hệ thống nội bộ.", "Quản trị viên" },
                    { new Guid("20000000-0000-0000-0000-000000000004"), "Customer", "Vai trò khách hàng sử dụng mini app để đặt lịch ký gửi.", "Khách hàng" }
                });

            migrationBuilder.InsertData(
                table: "KHACHHANG",
                columns: new[] { "Id", "Address", "BankAccountHolderName", "BankAccountNumber", "BankCode", "Status" },
                values: new object[] { new Guid("10000000-0000-0000-0000-000000000003"), "Địa chỉ mẫu tại TP. Hồ Chí Minh", "KHACH KY GUI DEMO", "000000000001", "VCB", "Active" });

            migrationBuilder.InsertData(
                table: "NHANVIEN",
                columns: new[] { "Id", "HiredOn", "JobTitle", "Status" },
                values: new object[] { new Guid("10000000-0000-0000-0000-000000000001"), new DateOnly(2026, 10, 1), "Quản lý cửa hàng", "Working" });

            migrationBuilder.InsertData(
                table: "PHANQUYEN",
                columns: new[] { "PermissionId", "RoleId" },
                values: new object[,]
                {
                    { new Guid("30000000-0000-0000-0000-000000000001"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000000002"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("30000000-0000-0000-0000-000000000001"), new Guid("20000000-0000-0000-0000-000000000002") },
                    { new Guid("30000000-0000-0000-0000-000000000001"), new Guid("20000000-0000-0000-0000-000000000003") },
                    { new Guid("30000000-0000-0000-0000-000000000002"), new Guid("20000000-0000-0000-0000-000000000003") },
                    { new Guid("30000000-0000-0000-0000-000000000003"), new Guid("20000000-0000-0000-0000-000000000004") }
                });

            migrationBuilder.InsertData(
                table: "TAIKHOAN",
                columns: new[] { "Id", "AccountType", "ActivatedAt", "CreatedAt", "CreatedByAccountId", "Email", "FullName", "IdentityNumber", "PasswordHash", "PhoneNumber", "PhoneVerifiedAt", "Status", "UpdatedAt", "Username" },
                values: new object[] { new Guid("10000000-0000-0000-0000-000000000002"), "Internal", null, new DateTimeOffset(new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("10000000-0000-0000-0000-000000000001"), "staff.demo@example.com", "Nhân viên demo", null, null, "0900000002", null, "PendingActivation", null, "staff.demo" });

            migrationBuilder.InsertData(
                table: "TAIKHOAN_VAITRO",
                columns: new[] { "AccountId", "RoleId" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), new Guid("20000000-0000-0000-0000-000000000001") },
                    { new Guid("10000000-0000-0000-0000-000000000003"), new Guid("20000000-0000-0000-0000-000000000004") }
                });

            migrationBuilder.InsertData(
                table: "NHANVIEN",
                columns: new[] { "Id", "HiredOn", "JobTitle", "Status" },
                values: new object[] { new Guid("10000000-0000-0000-0000-000000000002"), new DateOnly(2026, 10, 10), "Nhân viên POS", "Working" });

            migrationBuilder.InsertData(
                table: "TAIKHOAN_VAITRO",
                columns: new[] { "AccountId", "RoleId" },
                values: new object[] { new Guid("10000000-0000-0000-0000-000000000002"), new Guid("20000000-0000-0000-0000-000000000002") });

            migrationBuilder.CreateIndex(
                name: "IX_NHATKYHETHONG_ActorAccountId_OccurredAt",
                table: "NHATKYHETHONG",
                columns: new[] { "ActorAccountId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NHATKYHETHONG_EntityType_EntityId_OccurredAt",
                table: "NHATKYHETHONG",
                columns: new[] { "EntityType", "EntityId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NHATKYHETHONG_RecordedAt",
                table: "NHATKYHETHONG",
                column: "RecordedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PHANQUYEN_PermissionId",
                table: "PHANQUYEN",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_QUYEN_Code",
                table: "QUYEN",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TAIKHOAN_AccountType_Email",
                table: "TAIKHOAN",
                columns: new[] { "AccountType", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TAIKHOAN_AccountType_IdentityNumber",
                table: "TAIKHOAN",
                columns: new[] { "AccountType", "IdentityNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TAIKHOAN_AccountType_PhoneNumber",
                table: "TAIKHOAN",
                columns: new[] { "AccountType", "PhoneNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TAIKHOAN_CreatedByAccountId",
                table: "TAIKHOAN",
                column: "CreatedByAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TAIKHOAN_Username",
                table: "TAIKHOAN",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TAIKHOAN_VAITRO_RoleId",
                table: "TAIKHOAN_VAITRO",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_VAITRO_Code",
                table: "VAITRO",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KHACHHANG");

            migrationBuilder.DropTable(
                name: "NHANVIEN");

            migrationBuilder.DropTable(
                name: "NHATKYHETHONG");

            migrationBuilder.DropTable(
                name: "PHANQUYEN");

            migrationBuilder.DropTable(
                name: "TAIKHOAN_VAITRO");

            migrationBuilder.DropTable(
                name: "QUYEN");

            migrationBuilder.DropTable(
                name: "TAIKHOAN");

            migrationBuilder.DropTable(
                name: "VAITRO");
        }
    }
}
