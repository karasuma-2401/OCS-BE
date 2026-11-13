using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Configurations;

public sealed class ContractTermsRequestConfiguration : IEntityTypeConfiguration<ContractTermsRequest>
{
    public void Configure(EntityTypeBuilder<ContractTermsRequest> builder)
    {
        builder.HasKey(request => request.Id);
        builder.Property(request => request.CustomerPhone).HasMaxLength(10).IsRequired();
        builder.Property(request => request.Reason).HasMaxLength(2000).IsRequired();
        builder.Property(request => request.DecisionNote).HasMaxLength(2000);
        builder.Property(request => request.Status).HasMaxLength(20).IsRequired();
        builder.Property(request => request.Version).IsConcurrencyToken();
        builder.HasIndex(request => request.DraftKey)
            .IsUnique().HasFilter("\"Status\" IN ('Pending', 'Approved')");
    }
}
