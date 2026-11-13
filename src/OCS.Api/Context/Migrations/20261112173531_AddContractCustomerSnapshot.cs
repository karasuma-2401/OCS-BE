using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddContractCustomerSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerId",
                table: "ConsignmentContracts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "BankAccountHolderSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumberSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankCodeSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerAddressSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerEmailSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerIdentitySnapshot",
                table: "ConsignmentContracts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerNameSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPhoneSnapshot",
                table: "ConsignmentContracts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Do not discard guest or document snapshot data when rolling back.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "ConsignmentContracts" WHERE "CustomerId" IS NULL OR "CustomerNameSnapshot" IS NOT NULL OR "CustomerPhoneSnapshot" IS NOT NULL OR "CustomerEmailSnapshot" IS NOT NULL OR "CustomerIdentitySnapshot" IS NOT NULL OR "CustomerAddressSnapshot" IS NOT NULL OR "BankCodeSnapshot" IS NOT NULL OR "BankAccountNumberSnapshot" IS NOT NULL OR "BankAccountHolderSnapshot" IS NOT NULL) THEN
                        RAISE EXCEPTION 'Cannot roll back contract snapshots while saved snapshot or guest data exists';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropColumn(
                name: "BankAccountHolderSnapshot",
                table: "ConsignmentContracts");

            migrationBuilder.DropColumn(
                name: "BankAccountNumberSnapshot",
                table: "ConsignmentContracts");

            migrationBuilder.DropColumn(
                name: "BankCodeSnapshot",
                table: "ConsignmentContracts");

            migrationBuilder.DropColumn(
                name: "CustomerAddressSnapshot",
                table: "ConsignmentContracts");

            migrationBuilder.DropColumn(
                name: "CustomerEmailSnapshot",
                table: "ConsignmentContracts");

            migrationBuilder.DropColumn(
                name: "CustomerIdentitySnapshot",
                table: "ConsignmentContracts");

            migrationBuilder.DropColumn(
                name: "CustomerNameSnapshot",
                table: "ConsignmentContracts");

            migrationBuilder.DropColumn(
                name: "CustomerPhoneSnapshot",
                table: "ConsignmentContracts");

            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerId",
                table: "ConsignmentContracts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
