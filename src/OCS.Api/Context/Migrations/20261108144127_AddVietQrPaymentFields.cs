using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddVietQrPaymentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankAccountHolderName",
                table: "FinancialTransactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "FinancialTransactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankCode",
                table: "FinancialTransactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "FinancialTransactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                table: "FinancialTransactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderName",
                table: "FinancialTransactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrCodeUrl",
                table: "FinancialTransactions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrPayload",
                table: "FinancialTransactions",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransactions_PaymentReference",
                table: "FinancialTransactions",
                column: "PaymentReference",
                unique: true,
                filter: "\"PaymentReference\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FinancialTransactions_PaymentReference",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "BankAccountHolderName",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "BankCode",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "ProviderName",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "QrCodeUrl",
                table: "FinancialTransactions");

            migrationBuilder.DropColumn(
                name: "QrPayload",
                table: "FinancialTransactions");
        }
    }
}
