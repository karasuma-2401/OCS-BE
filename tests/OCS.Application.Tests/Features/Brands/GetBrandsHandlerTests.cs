using OCS.Application.Common;
using OCS.Application.Features.Brands.GetBrands;
using OCS.Domain.Enums;

namespace OCS.Application.Tests.Features.Brands;

public sealed class GetBrandsHandlerTests
{
    [Fact]
    public async Task HandleAsyncDelegatesTheRequestedPageToTheReadRepository()
    {
        var expected = new PagedResult<BrandListItem>(
            [new BrandListItem(Guid.NewGuid(), "OCS", BrandTier.LocalBrand, 15m, true)],
            1);
        var repository = new RecordingBrandReadRepository(expected);
        var handler = new GetBrandsHandler(repository);
        var query = new GetBrandsQuery(20, 10);

        var result = await handler.HandleAsync(query);

        Assert.Same(expected, result);
        Assert.Equal(query, repository.ReceivedQuery);
    }

    private sealed class RecordingBrandReadRepository(PagedResult<BrandListItem> result) : IBrandReadRepository
    {
        public GetBrandsQuery? ReceivedQuery { get; private set; }

        public Task<PagedResult<BrandListItem>> GetPageAsync(
            GetBrandsQuery query,
            CancellationToken cancellationToken = default)
        {
            ReceivedQuery = query;
            return Task.FromResult(result);
        }
    }
}
