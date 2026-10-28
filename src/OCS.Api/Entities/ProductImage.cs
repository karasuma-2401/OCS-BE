using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class ProductImage : BaseEntity
{
    public Guid ProductId { get; set; }

    public string Url { get; set; } = string.Empty;

    public ProductImageType ImageType { get; set; } = ProductImageType.Detail;

    public int SortOrder { get; set; }

    public Product Product { get; set; } = null!;
}