using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence.Config;

public sealed class EmployeeProfileConfig : IEntityTypeConfiguration<EmployeeProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeProfile> builder)
    {
        builder.ToTable("NHANVIEN");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.JobTitle)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.HiredOn)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(x => x.Account)
            .WithOne(x => x.EmployeeProfile)
            .HasForeignKey<EmployeeProfile>(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}