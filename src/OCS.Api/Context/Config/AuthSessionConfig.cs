using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class AuthSessionConfig : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("AUTH_SESSION", table =>
            table.HasCheckConstraint("CK_AUTH_SESSION_Lifetime", "\"ExpiresAt\" > \"CreatedAt\" AND (\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"CreatedAt\")"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasIndex(x => new { x.AccountId, x.ExpiresAt });
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
