using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class CustomerProfileConfig : IEntityTypeConfiguration<CustomerProfile>
{
    public void Configure(EntityTypeBuilder<CustomerProfile> builder)
    {
        builder.ToTable("KHACHHANG");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Address)
            .HasMaxLength(500);

        builder.Property(x => x.BankCode)
            .HasMaxLength(50);

        builder.Property(x => x.BankAccountNumber)
            .HasMaxLength(50);

        builder.Property(x => x.BankAccountHolderName)
            .HasMaxLength(200);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(x => x.Account)
            .WithOne(x => x.CustomerProfile)
            .HasForeignKey<CustomerProfile>(x => x.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }
}