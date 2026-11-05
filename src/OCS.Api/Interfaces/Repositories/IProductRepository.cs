using OCS.Api.Common;
using OCS.Api.Contracts.Products;
using OCS.Api.Entities;

namespace OCS.Api.Interfaces.Repositories;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
    Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PagedResponse<PublicProductResponse>> GetPublicProductsAsync(GetPublicProductsRequest request, CancellationToken cancellationToken = default);
    void Add(Product product);
}

