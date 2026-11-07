using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Contracts.Products;
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

    public Task<PagedResponse<PublicProductResponse>> GetPublicProductsAsync(
        GetPublicProductsRequest request,
        CancellationToken cancellationToken = default) =>
        unitOfWork.ProductRepository.GetPublicProductsAsync(request, cancellationToken);

    public async Task<Result<PublicProductDetailResponse>> GetPublicProductDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var product = await unitOfWork.ProductRepository.GetPublicProductDetailAsync(id, cancellationToken);
        if (product is null)
        {
            return Result.Failure<PublicProductDetailResponse>(
                ApplicationError.NotFound("PRODUCT.NOT_FOUND", "Không tìm thấy sản phẩm hoặc sản phẩm không còn mở bán công khai."));
        }
        return Result.Success(product);
    }

    public async Task<Result<ProductDto>> CreateAsync(CreateProductDto dto, CancellationToken cancellationToken = default)
    {
        // This legacy request cannot supply the required source or agreed terms.
        // Reject it until products can be created through the appraisal workflow.
        await Task.CompletedTask;
        return Result.Failure<ProductDto>(ApplicationError.Conflict("PRODUCT.SOURCE_REQUIRED",
            "Cần tạo sản phẩm từ hợp đồng ký gửi hoặc phiếu mua đứt đã thẩm định."));
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
