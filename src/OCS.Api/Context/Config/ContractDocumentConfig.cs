using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class ContractDocumentConfig : IEntityTypeConfiguration<ContractDocument>
{
    public void Configure(EntityTypeBuilder<ContractDocument> builder)
    {
        builder.HasKey(x => x.ContractId);
        builder.HasOne(x => x.Contract).WithOne().HasForeignKey<ContractDocument>(x => x.ContractId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.SignatureContentType).HasMaxLength(20);
        builder.Property(x => x.Terms).HasMaxLength(6000);
        builder.Property(x => x.TemplateVersion).HasMaxLength(40).IsRequired();
    }
}
