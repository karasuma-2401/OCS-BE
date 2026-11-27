using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Products;

namespace OCS.Api.Services;

public sealed class EarlyWithdrawalService(AppDbContext db, TimeProvider clock)
{
    private IQueryable<EarlyWithdrawal> Details() => db.EarlyWithdrawals
        .Include(x => x.Customer).ThenInclude(x => x.Account)
        .Include(x => x.Items).ThenInclude(x => x.Product).ThenInclude(x => x.ConsignmentContract);
    private Task<bool> CustomerActive(Guid id, CancellationToken ct) => db.CustomerProfiles.AnyAsync(x =>
        x.Id == id && x.Status == CustomerStatus.Active && x.Account.Status == AccountStatus.Active && x.Account.AccountType == AccountType.Customer, ct);
    private Task<bool> EmployeeActive(Guid id, CancellationToken ct) => db.EmployeeProfiles.AnyAsync(x =>
        x.Id == id && x.Status == EmployeeStatus.Working && x.Account.Status == AccountStatus.Active && x.Account.AccountType == AccountType.Internal, ct);

    public async Task<Result<IReadOnlyList<WithdrawalEligibleProductDto>>> EligibleAsync(Guid customer, CancellationToken ct)
    {
        if (!await CustomerActive(customer, ct))
            return Result.Failure<IReadOnlyList<WithdrawalEligibleProductDto>>(ApplicationError.Forbidden("WITHDRAWAL.CUSTOMER", "Hồ sơ khách hàng không hoạt động."));
        var now = clock.GetUtcNow();
        var products = await db.Products.AsNoTracking().Include(x => x.ConsignmentContract)
            .Where(x => x.SourceType == ProductSourceType.Consignment && x.Status == ProductStatus.OnSale && !x.IsReservedForWithdrawal &&
                x.ConsignmentContract != null && x.ConsignmentContract.CustomerId == customer &&
                x.ConsignmentContract.Status == ConsignmentContractStatus.Active && x.ConsignmentContract.SignedAt <= now && x.ConsignmentContract.EndsAt > now &&
                x.AgreedCommissionRate >= 0 && x.AgreedCommissionRate <= 100 &&
                !db.EarlyWithdrawalItems.Any(i => i.ProductId == x.Id && (i.EarlyWithdrawal.Status == "Pending" || i.EarlyWithdrawal.Status == "Approved")))
            .OrderBy(x => x.ConsignmentContract!.ContractNumber).ThenBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
        return Result.Success<IReadOnlyList<WithdrawalEligibleProductDto>>(products.Select(x =>
        {
            var rate = x.ConsignmentContract?.EarlyWithdrawalFeeRate ?? x.AgreedCommissionRate!.Value;
            return new WithdrawalEligibleProductDto(x.Id, x.Name, x.Barcode, x.ConsignmentContractId!.Value,
                x.ConsignmentContract!.ContractNumber, x.ConsignmentContract.EndsAt, x.ListedPrice,
                rate, x.CalculateEarlyWithdrawalFee(rate));
        }).ToArray());
    }

    public async Task<Result<IReadOnlyList<EarlyWithdrawalDto>>> ListAsync(Guid? customer, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || page > 100000 || pageSize is < 1 or > 100)
            return Result.Failure<IReadOnlyList<EarlyWithdrawalDto>>(ApplicationError.Validation("WITHDRAWAL.PAGE", "Phân trang không hợp lệ."));
        if (customer.HasValue && !await CustomerActive(customer.Value, ct))
            return Result.Failure<IReadOnlyList<EarlyWithdrawalDto>>(ApplicationError.Forbidden("WITHDRAWAL.CUSTOMER", "Hồ sơ khách hàng không hoạt động."));
        var rows = await Details().AsNoTracking().Where(x => !customer.HasValue || x.CustomerId == customer)
            .OrderByDescending(x => x.RequestedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Result.Success<IReadOnlyList<EarlyWithdrawalDto>>(rows.Select(Map).ToArray());
    }

