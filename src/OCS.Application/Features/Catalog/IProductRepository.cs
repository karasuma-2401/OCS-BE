using OCS.Domain.Entities;

namespace OCS.Application.Features.Catalog;

// Returned entities are tracked in the same scope as IUnitOfWork.
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
    void Add(Product product);
}
