using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence.Config;

public sealed class AppointmentConfig : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("DATLICH", table =>
        {
            table.HasCheckConstraint(
                "CK_DATLICH_ItemCount",
                """
                "EstimatedItemCount" >= 0
                """);

            table.HasCheckConstraint(
                "CK_DATLICH_Enums",
                """
                "Status" IN ('Scheduled', 'Cancelled', 'Completed')
                AND "SubmissionMethod" IN ('InPerson', 'Shipping')
                """);
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.AppointmentDate)
            .HasColumnType("date");

        builder.Property(x => x.TrackingNumber)
            .HasMaxLength(100);

        builder.Property(x => x.SubmissionMethod)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.TimeSlot)
            .WithMany()
            .HasForeignKey(x => x.TimeSlotId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.AppointmentDate,
            x.TimeSlotId,
            x.Status
        });
    }
}