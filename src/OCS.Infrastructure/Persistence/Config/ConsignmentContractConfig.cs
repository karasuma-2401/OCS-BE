using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence.Config;

public sealed class ConsignmentContractConfig : IEntityTypeConfiguration<ConsignmentContract>
{
    public void Configure(
        EntityTypeBuilder<ConsignmentContract> builder)
    {
        builder.ToTable("HOPDONGKYGUI", table =>
        {
            table.HasCheckConstraint(
                "CK_HOPDONGKYGUI_Dates",
                """
                "EndsAt" > "SignedAt"
                """);

            table.HasCheckConstraint(
                "CK_HOPDONGKYGUI_Enums",
                """
                "Status" IN ('Active', 'Completed')
                AND "ExpiryHandling" IN ('Return', 'Donate')
                """);
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.ContractNumber)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(x => x.ContractNumber)
            .IsUnique();

        builder.Property(x => x.SignedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.EndsAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.ExpiryHandling)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Appointment)
            .WithMany()
            .HasForeignKey(x => x.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.CreatedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.Status,
            x.EndsAt
        });
    }
}