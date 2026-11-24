using Microsoft.EntityFrameworkCore;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Customers;

namespace OCS.Api.Services;

public sealed class CustomerConsignmentService(AppDbContext db, ICustomerProfileRepository customerProfiles, PrivateProductImageStore? images = null, IContractService? contractService = null, TimeProvider? clock = null)
{
    private static readonly TimeZoneInfo VietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public async Task<ConsignmentTrackingResponse[]?> GetAsync(Guid customerId, CancellationToken cancellationToken)
    {
        // Xac thuc JWT da kiem tra phien, trang thai tai khoan, email va vai tro hien tai.
        // Dung repository ho so san co de kiem tra day la khach hang co ho so dang hoat dong.
        if (!await IsActiveCustomerAsync(customerId, cancellationToken)) return null;

        // Chi truy van hop dong thuoc chu tai khoan lay tu phien dang nhap.
        var contracts = await db.ConsignmentContracts.AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .Include(c => c.Products)
            .AsSplitQuery()
            .OrderByDescending(c => c.SignedAt).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        return await MapContractsAsync(contracts, cancellationToken);
    }

    // Truy van lower/contains duoc EF Core dich sang SQL va dung collation cua database.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304", Justification = "EF Core translates ToLower to PostgreSQL LOWER.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311", Justification = "EF Core translates ToLower to PostgreSQL LOWER.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1862", Justification = "StringComparison overloads cannot be translated by EF Core PostgreSQL.")]
    public async Task<ConsignmentSearchResponse?> SearchAsync(Guid customerId, ConsignmentSearchRequest request, CancellationToken ct)
    {
        if (!await IsActiveCustomerAsync(customerId, ct)) return null;
        var own = db.ConsignmentContracts.AsNoTracking().Where(c => c.CustomerId == customerId);
        var totalContracts = await own.CountAsync(ct);
        var activeContracts = await own.CountAsync(c => c.Status == ConsignmentContractStatus.Active, ct);
        var ownProducts = db.Products.AsNoTracking().Where(p => p.ConsignmentContract != null && p.ConsignmentContract.CustomerId == customerId);
        var onSale = await ownProducts.CountAsync(p => p.Status == ProductStatus.OnSale, ct);
        var sold = await ownProducts.CountAsync(p => p.Status == ProductStatus.Sold, ct);
        var settled = await db.Payouts.AsNoTracking().Where(p => p.ConsignmentContract.CustomerId == customerId && p.Status == PayoutStatus.Completed)
            .SumAsync(p => (decimal?)p.TotalAmount, ct) ?? 0m;
        var overview = new ConsignmentOverview(totalContracts, activeContracts, onSale, sold, settled);

        var filtered = own;
        var keyword = request.Query?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(keyword))
            filtered = filtered.Where(c => c.ContractNumber.ToLower().Contains(keyword) ||
                c.Products.Any(p => p.Name.ToLower().Contains(keyword) || p.Barcode.ToLower().Contains(keyword)));
        if (request.Status is not null && Enum.TryParse<ConsignmentContractStatus>(request.Status, out var status))
            filtered = filtered.Where(c => c.Status == status);
        if (ConsignmentSearchRequest.TryDate(request.SignedFrom, out var signedFrom))
            filtered = filtered.Where(c => c.SignedAt >= VietnamDayStart(signedFrom));
        if (ConsignmentSearchRequest.TryDate(request.SignedTo, out var signedTo) && signedTo != DateOnly.MaxValue)
            filtered = filtered.Where(c => c.SignedAt < VietnamDayStart(signedTo.AddDays(1)));
        if (ConsignmentSearchRequest.TryDate(request.ExpiresFrom, out var expiresFrom))
            filtered = filtered.Where(c => c.EndsAt >= VietnamDayStart(expiresFrom));
        if (ConsignmentSearchRequest.TryDate(request.ExpiresTo, out var expiresTo) && expiresTo != DateOnly.MaxValue)
            filtered = filtered.Where(c => c.EndsAt < VietnamDayStart(expiresTo.AddDays(1)));

