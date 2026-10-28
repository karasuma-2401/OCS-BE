using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Products;

namespace OCS.Api.Services;

public sealed class ProductService(IUnitOfWork unitOfWork) : IProductService
{
    public async Task<List<ProductDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var items = await unitOfWork.ProductRepository.GetAllAsync(cancellationToken);
        return items.Select(MapToDto).ToList();
    }

    public async Task<Result<ProductDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await unitOfWork.ProductRepository.GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return Result.Failure<ProductDto>(ApplicationError.NotFound("PRODUCT.NOT_FOUND", "Không tìm thấy sản phẩm."));
        }
        return Result.Success(MapToDto(product));
    }

    public async Task<Result<ProductDto>> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        var product = await unitOfWork.ProductRepository.GetByBarcodeAsync(barcode, cancellationToken);
        if (product is null)
        {
            return Result.Failure<ProductDto>(ApplicationError.NotFound("PRODUCT.NOT_FOUND", $"Không tìm thấy sản phẩm có mã barcode {barcode}."));
        }
        return Result.Success(MapToDto(product));
    }

    public async Task<Result<ProductDto>> CreateAsync(CreateProductDto dto, CancellationToken cancellationToken = default)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Barcode = dto.Barcode,
            Size = dto.Size,
            ListedPrice = dto.ListedPrice,
            Description = dto.Description,
            CategoryId = dto.CategoryId,
            BrandId = dto.BrandId,
            Status = ProductStatus.OnSale,
            CreatedAt = DateTimeOffset.UtcNow
        };

        unitOfWork.ProductRepository.Add(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(MapToDto(product));
    }

    private static ProductDto MapToDto(Product p) =>
        new(p.Id,
            p.Barcode,
            p.Name,
            p.Description,
            p.ListedPrice,
            p.Status.ToString(),
            p.Category?.Name,
            p.Brand?.Name,
            p.Brand?.Tier.ToString());
}
