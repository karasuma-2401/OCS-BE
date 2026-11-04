using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticationIdentityState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailVerifiedAt",
                table: "TAIKHOAN",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "TAIKHOAN",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockoutEndAt",
                table: "TAIKHOAN",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "TAIKHOAN",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true,
                collation: "C");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "TAIKHOAN",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "AUTH_SESSION",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AUTH_SESSION", x => x.Id);
                    table.CheckConstraint("CK_AUTH_SESSION_Lifetime", "\"ExpiresAt\" > \"CreatedAt\" AND (\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\")");
                    table.ForeignKey(
                        name: "FK_AUTH_SESSION_TAIKHOAN_AccountId",
                        column: x => x.AccountId,
                        principalTable: "TAIKHOAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OTP_CHALLENGE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false, collation: "C"),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContinuationTokenHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContinuationExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResendAvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InvalidatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveryStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OTP_CHALLENGE", x => x.Id);
                    table.CheckConstraint("CK_OTP_CHALLENGE_Attempts", "\"FailedAttempts\" >= 0");
                    table.CheckConstraint("CK_OTP_CHALLENGE_Delivery", "\"DeliveryStatus\" IN ('Pending', 'Sent', 'Failed') AND (\"DeliveryStatus\" <> 'Sent' OR \"SentAt\" IS NOT NULL)");
                    table.CheckConstraint("CK_OTP_CHALLENGE_Hashes", "length(btrim(\"CodeHash\")) > 0 AND length(btrim(\"ContinuationTokenHash\")) > 0");
                    table.CheckConstraint("CK_OTP_CHALLENGE_Lifetime", "\"ExpiresAt\" > \"CreatedAt\" AND \"ContinuationExpiresAt\" >= \"ExpiresAt\" AND \"ResendAvailableAt\" >= \"CreatedAt\"");
                    table.CheckConstraint("CK_OTP_CHALLENGE_Purpose", "\"Purpose\" = 'RegistrationActivation'");
                    table.ForeignKey(
                        name: "FK_OTP_CHALLENGE_TAIKHOAN_AccountId",
                        column: x => x.AccountId,
                        principalTable: "TAIKHOAN",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TAIKHOAN_LoginAttempts",
                table: "TAIKHOAN",
                sql: "\"FailedLoginAttempts\" >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_AUTH_SESSION_AccountId_ExpiresAt",
                table: "AUTH_SESSION",
                columns: new[] { "AccountId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AUTH_SESSION_ExpiresAt",
                table: "AUTH_SESSION",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_OTP_CHALLENGE_AccountId_Purpose",
                table: "OTP_CHALLENGE",
                columns: new[] { "AccountId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_OTP_CHALLENGE_ExpiresAt",
                table: "OTP_CHALLENGE",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AUTH_SESSION");

            migrationBuilder.DropTable(
                name: "OTP_CHALLENGE");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TAIKHOAN_LoginAttempts",
                table: "TAIKHOAN");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "TAIKHOAN");

            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "TAIKHOAN");

            migrationBuilder.DropColumn(
                name: "LockoutEndAt",
                table: "TAIKHOAN");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "TAIKHOAN");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "TAIKHOAN");
        }
    }
}
