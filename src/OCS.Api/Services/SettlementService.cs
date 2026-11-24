using System.Data;
using System.Globalization;
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
        var now = clock.GetUtcNow();
        var contracts = await db.ConsignmentContracts.AsNoTracking()
            .Include(x => x.Customer)
            .Where(x => x.Products.Any() || x.Payouts.Any())
            .OrderByDescending(x => x.CreatedAt)
            .ToArrayAsync(ct);
        if (contracts.Length == 0)
            return Result.Success<IReadOnlyList<SettlementContractDto>>([]);

        var contractIds = contracts.Select(x => x.Id).ToArray();
        var productStats = (await db.Products.AsNoTracking()
            .Where(x => x.ConsignmentContractId != null && contractIds.Contains(x.ConsignmentContractId.Value))
            .Select(x => new { ContractId = x.ConsignmentContractId!.Value, x.Status })
            .ToArrayAsync(ct))
            .GroupBy(x => x.ContractId)
            .ToDictionary(g => g.Key, g => new
            {
                Total = g.Count(),
                Sold = g.Count(p => p.Status == ProductStatus.Sold),
                Compensated = g.Count(p => p.Status == ProductStatus.Compensated),
                Active = g.Count(p => p.Status is ProductStatus.OnSale or ProductStatus.PendingProcessing or ProductStatus.IncidentPending)
            });

        var relevantPayouts = await db.Payouts.AsNoTracking()
            .Where(x => contractIds.Contains(x.ConsignmentContractId) &&
                (x.Status == PayoutStatus.Pending || x.Status == PayoutStatus.Approved ||
                 x.Status == PayoutStatus.Transferred || x.Status == PayoutStatus.Completed))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.ConsignmentContractId, x.PayoutNumber, x.Status, x.TotalAmount })
            .ToArrayAsync(ct);

        var openPayouts = relevantPayouts
            .Where(x => x.Status is PayoutStatus.Pending or PayoutStatus.Approved or PayoutStatus.Transferred)
            .GroupBy(x => x.ConsignmentContractId)
            .ToDictionary(g => g.Key, g => g.First());

        var completedPayoutStats = relevantPayouts
            .Where(x => x.Status == PayoutStatus.Completed)
            .GroupBy(x => x.ConsignmentContractId)
            .ToDictionary(g => g.Key, g => new { Count = g.Count(), Amount = g.Sum(x => x.TotalAmount) });

        var paidDetails = await db.PayoutDetails.AsNoTracking()
            .Where(x => contractIds.Contains(x.Payout.ConsignmentContractId) && !x.IsReleased)
            .Select(x => new { x.Payout.ConsignmentContractId, x.ProductId, x.Type, x.IncidentReportId })
            .ToArrayAsync(ct);

        var paidProductTypes = paidDetails
            .Where(x => x.ProductId != null)
            .Select(x => (x.ConsignmentContractId, ProductId: x.ProductId!.Value, x.Type))
            .ToHashSet();
        var paidIncidentIds = paidDetails
            .Where(x => x.IncidentReportId != null)
            .Select(x => x.IncidentReportId!.Value)
            .ToHashSet();

        var soldLines = (await db.SaleInvoiceLines.AsNoTracking()
            .Where(x => x.Product.ConsignmentContractId != null &&
                contractIds.Contains(x.Product.ConsignmentContractId.Value) &&
                x.Product.Status == ProductStatus.Sold &&
                x.SaleInvoice.PaymentStatus == InvoicePaymentStatus.Paid)
            .Select(x => new
            {
                ContractId = x.Product.ConsignmentContractId!.Value,
                x.ProductId,
                x.Product.AgreedCommissionRate,
                x.SalePrice
            })
            .ToArrayAsync(ct))
            .GroupBy(x => x.ContractId)
            .ToDictionary(g => g.Key, g => g.ToArray());

        var approvedIncidents = (await db.IncidentReports.AsNoTracking()
            .Where(x => x.Product.ConsignmentContractId != null &&
                contractIds.Contains(x.Product.ConsignmentContractId.Value) &&
                x.Status == IncidentStatus.Approved)
            .Select(x => new { ContractId = x.Product.ConsignmentContractId!.Value, x.Id, x.CompensationAmount })
            .ToArrayAsync(ct))
            .GroupBy(x => x.ContractId)
            .ToDictionary(g => g.Key, g => g.ToArray());

        var pendingIncidentContracts = (await db.IncidentReports.AsNoTracking()
            .Where(x => x.Product.ConsignmentContractId != null &&
                contractIds.Contains(x.Product.ConsignmentContractId.Value) &&
                (x.Status == IncidentStatus.Pending || x.Status == IncidentStatus.NeedsRecheck))
            .Select(x => x.Product.ConsignmentContractId!.Value)
            .Distinct()
            .ToArrayAsync(ct)).ToHashSet();

        var unpaidInvoiceContracts = (await db.SaleInvoiceLines.AsNoTracking()
            .Where(x => x.Product.ConsignmentContractId != null &&
                contractIds.Contains(x.Product.ConsignmentContractId.Value) &&
                x.Product.Status == ProductStatus.Sold &&
                x.SaleInvoice.PaymentStatus != InvoicePaymentStatus.Paid)
            .Select(x => x.Product.ConsignmentContractId!.Value)
            .Distinct()
            .ToArrayAsync(ct)).ToHashSet();

        var pendingTxContracts = (await db.FinancialTransactions.AsNoTracking()
            .Where(x => x.Status == FinancialTransactionStatus.Pending && x.SaleInvoice != null)
            .SelectMany(x => x.SaleInvoice!.Lines
                .Where(l => l.Product.ConsignmentContractId != null && contractIds.Contains(l.Product.ConsignmentContractId.Value))
                .Select(l => l.Product.ConsignmentContractId!.Value))
            .Distinct()
            .ToArrayAsync(ct)).ToHashSet();

        var rows = contracts.Select(c =>
        {
            var stats = productStats.GetValueOrDefault(c.Id);
            var totalCount = stats?.Total ?? 0;
            var soldCount = stats?.Sold ?? 0;
            var compensatedCount = stats?.Compensated ?? 0;
            var activeCount = stats?.Active ?? 0;
            var otherCount = Math.Max(0, totalCount - soldCount - compensatedCount - activeCount);

            decimal salesNet = 0m;
            if (soldLines.TryGetValue(c.Id, out var cSold))
            {
                foreach (var sale in cSold)
                {
                    if (!paidProductTypes.Contains((c.Id, sale.ProductId, PayoutLineType.SaleProceeds)))
                        salesNet += sale.SalePrice;
                    if (!paidProductTypes.Contains((c.Id, sale.ProductId, PayoutLineType.Commission)))
                        salesNet -= decimal.Round(sale.SalePrice * (sale.AgreedCommissionRate ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero);
                }
            }

            var unpaidComp = approvedIncidents.TryGetValue(c.Id, out var cIncidents)
                ? cIncidents.Where(i => !paidIncidentIds.Contains(i.Id)).ToArray()
                : [];
            var compAmount = unpaidComp.Sum(i => i.CompensationAmount);
            var compCount = unpaidComp.Length;

            var openPayout = openPayouts.GetValueOrDefault(c.Id);
            var completedPayout = completedPayoutStats.GetValueOrDefault(c.Id);
            var completedCount = completedPayout?.Count ?? 0;
            var completedAmount = decimal.Round(completedPayout?.Amount ?? 0m, 2, MidpointRounding.AwayFromZero);
            var isMidTerm = c.EndsAt > now && activeCount > 0;
            var blockReason = c.Status == ConsignmentContractStatus.Cancelled
                ? "Hợp đồng đã hủy, không thể lập quyết toán."
                : pendingIncidentContracts.Contains(c.Id)
                    ? "Còn biên bản sự cố đang chờ xử lý hoặc bổ sung."
                    : unpaidInvoiceContracts.Contains(c.Id)
                        ? "Còn hóa đơn bán hàng chưa thanh toán đầy đủ."
                        : pendingTxContracts.Contains(c.Id)
                            ? "Còn giao dịch thanh toán đang chờ xử lý."
                            : openPayout is not null
                                ? "Đợt quyết toán trước chưa hoàn tất xác nhận nhận tiền."
                                : isMidTerm && compCount == 0
                                    ? "Chỉ lập quyết toán cuối đợt khi hết hạn hợp đồng hoặc toàn bộ sản phẩm đã xử lý."
                                    : null;

            var eligibleAmount = blockReason is not null
                ? 0m
                : isMidTerm
                    ? compAmount
                    : Math.Max(0m, salesNet + compAmount);

            var (eligibilityKind, eligibilityReason, eligibilityFactors) = DescribeEligibility(
                c.Status, c.EndsAt, now, totalCount, soldCount, compensatedCount, activeCount, compCount,
                eligibleAmount, completedCount, openPayout?.PayoutNumber, blockReason);

            return new SettlementContractDto(
                c.Id,
                c.ContractNumber,
                c.CustomerNameSnapshot,
                c.CustomerPhoneSnapshot,
                FirstNonEmpty(c.BankCodeSnapshot, c.Customer?.BankCode),
                FirstNonEmpty(c.BankAccountNumberSnapshot, c.Customer?.BankAccountNumber),
                FirstNonEmpty(c.BankAccountHolderSnapshot, c.Customer?.BankAccountHolderName),
                totalCount,
                soldCount,
                c.Status.ToString(),
                c.EndsAt,
                activeCount,
                compensatedCount,
                compCount,
                decimal.Round(salesNet, 2, MidpointRounding.AwayFromZero),
                decimal.Round(compAmount, 2, MidpointRounding.AwayFromZero),
                decimal.Round(eligibleAmount, 2, MidpointRounding.AwayFromZero),
                openPayout?.Id,
                openPayout?.PayoutNumber,
                openPayout?.Status.ToString(),
                openPayout?.TotalAmount,
                blockReason,
                eligibilityKind,
                eligibilityReason,
                otherCount,
                completedCount,
                completedAmount,
                eligibilityFactors);
        }).ToArray();

        return Result.Success<IReadOnlyList<SettlementContractDto>>(rows);
    }

    public async Task<Result<SettlementPreviewDto>> PreviewAsync(Guid contractId, CancellationToken ct)
    {
        var contract = await db.ConsignmentContracts.AsNoTracking()
            .Include(x => x.Customer)
            .SingleOrDefaultAsync(x => x.Id == contractId, ct);
        if (contract is null) return Missing<SettlementPreviewDto>();

        var now = clock.GetUtcNow();
        var (salesLines, compLines, isMidTerm) = await SplitEligibleLinesAsync(contractId, ct);
        var blockReason = await GetBlockReasonAsync(contractId, ct, compLines.Count > 0);
        var contractPayouts = await db.Payouts.AsNoTracking()
            .Where(x => x.ConsignmentContractId == contractId &&
                (x.Status == PayoutStatus.Pending || x.Status == PayoutStatus.Approved ||
                 x.Status == PayoutStatus.Transferred || x.Status == PayoutStatus.Completed))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.PayoutNumber, x.Status, x.TotalAmount })
            .ToArrayAsync(ct);
        var openPayout = contractPayouts.FirstOrDefault(x =>
            x.Status is PayoutStatus.Pending or PayoutStatus.Approved or PayoutStatus.Transferred);
        var completedPayouts = contractPayouts.Where(x => x.Status == PayoutStatus.Completed).ToArray();
        var completedCount = completedPayouts.Length;
        var completedAmount = decimal.Round(completedPayouts.Sum(x => x.TotalAmount), 2, MidpointRounding.AwayFromZero);

        var productStatuses = await db.Products.AsNoTracking()
            .Where(x => x.ConsignmentContractId == contractId)
            .Select(x => x.Status)
            .ToArrayAsync(ct);
        var totalCount = productStatuses.Length;
        var soldCount = productStatuses.Count(p => p == ProductStatus.Sold);
        var compensatedCount = productStatuses.Count(p => p == ProductStatus.Compensated);
        var activeCount = productStatuses.Count(p => p is ProductStatus.OnSale or ProductStatus.PendingProcessing or ProductStatus.IncidentPending);
        var otherCount = Math.Max(0, totalCount - soldCount - compensatedCount - activeCount);

        List<SettlementLineDto> lines;
        IReadOnlyList<SettlementLineDto> deferredLines;
        decimal deferredAmount;
        var isCompensationOnly = false;

        if (!isMidTerm)
        {
            lines = [.. salesLines, .. compLines];
            deferredLines = [];
            deferredAmount = 0m;
        }
        else if (compLines.Count > 0 && blockReason is null)
        {
            lines = compLines;
            deferredLines = salesLines;
            deferredAmount = decimal.Round(salesLines.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
            isCompensationOnly = true;
        }
        else
        {
            lines = [.. salesLines, .. compLines];
            deferredLines = salesLines;
            deferredAmount = decimal.Round(salesLines.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
        }

        var eligibleAmount = blockReason is not null
            ? 0m
            : isMidTerm
                ? decimal.Round(compLines.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero)
                : decimal.Round(Math.Max(0m, lines.Sum(x => x.Amount)), 2, MidpointRounding.AwayFromZero);
        var (eligibilityKind, eligibilityReason, eligibilityFactors) = DescribeEligibility(
            contract.Status, contract.EndsAt, now, totalCount, soldCount, compensatedCount, activeCount, compLines.Count,
            eligibleAmount, completedCount, openPayout?.PayoutNumber, blockReason);

        return Result.Success(new SettlementPreviewDto(
            contract.Id,
            contract.ContractNumber,
            contract.CustomerNameSnapshot,
            lines,
            decimal.Round(lines.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero),
            blockReason,
            contract.CustomerPhoneSnapshot,
            FirstNonEmpty(contract.BankCodeSnapshot, contract.Customer?.BankCode),
            FirstNonEmpty(contract.BankAccountNumberSnapshot, contract.Customer?.BankAccountNumber),
            FirstNonEmpty(contract.BankAccountHolderSnapshot, contract.Customer?.BankAccountHolderName),
            contract.EndsAt,
            deferredLines,
            deferredAmount,
            openPayout?.Id,
            openPayout?.PayoutNumber,
            openPayout?.Status.ToString(),
            openPayout?.TotalAmount,
            isCompensationOnly,
            isMidTerm,
            eligibilityKind,
            eligibilityReason,
            totalCount,
            soldCount,
            activeCount,
            compensatedCount,
            otherCount,
            completedCount,
            completedAmount,
            eligibilityFactors));
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

    public async Task<Result<PayoutDto>> CreatePayoutAsync(Guid contractId, Guid actor, CreatePayoutRequest request, CancellationToken ct)
    {
        if (request.ServiceFee < 0 || decimal.Round(request.ServiceFee, 2) != request.ServiceFee || request.ServiceFee > 9999999999999999m ||
            request.ServiceFeeDescription?.Length > 1000 ||
            (request.ServiceFee > 0 && string.IsNullOrWhiteSpace(request.ServiceFeeDescription)))
            return Invalid<PayoutDto>("SETTLEMENT.SERVICE_FEE", "Phí dịch vụ phải không âm, tối đa 2 chữ số thập phân; cần mô tả khi thu phí.");
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var contract = await db.ConsignmentContracts
            .Include(x => x.Customer)
            .SingleOrDefaultAsync(x => x.Id == contractId, ct);
        if (contract is null) return Missing<PayoutDto>();
        var (salesLines, compLines, isMidTerm) = await SplitEligibleLinesAsync(contractId, ct);
        var blockReason = await GetBlockReasonAsync(contractId, ct, compLines.Count > 0);
        if (blockReason is not null) return Conflict<PayoutDto>(blockReason);
        var lines = isMidTerm ? compLines : [.. salesLines, .. compLines];
        if (lines.Count == 0) return Conflict<PayoutDto>("Chưa có khoản tiền đủ điều kiện để quyết toán.");
        if (request.ServiceFee > 0)
            lines.Add(new SettlementLineDto(nameof(PayoutLineType.ServiceFee), request.ServiceFeeDescription!.Trim(), -request.ServiceFee, null, null));
        var total = decimal.Round(lines.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
        if (total <= 0) return Conflict<PayoutDto>("Tổng số tiền chi trả phải lớn hơn 0.");

        var now = clock.GetUtcNow();
        var payout = new Payout
        {
            PayoutNumber = $"PO-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..28],
            ConsignmentContractId = contractId,
            CreatedByEmployeeId = actor,
            BankCode = FirstNonEmpty(contract.BankCodeSnapshot, contract.Customer?.BankCode),
            BankAccountNumber = FirstNonEmpty(contract.BankAccountNumberSnapshot, contract.Customer?.BankAccountNumber),
            BankAccountHolder = FirstNonEmpty(contract.BankAccountHolderSnapshot, contract.Customer?.BankAccountHolderName),
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
        if (string.IsNullOrWhiteSpace(payout.BankCode) || string.IsNullOrWhiteSpace(payout.BankAccountNumber) ||
            string.IsNullOrWhiteSpace(payout.BankAccountHolder))
            return Conflict<PayoutDto>("Cần nhập đủ thông tin tài khoản nhận trước khi duyệt lệnh chi.");
        payout.Status = PayoutStatus.Approved;
        payout.ApprovedByEmployeeId = actor;
        payout.ApprovedAt = clock.GetUtcNow();
        payout.UpdatedAt = payout.ApprovedAt;
        await MarkLinkedIncidentsCompensatedAsync([payout], payout.ApprovedAt.Value, ct);
        return await SavePayoutAsync(payout, ct);
    }

    public async Task<Result<PayoutDto>> UpdatePaymentAsync(Guid id, Guid actor, UpdatePayoutPaymentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BankCode) || request.BankCode.Trim().Length > 50 ||
            string.IsNullOrWhiteSpace(request.BankAccountNumber) || request.BankAccountNumber.Trim().Length > 50 ||
            string.IsNullOrWhiteSpace(request.BankAccountHolder) || request.BankAccountHolder.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return Invalid<PayoutDto>("SETTLEMENT.PAYMENT", "Cần đủ thông tin ngân hàng và lý do cập nhật.");
        var payout = await Payouts().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (payout is null) return Missing<PayoutDto>();
        if (payout.Status != PayoutStatus.Pending) return Conflict<PayoutDto>("Thông tin thanh toán đã khóa sau khi duyệt.");
        payout.BankCode = request.BankCode.Trim();
        payout.BankAccountNumber = request.BankAccountNumber.Trim();
        payout.BankAccountHolder = request.BankAccountHolder.Trim();
        payout.UpdatedAt = clock.GetUtcNow();
        db.AuditLogs.Add(new AuditLog
        {
            ActorAccountId = actor,
            ActorType = AuditActorType.User,
            Action = "UpdatePayment",
            EntityType = nameof(Payout),
            EntityId = payout.Id.ToString(),
            Reason = request.Reason.Trim(),
            Source = "SettlementService",
            OccurredAt = payout.UpdatedAt.Value
        });
        return await SavePayoutAsync(payout, ct);
    }

    public async Task<Result<PayoutDto>> UpdateServiceFeeAsync(Guid id, Guid actor, UpdatePayoutServiceFeeRequest request, CancellationToken ct)
    {
        if (request.ServiceFee < 0 || decimal.Round(request.ServiceFee, 2) != request.ServiceFee || request.ServiceFee > 9999999999999999m ||
            request.Description?.Length > 1000 || (request.ServiceFee > 0 && string.IsNullOrWhiteSpace(request.Description)) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return Invalid<PayoutDto>("SETTLEMENT.SERVICE_FEE", "Phí dịch vụ hoặc lý do cập nhật không hợp lệ.");
        var payout = await Payouts().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (payout is null) return Missing<PayoutDto>();
        if (payout.Status != PayoutStatus.Pending) return Conflict<PayoutDto>("Bảng kê đã khóa sau khi duyệt hoặc từ chối.");
        var net = payout.Details.Where(x => x.Type != PayoutLineType.ServiceFee).Sum(x => x.Amount) - request.ServiceFee;
        if (net <= 0) return Invalid<PayoutDto>("SETTLEMENT.TOTAL", "Số tiền thực trả phải lớn hơn 0.");
        var feeLine = payout.Details.SingleOrDefault(x => x.Type == PayoutLineType.ServiceFee);
        if (request.ServiceFee == 0 && feeLine is not null)
        {
            payout.Details.Remove(feeLine);
            db.PayoutDetails.Remove(feeLine);
        }
        else if (request.ServiceFee > 0 && feeLine is null)
        {
            payout.Details.Add(new PayoutDetail
            {
                PayoutId = payout.Id,
                Type = PayoutLineType.ServiceFee,
                Description = request.Description!.Trim(),
                Amount = -request.ServiceFee
            });
        }
        else if (feeLine is not null)
        {
            feeLine.Description = request.Description!.Trim();
            feeLine.Amount = -request.ServiceFee;
        }
        payout.TotalAmount = net;
        payout.UpdatedAt = clock.GetUtcNow();
        db.AuditLogs.Add(new AuditLog
        {
            ActorAccountId = actor,
            ActorType = AuditActorType.User,
            Action = "UpdateServiceFee",
            EntityType = nameof(Payout),
            EntityId = payout.Id.ToString(),
            Reason = request.Reason.Trim(),
            Source = "SettlementService",
            OccurredAt = payout.UpdatedAt.Value
        });
        return await SavePayoutAsync(payout, ct);
    }

    public async Task<Result<PayoutDto>> RejectAsync(Guid id, Guid actor, RejectPayoutRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return Invalid<PayoutDto>("SETTLEMENT.REJECTION", "Cần nhập lý do từ chối, tối đa 1000 ký tự.");
        var payout = await Payouts().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (payout is null) return Missing<PayoutDto>();
        if (payout.Status != PayoutStatus.Pending) return Conflict<PayoutDto>("Chỉ có thể từ chối lệnh chi đang chờ duyệt.");
        payout.Status = PayoutStatus.Rejected;
        payout.RejectedByEmployeeId = actor;
        payout.RejectedAt = clock.GetUtcNow();
        payout.RejectionReason = request.Reason.Trim();
        payout.UpdatedAt = payout.RejectedAt;
        foreach (var line in payout.Details) line.IsReleased = true;
        return await SavePayoutAsync(payout, ct);
    }

    public async Task<Result<IReadOnlyList<PayoutHistoryDto>>> HistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Payouts.AnyAsync(x => x.Id == id, ct)) return Missing<IReadOnlyList<PayoutHistoryDto>>();
        var logs = await db.AuditLogs.AsNoTracking().Where(x => x.EntityType == nameof(Payout) && x.EntityId == id.ToString())
            .OrderBy(x => x.OccurredAt).Select(x => new PayoutHistoryDto(x.Action, x.Changes, x.Reason, x.ActorAccountId,
                x.ActorAccount == null ? null : x.ActorAccount.FullName, x.OccurredAt)).ToArrayAsync(ct);
        return Result.Success<IReadOnlyList<PayoutHistoryDto>>(logs);
    }

    public async Task<Result<PayoutDto>> RecordTransferAsync(Guid id, Guid actor, RecordPayoutTransferRequest request, CancellationToken ct)
    {
        var proof = NormalizeProof(request.ProofUrl);
        if (proof is null || string.IsNullOrWhiteSpace(request.BankTransactionReference) || request.BankTransactionReference.Trim().Length > 100)
            return Invalid<PayoutDto>("SETTLEMENT.TRANSFER", "Cần ảnh chứng từ hợp lệ và mã giao dịch chuyển khoản.");
        var key = imageStore.GetKey(proof);
        if (key is null || await imageStore.ReadAsync(key, ct) is null)
            return Invalid<PayoutDto>("SETTLEMENT.PROOF", "Ảnh chứng từ không tồn tại trên R2 hoặc không hợp lệ.");
        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var payout = await Payouts().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (payout is null) return Missing<PayoutDto>();
        if (payout.Status != PayoutStatus.Approved) return Conflict<PayoutDto>("Chỉ lệnh đã duyệt mới được ghi nhận chuyển khoản.");
        if (string.IsNullOrWhiteSpace(payout.BankCode) || string.IsNullOrWhiteSpace(payout.BankAccountNumber) ||
            string.IsNullOrWhiteSpace(payout.BankAccountHolder))
            return Conflict<PayoutDto>("Lệnh chi thiếu thông tin tài khoản nhận; cần quy trình điều chỉnh trước khi giải ngân.");
        if (await db.Payouts.AnyAsync(x => x.Id != id && x.ProofUrl == proof, ct))
            return Conflict<PayoutDto>("Chứng từ này đã gắn với lệnh chi khác.");
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
            BankCode = payout.BankCode,
            BankAccountNumber = payout.BankAccountNumber,
            BankAccountHolderName = payout.BankAccountHolder,
            ProofUrl = proof,
            Status = FinancialTransactionStatus.Success,
            OccurredAt = now,
            PerformedByEmployeeId = actor
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict<PayoutDto>("Lệnh chi đã thay đổi. Vui lòng tải lại trước khi ghi nhận chuyển khoản."); }
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
        .Include(x => x.Details)
        .Include(x => x.CreatedByEmployee).ThenInclude(x => x!.Account)
        .Include(x => x.ApprovedByEmployee).ThenInclude(x => x!.Account)
        .Include(x => x.RejectedByEmployee).ThenInclude(x => x!.Account);

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

    private async Task<string?> GetBlockReasonAsync(Guid contractId, CancellationToken ct, bool allowInterimCompensation = false)
    {
        var contract = await db.ConsignmentContracts.AsNoTracking().SingleAsync(x => x.Id == contractId, ct);
        if (contract.Status == ConsignmentContractStatus.Cancelled)
            return "Hợp đồng đã hủy, không thể lập quyết toán.";
        var hasActiveProducts = await db.Products.AnyAsync(x => x.ConsignmentContractId == contractId &&
            (x.Status == ProductStatus.OnSale || x.Status == ProductStatus.PendingProcessing || x.Status == ProductStatus.IncidentPending), ct);
        if (contract.EndsAt > clock.GetUtcNow() && hasActiveProducts && !allowInterimCompensation)
            return "Chỉ lập quyết toán cuối đợt khi hết hạn hợp đồng hoặc toàn bộ sản phẩm đã xử lý.";
        if (await db.IncidentReports.AnyAsync(x => x.Product.ConsignmentContractId == contractId &&
                (x.Status == IncidentStatus.Pending || x.Status == IncidentStatus.NeedsRecheck), ct))
            return "Còn biên bản sự cố đang chờ xử lý hoặc bổ sung.";
        if (await db.SaleInvoiceLines.AnyAsync(x => x.Product.ConsignmentContractId == contractId &&
                x.Product.Status == ProductStatus.Sold && x.SaleInvoice.PaymentStatus != InvoicePaymentStatus.Paid, ct))
            return "Còn hóa đơn bán hàng chưa thanh toán đầy đủ.";
        if (await db.FinancialTransactions.AnyAsync(x => x.Status == FinancialTransactionStatus.Pending &&
                x.SaleInvoice != null && x.SaleInvoice.Lines.Any(line => line.Product.ConsignmentContractId == contractId), ct))
            return "Còn giao dịch thanh toán đang chờ xử lý.";
        if (await db.Payouts.AnyAsync(x => x.ConsignmentContractId == contractId &&
                (x.Status == PayoutStatus.Pending || x.Status == PayoutStatus.Approved || x.Status == PayoutStatus.Transferred), ct))
            return "Đợt quyết toán trước chưa hoàn tất xác nhận nhận tiền.";
        return null;
    }

    private async Task<(List<SettlementLineDto> SalesLines, List<SettlementLineDto> CompensationLines, bool IsMidTerm)> SplitEligibleLinesAsync(
        Guid contractId, CancellationToken ct)
    {
        var contract = await db.ConsignmentContracts.AsNoTracking().SingleAsync(x => x.Id == contractId, ct);
        var hasActiveProducts = await db.Products.AnyAsync(x => x.ConsignmentContractId == contractId &&
            (x.Status == ProductStatus.OnSale || x.Status == ProductStatus.PendingProcessing || x.Status == ProductStatus.IncidentPending), ct);
        var isMidTerm = contract.EndsAt > clock.GetUtcNow() && hasActiveProducts;

        var alreadyPaid = await db.PayoutDetails.Where(x => x.Payout.ConsignmentContractId == contractId && !x.IsReleased && x.ProductId != null)
            .Select(x => new { x.ProductId, x.Type }).ToArrayAsync(ct);
        var sold = await db.SaleInvoiceLines.AsNoTracking()
            .Where(x => x.Product.ConsignmentContractId == contractId && x.Product.Status == ProductStatus.Sold &&
                x.SaleInvoice.PaymentStatus == InvoicePaymentStatus.Paid)
            .Select(x => new { x.ProductId, x.Product.Name, x.Product.Barcode, x.Product.AgreedCommissionRate, x.SalePrice })
            .ToArrayAsync(ct);
        var salesLines = new List<SettlementLineDto>();
        foreach (var sale in sold)
        {
            if (!alreadyPaid.Any(x => x.ProductId == sale.ProductId && x.Type == PayoutLineType.SaleProceeds))
                salesLines.Add(new SettlementLineDto(nameof(PayoutLineType.SaleProceeds), $"Tiền bán {sale.Name} ({sale.Barcode})", sale.SalePrice, sale.ProductId, null));
            if (!alreadyPaid.Any(x => x.ProductId == sale.ProductId && x.Type == PayoutLineType.Commission))
            {
                var commission = decimal.Round(sale.SalePrice * (sale.AgreedCommissionRate ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero);
                if (commission > 0)
                    salesLines.Add(new SettlementLineDto(nameof(PayoutLineType.Commission), $"Hoa hồng {sale.Name} ({sale.Barcode})", -commission, sale.ProductId, null));
            }
        }
        var paidIncidentIds = await db.PayoutDetails.Where(x => !x.IsReleased && x.IncidentReportId != null && x.Payout.ConsignmentContractId == contractId)
            .Select(x => x.IncidentReportId!.Value).ToArrayAsync(ct);
        var incidents = await db.IncidentReports.AsNoTracking().Include(x => x.Product)
            .Where(x => x.Product.ConsignmentContractId == contractId && x.Status == IncidentStatus.Approved &&
                !paidIncidentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Product.Name, x.Product.Barcode, x.CompensationAmount })
            .ToArrayAsync(ct);
        var compLines = incidents.Select(x => new SettlementLineDto(nameof(PayoutLineType.Compensation),
            $"Bồi thường sự cố: {x.Name} ({x.Barcode})", x.CompensationAmount, null, x.Id)).ToList();
        return (salesLines, compLines, isMidTerm);
    }

    private static string? FirstNonEmpty(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary) ? primary.Trim() : string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();

    private static (string Kind, string Reason, IReadOnlyList<string> Factors) DescribeEligibility(
        ConsignmentContractStatus contractStatus,
        DateTimeOffset endsAt,
        DateTimeOffset now,
        int totalCount,
        int soldCount,
        int compensatedCount,
        int activeCount,
        int unpaidApprovedIncidentCount,
        decimal eligibleAmount,
        int completedPayoutCount,
        string? openPayoutNumber,
        string? blockReason)
    {
        var vnEndsAt = endsAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var factors = new List<string>();

        if (contractStatus == ConsignmentContractStatus.Cancelled)
        {
            factors.Add("Đã hủy hợp đồng");
            return ("Cancelled", "Đã hủy hợp đồng", factors);
        }

        factors.Add(endsAt <= now ? $"Đã hết hạn ({vnEndsAt})" : $"Còn hạn (đến {vnEndsAt})");
        if (totalCount == 0)
            factors.Add("Chưa có sản phẩm");
        else if (activeCount == 0 && soldCount == totalCount)
            factors.Add($"Đã bán xong ({soldCount}/{totalCount} SP)");
        else if (activeCount == 0)
            factors.Add($"Đã xử lý xong ({totalCount}/{totalCount} SP)");
        else
            factors.Add($"Còn {activeCount}/{totalCount} SP đang bán");

        if (!string.IsNullOrWhiteSpace(openPayoutNumber))
        {
            factors.Add($"Lệnh chi mở: {openPayoutNumber}");
            return ("OpenPayout", "Đang có lệnh chi mở", factors);
        }

        if (!string.IsNullOrWhiteSpace(blockReason))
        {
            if (blockReason.Contains("sự cố", StringComparison.Ordinal))
                factors.Add("Chờ duyệt/bổ sung sự cố");
            else if (blockReason.Contains("hóa đơn", StringComparison.Ordinal))
                factors.Add("Chờ thu đủ tiền hóa đơn");
            else if (blockReason.Contains("giao dịch", StringComparison.Ordinal))
                factors.Add("Chờ hoàn tất giao dịch");
            return ("Blocked", "Chưa đủ điều kiện lập", factors);
        }

        var isMidTerm = endsAt > now && activeCount > 0;
        if (isMidTerm && unpaidApprovedIncidentCount > 0 && eligibleAmount > 0)
        {
            factors.Add($"{unpaidApprovedIncidentCount} bồi thường chờ chi");
            return ("ReadyInterim", "Chi bồi thường giữa đợt", factors);
        }

        if (eligibleAmount > 0)
        {
            if (unpaidApprovedIncidentCount > 0)
                factors.Add($"{unpaidApprovedIncidentCount} bồi thường chờ chi");
            return ("ReadyFinal", endsAt <= now ? "Đủ ĐK quyết toán cuối đợt" : "Đủ ĐK quyết toán sớm", factors);
        }

        if (completedPayoutCount > 0)
            factors.Add($"Đã tất toán ({completedPayoutCount} lệnh chi)");
        else if (soldCount == 0 && compensatedCount == 0 && totalCount > 0)
            factors.Add("Chưa phát sinh bán / bồi thường");
        else
            factors.Add("Khoản dư chờ chi = 0 ₫");

        return ("SettledOrEmpty", "Không phát sinh khoản chi", factors);
    }

    private static void Complete(Payout payout, DateTimeOffset now, bool customerConfirmed)
    {
        payout.Status = PayoutStatus.Completed;
        payout.CompletedAt = now;
        if (customerConfirmed) payout.CustomerConfirmedAt = now;
        payout.UpdatedAt = now;
    }

    private async Task MarkLinkedIncidentsCompensatedAsync(IReadOnlyList<Payout> payouts, DateTimeOffset now, CancellationToken ct)
    {
        var incidentIds = payouts.SelectMany(x => x.Details).Where(x => x.IncidentReportId.HasValue)
            .Select(x => x.IncidentReportId!.Value).Distinct().ToArray();
        if (incidentIds.Length == 0) return;
        var incidents = await db.IncidentReports
            .Where(x => incidentIds.Contains(x.Id) && x.Status == IncidentStatus.Approved)
            .ToListAsync(ct);
        foreach (var incident in incidents)
        {
            incident.Status = IncidentStatus.Compensated;
            incident.UpdatedAt = now;
        }
    }

    private async Task CompleteLinkedIncidentsAndContractsAsync(IReadOnlyList<Payout> payouts, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await MarkLinkedIncidentsCompensatedAsync(payouts, now, ct);
        var contractIds = payouts.Select(x => x.ConsignmentContractId).Distinct().ToArray();
        var contracts = await db.ConsignmentContracts.Where(x => contractIds.Contains(x.Id)).ToListAsync(ct);
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
            contract.CustomerNameSnapshot, contract.CustomerPhoneSnapshot, payout.BankCode,
            payout.BankAccountNumber, payout.BankAccountHolder, payout.TotalAmount,
            payout.Status.ToString(), payout.ProofUrl, payout.BankTransactionReference, payout.CreatedAt,
            payout.ApprovedAt, payout.TransferredAt, payout.CustomerConfirmedAt, payout.CompletedAt,
            payout.Details.OrderBy(x => x.CreatedAt).Select(x => new SettlementLineDto(x.Type.ToString(), x.Description,
                x.Amount, x.ProductId, x.IncidentReportId)).ToArray(), payout.CreatedByEmployeeId,
            payout.ApprovedByEmployeeId, payout.RejectedByEmployeeId, payout.RejectedAt, payout.RejectionReason,
            payout.CreatedByEmployee?.Account.FullName, payout.ApprovedByEmployee?.Account.FullName,
            payout.RejectedByEmployee?.Account.FullName);
    }

    private async Task<Result<PayoutDto>> SavePayoutAsync(Payout payout, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict<PayoutDto>("Lệnh chi đã thay đổi. Vui lòng tải lại trước khi thao tác."); }
        return Result.Success(Map(payout));
    }

    private static Result<T> Invalid<T>(string code, string message) => Result.Failure<T>(ApplicationError.Validation(code, message));
    private static Result<T> Missing<T>() => Result.Failure<T>(ApplicationError.NotFound("SETTLEMENT.NOT_FOUND", "Không tìm thấy dữ liệu quyết toán."));
    private static Result<T> Conflict<T>(string message) => Result.Failure<T>(ApplicationError.Conflict("SETTLEMENT.STATE", message));
}

public sealed record ProtectedPayoutProof(byte[] Content, string ContentType);
