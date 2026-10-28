using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class NotificationConfig : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("THONGBAO", table =>
            table.HasCheckConstraint(
                "CK_THONGBAO_Type",
                "\"Type\" IN ('System', 'Manual', 'Promotion')"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Content).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.SentAt).HasColumnType("timestamp with time zone");
        builder.HasOne(x => x.RecipientAccount).WithMany().HasForeignKey(x => x.RecipientAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SenderAccount).WithMany().HasForeignKey(x => x.SenderAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.RecipientAccountId, x.IsRead, x.SentAt });
    }
}
