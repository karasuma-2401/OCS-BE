using Microsoft.EntityFrameworkCore;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;

namespace OCS.Api.Repositories;

public sealed class ProductRepository(AppDbContext dbContext) : IProductRepository
{
    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Product?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default) =>
        dbContext.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .SingleOrDefaultAsync(p => p.Barcode == barcode, cancellationToken);

    public Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default) =>
        dbContext.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public void Add(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        dbContext.Products.Add(product);
    }
}

