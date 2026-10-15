using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence.Config;

public sealed class AppointmentTimeSlotConfig : IEntityTypeConfiguration<AppointmentTimeSlot>
{
    public void Configure(
        EntityTypeBuilder<AppointmentTimeSlot> builder)
    {
        builder.ToTable("KHUNGGIOHEN", table =>
        {
            table.HasCheckConstraint(
                "CK_KHUNGGIOHEN_TimeRange",
                """
                "EndTime" > "StartTime"
                """);

            table.HasCheckConstraint(
                "CK_KHUNGGIOHEN_MaxCustomers",
                """
                "MaxCustomers" > 0
                """);
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.StartTime)
            .HasColumnType("time without time zone")
            .IsRequired();

        builder.Property(x => x.EndTime)
            .HasColumnType("time without time zone")
            .IsRequired();

        builder.Property(x => x.MaxCustomers)
            .IsRequired();

        builder.HasIndex(x => new
        {
            x.StartTime,
            x.EndTime
        }).IsUnique();
    }
}