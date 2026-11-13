using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class ContractTemplateConfig : IEntityTypeConfiguration<ContractTemplate>
{
    public void Configure(EntityTypeBuilder<ContractTemplate> builder)
    {
        builder.ToTable("ContractTemplates", table => table.HasCheckConstraint("CK_ContractTemplates_Singleton", "\"Id\" = 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.OrganizationName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.HeaderText).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.FooterText).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.DefaultTerms).HasMaxLength(6000).IsRequired();
        builder.Property(x => x.FieldMappingsJson).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
    }
}
