using OCS.Application.Common;

namespace OCS.Application.Features.Brands.GetBrands;

public sealed class GetBrandsHandler(IBrandReadRepository brandReadRepository)
{
    public Task<PagedResult<BrandListItem>> HandleAsync(
        GetBrandsQuery query,
        CancellationToken cancellationToken = default) =>
        brandReadRepository.GetPageAsync(query, cancellationToken);
}
