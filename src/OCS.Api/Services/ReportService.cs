using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Models.Reports;

namespace OCS.Api.Services;

public sealed class ReportService(AppDbContext db, TimeProvider clock)
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public async Task<Result<ReportOverviewDto>> GetOverviewAsync(ReportFilterRequest filter, CancellationToken ct)
    {
        if (!TryResolveWindow(filter, out var from, out var to, out var groupBy, out var startUtc, out var endUtc, out var error))
            return Result.Failure<ReportOverviewDto>(error!);

        var activeOnSaleCount = await db.Products.AsNoTracking()
            .CountAsync(x => x.Status == ProductStatus.OnSale, ct);

        var posTx = await db.FinancialTransactions.AsNoTracking()
            .Where(x => x.Type == FinancialTransactionType.Income && x.SaleInvoiceId != null &&
                        x.OccurredAt >= startUtc && x.OccurredAt < endUtc)
            .Select(x => new
            {
                x.SaleInvoiceId,
                x.Status,
                x.PaymentMethod,
                x.Amount,
                x.OccurredAt,
                InvoiceStatus = x.SaleInvoice!.PaymentStatus
            })
            .ToArrayAsync(ct);

        var successPos = posTx
            .Where(x => x.Status == FinancialTransactionStatus.Success && x.InvoiceStatus == InvoicePaymentStatus.Paid)
            .ToArray();
        var pendingPosCount = posTx.Count(x => x.Status == FinancialTransactionStatus.Pending);
        var refundedPosAmount = posTx.Where(x => x.Status == FinancialTransactionStatus.Refunded).Sum(x => x.Amount);

        var completedPayouts = await db.Payouts.AsNoTracking()
            .Where(x => x.Status == PayoutStatus.Completed && x.CompletedAt != null &&
                        x.CompletedAt >= startUtc && x.CompletedAt < endUtc)
            .Select(x => new
            {
                x.Id,
                CompletedAt = x.CompletedAt!.Value,
                Commission = x.Details
                    .Where(d => !d.IsReleased && d.Type == PayoutLineType.Commission)
                    .Sum(d => d.Amount < 0 ? -d.Amount : d.Amount)
            })
            .ToArrayAsync(ct);

        var paidBuyouts = await db.Buyouts.AsNoTracking()
            .Where(x => x.Status == BuyoutStatus.Approved && x.PaidAt != null &&
                        x.PaidAt >= startUtc && x.PaidAt < endUtc)
            .Select(x => new { PaidAt = x.PaidAt!.Value, x.TotalPurchasePrice })
            .ToArrayAsync(ct);

        var soldBuyoutRows = await QuerySoldPaidBuyoutLines(startUtc, endUtc)
            .Select(x => new { x.SoldAt, x.PurchasePrice, x.SalePrice })
            .ToArrayAsync(ct);

        var buckets = BuildBuckets(from, to, groupBy);
        var posByBucket = successPos
            .GroupBy(x => GetBucketKey(x.OccurredAt, groupBy))
            .ToDictionary(g => g.Key, g => g.ToArray());
        var commByBucket = completedPayouts
            .GroupBy(x => GetBucketKey(x.CompletedAt, groupBy))
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Commission));
        var buyoutCostByBucket = paidBuyouts
            .GroupBy(x => GetBucketKey(x.PaidAt, groupBy))
            .ToDictionary(g => g.Key, g => g.Sum(i => i.TotalPurchasePrice));
        var buyoutProfitByBucket = soldBuyoutRows
            .GroupBy(x => GetBucketKey(x.SoldAt, groupBy))
            .ToDictionary(g => g.Key, g => g.Sum(i => i.SalePrice - i.PurchasePrice));

        var series = buckets.Select(b =>
        {
            var txs = posByBucket.GetValueOrDefault(b.Key, []);
            var cash = txs.Where(x => x.PaymentMethod == PaymentMethod.Cash).Sum(x => x.Amount);
            var vietQr = txs.Where(x => x.PaymentMethod == PaymentMethod.VietQr).Sum(x => x.Amount);
            var other = txs.Where(x => x.PaymentMethod != PaymentMethod.Cash && x.PaymentMethod != PaymentMethod.VietQr).Sum(x => x.Amount);
            return new OverviewSeriesPointDto(
                b.Key, b.Label, cash, vietQr, other, cash + vietQr + other,
                commByBucket.GetValueOrDefault(b.Key, 0m),
                buyoutCostByBucket.GetValueOrDefault(b.Key, 0m),
                buyoutProfitByBucket.GetValueOrDefault(b.Key, 0m));
        }).ToArray();

        var resaleRevenue = soldBuyoutRows.Sum(x => x.SalePrice);
        var resaleCost = soldBuyoutRows.Sum(x => x.PurchasePrice);

        return Result.Success(new ReportOverviewDto(
            from, to, groupBy, activeOnSaleCount,
            successPos.Sum(x => x.Amount),
            successPos.Select(x => x.SaleInvoiceId).Distinct().Count(),
            pendingPosCount,
            refundedPosAmount,
            completedPayouts.Sum(x => x.Commission),
            completedPayouts.Length,
            paidBuyouts.Sum(x => x.TotalPurchasePrice),
            paidBuyouts.Length,
            resaleRevenue,
            resaleCost,
            resaleRevenue - resaleCost,
            soldBuyoutRows.Length,
            series));
    }

    public async Task<Result<PosRevenueReportDto>> GetPosRevenueAsync(ReportFilterRequest filter, CancellationToken ct)
    {
        if (!TryResolveWindow(filter, out var from, out var to, out var groupBy, out var startUtc, out var endUtc, out var error))
            return Result.Failure<PosRevenueReportDto>(error!);

        if (!TryParsePaymentMethod(filter.PaymentMethod, out var methodFilter))
            return Result.Failure<PosRevenueReportDto>(ApplicationError.Validation("REPORTS.PAYMENT_METHOD", "Phương thức thanh toán không hợp lệ."));

        var allTx = await db.FinancialTransactions.AsNoTracking()
            .Where(x => x.Type == FinancialTransactionType.Income && x.SaleInvoiceId != null &&
                        x.OccurredAt >= startUtc && x.OccurredAt < endUtc)
            .Select(x => new
            {
                x.Id,
                SaleInvoiceId = x.SaleInvoiceId!.Value,
                InvoiceNumber = x.SaleInvoice!.InvoiceNumber,
                InvoiceStatus = x.SaleInvoice.PaymentStatus,
                x.OccurredAt,
                x.PaymentMethod,
                x.Status,
                x.Amount,
                ItemCount = x.SaleInvoice.Lines.Count,
                CashierName = x.SaleInvoice.SoldByEmployee.Account.FullName
            })
            .ToArrayAsync(ct);

        var filteredSuccess = allTx
            .Where(x => x.Status == FinancialTransactionStatus.Success
                        && x.InvoiceStatus == InvoicePaymentStatus.Paid
                        && (methodFilter is null || x.PaymentMethod == methodFilter.Value))
            .OrderByDescending(x => x.OccurredAt)
            .ToArray();

        var totalRevenue = filteredSuccess.Sum(x => x.Amount);
        var breakdown = filteredSuccess
            .GroupBy(x => x.PaymentMethod)
            .Select(g =>
            {
                var amount = g.Sum(x => x.Amount);
                var pct = totalRevenue > 0 ? decimal.Round(amount * 100m / totalRevenue, 1, MidpointRounding.AwayFromZero) : 0m;
                return new PosMethodBreakdownDto(g.Key.ToString(), FormatPaymentMethod(g.Key), amount, g.Count(), pct);
            })
            .OrderByDescending(x => x.Amount)
            .ToArray();

        var buckets = BuildBuckets(from, to, groupBy);
        var byBucket = filteredSuccess.GroupBy(x => GetBucketKey(x.OccurredAt, groupBy)).ToDictionary(g => g.Key, g => g.ToArray());
        var series = buckets.Select(b =>
        {
            var txs = byBucket.GetValueOrDefault(b.Key, []);
            var cash = txs.Where(x => x.PaymentMethod == PaymentMethod.Cash).Sum(x => x.Amount);
            var vietQr = txs.Where(x => x.PaymentMethod == PaymentMethod.VietQr).Sum(x => x.Amount);
            var other = txs.Where(x => x.PaymentMethod != PaymentMethod.Cash && x.PaymentMethod != PaymentMethod.VietQr).Sum(x => x.Amount);
            return new OverviewSeriesPointDto(b.Key, b.Label, cash, vietQr, other, cash + vietQr + other, 0m, 0m, 0m);
        }).ToArray();

        var (page, pageSize) = NormalizePaging(filter);
        var paged = filteredSuccess.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new PosRevenueDetailDto(
                x.Id, x.SaleInvoiceId, x.InvoiceNumber, x.OccurredAt,
                x.PaymentMethod.ToString(), x.Amount, x.ItemCount, x.CashierName))
            .ToArray();

        return Result.Success(new PosRevenueReportDto(
            from, to, groupBy, totalRevenue,
            filteredSuccess.Select(x => x.SaleInvoiceId).Distinct().Count(),
            allTx.Count(x => x.Status == FinancialTransactionStatus.Pending),
            allTx.Where(x => x.Status == FinancialTransactionStatus.Refunded).Sum(x => x.Amount),
            breakdown, series,
            new PagedReportItems<PosRevenueDetailDto>(paged, page, pageSize, filteredSuccess.Length)));
    }

    public async Task<Result<ConsignmentCommissionReportDto>> GetConsignmentCommissionsAsync(ReportFilterRequest filter, CancellationToken ct)
    {
        if (!TryResolveWindow(filter, out var from, out var to, out var groupBy, out var startUtc, out var endUtc, out var error))
            return Result.Failure<ConsignmentCommissionReportDto>(error!);

        var rows = await QueryConsignmentCommissionDetails(startUtc, endUtc).ToArrayAsync(ct);

        var buckets = BuildBuckets(from, to, groupBy);
        var byBucket = rows.GroupBy(x => GetBucketKey(x.CompletedAt, groupBy)).ToDictionary(g => g.Key, g => g.ToArray());
        var series = buckets.Select(b =>
        {
            var items = byBucket.GetValueOrDefault(b.Key, []);
            return new ConsignmentCommissionSeriesDto(
                b.Key, b.Label,
                items.Sum(x => x.CommissionAmount),
                items.Sum(x => x.SaleProceedsAmount),
                items.Length);
        }).ToArray();

        var (page, pageSize) = NormalizePaging(filter);
        var paged = rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray();

        return Result.Success(new ConsignmentCommissionReportDto(
            from, to, groupBy,
            rows.Sum(x => x.CommissionAmount),
            rows.Sum(x => x.SaleProceedsAmount),
            rows.Sum(x => x.NetPayoutAmount),
            rows.Length,
            series,
            new PagedReportItems<ConsignmentCommissionDetailDto>(paged, page, pageSize, rows.Length)));
    }

    public async Task<Result<BuyoutProfitReportDto>> GetBuyoutProfitAsync(ReportFilterRequest filter, CancellationToken ct)
    {
        if (!TryResolveWindow(filter, out var from, out var to, out var groupBy, out var startUtc, out var endUtc, out var error))
            return Result.Failure<BuyoutProfitReportDto>(error!);

        var paidBuyouts = await db.Buyouts.AsNoTracking()
            .Where(x => x.Status == BuyoutStatus.Approved && x.PaidAt != null &&
                        x.PaidAt >= startUtc && x.PaidAt < endUtc)
            .Select(x => new { PaidAt = x.PaidAt!.Value, x.TotalPurchasePrice })
            .ToArrayAsync(ct);

        var soldRows = (await QuerySoldPaidBuyoutLines(startUtc, endUtc).ToArrayAsync(ct))
            .OrderByDescending(x => x.SoldAt)
            .ToArray();

        var buckets = BuildBuckets(from, to, groupBy);
        var costByBucket = paidBuyouts.GroupBy(x => GetBucketKey(x.PaidAt, groupBy)).ToDictionary(g => g.Key, g => g.Sum(i => i.TotalPurchasePrice));
        var soldByBucket = soldRows.GroupBy(x => GetBucketKey(x.SoldAt, groupBy)).ToDictionary(g => g.Key, g => g.ToArray());

        var series = buckets.Select(b =>
        {
            var sold = soldByBucket.GetValueOrDefault(b.Key, []);
            var rev = sold.Sum(x => x.SalePrice);
            var profit = sold.Sum(x => x.Profit);
            return new BuyoutProfitSeriesDto(b.Key, b.Label, costByBucket.GetValueOrDefault(b.Key, 0m), rev, profit);
        }).ToArray();

        var (page, pageSize) = NormalizePaging(filter);
        var paged = soldRows.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var resaleRevenue = soldRows.Sum(x => x.SalePrice);
        var resaleCost = soldRows.Sum(x => x.PurchasePrice);

        return Result.Success(new BuyoutProfitReportDto(
            from, to, groupBy,
            paidBuyouts.Sum(x => x.TotalPurchasePrice),
            paidBuyouts.Length,
            resaleRevenue,
            resaleCost,
            resaleRevenue - resaleCost,
            soldRows.Length,
            series,
            new PagedReportItems<BuyoutProfitDetailDto>(paged, page, pageSize, soldRows.Length)));
    }

    public async Task<Result<(byte[] Content, string FileName)>> ExportCsvAsync(string reportType, ReportFilterRequest filter, CancellationToken ct)
    {
        var normalizedType = (reportType ?? "pos").Trim().ToLowerInvariant();
        if (normalizedType is not ("pos" or "commission" or "buyout"))
            return Result.Failure<(byte[], string)>(ApplicationError.Validation("REPORTS.TYPE", "Loại báo cáo xuất CSV không hợp lệ."));

        if (!TryResolveWindow(filter, out var from, out var to, out _, out var startUtc, out var endUtc, out var error))
            return Result.Failure<(byte[], string)>(error!);

        var sb = new StringBuilder();

        switch (normalizedType)
        {
            case "pos":
                {
                    if (!TryParsePaymentMethod(filter.PaymentMethod, out var methodFilter))
                        return Result.Failure<(byte[], string)>(ApplicationError.Validation("REPORTS.PAYMENT_METHOD", "Phương thức thanh toán không hợp lệ."));

                    var rows = await db.FinancialTransactions.AsNoTracking()
                        .Where(x => x.Type == FinancialTransactionType.Income
                                    && x.SaleInvoiceId != null
                                    && x.Status == FinancialTransactionStatus.Success
                                    && x.SaleInvoice!.PaymentStatus == InvoicePaymentStatus.Paid
                                    && x.OccurredAt >= startUtc
                                    && x.OccurredAt < endUtc
                                    && (methodFilter == null || x.PaymentMethod == methodFilter.Value))
                        .OrderByDescending(x => x.OccurredAt)
                        .Select(x => new PosRevenueDetailDto(
                            x.Id,
                            x.SaleInvoiceId!.Value,
                            x.SaleInvoice!.InvoiceNumber,
                            x.OccurredAt,
                            x.PaymentMethod.ToString(),
                            x.Amount,
                            x.SaleInvoice.Lines.Count,
                            x.SaleInvoice.SoldByEmployee.Account.FullName))
                        .ToArrayAsync(ct);

                    sb.AppendLine("Ma hoa don,Thoi gian (UTC+7),Phuong thuc,So tien (VND),So san pham,Thu ngan");
                    foreach (var row in rows)
                    {
                        sb.AppendLine(string.Join(',',
                            Csv(row.InvoiceNumber),
                            Csv(FormatVnTime(row.OccurredAt)),
                            Csv(row.PaymentMethod),
                            row.Amount.ToString("0.##", CultureInfo.InvariantCulture),
                            row.ItemCount.ToString(CultureInfo.InvariantCulture),
                            Csv(row.CashierName ?? "")));
                    }
                    return Result.Success((WithUtf8Bom(sb.ToString()), $"bao-cao-doanh-thu-pos-{from:yyyyMMdd}-{to:yyyyMMdd}.csv"));
                }
            case "commission":
                {
                    var rows = await QueryConsignmentCommissionDetails(startUtc, endUtc).ToArrayAsync(ct);
                    sb.AppendLine("Ma lenh chi,Ma hop dong,Khach hang,Hoan tat luc (UTC+7),Doanh so ban (VND),Hoa hong (VND),Phi dich vu (VND),Thuc chi khach (VND)");
                    foreach (var row in rows)
                    {
                        sb.AppendLine(string.Join(',',
                            Csv(row.PayoutNumber),
                            Csv(row.ContractNumber),
                            Csv(row.CustomerName ?? ""),
                            Csv(FormatVnTime(row.CompletedAt)),
                            row.SaleProceedsAmount.ToString("0.##", CultureInfo.InvariantCulture),
                            row.CommissionAmount.ToString("0.##", CultureInfo.InvariantCulture),
                            row.ServiceFeeAmount.ToString("0.##", CultureInfo.InvariantCulture),
                            row.NetPayoutAmount.ToString("0.##", CultureInfo.InvariantCulture)));
                    }
                    return Result.Success((WithUtf8Bom(sb.ToString()), $"bao-cao-hoa-hong-ky-gui-{from:yyyyMMdd}-{to:yyyyMMdd}.csv"));
                }
            default:
                {
                    var rows = (await QuerySoldPaidBuyoutLines(startUtc, endUtc).ToArrayAsync(ct))
                        .OrderByDescending(x => x.SoldAt)
                        .ToArray();
                    sb.AppendLine("Barcode,San pham,Ma phieu thu mua,Ma hoa don,Ban luc (UTC+7),Gia thu mua (VND),Gia ban lai (VND),Loi nhuan (VND)");
                    foreach (var row in rows)
                    {
                        sb.AppendLine(string.Join(',',
                            Csv(row.Barcode),
                            Csv(row.ProductName),
                            Csv(row.BuyoutNumber ?? ""),
                            Csv(row.InvoiceNumber),
                            Csv(FormatVnTime(row.SoldAt)),
                            row.PurchasePrice.ToString("0.##", CultureInfo.InvariantCulture),
                            row.SalePrice.ToString("0.##", CultureInfo.InvariantCulture),
                            row.Profit.ToString("0.##", CultureInfo.InvariantCulture)));
                    }
                    return Result.Success((WithUtf8Bom(sb.ToString()), $"bao-cao-mua-dut-loi-nhuan-{from:yyyyMMdd}-{to:yyyyMMdd}.csv"));
                }
        }
    }

    private IQueryable<ConsignmentCommissionDetailDto> QueryConsignmentCommissionDetails(DateTimeOffset startUtc, DateTimeOffset endUtc) =>
        db.Payouts.AsNoTracking()
            .Where(x => x.Status == PayoutStatus.Completed && x.CompletedAt != null &&
                        x.CompletedAt >= startUtc && x.CompletedAt < endUtc)
            .OrderByDescending(x => x.CompletedAt)
            .Select(x => new ConsignmentCommissionDetailDto(
                x.Id,
                x.PayoutNumber,
                x.ConsignmentContract.ContractNumber,
                x.ConsignmentContract.CustomerNameSnapshot,
                x.CompletedAt!.Value,
                x.Details.Where(d => !d.IsReleased && d.Type == PayoutLineType.SaleProceeds).Sum(d => d.Amount),
                x.Details.Where(d => !d.IsReleased && d.Type == PayoutLineType.Commission).Sum(d => d.Amount < 0 ? -d.Amount : d.Amount),
                x.Details.Where(d => !d.IsReleased && d.Type == PayoutLineType.ServiceFee).Sum(d => d.Amount < 0 ? -d.Amount : d.Amount),
                x.TotalAmount));

    private IQueryable<BuyoutProfitDetailDto> QuerySoldPaidBuyoutLines(DateTimeOffset startUtc, DateTimeOffset endUtc) =>
        from line in db.SaleInvoiceLines.AsNoTracking()
        join tx in db.FinancialTransactions.AsNoTracking().Where(t =>
                t.SaleInvoiceId != null &&
                t.Type == FinancialTransactionType.Income &&
                t.Status == FinancialTransactionStatus.Success &&
                t.OccurredAt >= startUtc &&
                t.OccurredAt < endUtc)
            on line.SaleInvoiceId equals tx.SaleInvoiceId!.Value
        where line.Product.SourceType == ProductSourceType.Buyout
              && line.SaleInvoice.PaymentStatus == InvoicePaymentStatus.Paid
        select new BuyoutProfitDetailDto(
            line.ProductId,
            line.Product.Barcode,
            line.Product.Name,
            line.Product.Buyout!.BuyoutNumber,
            line.SaleInvoice.InvoiceNumber,
            tx.OccurredAt,
            line.Product.PurchasePrice ?? 0m,
            line.SalePrice,
            line.SalePrice - (line.Product.PurchasePrice ?? 0m));

    private bool TryResolveWindow(
        ReportFilterRequest filter,
        out DateOnly from,
        out DateOnly to,
        out string groupBy,
        out DateTimeOffset startUtc,
        out DateTimeOffset endUtc,
        out ApplicationError? error)
    {
        var todayVn = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(VietnamOffset).DateTime);
        to = filter.To ?? todayVn;
        from = filter.From ?? to.AddDays(-6);
        groupBy = (filter.GroupBy ?? "day").Trim().ToLowerInvariant();
        startUtc = default;
        endUtc = default;

        if (from > to)
        {
            error = ApplicationError.Validation("REPORTS.RANGE", "Ngày bắt đầu phải nhỏ hơn hoặc bằng ngày kết thúc.");
            return false;
        }
        if (to.DayNumber - from.DayNumber > 366)
        {
            error = ApplicationError.Validation("REPORTS.RANGE", "Khoảng thời gian báo cáo tối đa 366 ngày.");
            return false;
        }
        if (groupBy is not ("hour" or "day" or "week" or "month"))
        {
            error = ApplicationError.Validation("REPORTS.GROUP_BY", "Chu kỳ nhóm phải là hour, day, week hoặc month.");
            return false;
        }
        if (groupBy == "hour" && from != to)
            groupBy = "day";

        startUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), VietnamOffset).ToUniversalTime();
        endUtc = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), VietnamOffset).ToUniversalTime();
        error = null;
        return true;
    }

    private static bool TryParsePaymentMethod(string? raw, out PaymentMethod? method)
    {
        method = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (Enum.TryParse<PaymentMethod>(raw.Trim(), ignoreCase: true, out var parsed))
        {
            method = parsed;
            return true;
        }
        return false;
    }

    private static (int Page, int PageSize) NormalizePaging(ReportFilterRequest filter) =>
        (Math.Max(1, filter.Page), Math.Clamp(filter.PageSize, 1, 200));

    private static string FormatPaymentMethod(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Tiền mặt",
        PaymentMethod.VietQr => "VietQR",
        PaymentMethod.BankTransfer => "Chuyển khoản",
        _ => method.ToString()
    };

    private static string FormatVnTime(DateTimeOffset utc) =>
        utc.ToOffset(VietnamOffset).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string GetBucketKey(DateTimeOffset timestamp, string groupBy)
    {
        var vn = timestamp.ToOffset(VietnamOffset);
        return groupBy switch
        {
            "hour" => $"{(vn.Hour / 2) * 2:00}h",
            "week" => $"{ISOWeek.GetYear(vn.DateTime)}-W{ISOWeek.GetWeekOfYear(vn.DateTime):00}",
            "month" => $"{vn.Year:0000}-{vn.Month:00}",
            _ => vn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
    }

    private static IReadOnlyList<(string Key, string Label)> BuildBuckets(DateOnly from, DateOnly to, string groupBy)
    {
        if (groupBy == "hour")
        {
            return Enumerable.Range(0, 12).Select(i =>
            {
                var h = i * 2;
                var label = $"{h:00}h";
                return (label, label);
            }).ToArray();
        }

        var list = new List<(string Key, string Label)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var dt = d.ToDateTime(TimeOnly.MinValue);
            var (key, label) = groupBy switch
            {
                "week" => ($"{ISOWeek.GetYear(dt)}-W{ISOWeek.GetWeekOfYear(dt):00}", $"T{ISOWeek.GetWeekOfYear(dt):00}"),
                "month" => ($"{d.Year:0000}-{d.Month:00}", $"{d.Month:00}/{d.Year}"),
                _ => (d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), d.ToString("dd/MM", CultureInfo.InvariantCulture))
            };
            if (seen.Add(key)) list.Add((key, label));
        }
        return list;
    }

    private static string Csv(string value)
    {
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
        return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static byte[] WithUtf8Bom(string content)
    {
        var utf8 = Encoding.UTF8.GetBytes(content);
        var bom = Encoding.UTF8.GetPreamble();
        var result = new byte[bom.Length + utf8.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(utf8, 0, result, bom.Length, utf8.Length);
        return result;
    }
}