        var total = await filtered.CountAsync(ct);
        var offset = (long)(request.Page - 1) * request.PageSize;
        var contracts = offset >= total ? [] : await filtered.Include(c => c.Products).AsSplitQuery()
            .OrderByDescending(c => c.SignedAt).ThenBy(c => c.Id)
            .Skip((int)offset).Take(request.PageSize).ToListAsync(ct);
        var items = await MapContractsAsync(contracts, ct);
        var pages = (int)Math.Ceiling((decimal)total / request.PageSize);
        return new ConsignmentSearchResponse(items, request.Page, request.PageSize, total, pages, overview);
    }

    // Lay dau ngay theo mui gio Viet Nam, bao gom quy tac lich su cua mui gio.
    private static DateTimeOffset VietnamDayStart(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var utcTicks = Math.Clamp(local.Ticks - VietnamTimeZone.GetUtcOffset(local).Ticks, DateTimeOffset.MinValue.Ticks, DateTimeOffset.MaxValue.Ticks);
        return new DateTimeOffset(utcTicks, TimeSpan.Zero);
    }

    private async Task<ConsignmentTrackingResponse[]> MapContractsAsync(List<ConsignmentContract> contracts, CancellationToken cancellationToken)
    {
        var contractIds = contracts.Select(c => c.Id).ToArray();
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var withdrawals = await db.EarlyWithdrawalItems.AsNoTracking()
            .Where(i => i.Product.ConsignmentContractId != null && contractIds.Contains(i.Product.ConsignmentContractId.Value) &&
                i.EarlyWithdrawal.CustomerId == i.Product.ConsignmentContract!.CustomerId)
            .Select(i => new { i.ProductId, Info = new ConsignmentWithdrawalInfo(i.EarlyWithdrawalId,
                i.EarlyWithdrawal.Status, i.EarlyWithdrawal.PaymentStatus.ToString(), i.EarlyWithdrawal.DecisionNote, i.EarlyWithdrawal.RequestedAt) })
            .ToListAsync(cancellationToken);
        var withdrawalByProduct = withdrawals.GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.Info.RequestedAt).ThenByDescending(i => i.Info.Id).First().Info);
        var activeWithdrawalIds = withdrawals.Where(i => i.Info.Status is "Pending" or "Approved").Select(i => i.ProductId).ToHashSet();
        // Customer tracking needs only completed totals, not internal payout/approver records.
        var settledByContract = await db.Payouts.AsNoTracking()
            .Where(p => contractIds.Contains(p.ConsignmentContractId) && p.Status == PayoutStatus.Completed)
            .GroupBy(p => p.ConsignmentContractId)
            .Select(g => new { ContractId = g.Key, Total = g.Sum(p => p.TotalAmount) })
            .ToDictionaryAsync(p => p.ContractId, p => p.Total, cancellationToken);
        var expiryItems = await db.ExpiryProcessingItems.AsNoTracking()
            .Where(i => i.Product.ConsignmentContractId != null && contractIds.Contains(i.Product.ConsignmentContractId.Value))
            .Select(i => new
            {
                i.ProductId,
                i.HandledAt,
                i.Product.ConsignmentContractId,
                i.ExpiryProcessing.Type,
                i.ExpiryProcessing.Status,
                i.ExpiryProcessing.CollectionDeadline
            })
            .ToListAsync(cancellationToken);
        var expiryByProduct = expiryItems.ToDictionary(i => i.ProductId);
        var expiryByContract = expiryItems.GroupBy(i => i.ConsignmentContractId!.Value)
            .ToDictionary(g => g.Key, g => g.First());
        var documents = await db.ConsignmentDocuments.AsNoTracking()
            .Where(d => contractIds.Contains(d.ContractId))
            .Select(d => new { d.ContractId, d.FileName, d.ContentType, d.UploadedAt })
            .ToDictionaryAsync(d => d.ContractId, cancellationToken);
        var uploadedIds = documents.Keys.ToArray();
        var generatedDocuments = await db.ContractDocuments.AsNoTracking()
            .Where(d => contractIds.Contains(d.ContractId) && !uploadedIds.Contains(d.ContractId) && d.Content.Length > 0)
            .Select(d => new { d.ContractId, d.Content, d.CreatedAt })
            .ToListAsync(cancellationToken);
        var generatedMetadata = generatedDocuments.Select(d => (d.ContractId, Metadata: DocumentMetadata(d.ContractId, d.Content, d.CreatedAt)))
            .Where(d => d.Metadata is not null).ToDictionary(d => d.ContractId, d => d.Metadata!);

        return contracts.Select(c =>
        {
            // Dung phep tinh san co cua Product; thieu hoa hong thi khong the uoc tinh.
            var products = c.Products.OrderBy(p => p.Name).ThenBy(p => p.Id).Select(p =>
                new ConsignmentProductResponse(p.Id, p.Barcode, p.Name, p.Status.ToString(), p.ListedPrice,
                    p.AgreedCommissionRate,
                    p.AgreedCommissionRate is null ? null : p.CalculateEstimatedConsignorPayout(),
                    HandledAt: expiryByProduct.GetValueOrDefault(p.Id)?.HandledAt,
                    CanWithdrawEarly: p.CanRequestEarlyWithdrawal(now) && !activeWithdrawalIds.Contains(p.Id),
                    EarlyWithdrawalFee: p.AgreedCommissionRate is >= 0 and <= 100 ? p.CalculateEarlyWithdrawalFee() : null,
                    Withdrawal: withdrawalByProduct.GetValueOrDefault(p.Id),
                    EarlyWithdrawalBlockReason: WithdrawalBlockReason(p, withdrawalByProduct.GetValueOrDefault(p.Id), activeWithdrawalIds.Contains(p.Id), now))).ToArray();
            // Tong du kien tinh cho moi san pham, khong tru cac khoan da quyet toan.
            // Tong la null neu thieu uoc tinh cua bat ky san pham nao.
            var estimatesComplete = products.All(p => p.EstimatedPayout.HasValue);
            return new ConsignmentTrackingResponse(c.Id, c.ContractNumber, c.AppointmentId, c.SignedAt,
                c.EndsAt, c.Status.ToString(), c.ExpiryHandling.ToString(), products,
                estimatesComplete ? products.Sum(p => p.EstimatedPayout!.Value) : null,
                // Chi cong quyet toan hoan tat; Pending va Transferred chua duoc tinh.
                settledByContract.GetValueOrDefault(c.Id),
                documents.TryGetValue(c.Id, out var document)
                    ? new ConsignmentDocumentMetadata(document.FileName, document.ContentType, document.UploadedAt)
                    : generatedMetadata.GetValueOrDefault(c.Id) ?? (contractService is null ? null : new ConsignmentDocumentMetadata($"ban-xem-{c.Id}.pdf", "application/pdf", null)),
                expiryByContract.TryGetValue(c.Id, out var expiry) ? expiry.Type.ToString() : null,
                expiryByContract.TryGetValue(c.Id, out expiry) ? expiry.Status.ToString() : null,
                expiryByContract.TryGetValue(c.Id, out expiry) ? expiry.CollectionDeadline : null);
        }).ToArray();
    }
    public async Task<bool> IsActiveCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var account = await customerProfiles.GetSelfProfileAsync(customerId, cancellationToken);
        return account?.CustomerProfile?.Status == CustomerStatus.Active;
    }

    private static string? WithdrawalBlockReason(Product product, ConsignmentWithdrawalInfo? withdrawal, bool hasActiveRequest, DateTimeOffset now)
    {
        if (product.Status == ProductStatus.OnSale && hasActiveRequest)
            return withdrawal?.Status == "Approved"
                ? withdrawal.PaymentStatus == "Paid" ? "Yêu cầu rút món này đã được xử lý phí và đang chờ bàn giao." : "Yêu cầu rút món này đã được duyệt, đang chờ thu phí và bàn giao."
                : "Món này đã có yêu cầu rút đang chờ duyệt. Bạn có thể xem tiến trình tại Theo dõi yêu cầu.";
        return product.GetEarlyWithdrawalBlockReason(now);
    }

    private static ConsignmentDocumentMetadata? DocumentMetadata(Guid contractId, byte[] content, DateTimeOffset createdAt)
    {
        if (content.AsSpan().StartsWith("%PDF"u8)) return new($"hop-dong-{contractId}.pdf", "application/pdf", createdAt);
        if (content.AsSpan().StartsWith("PK"u8)) return new($"hop-dong-{contractId}.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", createdAt);
        return null;
    }

    public async Task<ConsignmentDocument?> GetDocumentAsync(Guid customerId, Guid contractId, CancellationToken ct)
    {
        var uploaded = await db.ConsignmentDocuments.AsNoTracking().SingleOrDefaultAsync(
            d => d.ContractId == contractId && d.Contract.CustomerId == customerId, ct);
        if (uploaded is not null) return uploaded;
        var generated = await db.ContractDocuments.AsNoTracking().SingleOrDefaultAsync(
            d => d.ContractId == contractId && d.Contract.CustomerId == customerId, ct);
        if (generated is not null)
        {
            var metadata = DocumentMetadata(contractId, generated.Content, generated.CreatedAt);
            if (metadata is not null) return new ConsignmentDocument { ContractId = contractId, FileName = metadata.FileName, ContentType = metadata.ContentType, UploadedAt = generated.CreatedAt, Content = generated.Content };
        }
        if (contractService is null || !await db.ConsignmentContracts.AsNoTracking().AnyAsync(c => c.Id == contractId && c.CustomerId == customerId, ct)) return null;
        var preview = await contractService.GetCustomerPreviewPdfAsync(contractId, ct);
        return preview is null || preview.Length == 0 ? null : new ConsignmentDocument { ContractId = contractId, FileName = $"ban-xem-{contractId}.pdf", ContentType = "application/pdf", Content = preview };
    }

    public async Task<ConsignmentProductDetailResponse?> GetProductAsync(Guid customerId, Guid contractId, Guid productId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().Include(p => p.Brand).Include(p => p.Category).Include(p => p.Images).Include(p => p.ConsignmentContract)
            .SingleOrDefaultAsync(p => p.Id == productId && p.ConsignmentContractId == contractId &&
                p.ConsignmentContract != null && p.ConsignmentContract.CustomerId == customerId, ct);
        if (product is null) return null;
        var withdrawal = await db.EarlyWithdrawalItems.AsNoTracking()
            .Where(i => i.ProductId == productId && i.EarlyWithdrawal.CustomerId == customerId)
            .OrderByDescending(i => i.EarlyWithdrawal.RequestedAt).ThenByDescending(i => i.EarlyWithdrawalId)
            .Select(i => new ConsignmentWithdrawalInfo(i.EarlyWithdrawalId, i.EarlyWithdrawal.Status,
                i.EarlyWithdrawal.PaymentStatus.ToString(), i.EarlyWithdrawal.DecisionNote, i.EarlyWithdrawal.RequestedAt))
            .FirstOrDefaultAsync(ct);
        var expiry = await db.ExpiryProcessingItems.AsNoTracking()
            .Where(i => i.ProductId == productId)
            .Select(i => new
            {
                i.HandledAt,
                i.ExpiryProcessing.Type,
                i.ExpiryProcessing.Status,
                i.ExpiryProcessing.CollectionDeadline
            }).SingleOrDefaultAsync(ct);
        var imageUrls = product.Images.Where(i => i.ImageType is ProductImageType.Cover or ProductImageType.Detail)
            .Where(i => images?.GetKey(i.Url) is not null).OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
            .Select(i => $"/api/customers/me/consignments/{contractId}/products/{productId}/images/{i.Id}").ToArray();
        return new ConsignmentProductDetailResponse(product.Id, product.Barcode, product.Name, product.Status.ToString(),
            product.ListedPrice, product.AgreedCommissionRate,
            product.AgreedCommissionRate is null ? null : product.CalculateEstimatedConsignorPayout(),
            product.Description, product.Brand?.Name, product.Category?.Name, product.Size,
            $"{product.ConditionPercentage}%", imageUrls, expiry?.HandledAt,
            expiry?.Type.ToString(), expiry?.Status.ToString(), expiry?.CollectionDeadline,
            product.CanRequestEarlyWithdrawal((clock ?? TimeProvider.System).GetUtcNow()) && withdrawal?.Status is not ("Pending" or "Approved"),
            product.AgreedCommissionRate is >= 0 and <= 100 ? product.CalculateEarlyWithdrawalFee() : null, withdrawal,
            WithdrawalBlockReason(product, withdrawal, withdrawal?.Status is "Pending" or "Approved", (clock ?? TimeProvider.System).GetUtcNow()));
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
