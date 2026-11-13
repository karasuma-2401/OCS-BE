using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class BarcodeLabelTemplateConfig : IEntityTypeConfiguration<BarcodeLabelTemplate>
{
    public void Configure(EntityTypeBuilder<BarcodeLabelTemplate> builder)
    {
        builder.ToTable("BarcodeLabelTemplates", table => table.HasCheckConstraint("CK_BarcodeLabelTemplates_Singleton", "\"Id\" = 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Version).IsConcurrencyToken();
    }
}
