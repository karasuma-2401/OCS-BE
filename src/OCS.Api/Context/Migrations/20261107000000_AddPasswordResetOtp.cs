using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OCS.Api.Context;

#nullable disable
namespace OCS.Api.Context.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261107000000_AddPasswordResetOtp")]
public sealed class AddPasswordResetOtp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_OtpChallenges_Purpose", "OtpChallenges");
        migrationBuilder.AddColumn<DateTimeOffset>("VerifiedAt", "OtpChallenges", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddCheckConstraint("CK_OtpChallenges_Purpose", "OtpChallenges", "\"Purpose\" IN ('RegistrationActivation', 'PasswordReset')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_OtpChallenges_Purpose", "OtpChallenges");
        migrationBuilder.DropColumn("VerifiedAt", "OtpChallenges");
        migrationBuilder.AddCheckConstraint("CK_OtpChallenges_Purpose", "OtpChallenges", "\"Purpose\" = 'RegistrationActivation'");
    }
}
