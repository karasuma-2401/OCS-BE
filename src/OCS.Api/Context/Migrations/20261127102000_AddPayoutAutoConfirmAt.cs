using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OCS.Api.Context;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261127102000_AddPayoutAutoConfirmAt")]
    public partial class AddPayoutAutoConfirmAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AutoConfirmAt",
                table: "Payouts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Payouts"
                SET "AutoConfirmAt" = "TransferredAt" + make_interval(hours => COALESCE(
                    (SELECT CASE
                        WHEN ltrim(btrim("Value"), '0') ~ '^[1-9][0-9]{0,2}$' THEN
                            CASE
                                WHEN ltrim(btrim("Value"), '0')::int BETWEEN 1 AND 720 THEN ltrim(btrim("Value"), '0')::int
                                ELSE 48
                            END
                        ELSE 48
                     END
                     FROM "SystemParameters"
                     WHERE "Key" = 'payout.auto_confirm_hours'
                     LIMIT 1),
                    48))
                WHERE "Status" = 'Transferred' AND "TransferredAt" IS NOT NULL AND "AutoConfirmAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoConfirmAt",
                table: "Payouts");
        }
    }
}
