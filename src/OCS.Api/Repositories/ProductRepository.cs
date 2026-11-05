using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Contracts.Products;
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

    public async Task<PagedResponse<PublicProductResponse>> GetPublicProductsAsync(
        GetPublicProductsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = dbContext.Products
            .AsNoTracking()
            .Where(p => p.Status == ProductStatus.OnSale);

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var keyword = request.Q.Trim().ToLower(CultureInfo.InvariantCulture);
            query = query.Where(p =>
                p.Name.ToLower().Contains(keyword) ||
                p.Brand.Name.ToLower().Contains(keyword) ||
                p.Category.Name.ToLower().Contains(keyword));
        }

        if (request.CategoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == request.CategoryId.Value);
        }

        if (request.BrandId.HasValue)
        {
            query = query.Where(p => p.BrandId == request.BrandId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Size))
        {
            var trimmedSize = request.Size.Trim().ToLower(CultureInfo.InvariantCulture);
            query = query.Where(p => p.Size.ToLower() == trimmedSize);
        }

        if (request.MinPrice.HasValue)
        {
            query = query.Where(p => p.ListedPrice >= request.MinPrice.Value);
        }

        if (request.MaxPrice.HasValue)
        {
            query = query.Where(p => p.ListedPrice <= request.MaxPrice.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.ListedPrice,
                p.Size,
                p.ConditionPercentage,
                Category = new
                {
                    p.Category.Id,
                    p.Category.Name
                },
                Brand = new
                {
                    p.Brand.Id,
                    p.Brand.Name
                },
                CoverImageUrl = p.Images
                    .Where(img => img.ImageType == ProductImageType.Cover)
                    .OrderBy(img => img.SortOrder)
                    .ThenBy(img => img.Id)
                    .Select(img => img.Url)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new PublicProductResponse(
                r.Id,
                r.Name,
                r.Description,
                r.ListedPrice.ToString("0.00", CultureInfo.InvariantCulture),
                r.Size,
                r.ConditionPercentage,
                new PublicCategoryResponse(r.Category.Id, r.Category.Name),
                new PublicBrandResponse(r.Brand.Id, r.Brand.Name),
                r.CoverImageUrl))
            .ToList();

        return new PagedResponse<PublicProductResponse>(
            items,
            request.Page,
            request.PageSize,
            totalCount);
    }

    public void Add(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        dbContext.Products.Add(product);
    }
}

