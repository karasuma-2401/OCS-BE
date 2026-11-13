using Microsoft.EntityFrameworkCore;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Models.Customers;

namespace OCS.Api.Services;

public sealed class CustomerConsignmentService(AppDbContext db, ICustomerProfileRepository customerProfiles, PrivateProductImageStore? images = null)
{
    public async Task<ConsignmentTrackingResponse[]?> GetAsync(Guid customerId, CancellationToken cancellationToken)
    {
        // Xac thuc JWT da kiem tra phien, trang thai tai khoan, email va vai tro hien tai.
        // Dung repository ho so san co de kiem tra day la khach hang co ho so dang hoat dong.
        if (!await IsActiveCustomerAsync(customerId, cancellationToken)) return null;

        // Chi truy van hop dong thuoc chu tai khoan lay tu phien dang nhap.
        var contracts = await db.ConsignmentContracts.AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .Include(c => c.Products)
            .Include(c => c.Payouts)
            .AsSplitQuery()
            .OrderByDescending(c => c.SignedAt).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        var contractIds = contracts.Select(c => c.Id).ToArray();
        var documents = await db.ConsignmentDocuments.AsNoTracking()
            .Where(d => contractIds.Contains(d.ContractId))
            .Select(d => new { d.ContractId, d.FileName, d.ContentType, d.UploadedAt })
            .ToDictionaryAsync(d => d.ContractId, cancellationToken);

        return contracts.Select(c =>
        {
            // Dung phep tinh san co cua Product; thieu hoa hong thi khong the uoc tinh.
            var products = c.Products.OrderBy(p => p.Name).ThenBy(p => p.Id).Select(p =>
                new ConsignmentProductResponse(p.Id, p.Barcode, p.Name, p.Status.ToString(), p.ListedPrice,
                    p.AgreedCommissionRate,
                    p.AgreedCommissionRate is null ? null : p.CalculateEstimatedConsignorPayout())).ToArray();
            // Tong du kien tinh cho moi san pham, khong tru cac khoan da quyet toan.
            // Tong la null neu thieu uoc tinh cua bat ky san pham nao.
            var estimatesComplete = products.All(p => p.EstimatedPayout.HasValue);
            return new ConsignmentTrackingResponse(c.Id, c.ContractNumber, c.AppointmentId, c.SignedAt,
                c.EndsAt, c.Status.ToString(), c.ExpiryHandling.ToString(), products,
                estimatesComplete ? products.Sum(p => p.EstimatedPayout!.Value) : null,
                // Chi cong quyet toan hoan tat; Pending va Transferred chua duoc tinh.
                c.Payouts.Where(p => p.Status == PayoutStatus.Completed).Sum(p => p.TotalAmount),
                documents.TryGetValue(c.Id, out var document)
                    ? new ConsignmentDocumentMetadata(document.FileName, document.ContentType, document.UploadedAt) : null);
        }).ToArray();
    }
    public async Task<bool> IsActiveCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var account = await customerProfiles.GetSelfProfileAsync(customerId, cancellationToken);
        return account?.CustomerProfile?.Status == CustomerStatus.Active;
    }

    public Task<ConsignmentDocument?> GetDocumentAsync(Guid customerId, Guid contractId, CancellationToken ct) =>
        db.ConsignmentDocuments.AsNoTracking().SingleOrDefaultAsync(
            d => d.ContractId == contractId && d.Contract.CustomerId == customerId, ct);

    public async Task<ConsignmentProductDetailResponse?> GetProductAsync(Guid customerId, Guid contractId, Guid productId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().Include(p => p.Brand).Include(p => p.Category).Include(p => p.Images)
            .SingleOrDefaultAsync(p => p.Id == productId && p.ConsignmentContractId == contractId &&
                p.ConsignmentContract != null && p.ConsignmentContract.CustomerId == customerId, ct);
        if (product is null) return null;
        var imageUrls = product.Images.Where(i => i.ImageType is ProductImageType.Cover or ProductImageType.Detail)
            .Where(i => images?.GetKey(i.Url) is not null).OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => $"/api/customers/me/consignments/{contractId}/products/{productId}/images/{i.Id}").ToArray();
        return new ConsignmentProductDetailResponse(product.Id, product.Barcode, product.Name, product.Status.ToString(),
            product.ListedPrice, product.AgreedCommissionRate,
            product.AgreedCommissionRate is null ? null : product.CalculateEstimatedConsignorPayout(),
            product.Description, product.Brand?.Name, product.Category?.Name, product.Size,
            $"{product.ConditionPercentage}%", imageUrls);
    }
    public async Task<(byte[] Content, string ContentType)?> GetImageAsync(Guid customerId, Guid contractId, Guid productId, Guid imageId, CancellationToken ct)
    {
        var image = await db.ProductImages.AsNoTracking().SingleOrDefaultAsync(i => i.Id == imageId &&
            i.ProductId == productId && i.Product.ConsignmentContractId == contractId &&
            i.Product.ConsignmentContract != null && i.Product.ConsignmentContract.CustomerId == customerId &&
            (i.ImageType == ProductImageType.Cover || i.ImageType == ProductImageType.Detail), ct);
        var key = image is null ? null : images?.GetKey(image.Url);
        return key is null ? null : await images!.ReadAsync(key, ct);
    }
}