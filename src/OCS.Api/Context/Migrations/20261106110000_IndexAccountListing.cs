using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OCS.Api.Context;

#nullable disable

namespace OCS.Api.Context.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261106110000_IndexAccountListing")]
public sealed class IndexAccountListing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_Accounts_CreatedAt_Id",
            table: "Accounts",
            columns: ["CreatedAt", "Id"],
            descending: [true, false]);

        migrationBuilder.CreateIndex(
            name: "IX_Accounts_AccountType_CreatedAt_Id",
            table: "Accounts",
            columns: ["AccountType", "CreatedAt", "Id"],
            descending: [false, true, false]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Accounts_CreatedAt_Id", table: "Accounts");
        migrationBuilder.DropIndex(name: "IX_Accounts_AccountType_CreatedAt_Id", table: "Accounts");
    }
}
