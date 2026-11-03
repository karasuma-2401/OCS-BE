using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class OtpChallengeConfig : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("OTP_CHALLENGE", table =>
        {
            table.HasCheckConstraint("CK_OTP_CHALLENGE_Purpose", "\"Purpose\" = 'RegistrationActivation'");
            table.HasCheckConstraint("CK_OTP_CHALLENGE_Attempts", "\"FailedAttempts\" >= 0");
            table.HasCheckConstraint("CK_OTP_CHALLENGE_Lifetime", "\"ExpiresAt\" > \"CreatedAt\" AND \"ContinuationExpiresAt\" >= \"ExpiresAt\" AND \"ResendAvailableAt\" >= \"CreatedAt\"");
            table.HasCheckConstraint("CK_OTP_CHALLENGE_Delivery", "\"DeliveryStatus\" IN ('Pending', 'Sent', 'Failed') AND (\"DeliveryStatus\" <> 'Sent' OR \"SentAt\" IS NOT NULL)");
            table.HasCheckConstraint("CK_OTP_CHALLENGE_Hashes", "length(btrim(\"CodeHash\")) > 0 AND length(btrim(\"ContinuationTokenHash\")) > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(254).UseCollation("C").IsRequired();
        builder.Property(x => x.Purpose).HasMaxLength(40).IsRequired();
        builder.Property(x => x.CodeHash).HasMaxLength(512).IsRequired();
        builder.Property(x => x.ContinuationTokenHash).HasMaxLength(512).IsRequired();
        builder.Property(x => x.DeliveryStatus).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasIndex(x => new { x.AccountId, x.Purpose });
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
