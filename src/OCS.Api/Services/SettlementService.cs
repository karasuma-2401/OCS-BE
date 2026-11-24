using System.Data;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Settlements;

namespace OCS.Api.Services;

public sealed class SettlementService(AppDbContext db, TimeProvider clock, PrivateProductImageStore imageStore)
{
    private static readonly TimeSpan CustomerConfirmationWindow = TimeSpan.FromHours(48);

    public async Task<Result<IReadOnlyList<SettlementContractDto>>> ContractsAsync(CancellationToken ct)
    {
        var contracts = await db.ConsignmentContracts.AsNoTracking()
            .Where(x => x.Products.Any())
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new SettlementContractDto(x.Id, x.ContractNumber, x.CustomerNameSnapshot,
                x.CustomerPhoneSnapshot, x.BankCodeSnapshot, x.BankAccountNumberSnapshot,
                x.BankAccountHolderSnapshot, x.Products.Count,
                x.Products.Count(p => p.Status == ProductStatus.Sold), x.Status.ToString()))
            .ToArrayAsync(ct);
        return Result.Success<IReadOnlyList<SettlementContractDto>>(contracts);
    }

    public async Task<Result<SettlementPreviewDto>> PreviewAsync(Guid contractId, CancellationToken ct)
    {
        var contract = await db.ConsignmentContracts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == contractId, ct);
        if (contract is null) return Missing<SettlementPreviewDto>();
        var blockReason = await GetBlockReasonAsync(contractId, ct);
        var lines = blockReason is null ? await EligibleLinesAsync(contractId, ct) : [];
        return Result.Success(new SettlementPreviewDto(contract.Id, contract.ContractNumber,
            contract.CustomerNameSnapshot, lines, lines.Sum(x => x.Amount), blockReason));
    }

    public async Task<Result<IReadOnlyList<PayoutDto>>> ListPayoutsAsync(CancellationToken ct)
    {
        await CompleteExpiredAsync(ct);
        var payouts = await Payouts().AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        return Result.Success<IReadOnlyList<PayoutDto>>(payouts.Select(Map).ToArray());
    }

    public async Task<Result<PayoutDto>> PayoutDetailAsync(Guid id, CancellationToken ct)
    {
        await CompleteExpiredAsync(ct);
        var payout = await Payouts().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return payout is null ? Missing<PayoutDto>() : Result.Success(Map(payout));
    }

    public Task<Result<ProtectedPayoutProof>> PayoutProofAsync(Guid id, CancellationToken ct) => ReadProofAsync(id, null, ct);

    public Task<Result<ProtectedPayoutProof>> CustomerPayoutProofAsync(Guid id, Guid customerId, CancellationToken ct) => ReadProofAsync(id, customerId, ct);

    public async Task<Result<PayoutDto>> CreatePayoutAsync(Guid contractId, Guid actor, CancellationToken ct)
    {
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var contract = await db.ConsignmentContracts.SingleOrDefaultAsync(x => x.Id == contractId, ct);
        if (contract is null) return Missing<PayoutDto>();
        var blockReason = await GetBlockReasonAsync(contractId, ct);
        if (blockReason is not null) return Conflict<PayoutDto>(blockReason);
        var lines = await EligibleLinesAsync(contractId, ct);
        if (lines.Count == 0) return Conflict<PayoutDto>("Chưa có khoản tiền đủ điều kiện để quyết toán.");
        var total = decimal.Round(lines.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
        if (total <= 0) return Conflict<PayoutDto>("Tổng số tiền chi trả phải lớn hơn 0.");

        var now = clock.GetUtcNow();
        var payout = new Payout
        {
            PayoutNumber = $"PO-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..28],
            ConsignmentContractId = contractId,
            CreatedByEmployeeId = actor,
            TotalAmount = total,
            Status = PayoutStatus.Pending,
            CreatedAt = now,
            Details = lines.Select(line => new PayoutDetail
            {
                Type = Enum.Parse<PayoutLineType>(line.Type),
                Description = line.Description,
                Amount = line.Amount,
                ProductId = line.ProductId,
                IncidentReportId = line.IncidentReportId
            }).ToList()
        };
        db.Payouts.Add(payout);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        var result = await PayoutDetailAsync(payout.Id, ct);
        return result;
    }

    public async Task<Result<PayoutDto>> ApproveAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var payout = await Payouts().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (payout is null) return Missing<PayoutDto>();
        if (payout.Status != PayoutStatus.Pending) return Conflict<PayoutDto>("Lệnh chi không còn chờ duyệt.");
        payout.Status = PayoutStatus.Approved;
        payout.ApprovedByEmployeeId = actor;
        payout.ApprovedAt = clock.GetUtcNow();
        payout.UpdatedAt = payout.ApprovedAt;
        await db.SaveChangesAsync(ct);
        return Result.Success(Map(payout));
    }

    public async Task<Result<PayoutDto>> RecordTransferAsync(Guid id, Guid actor, RecordPayoutTransferRequest request, CancellationToken ct)
    {
        var proof = NormalizeProof(request.ProofUrl);
        if (proof is null || string.IsNullOrWhiteSpace(request.BankTransactionReference) || request.BankTransactionReference.Trim().Length > 100)
            return Invalid<PayoutDto>("SETTLEMENT.TRANSFER", "Cần ảnh chứng từ hợp lệ và mã giao dịch chuyển khoản.");
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var payout = await Payouts().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (payout is null) return Missing<PayoutDto>();
        if (payout.Status != PayoutStatus.Approved) return Conflict<PayoutDto>("Chỉ lệnh đã duyệt mới được ghi nhận chuyển khoản.");
        var now = clock.GetUtcNow();
        payout.Status = PayoutStatus.Transferred;
        payout.ProofUrl = proof;
        payout.BankTransactionReference = request.BankTransactionReference.Trim();
        payout.TransferredAt = now;
        payout.UpdatedAt = now;
        db.FinancialTransactions.Add(new FinancialTransaction
        {
            CustomerId = payout.ConsignmentContract.CustomerId,
            PayoutId = payout.Id,
            Type = FinancialTransactionType.Expense,
            Amount = payout.TotalAmount,
            PaymentMethod = PaymentMethod.BankTransfer,
            BankTransactionReference = payout.BankTransactionReference,
            ProofUrl = proof,
            Status = FinancialTransactionStatus.Success,
            OccurredAt = now,
            PerformedByEmployeeId = actor
        });
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return Result.Success(Map(payout));
    }

    public async Task<Result<IReadOnlyList<PayoutDto>>> CustomerPayoutsAsync(Guid customerId, CancellationToken ct)
    {
        await CompleteExpiredAsync(ct);
        var rows = await Payouts().AsNoTracking().Where(x => x.ConsignmentContract.CustomerId == customerId &&
                (x.Status == PayoutStatus.Transferred || x.Status == PayoutStatus.Completed))
            .OrderByDescending(x => x.CreatedAt).ToArrayAsync(ct);
        return Result.Success<IReadOnlyList<PayoutDto>>(rows.Select(Map).ToArray());
    }

    public async Task<Result<PayoutDto>> CustomerPayoutDetailAsync(Guid id, Guid customerId, CancellationToken ct)
    {
        await CompleteExpiredAsync(ct);
        var payout = await Payouts().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id &&
            x.ConsignmentContract.CustomerId == customerId && (x.Status == PayoutStatus.Transferred || x.Status == PayoutStatus.Completed), ct);
        return payout is null ? Missing<PayoutDto>() : Result.Success(Map(payout));
    }

    public async Task<Result<PayoutDto>> ConfirmReceivedAsync(Guid id, Guid customerId, CancellationToken ct)
    {
        var payout = await Payouts().SingleOrDefaultAsync(x => x.Id == id && x.ConsignmentContract.CustomerId == customerId, ct);
        if (payout is null || payout.Status is not PayoutStatus.Transferred and not PayoutStatus.Completed)
            return Missing<PayoutDto>();
        if (payout.Status == PayoutStatus.Completed) return Result.Success(Map(payout));
        Complete(payout, clock.GetUtcNow(), customerConfirmed: true);
        await CompleteLinkedIncidentsAndContractsAsync([payout], ct);
        await db.SaveChangesAsync(ct);
        return Result.Success(Map(payout));
    }

    public async Task<int> CompleteExpiredAsync(CancellationToken ct)
    {
        var deadline = clock.GetUtcNow() - CustomerConfirmationWindow;
        var payouts = await Payouts().Where(x => x.Status == PayoutStatus.Transferred && x.TransferredAt <= deadline).ToListAsync(ct);
        if (payouts.Count == 0) return 0;
        var now = clock.GetUtcNow();
        foreach (var payout in payouts) Complete(payout, now, customerConfirmed: false);
        await CompleteLinkedIncidentsAndContractsAsync(payouts, ct);
        await db.SaveChangesAsync(ct);
        return payouts.Count;
    }

    private IQueryable<Payout> Payouts() => db.Payouts
        .Include(x => x.ConsignmentContract)
        .Include(x => x.Details);

    private async Task<Result<ProtectedPayoutProof>> ReadProofAsync(Guid id, Guid? customerId, CancellationToken ct)
    {
        var payout = await db.Payouts.AsNoTracking().Include(x => x.ConsignmentContract)
            .SingleOrDefaultAsync(x => x.Id == id &&
                (customerId == null || x.ConsignmentContract.CustomerId == customerId) &&
                (x.Status == PayoutStatus.Transferred || x.Status == PayoutStatus.Completed), ct);
        if (payout is null || string.IsNullOrWhiteSpace(payout.ProofUrl)) return Missing<ProtectedPayoutProof>();
        var key = imageStore.GetKey(payout.ProofUrl);
        if (key is null) return Missing<ProtectedPayoutProof>();
        var content = await imageStore.ReadAsync(key, ct);
        return content is null ? Missing<ProtectedPayoutProof>() : Result.Success(new ProtectedPayoutProof(content.Value.Content, content.Value.ContentType));
    }

    private async Task<string?> GetBlockReasonAsync(Guid contractId, CancellationToken ct)
    {
        if (await db.IncidentReports.AnyAsync(x => x.Product.ConsignmentContractId == contractId &&
                (x.Status == IncidentStatus.Pending || x.Status == IncidentStatus.NeedsRecheck), ct))
            return "Còn biên bản sự cố đang chờ xử lý hoặc bổ sung.";
        if (await db.SaleInvoiceLines.AnyAsync(x => x.Product.ConsignmentContractId == contractId &&
                x.Product.Status == ProductStatus.Sold && x.SaleInvoice.PaymentStatus != InvoicePaymentStatus.Paid, ct))
            return "Còn hóa đơn bán hàng chưa thanh toán đầy đủ.";
        if (await db.Payouts.AnyAsync(x => x.ConsignmentContractId == contractId &&
                x.Status != PayoutStatus.Completed, ct))
            return "Đợt quyết toán trước chưa hoàn tất xác nhận nhận tiền.";
        return null;
    }

    private async Task<IReadOnlyList<SettlementLineDto>> EligibleLinesAsync(Guid contractId, CancellationToken ct)
    {
        var alreadyPaid = await db.PayoutDetails.Where(x => x.Payout.ConsignmentContractId == contractId && x.ProductId != null)
            .Select(x => new { x.ProductId, x.Type }).ToArrayAsync(ct);
        var sold = await db.SaleInvoiceLines.AsNoTracking()
            .Where(x => x.Product.ConsignmentContractId == contractId && x.Product.Status == ProductStatus.Sold &&
                x.SaleInvoice.PaymentStatus == InvoicePaymentStatus.Paid)
            .Select(x => new { x.ProductId, x.Product.Name, x.Product.Barcode, x.Product.AgreedCommissionRate, x.SalePrice })
            .ToArrayAsync(ct);
        var lines = new List<SettlementLineDto>();
        foreach (var sale in sold)
        {
            if (!alreadyPaid.Any(x => x.ProductId == sale.ProductId && x.Type == PayoutLineType.SaleProceeds))
                lines.Add(new SettlementLineDto(nameof(PayoutLineType.SaleProceeds), $"Tiền bán {sale.Name} ({sale.Barcode})", sale.SalePrice, sale.ProductId, null));
            if (!alreadyPaid.Any(x => x.ProductId == sale.ProductId && x.Type == PayoutLineType.Commission))
            {
                var commission = decimal.Round(sale.SalePrice * (sale.AgreedCommissionRate ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero);
                if (commission > 0)
                    lines.Add(new SettlementLineDto(nameof(PayoutLineType.Commission), $"Hoa hồng {sale.Name} ({sale.Barcode})", -commission, sale.ProductId, null));
            }
        }
        var paidIncidentIds = await db.PayoutDetails.Where(x => x.IncidentReportId != null && x.Payout.ConsignmentContractId == contractId)
            .Select(x => x.IncidentReportId!.Value).ToArrayAsync(ct);
        var incidents = await db.IncidentReports.AsNoTracking().Include(x => x.Product)
            .Where(x => x.Product.ConsignmentContractId == contractId && x.Status == IncidentStatus.Approved &&
                !paidIncidentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Product.Name, x.Product.Barcode, x.CompensationAmount })
            .ToArrayAsync(ct);
        lines.AddRange(incidents.Select(x => new SettlementLineDto(nameof(PayoutLineType.Compensation),
            $"Bồi thường {x.Name} ({x.Barcode})", x.CompensationAmount, null, x.Id)));
        return lines;
    }

    private static void Complete(Payout payout, DateTimeOffset now, bool customerConfirmed)
    {
        payout.Status = PayoutStatus.Completed;
        payout.CompletedAt = now;
        if (customerConfirmed) payout.CustomerConfirmedAt = now;
        payout.UpdatedAt = now;
    }

    private async Task CompleteLinkedIncidentsAndContractsAsync(IReadOnlyList<Payout> payouts, CancellationToken ct)
    {
        var incidentIds = payouts.SelectMany(x => x.Details).Where(x => x.IncidentReportId.HasValue)
            .Select(x => x.IncidentReportId!.Value).Distinct().ToArray();
        if (incidentIds.Length > 0)
        {
            var incidents = await db.IncidentReports.Where(x => incidentIds.Contains(x.Id)).ToListAsync(ct);
            foreach (var incident in incidents.Where(x => x.Status == IncidentStatus.Approved))
            {
                incident.Status = IncidentStatus.Compensated;
                incident.UpdatedAt = clock.GetUtcNow();
            }
        }
        var now = clock.GetUtcNow();
        var contractIds = payouts.Select(x => x.ConsignmentContractId).Distinct().ToArray();
        var contracts = await db.ConsignmentContracts.Where(x => contractIds.Contains(x.Id) && x.EndsAt <= now).ToListAsync(ct);
        foreach (var contract in contracts)
        {
            var hasActiveProduct = await db.Products.AnyAsync(x => x.ConsignmentContractId == contract.Id &&
                (x.Status == ProductStatus.OnSale || x.Status == ProductStatus.PendingProcessing || x.Status == ProductStatus.IncidentPending), ct);
            if (!hasActiveProduct) contract.Status = ConsignmentContractStatus.Completed;
        }
    }

    private static string? NormalizeProof(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048) return null;
        var path = Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.AbsolutePath : value;
        const string prefix = "/api/storage/inspections/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var key = path[prefix.Length..];
        return key.Length > 0 && !key.Contains('\\') && !key.Split('/').Any(x => x is "" or "." or "..") &&
            !value.Contains('?') && !value.Contains('#') ? path : null;
    }

    private static PayoutDto Map(Payout payout)
    {
        var contract = payout.ConsignmentContract;
        return new PayoutDto(payout.Id, payout.PayoutNumber, contract.Id, contract.ContractNumber,
            contract.CustomerNameSnapshot, contract.CustomerPhoneSnapshot, contract.BankCodeSnapshot,
            contract.BankAccountNumberSnapshot, contract.BankAccountHolderSnapshot, payout.TotalAmount,
            payout.Status.ToString(), payout.ProofUrl, payout.BankTransactionReference, payout.CreatedAt,
            payout.ApprovedAt, payout.TransferredAt, payout.CustomerConfirmedAt, payout.CompletedAt,
            payout.Details.OrderBy(x => x.CreatedAt).Select(x => new SettlementLineDto(x.Type.ToString(), x.Description,
                x.Amount, x.ProductId, x.IncidentReportId)).ToArray());
    }

    private static Result<T> Invalid<T>(string code, string message) => Result.Failure<T>(ApplicationError.Validation(code, message));
    private static Result<T> Missing<T>() => Result.Failure<T>(ApplicationError.NotFound("SETTLEMENT.NOT_FOUND", "Không tìm thấy dữ liệu quyết toán."));
    private static Result<T> Conflict<T>(string message) => Result.Failure<T>(ApplicationError.Conflict("SETTLEMENT.STATE", message));
}

public sealed record ProtectedPayoutProof(byte[] Content, string ContentType);
