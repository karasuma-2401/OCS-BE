using OCS.Application.Common;

namespace OCS.Application.Features.Brands.GetBrands;

public interface IBrandReadRepository
{
    Task<PagedResult<BrandListItem>> GetPageAsync(
        GetBrandsQuery query,
        CancellationToken cancellationToken = default);
}
