using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class ConsignmentDocumentConfig : IEntityTypeConfiguration<ConsignmentDocument>
{
    public void Configure(EntityTypeBuilder<ConsignmentDocument> builder)
    {
        builder.ToTable("ConsignmentDocuments");
        builder.HasKey(d => d.ContractId);
        builder.Property(d => d.FileName).HasMaxLength(150).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(30).IsRequired();
        builder.Property(d => d.Content).IsRequired();
        builder.HasOne(d => d.Contract).WithOne().HasForeignKey<ConsignmentDocument>(d => d.ContractId).OnDelete(DeleteBehavior.Cascade);
    }
}
