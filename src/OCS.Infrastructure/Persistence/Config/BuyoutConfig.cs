using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence.Config;

public sealed class BuyoutConfig : IEntityTypeConfiguration<Buyout>
{
    public void Configure(EntityTypeBuilder<Buyout> builder)
    {
        builder.ToTable("THUMUA", table =>
        {
            table.HasCheckConstraint(
                "CK_THUMUA_Total",
                """
                "TotalPurchasePrice" >= 0
                """);

            table.HasCheckConstraint(
                "CK_THUMUA_Status",
                """
                "Status" IN ('Pending', 'Approved', 'Rejected')
                """);

            table.HasCheckConstraint(
                "CK_THUMUA_Approver",
                """
                "Status" <> 'Approved'
                OR "ApprovedByEmployeeId" IS NOT NULL
                """);
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.BuyoutNumber)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasIndex(x => x.BuyoutNumber)
            .IsUnique();

        builder.Property(x => x.TotalPurchasePrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.CreatedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ApprovedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.ApprovedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
