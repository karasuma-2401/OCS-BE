using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OCS.Api.Context.Migrations
{
    /// <inheritdoc />
    public partial class AddBuyoutReceptionSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "SignatureContent",
                table: "Buyouts",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureContentType",
                table: "Buyouts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureContent",
                table: "Buyouts");

            migrationBuilder.DropColumn(
                name: "SignatureContentType",
                table: "Buyouts");
        }
    }
}
