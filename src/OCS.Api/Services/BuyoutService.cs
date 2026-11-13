using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Buyouts;
using OCS.Api.Authorization;

namespace OCS.Api.Services;

public sealed class BuyoutService(AppDbContext db, TimeProvider clock)
{
    private const decimal MaxMoney = 9999999999999999.99m;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304", Justification = "EF Core translates ToLower to database LOWER.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311", Justification = "EF Core translates ToLower to database LOWER.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1862", Justification = "StringComparison cannot be translated by EF Core PostgreSQL.")]
    public async Task<Result<IReadOnlyList<BuyoutCustomerResponse>>> FindCustomersAsync(Guid actor, string? query, CancellationToken ct)
    {
        if (!await EmployeeActive(actor, ct)) return Fail<IReadOnlyList<BuyoutCustomerResponse>>("BUYOUT.EMPLOYEE_INVALID", "Cần tài khoản nhân viên đang làm việc.");
        var term = query?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(term) || term.Length is < 2 or > 100)
            return Invalid<IReadOnlyList<BuyoutCustomerResponse>>("Nhập từ 2 đến 100 ký tự tên hoặc số điện thoại khách hàng.");
        var customers = await db.CustomerProfiles.AsNoTracking()
            .Where(c => c.Status == CustomerStatus.Active && c.Account.AccountType == AccountType.Customer && c.Account.Status == AccountStatus.Active &&
                (c.Account.FullName.ToLower().Contains(term) || c.Account.PhoneNumber.Contains(term)))
            .OrderBy(c => c.Account.FullName).ThenBy(c => c.Id).Take(20)
            .Select(c => new BuyoutCustomerResponse(c.Id, c.Account.FullName, c.Account.PhoneNumber)).ToListAsync(ct);
        return Result.Success<IReadOnlyList<BuyoutCustomerResponse>>(customers);
    }

    public async Task<Result<BuyoutInspectionResponse>> InspectAsync(Guid actor, CreateBuyoutInspectionRequest request, CancellationToken ct)
    {
        if (!await EmployeeActive(actor, ct)) return Fail<BuyoutInspectionResponse>("BUYOUT.EMPLOYEE_INVALID", "Cần tài khoản nhân viên đang làm việc.");
        if (!request.Passed || request.CustomerId == Guid.Empty || request.BrandId == Guid.Empty || request.CategoryId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200 || string.IsNullOrWhiteSpace(request.Size) || request.Size.Trim().Length > 30 ||
            string.IsNullOrWhiteSpace(request.InspectionNotes) || request.InspectionNotes.Trim().Length > 1000 || request.Description?.Length > 2000 || request.ConditionPercentage is < 0 or > 100)
            return Invalid<BuyoutInspectionResponse>("Cần kết quả kiểm định đạt và thông tin sản phẩm hợp lệ.");
        var customerName = await db.CustomerProfiles.Where(c => c.Id == request.CustomerId && c.Status == CustomerStatus.Active && c.Account.Status == AccountStatus.Active && c.Account.AccountType == AccountType.Customer).Select(c => c.Account.FullName).SingleOrDefaultAsync(ct);
        if (customerName is null)
            return Invalid<BuyoutInspectionResponse>("Khách hàng không tồn tại hoặc không hoạt động.");
        var brandName = await db.Brands.Where(b => b.Id == request.BrandId && b.IsActive).Select(b => b.Name).SingleOrDefaultAsync(ct);
        var categoryName = await db.Categories.Where(c => c.Id == request.CategoryId && c.IsActive).Select(c => c.Name).SingleOrDefaultAsync(ct);
        if (brandName is null || categoryName is null)
            return Invalid<BuyoutInspectionResponse>("Thương hiệu và danh mục phải đang hoạt động.");
        var i = new BuyoutInspection { CustomerId = request.CustomerId, InspectedByEmployeeId = actor, Name = request.Name.Trim(), Size = request.Size.Trim(), Description = request.Description?.Trim(), ConditionPercentage = request.ConditionPercentage, BrandId = request.BrandId, CategoryId = request.CategoryId, InspectionNotes = request.InspectionNotes.Trim(), CreatedAt = clock.GetUtcNow() };
        db.BuyoutInspections.Add(i);
        await db.SaveChangesAsync(ct);
        return Result.Success(new BuyoutInspectionResponse(i.Id, i.CustomerId, i.Name, i.Size, i.Description, i.ConditionPercentage, i.BrandId, i.CategoryId, i.InspectionNotes, i.InspectedByEmployeeId, i.CreatedAt, true, customerName, brandName, categoryName));
    }

    public async Task<Result<IReadOnlyList<BuyoutInspectionResponse>>> ListInspectionsAsync(Guid? customerId, CancellationToken ct)
    {
        var query = db.BuyoutInspections.AsNoTracking();
        if (customerId.HasValue) query = query.Where(i => i.CustomerId == customerId.Value);
        var result = await query.OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id)
            .Select(i => new BuyoutInspectionResponse(i.Id, i.CustomerId, i.Name, i.Size, i.Description, i.ConditionPercentage,
                i.BrandId, i.CategoryId, i.InspectionNotes, i.InspectedByEmployeeId, i.CreatedAt,
                !db.BuyoutProposalItems.Any(p => p.InspectionId == i.Id && p.IsActive), i.Customer.Account.FullName, i.Brand.Name, i.Category.Name)).ToListAsync(ct);
        return Result.Success<IReadOnlyList<BuyoutInspectionResponse>>(result);
    }

    public async Task<Result<BuyoutResponse>> CreateAsync(Guid actor, CreateBuyoutRequest request, CancellationToken ct)
    {
        if (!await EmployeeActive(actor, ct)) return Fail<BuyoutResponse>("BUYOUT.EMPLOYEE_INVALID", "Cần tài khoản nhân viên đang làm việc.");
        if (request.InspectionId == Guid.Empty || !Money(request.PurchasePrice) || request.PurchasePrice == 0 || !Money(request.ListedPrice) || request.ListedPrice == 0) return Invalid<BuyoutResponse>("Cần bản kiểm định và giá mua, giá bán hợp lệ.");
        var inspection = await db.BuyoutInspections.Include(i => i.Customer).ThenInclude(c => c.Account).Include(i => i.Brand).Include(i => i.Category).SingleOrDefaultAsync(i => i.Id == request.InspectionId, ct);
        if (inspection is null) return Missing<BuyoutResponse>();
        if (inspection.Customer.Status != CustomerStatus.Active || inspection.Customer.Account.Status != AccountStatus.Active || !inspection.Brand.IsActive || !inspection.Category.IsActive)
            return Invalid<BuyoutResponse>("Khách hàng, thương hiệu và danh mục phải đang hoạt động.");
        if (await db.BuyoutProposalItems.AnyAsync(i => i.InspectionId == inspection.Id && i.IsActive, ct)) return Conflict<BuyoutResponse>("Sản phẩm kiểm định đã có phiếu thu mua đang hoạt động.");
        var buyout = new Buyout { CustomerId = inspection.CustomerId, CreatedByEmployeeId = actor, TotalPurchasePrice = request.PurchasePrice, CreatedAt = clock.GetUtcNow() };
        buyout.BuyoutNumber = $"BO-{buyout.Id:N}";
        var item = new BuyoutProposalItem { BuyoutId = buyout.Id, InspectionId = inspection.Id, ListedPrice = request.ListedPrice, Buyout = buyout, Inspection = inspection };
        db.BuyoutProposalItems.Add(item);
        var reviewers = await db.Accounts.Where(a => a.AccountType == AccountType.Internal && a.Status == AccountStatus.Active &&
            a.EmployeeProfile != null && a.EmployeeProfile.Status == EmployeeStatus.Working &&
            a.AccountRoles.Any(ar => ar.Role.Code == "Admin" || ar.Role.Code == "Manager") &&
            a.AccountRoles.Any(ar => ar.Role.RolePermissions.Any(rp => rp.Permission.Code == PermissionCatalog.ProductsApprove)))
            .Select(a => a.Id).Distinct().ToListAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var reviewer in reviewers)
            db.Notifications.Add(new Notification
            {
                RecipientAccountId = reviewer,
                SenderAccountId = actor,
                Type = NotificationType.System,
                Title = "Phiếu mua đứt chờ duyệt",
                Content = $"Phiếu {buyout.BuyoutNumber} đang chờ xét duyệt.",
                SentAt = now,
                CreatedAt = now
            });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { return Conflict<BuyoutResponse>("Sản phẩm kiểm định đã có phiếu thu mua đang hoạt động."); }
        return Result.Success(Map(buyout, item, null));
    }

    public async Task<Result<BuyoutResponse>> GetAsync(Guid id, CancellationToken ct)
    {
        var item = await Items().SingleOrDefaultAsync(x => x.BuyoutId == id, ct);
        return item is null ? Missing<BuyoutResponse>() : Result.Success(Map(item.Buyout, item, item.Buyout.Products.SingleOrDefault()));
    }

    public async Task<Result<BuyoutReceiptResponse>> GetReceiptAsync(Guid id, CancellationToken ct)
    {
        var item = await Items().AsNoTracking().Include(i => i.Buyout).ThenInclude(b => b.Customer).ThenInclude(c => c.Account)
            .SingleOrDefaultAsync(i => i.BuyoutId == id && i.Buyout.PaidAt != null, ct);
        if (item is null) return Missing<BuyoutReceiptResponse>();
        var transaction = await db.FinancialTransactions.AsNoTracking().Include(t => t.PerformedByEmployee).ThenInclude(e => e.Account)
            .SingleOrDefaultAsync(t => t.BuyoutId == id && t.Type == FinancialTransactionType.Expense && t.Status == FinancialTransactionStatus.Success, ct);
        if (transaction is null) return Missing<BuyoutReceiptResponse>();
        return Result.Success(new BuyoutReceiptResponse(item.BuyoutId, item.Buyout.BuyoutNumber, item.Buyout.CustomerId,
            item.Buyout.Customer.Account.FullName, item.Inspection.Name, item.Inspection.Size, item.Inspection.ConditionPercentage,
            transaction.Amount, item.ListedPrice, transaction.Id, transaction.PaymentMethod.ToString(), transaction.BankTransactionReference,
            transaction.OccurredAt, transaction.PerformedByEmployeeId, transaction.PerformedByEmployee.Account.FullName,
            item.Buyout.Products.SingleOrDefault()?.Barcode));
    }

    public async Task<Result<IReadOnlyList<BuyoutResponse>>> ListAsync(CancellationToken ct)
    {
        var items = await Items().AsNoTracking().OrderByDescending(i => i.Buyout.CreatedAt).ThenBy(i => i.BuyoutId).ToListAsync(ct);
        return Result.Success<IReadOnlyList<BuyoutResponse>>(items.Select(i => Map(i.Buyout, i, i.Buyout.Products.SingleOrDefault())).ToArray());
    }

    public async Task<Result<BuyoutResponse>> ReviewAsync(Guid id, Guid actor, ReviewBuyoutRequest request, CancellationToken ct)
    {
        if (!await EmployeeActive(actor, ct)) return Fail<BuyoutResponse>("BUYOUT.EMPLOYEE_INVALID", "Cần tài khoản nhân viên đang làm việc.");
        var item = await Items().SingleOrDefaultAsync(x => x.BuyoutId == id, ct);
        if (item is null) return Missing<BuyoutResponse>();
        var b = item.Buyout;
        if (request.ExpectedVersion == Guid.Empty || b.Version != request.ExpectedVersion || b.Status != BuyoutStatus.Pending || b.PaidAt is not null)
            return Conflict<BuyoutResponse>("Phiếu đã thay đổi hoặc không còn chờ duyệt.");
        if (!request.Approve && (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)) return Invalid<BuyoutResponse>("Cần lý do từ chối, tối đa 1000 ký tự.");
        b.Status = request.Approve ? BuyoutStatus.Approved : BuyoutStatus.Rejected;
        b.ApprovedByEmployeeId = request.Approve ? actor : null;
        b.RejectionReason = request.Approve ? null : request.Reason!.Trim();
        b.Version = Guid.NewGuid();
        if (!request.Approve) item.IsActive = false;
        var now = clock.GetUtcNow();
        db.Notifications.Add(new Notification
        {
            RecipientAccountId = b.CreatedByEmployeeId,
            SenderAccountId = actor,
            Type = NotificationType.System,
            Title = request.Approve ? "Phiếu mua đứt đã được duyệt" : "Phiếu mua đứt bị từ chối",
            Content = request.Approve ? $"Phiếu {b.BuyoutNumber} đã được duyệt và có thể ghi nhận chi tiền." : $"Phiếu {b.BuyoutNumber} đã bị từ chối. Xem chi tiết để biết lý do.",
            SentAt = now,
            CreatedAt = now
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict<BuyoutResponse>("Phiếu đã thay đổi. Vui lòng tải lại."); }
        return Result.Success(Map(b, item, null));
    }

    public async Task<Result<BuyoutResponse>> PayAsync(Guid id, Guid actor, PayBuyoutRequest request, CancellationToken ct)
    {
        if (!await EmployeeActive(actor, ct)) return Fail<BuyoutResponse>("BUYOUT.EMPLOYEE_INVALID", "Cần tài khoản nhân viên đang làm việc.");
        var item = await Items().Include(i => i.Inspection).ThenInclude(i => i.Brand).Include(i => i.Inspection).ThenInclude(i => i.Category).SingleOrDefaultAsync(x => x.BuyoutId == id, ct);
        if (item is null) return Missing<BuyoutResponse>();
        var b = item.Buyout;
        if (request.ExpectedVersion == Guid.Empty || b.Version != request.ExpectedVersion || b.Status != BuyoutStatus.Approved || b.ApprovedByEmployeeId is null || b.PaidAt is not null || b.Products.Count != 0)
            return Conflict<BuyoutResponse>("Phiếu chưa được duyệt, đã thay đổi hoặc đã chi tiền.");
        if (!request.CustomerAgreed || request.PaymentMethod is not ("Cash" or "BankTransfer") || request.StorageLocationId == Guid.Empty ||
            (request.PaymentMethod == "BankTransfer" && string.IsNullOrWhiteSpace(request.BankTransactionReference)) || request.BankTransactionReference?.Trim().Length > 100)
            return Invalid<BuyoutResponse>("Cần xác nhận khách đồng ý, hình thức chi hợp lệ, vị trí kho hoạt động và mã giao dịch khi chuyển khoản.");
        if (!await db.StorageLocations.AnyAsync(l => l.Id == request.StorageLocationId && l.IsActive, ct) || !item.Inspection.Brand.IsActive || !item.Inspection.Category.IsActive)
            return Invalid<BuyoutResponse>("Vị trí kho, thương hiệu và danh mục phải đang hoạt động.");
        var now = clock.GetUtcNow();
        var product = new Product { Name = item.Inspection.Name, Size = item.Inspection.Size, Description = item.Inspection.Description, ConditionPercentage = item.Inspection.ConditionPercentage, BrandId = item.Inspection.BrandId, CategoryId = item.Inspection.CategoryId, StorageLocationId = request.StorageLocationId, ApprovalStatus = ProductApprovalStatus.Approved };
        product.Barcode = $"BO-{product.Id:N}";
        product.AssignBuyoutTerms(b.Id, item.ListedPrice, b.TotalPurchasePrice);
        product.OfferForSale();
        b.PaidAt = now; b.PaidByEmployeeId = actor; b.Version = Guid.NewGuid();
        db.Products.Add(product);
        db.ProductStatusHistories.Add(new ProductStatusHistory { ProductId = product.Id, PreviousStatus = ProductStatus.PendingProcessing, NewStatus = ProductStatus.OnSale, ChangedByAccountId = actor, OccurredAt = now, Reason = "Customer agreed and buyout payment confirmed" });
        db.FinancialTransactions.Add(new FinancialTransaction { BuyoutId = b.Id, CustomerId = b.CustomerId, Type = FinancialTransactionType.Expense, Amount = b.TotalPurchasePrice, PaymentMethod = request.PaymentMethod == "Cash" ? PaymentMethod.Cash : PaymentMethod.BankTransfer, BankTransactionReference = request.BankTransactionReference?.Trim(), Status = FinancialTransactionStatus.Success, OccurredAt = now, PerformedByEmployeeId = actor });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict<BuyoutResponse>("Phiếu đã thay đổi. Vui lòng tải lại."); }
        return Result.Success(Map(b, item, product));
    }

    private IQueryable<BuyoutProposalItem> Items() => db.BuyoutProposalItems.Include(i => i.Inspection).ThenInclude(i => i.Customer).ThenInclude(c => c.Account)
        .Include(i => i.Inspection).ThenInclude(i => i.Brand).Include(i => i.Inspection).ThenInclude(i => i.Category)
        .Include(i => i.Buyout).ThenInclude(b => b.Products);
    private Task<bool> EmployeeActive(Guid actor, CancellationToken ct) => db.EmployeeProfiles.AnyAsync(e => e.Id == actor && e.Status == EmployeeStatus.Working && e.Account.Status == AccountStatus.Active && e.Account.AccountType == AccountType.Internal, ct);
    private static bool Money(decimal value) => value >= 0 && value <= MaxMoney && decimal.Round(value, 2) == value;
    private static BuyoutResponse Map(Buyout b, BuyoutProposalItem i, Product? p) => new(b.Id, b.BuyoutNumber, b.CustomerId, i.InspectionId, b.CreatedByEmployeeId, b.ApprovedByEmployeeId, b.PaidAt.HasValue ? "Paid" : b.Status.ToString(), b.TotalPurchasePrice, i.ListedPrice, b.Version, b.CreatedAt, b.PaidAt, b.PaidByEmployeeId, b.RejectionReason, p?.Id, p?.Barcode, new BuyoutInspectionSnapshot(i.Inspection.Id, i.Inspection.CustomerId, i.Inspection.Name, i.Inspection.Size, i.Inspection.Description, i.Inspection.ConditionPercentage, i.Inspection.BrandId, i.Inspection.CategoryId, i.Inspection.InspectionNotes, i.Inspection.InspectedByEmployeeId, i.Inspection.CreatedAt, i.Inspection.Brand.Name, i.Inspection.Category.Name), i.Inspection.Customer.Account.FullName);
    private static Result<T> Invalid<T>(string message) => Result.Failure<T>(ApplicationError.Validation("BUYOUT.INVALID", message));
    private static Result<T> Conflict<T>(string message) => Result.Failure<T>(ApplicationError.Conflict("BUYOUT.CONFLICT", message));
    private static Result<T> Missing<T>() => Result.Failure<T>(ApplicationError.NotFound("BUYOUT.NOT_FOUND", "Không tìm thấy phiếu thu mua hoặc bản kiểm định."));
    private static Result<T> Fail<T>(string code, string message) => Result.Failure<T>(ApplicationError.Forbidden(code, message));
}