    public async Task<Result<EarlyWithdrawalDto>> DetailAsync(Guid id, Guid? customer, CancellationToken ct)
    {
        if (customer.HasValue && !await CustomerActive(customer.Value, ct)) return Forbidden();
        var row = await Details().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && (!customer.HasValue || x.CustomerId == customer), ct);
        return row is null ? Missing() : Result.Success(Map(row));
    }

    public async Task<Result<WithdrawalSummaryDto>> SummaryAsync(Guid? customer, CancellationToken ct)
    {
        if (customer.HasValue && !await CustomerActive(customer.Value, ct))
            return Result.Failure<WithdrawalSummaryDto>(ApplicationError.Forbidden("WITHDRAWAL.CUSTOMER", "Hồ sơ khách hàng không hoạt động."));
        var counts = await db.EarlyWithdrawals.AsNoTracking().Where(x => !customer.HasValue || x.CustomerId == customer)
            .GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        return Result.Success(new WithdrawalSummaryDto(counts.Values.Sum(), counts.GetValueOrDefault("Pending"),
            counts.GetValueOrDefault("Approved"), counts.GetValueOrDefault("Completed"), counts.GetValueOrDefault("Rejected")));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1304", Justification = "EF Core translates ToLower to database LOWER.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1311", Justification = "EF Core translates ToLower to database LOWER.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1862", Justification = "StringComparison cannot be translated by EF Core PostgreSQL.")]
    public async Task<Result<PagedResponse<EarlyWithdrawalDto>>> SearchAsync(Guid? customer, WithdrawalQuery filter, CancellationToken ct)
    {
        if (customer.HasValue && !await CustomerActive(customer.Value, ct))
            return Result.Failure<PagedResponse<EarlyWithdrawalDto>>(ApplicationError.Forbidden("WITHDRAWAL.CUSTOMER", "Hồ sơ khách hàng không hoạt động."));
        if (filter.Page is < 1 or > 100000 || filter.PageSize is < 1 or > 100 || filter.Q?.Length > 200 ||
            filter.Status is not (null or "" or "Pending" or "Approved" or "Completed" or "Rejected" or "Cancelled" or "Legacy") ||
            filter.PaymentStatus is not (null or "" or "Unpaid" or "Partial" or "Paid") || filter.Sort is not (null or "" or "newest" or "oldest"))
            return Result.Failure<PagedResponse<EarlyWithdrawalDto>>(ApplicationError.Validation("WITHDRAWAL.FILTER", "Bộ lọc hoặc phân trang không hợp lệ."));
        var query = Details().AsNoTracking().Where(x => !customer.HasValue || x.CustomerId == customer);
        if (!string.IsNullOrEmpty(filter.Status)) query = query.Where(x => x.Status == filter.Status);
        if (!string.IsNullOrEmpty(filter.PaymentStatus))
        {
            var payment = Enum.Parse<InvoicePaymentStatus>(filter.PaymentStatus);
            query = query.Where(x => x.PaymentStatus == payment);
        }
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var term = filter.Q.Trim().ToLowerInvariant();
            query = query.Where(x => x.Id.ToString().StartsWith(term) || x.Customer.Account.FullName.ToLower().Contains(term) ||
                x.Customer.Account.PhoneNumber.Contains(term) || x.Items.Any(i => i.Product.Name.ToLower().Contains(term) ||
                    i.Product.Barcode.ToLower().Contains(term) || i.Product.ConsignmentContract != null && i.Product.ConsignmentContract.ContractNumber.ToLower().Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var ordered = filter.Sort == "oldest" ? query.OrderBy(x => x.RequestedAt).ThenBy(x => x.Id)
            : query.OrderByDescending(x => x.RequestedAt).ThenBy(x => x.Id);
        var rows = await ordered.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).AsSplitQuery().ToListAsync(ct);
        return Result.Success(new PagedResponse<EarlyWithdrawalDto>(rows.Select(Map).ToArray(), filter.Page, filter.PageSize, total));
    }

    public async Task<Result<EarlyWithdrawalDto>> CreateAsync(Guid customer, CreateEarlyWithdrawalRequest request, CancellationToken ct)
    {
        if (!await CustomerActive(customer, ct)) return Forbidden();
        if (request.ProductIds is not { Length: > 0 and <= 100 } || request.ProductIds.Contains(Guid.Empty) ||
            request.ProductIds.Distinct().Count() != request.ProductIds.Length || !Text(request.Reason)) return Invalid();
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var products = await db.Products.Include(x => x.ConsignmentContract)
            .Where(x => request.ProductIds.Contains(x.Id) && x.ConsignmentContract != null && x.ConsignmentContract.CustomerId == customer).ToListAsync(ct);
        if (products.Count != request.ProductIds.Length) return Missing();
        var now = clock.GetUtcNow();
        var unavailable = products.FirstOrDefault(x => !Available(x, now));
        if (unavailable is not null) return Conflict($"{unavailable.Name}: {unavailable.GetEarlyWithdrawalBlockReason(now)}");
        if (await db.EarlyWithdrawalItems.AnyAsync(x => request.ProductIds.Contains(x.ProductId) &&
            (x.EarlyWithdrawal.Status == "Pending" || x.EarlyWithdrawal.Status == "Approved"), ct))
            return Conflict("Sản phẩm đã có yêu cầu rút sớm đang xử lý.");
        var row = new EarlyWithdrawal { CustomerId = customer, Reason = request.Reason.Trim(), RequestedAt = now };
        foreach (var p in products)
        {
            var rate = p.ConsignmentContract?.EarlyWithdrawalFeeRate ?? p.AgreedCommissionRate!.Value;
            row.Items.Add(new EarlyWithdrawalItem
            {
                ProductId = p.Id,
                Product = p,
                ListedPrice = p.ListedPrice,
                PenaltyRate = rate,
                PenaltyAmount = p.CalculateEarlyWithdrawalFee(rate)
            });
            // Serialize competing requests and sales through the existing inventory concurrency token.
            p.InventoryVersion = Guid.NewGuid();
        }
        row.TotalPenaltyAmount = row.Items.Sum(x => x.PenaltyAmount);
        if (row.TotalPenaltyAmount > 9999999999999999.99m) return Invalid();
        db.EarlyWithdrawals.Add(row);
        if (!await PersistAsync(tx, ct)) return Conflict("Dữ liệu vừa thay đổi. Hãy tải lại trước khi thử lại.");
        return Result.Success(Map(row));
    }

    public Task<Result<EarlyWithdrawalDto>> ApproveAsync(Guid id, Guid actor, CancellationToken ct) =>
        ChangeAsync(id, actor, "approve", null, null, null, ct);
    public Task<Result<EarlyWithdrawalDto>> RejectAsync(Guid id, Guid actor, WithdrawalDecisionRequest request, CancellationToken ct) =>
        ChangeAsync(id, actor, "reject", request.Note, null, null, ct);
    public Task<Result<EarlyWithdrawalDto>> CancelAsync(Guid id, Guid customer, CancellationToken ct) =>
        ChangeAsync(id, customer, "cancel", null, null, customer, ct);
    public Task<Result<EarlyWithdrawalDto>> PayAsync(Guid id, Guid actor, WithdrawalPaymentRequest request, CancellationToken ct) =>
        ChangeAsync(id, actor, "pay", null, request, null, ct);
    public Task<Result<EarlyWithdrawalDto>> HandoverAsync(Guid id, Guid actor, WithdrawalHandoverRequest request, CancellationToken ct) =>
        ChangeAsync(id, actor, "handover", request.Evidence, null, null, ct);

    private async Task<Result<EarlyWithdrawalDto>> ChangeAsync(Guid id, Guid actor, string action, string? note,
        WithdrawalPaymentRequest? payment, Guid? customer, CancellationToken ct)
    {
        if (customer.HasValue ? !await CustomerActive(actor, ct) : !await EmployeeActive(actor, ct)) return Forbidden();
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var row = await Details().Include(x => x.Items).ThenInclude(x => x.Product).ThenInclude(x => x.ConsignmentContract)
            .SingleOrDefaultAsync(x => x.Id == id && (!customer.HasValue || x.CustomerId == customer), ct);
        if (row is null) return Missing();
        var now = clock.GetUtcNow();
        switch (action)
        {
            case "approve":
                if (row.Status != "Pending" || row.Items.Count == 0 || row.Items.Any(x => !Available(x.Product, now)))
                    return Conflict("Yêu cầu hoặc sản phẩm không còn đủ điều kiện duyệt.");
                // Expired unpaid invoices may retain their lines; late payment reconciliation
                // checks the product reservation before completing a sale.
                var ids = row.Items.Select(x => x.ProductId).ToArray();
                if (await db.SaleInvoiceLines.AnyAsync(x => ids.Contains(x.ProductId) &&
                    (x.SaleInvoice.PaymentStatus != InvoicePaymentStatus.Unpaid ||
                     db.FinancialTransactions.Any(t => t.SaleInvoiceId == x.SaleInvoiceId &&
                         (t.Status == FinancialTransactionStatus.Success || t.Status == FinancialTransactionStatus.Pending &&
                             (!t.ExpiresAt.HasValue || t.ExpiresAt > now)))), ct))
                    return Conflict("Sản phẩm đang có hóa đơn đã thu tiền hoặc phiên thanh toán chưa hết hạn.");
                row.Status = "Approved";
                row.ApprovedAt = now;
                row.ProcessedByEmployeeId = actor;
                foreach (var item in row.Items) item.Product.IsReservedForWithdrawal = true;
                if (row.TotalPenaltyAmount == 0) row.PaymentStatus = InvoicePaymentStatus.Paid;
                break;
            case "reject":
                if (!Text(note)) return Invalid();
                if (row.Status is not ("Pending" or "Approved") || row.PaidAt.HasValue) return Conflict("Không thể từ chối phiếu đã thu phí hoặc đã kết thúc.");
                if (await HasActiveQrAsync(row.Id, now, ct)) return Conflict("Phiên VietQR vẫn còn hiệu lực. Kiểm tra thanh toán hoặc chờ mã hết hạn trước khi từ chối.");
                row.Status = "Rejected";
                row.DecisionNote = note!.Trim();
                row.ProcessedByEmployeeId = actor;
                foreach (var item in row.Items) item.Product.IsReservedForWithdrawal = false;
                break;
            case "cancel":
                if (row.Status != "Pending") return Conflict("Chỉ được hủy yêu cầu đang chờ duyệt.");
                row.Status = "Cancelled";
                break;
            case "pay":
                if (row.Status != "Approved" || row.PaymentStatus == InvoicePaymentStatus.Paid || row.TotalPenaltyAmount <= 0)
                    return Conflict("Phiếu phải được duyệt và chưa thu phí.");
                if (await HasActiveQrAsync(row.Id, now, ct)) return Conflict("Phiên VietQR vẫn còn hiệu lực. Kiểm tra thanh toán hoặc chờ mã hết hạn trước khi thu bằng phương thức khác.");
                if (payment is null || payment.Amount != row.TotalPenaltyAmount ||
                    payment.PaymentMethod is not ("Cash" or "BankTransfer") || payment.Reference?.Length > 64 ||
                    payment.PaymentMethod == "BankTransfer" && string.IsNullOrWhiteSpace(payment.Reference)) return Invalid();
                db.FinancialTransactions.Add(new FinancialTransaction
                {
                    CustomerId = row.CustomerId,
                    EarlyWithdrawalId = row.Id,
                    Type = FinancialTransactionType.Income,
                    Amount = row.TotalPenaltyAmount,
                    PaymentMethod = Enum.Parse<PaymentMethod>(payment.PaymentMethod),
                    PaymentReference = payment.Reference?.Trim(),
                    Status = FinancialTransactionStatus.Success,
                    OccurredAt = now,
                    PerformedByEmployeeId = actor
                });
                row.PaymentStatus = InvoicePaymentStatus.Paid;
                row.PaidAt = now;
                row.PaidByEmployeeId = actor;
                break;
            case "handover":
                if (!Text(note)) return Invalid();
                if (row.Status != "Approved" || row.PaymentStatus != InvoicePaymentStatus.Paid || row.Items.Count == 0 ||
                    row.Items.Any(x => x.Product.Status != ProductStatus.OnSale || !x.Product.IsReservedForWithdrawal))
                    return Conflict("Cần duyệt, thu đủ phí và kiểm tra hàng trước khi bàn giao.");
                row.Status = "Completed";
                row.HandedOverAt = now;
                row.HandedOverByEmployeeId = actor;
                row.HandoverEvidence = note!.Trim();
                foreach (var item in row.Items)
                {
                    item.Product.Status = ProductStatus.EarlyWithdrawn;
                    item.Product.IsReservedForWithdrawal = false;
                    item.Product.StorageLocationId = null;
                    db.ProductStatusHistories.Add(new ProductStatusHistory
                    {
                        ProductId = item.ProductId,
                        PreviousStatus = ProductStatus.OnSale,
                        NewStatus = ProductStatus.EarlyWithdrawn,
                        ChangedByAccountId = actor,
                        OccurredAt = now,
                        Reason = $"Rút sớm {row.Id}: {row.HandoverEvidence}"
                    });
                }
                break;
            default: return Invalid();
        }
        row.Version = Guid.NewGuid();
        foreach (var item in row.Items) item.Product.InventoryVersion = Guid.NewGuid();
        if (!await PersistAsync(tx, ct)) return Conflict("Dữ liệu vừa thay đổi. Hãy tải lại trước khi thử lại.");
        return Result.Success(Map(row));
    }

    private async Task<bool> PersistAsync(IDbContextTransaction? tx, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return false;
        }
        catch (Exception ex) when (ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } ||
            ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } })
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private static bool Available(Product p, DateTimeOffset now) => p.CanRequestEarlyWithdrawal(now);
    private Task<bool> HasActiveQrAsync(Guid id, DateTimeOffset now, CancellationToken ct) => db.FinancialTransactions.AnyAsync(
        t => t.EarlyWithdrawalId == id && t.PaymentMethod == PaymentMethod.VietQr && t.Status == FinancialTransactionStatus.Pending &&
            (!t.ExpiresAt.HasValue || t.ExpiresAt > now), ct);
    private static bool Text(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 1000;
    private static Result<EarlyWithdrawalDto> Missing() => Result.Failure<EarlyWithdrawalDto>(ApplicationError.NotFound("WITHDRAWAL.NOT_FOUND", "Không tìm thấy yêu cầu hoặc sản phẩm."));
    private static Result<EarlyWithdrawalDto> Invalid() => Result.Failure<EarlyWithdrawalDto>(ApplicationError.Validation("WITHDRAWAL.INVALID", "Thông tin yêu cầu không hợp lệ."));
    private static Result<EarlyWithdrawalDto> Forbidden() => Result.Failure<EarlyWithdrawalDto>(ApplicationError.Forbidden("WITHDRAWAL.ACTOR", "Tài khoản không hoạt động hoặc sai loại hồ sơ."));
    private static Result<EarlyWithdrawalDto> Conflict(string message) => Result.Failure<EarlyWithdrawalDto>(ApplicationError.Conflict("WITHDRAWAL.CONFLICT", message));
    private static EarlyWithdrawalDto Map(EarlyWithdrawal row) => new(row.Id, row.CustomerId, row.Status, row.Reason,
        row.TotalPenaltyAmount, row.PaymentStatus.ToString(), row.RequestedAt, row.ProcessedByEmployeeId, row.ApprovedAt, row.DecisionNote,
        row.PaidAt, row.PaidByEmployeeId, row.HandedOverAt, row.HandedOverByEmployeeId, row.HandoverEvidence,
        row.Items.OrderBy(x => x.ProductId).Select(x => new EarlyWithdrawalItemDto(x.ProductId, x.Product.Name, x.Product.ConsignmentContractId, x.ListedPrice, x.PenaltyRate, x.PenaltyAmount, x.Product.ConsignmentContract?.ContractNumber)).ToArray(),
        row.Customer?.Account?.FullName, row.Customer?.Account?.PhoneNumber);
}
