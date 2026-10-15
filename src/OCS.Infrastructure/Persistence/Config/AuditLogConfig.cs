using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence.Config;

public sealed class AuditLogConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("NHATKYHETHONG", table =>
        {
            table.HasCheckConstraint(
                "CK_NHATKYHETHONG_ActorType",
                """
                "ActorType" IN ('User', 'System', 'Anonymous')
                """);

            table.HasCheckConstraint(
                "CK_NHATKYHETHONG_UserActor",
                """
                "ActorType" <> 'User'
                OR "ActorAccountId" IS NOT NULL
                """);
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.ActorType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Action)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.EntityType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.EntityId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Changes)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasMaxLength(1000);

        builder.Property(x => x.Source)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.IpAddress)
            .HasMaxLength(45);

        builder.Property(x => x.CorrelationId)
            .HasMaxLength(128);

        builder.Property(x => x.OccurredAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.RecordedAt)
            .HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.ActorAccount)
            .WithMany()
            .HasForeignKey(x => x.ActorAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.EntityType,
            x.EntityId,
            x.OccurredAt
        });

        builder.HasIndex(x => new
        {
            x.ActorAccountId,
            x.OccurredAt
        });

        builder.HasIndex(x => x.RecordedAt);
    }
}
