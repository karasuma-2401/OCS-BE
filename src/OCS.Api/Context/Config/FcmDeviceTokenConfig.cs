using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class FcmDeviceTokenConfig : IEntityTypeConfiguration<FcmDeviceToken>
{
    public void Configure(EntityTypeBuilder<FcmDeviceToken> builder)
    {
        builder.ToTable("FcmDeviceTokens");
        // A browser installation has exactly one current account, including across API instances.
        builder.HasKey(x => x.Token);
        builder.Property(x => x.Token).HasMaxLength(512);
        builder.Property(x => x.DeviceType).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => new { x.AccountId, x.LastUsedAt });
        builder.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
    }
}
