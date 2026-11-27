namespace OCS.Api.Models.Reports;

public sealed record ReportFilterRequest(
    DateOnly? From = null,
    DateOnly? To = null,
    string GroupBy = "day",
    string? PaymentMethod = null,
    int Page = 1,
    int PageSize = 20);

public sealed record PagedReportItems<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record ReportOverviewDto(
    DateOnly From,
    DateOnly To,
    string GroupBy,
    int ActiveOnSaleCount,
    decimal PosRevenue,
    int PosInvoiceCount,
    int PendingPosTransactionCount,
    decimal RefundedPosAmount,
    decimal CompletedCommission,
    int CompletedPayoutCount,
    decimal BuyoutPurchaseCost,
    int PaidBuyoutCount,
    decimal BuyoutResaleRevenue,
    decimal BuyoutResaleCost,
    decimal BuyoutResaleProfit,
    int SoldBuyoutProductCount,
    IReadOnlyList<OverviewSeriesPointDto> Series);

public sealed record OverviewSeriesPointDto(
    string BucketKey,
    string Label,
    decimal PosCashRevenue,
    decimal PosVietQrRevenue,
    decimal PosOtherRevenue,
    decimal PosTotalRevenue,
    decimal CommissionAmount,
    decimal BuyoutPurchaseCost,
    decimal BuyoutResaleProfit);

public sealed record PosMethodBreakdownDto(
    string Method,
    string Label,
    decimal Amount,
    int TransactionCount,
    decimal Percentage);

public sealed record PosRevenueDetailDto(
    Guid TransactionId,
    Guid SaleInvoiceId,
    string InvoiceNumber,
    DateTimeOffset OccurredAt,
    string PaymentMethod,
    decimal Amount,
    int ItemCount,
    string? CashierName);

public sealed record PosRevenueReportDto(
    DateOnly From,
    DateOnly To,
    string GroupBy,
    decimal TotalRevenue,
    int InvoiceCount,
    int PendingTransactionCount,
    decimal RefundedAmount,
    IReadOnlyList<PosMethodBreakdownDto> Breakdown,
    IReadOnlyList<OverviewSeriesPointDto> Series,
    PagedReportItems<PosRevenueDetailDto> Details);

public sealed record ConsignmentCommissionDetailDto(
    Guid PayoutId,
    string PayoutNumber,
    string ContractNumber,
    string? CustomerName,
    DateTimeOffset CompletedAt,
    decimal SaleProceedsAmount,
    decimal CommissionAmount,
    decimal ServiceFeeAmount,
    decimal NetPayoutAmount);

public sealed record ConsignmentCommissionSeriesDto(
    string BucketKey,
    string Label,
    decimal CommissionAmount,
    decimal SaleProceedsAmount,
    int PayoutCount);

public sealed record ConsignmentCommissionReportDto(
    DateOnly From,
    DateOnly To,
    string GroupBy,
    decimal TotalCommission,
    decimal TotalSaleProceeds,
    decimal TotalNetPayout,
    int CompletedPayoutCount,
    IReadOnlyList<ConsignmentCommissionSeriesDto> Series,
    PagedReportItems<ConsignmentCommissionDetailDto> Details);

public sealed record BuyoutProfitDetailDto(
    Guid ProductId,
    string Barcode,
    string ProductName,
    string? BuyoutNumber,
    string InvoiceNumber,
    DateTimeOffset SoldAt,
    decimal PurchasePrice,
    decimal SalePrice,
    decimal Profit);

public sealed record BuyoutProfitSeriesDto(
    string BucketKey,
    string Label,
    decimal PurchaseCost,
    decimal ResaleRevenue,
    decimal ResaleProfit);

public sealed record BuyoutProfitReportDto(
    DateOnly From,
    DateOnly To,
    string GroupBy,
    decimal TotalPurchaseCost,
    int PaidBuyoutCount,
    decimal ResaleRevenue,
    decimal ResaleCost,
    decimal ResaleProfit,
    int SoldProductCount,
    IReadOnlyList<BuyoutProfitSeriesDto> Series,
    PagedReportItems<BuyoutProfitDetailDto> Details);
