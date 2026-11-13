using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Configurations;

public sealed class WordDocumentTemplateConfiguration : IEntityTypeConfiguration<WordDocumentTemplate>
{
    public void Configure(EntityTypeBuilder<WordDocumentTemplate> builder)
    {
        builder.HasKey(template => template.Kind);
        builder.Property(template => template.Kind).HasMaxLength(20);
        builder.Property(template => template.Content).IsRequired();
        builder.Property(template => template.Version).IsConcurrencyToken();
    }
}
