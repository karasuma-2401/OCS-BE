using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AllowEarlyWithdrawalResubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EarlyWithdrawalItems_ProductId",
                table: "EarlyWithdrawalItems");

            migrationBuilder.CreateIndex(
                name: "IX_EarlyWithdrawalItems_ProductId",
                table: "EarlyWithdrawalItems",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EarlyWithdrawalItems_ProductId",
                table: "EarlyWithdrawalItems");

            migrationBuilder.CreateIndex(
                name: "IX_EarlyWithdrawalItems_ProductId",
                table: "EarlyWithdrawalItems",
                column: "ProductId",
                unique: true);
        }
    }
}
