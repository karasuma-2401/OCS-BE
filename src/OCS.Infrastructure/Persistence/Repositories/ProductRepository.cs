using Microsoft.EntityFrameworkCore;
using OCS.Application.Features.Catalog;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence.Repositories;

internal sealed class ProductRepository(AppDbContext dbContext) : IProductRepository
{
    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Products.SingleOrDefaultAsync(product => product.Id == id, cancellationToken);

    public Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) =>
        dbContext.Products.SingleOrDefaultAsync(product => product.Barcode == barcode, cancellationToken);

    public void Add(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        dbContext.Products.Add(product);
    }
}
