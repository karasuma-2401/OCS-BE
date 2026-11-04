using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class AppointmentTimeSlotConfig : IEntityTypeConfiguration<AppointmentTimeSlot>
{
    public void Configure(
        EntityTypeBuilder<AppointmentTimeSlot> builder)
    {
        builder.ToTable("AppointmentTimeSlots", table =>
        {
            table.HasCheckConstraint(
                "CK_AppointmentTimeSlots_TimeRange",
                """
                "EndTime" > "StartTime"
                """);

            table.HasCheckConstraint(
                "CK_AppointmentTimeSlots_MaxCustomers",
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