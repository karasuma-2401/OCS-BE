using OCS.Api.Common;
using OCS.Api.Models.Products;

namespace OCS.Api.Interfaces.Services;

public interface IProductService
{
    Task<List<ProductDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
    Task<Result<ProductDto>> CreateAsync(CreateProductDto dto, CancellationToken cancellationToken = default);
}

