using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Context.Config;

public sealed class ProductStatusHistoryConfig : IEntityTypeConfiguration<ProductStatusHistory>
{
    public void Configure(EntityTypeBuilder<ProductStatusHistory> builder)
    {
        // Values come only from the application enum, never from user-supplied SQL.
        var allowedStatuses = string.Join(", ", Enum.GetNames<ProductStatus>().Select(status => $"'{status}'"));

        builder.ToTable("LICHSUTRANGTHAISP", table =>
        {
            table.HasCheckConstraint(
                "CK_LICHSUTRANGTHAISP_DifferentStatus",
                """
                "PreviousStatus" IS NULL
                OR "PreviousStatus" <> "NewStatus"
                """);

            table.HasCheckConstraint(
                "CK_LICHSUTRANGTHAISP_NewStatus",
                $"\"NewStatus\" IN ({allowedStatuses})");

            table.HasCheckConstraint(
                "CK_LICHSUTRANGTHAISP_PreviousStatus",
                $"\"PreviousStatus\" IS NULL OR \"PreviousStatus\" IN ({allowedStatuses})");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.PreviousStatus)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(x => x.NewStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.OccurredAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.Reason)
            .HasMaxLength(1000);

        builder.HasOne(x => x.Product)
            .WithMany(x => x.StatusHistories)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ChangedByAccount)
            .WithMany()
            .HasForeignKey(x => x.ChangedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.ProductId,
            x.OccurredAt
        });
    }
}
