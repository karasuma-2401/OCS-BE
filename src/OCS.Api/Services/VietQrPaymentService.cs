using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OCS.Api.Common;
using OCS.Api.Configuration;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Pos;

namespace OCS.Api.Services;

public sealed class VietQrPaymentService(
    AppDbContext db,
    IPaymentGatewayAdapter paymentGatewayAdapter,
    IOptions<VietQrOptions> options,
    TimeProvider timeProvider) : IVietQrPaymentService
{
    private const string ReferenceAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public async Task<Result<VietQrPaymentSessionDto>> InitiateAsync(
        Guid actorAccountId,
        InitiateVietQrPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.SaleInvoiceId == Guid.Empty)
        {
            return Result.Failure<VietQrPaymentSessionDto>(
                ApplicationError.Validation("POS.INVALID_INVOICE_ID", "Mã hóa đơn không hợp lệ."));
        }

        var invoice = await db.SaleInvoices
            .SingleOrDefaultAsync(x => x.Id == request.SaleInvoiceId, cancellationToken);

        if (invoice is null)
        {
            return Result.Failure<VietQrPaymentSessionDto>(
                ApplicationError.NotFound("POS.INVOICE_NOT_FOUND", "Không tìm thấy hóa đơn bán lẻ."));
        }

        if (invoice.PaymentStatus == InvoicePaymentStatus.Paid)
        {
            return Result.Failure<VietQrPaymentSessionDto>(
                ApplicationError.Conflict("POS.INVOICE_ALREADY_PAID", "Hóa đơn này đã được thanh toán hoàn tất."));
        }

        if (invoice.TotalAmount <= 0 || decimal.Round(invoice.TotalAmount, 2) != invoice.TotalAmount)
        {
            return Result.Failure<VietQrPaymentSessionDto>(
                ApplicationError.Validation("POS.INVALID_INVOICE_AMOUNT", "Tổng tiền hóa đơn phải lớn hơn 0 để tạo mã VietQR."));
        }

        var now = timeProvider.GetUtcNow();

        var pendingTransactions = await db.FinancialTransactions
            .Where(x => x.SaleInvoiceId == invoice.Id
                        && x.PaymentMethod == PaymentMethod.VietQr
                        && x.Status == FinancialTransactionStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        FinancialTransaction? activePending = null;
        var hasExpiredOrSuperseded = false;

        foreach (var pending in pendingTransactions)
        {
            var isStillValid = !request.ForceNew
                && activePending is null
                && pending.Amount == invoice.TotalAmount
                && pending.ExpiresAt.HasValue
                && pending.ExpiresAt.Value > now
                && !string.IsNullOrWhiteSpace(pending.PaymentReference)
                && !string.IsNullOrWhiteSpace(pending.QrCodeUrl);

            if (isStillValid)
            {
                activePending = pending;
            }
            else
            {
                pending.Status = FinancialTransactionStatus.Failed;
                hasExpiredOrSuperseded = true;
            }
        }

        if (activePending is not null)
        {
            if (hasExpiredOrSuperseded)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return Result.Success(Map(activePending, invoice, now));
        }

        var performedByEmployeeId = invoice.SoldByEmployeeId;
        if (actorAccountId != Guid.Empty
            && await db.EmployeeProfiles.AnyAsync(x => x.Id == actorAccountId, cancellationToken))
        {
            performedByEmployeeId = actorAccountId;
        }

        var cfg = options.Value;
        var expiryMinutes = cfg.ExpiryMinutes is > 0 and <= 120 ? cfg.ExpiryMinutes : 10;
        var expiresAt = now.AddMinutes(expiryMinutes);

        var paymentReference = await GenerateUniqueReferenceAsync(cfg.ReferencePrefix, now, cancellationToken);
        if (paymentReference is null)
        {
            return Result.Failure<VietQrPaymentSessionDto>(
                ApplicationError.Conflict("POS.REFERENCE_GENERATION_FAILED", "Không thể sinh mã tham chiếu thanh toán duy nhất. Vui lòng thử lại."));
        }

        var transactionId = Guid.NewGuid();
        var adapterResult = await paymentGatewayAdapter.CreateQrPaymentAsync(
            new QrPaymentAdapterRequest(
                TransactionId: transactionId,
                SaleInvoiceId: invoice.Id,
                InvoiceNumber: invoice.InvoiceNumber,
                Amount: invoice.TotalAmount,
                PaymentReference: paymentReference,
                ExpiresAt: expiresAt),
            cancellationToken);

        if (!adapterResult.IsSuccess)
        {
            return Result.Failure<VietQrPaymentSessionDto>(adapterResult.Error!);
        }

        var qrData = adapterResult.Data!;
        var transaction = new FinancialTransaction
        {
            Id = transactionId,
            Type = FinancialTransactionType.Income,
            SaleInvoiceId = invoice.Id,
            Amount = invoice.TotalAmount,
            PaymentMethod = PaymentMethod.VietQr,
            PaymentReference = qrData.PaymentReference,
            ExpiresAt = expiresAt,
            QrCodeUrl = qrData.QrCodeUrl,
            QrPayload = qrData.QrPayload,
            ProviderName = qrData.ProviderName,
            BankCode = qrData.BankCode,
            BankAccountNumber = qrData.BankAccountNumber,
            BankAccountHolderName = qrData.BankAccountHolderName,
            Status = FinancialTransactionStatus.Pending,
            OccurredAt = now,
            CreatedAt = now,
            PerformedByEmployeeId = performedByEmployeeId
        };

        db.FinancialTransactions.Add(transaction);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Result.Failure<VietQrPaymentSessionDto>(
                ApplicationError.Conflict("POS.REFERENCE_CONFLICT", "Mã tham chiếu giao dịch bị trùng lặp. Vui lòng thử lại."));
        }

        return Result.Success(Map(transaction, invoice, now));
    }

    public async Task<Result<VietQrPaymentSessionDto>> GetStatusAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        if (transactionId == Guid.Empty)
        {
            return Result.Failure<VietQrPaymentSessionDto>(
                ApplicationError.Validation("POS.INVALID_TRANSACTION_ID", "Mã giao dịch thanh toán không hợp lệ."));
        }

        var transaction = await db.FinancialTransactions
            .Include(x => x.SaleInvoice)
            .SingleOrDefaultAsync(
                x => x.Id == transactionId && x.PaymentMethod == PaymentMethod.VietQr,
                cancellationToken);

        if (transaction is null || transaction.SaleInvoice is null)
        {
            return Result.Failure<VietQrPaymentSessionDto>(
                ApplicationError.NotFound("POS.PAYMENT_NOT_FOUND", "Không tìm thấy giao dịch thanh toán VietQR."));
        }

        var now = timeProvider.GetUtcNow();
        var invoice = transaction.SaleInvoice;
        var hasChanges = false;

        if (transaction.Status == FinancialTransactionStatus.Pending)
        {
            if (!string.IsNullOrWhiteSpace(transaction.PaymentReference))
            {
                var providerResult = await paymentGatewayAdapter.QueryPaymentStatusAsync(
                    transaction.PaymentReference,
                    cancellationToken);

                if (providerResult.IsSuccess && providerResult.Data is { } providerStatus)
                {
                    if (providerStatus.Status == FinancialTransactionStatus.Success)
                    {
                        transaction.Status = FinancialTransactionStatus.Success;
                        if (!string.IsNullOrWhiteSpace(providerStatus.BankTransactionReference))
                        {
                            transaction.BankTransactionReference = providerStatus.BankTransactionReference;
                        }
                        invoice.PaymentStatus = InvoicePaymentStatus.Paid;
                        hasChanges = true;
                    }
                    else if (providerStatus.Status == FinancialTransactionStatus.Failed)
                    {
                        transaction.Status = FinancialTransactionStatus.Failed;
                        hasChanges = true;
                    }
                }
            }

            if (transaction.Status == FinancialTransactionStatus.Pending
                && transaction.ExpiresAt.HasValue
                && now >= transaction.ExpiresAt.Value)
            {
                transaction.Status = FinancialTransactionStatus.Failed;
                hasChanges = true;
            }
        }

        if (hasChanges)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(Map(transaction, invoice, now));
    }

    public async Task<Result<IReadOnlyList<PosPendingInvoiceSummaryDto>>> GetPendingInvoicesAsync(
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var unpaidInvoices = await db.SaleInvoices
            .AsNoTracking()
            .Where(x => x.PaymentStatus != InvoicePaymentStatus.Paid && x.TotalAmount > 0)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        var invoiceIds = unpaidInvoices.Select(x => x.Id).ToArray();
        var pendingTxByInvoice = invoiceIds.Length == 0
            ? new Dictionary<Guid, FinancialTransaction>()
            : (await db.FinancialTransactions
                .AsNoTracking()
                .Where(x => x.SaleInvoiceId.HasValue
                            && invoiceIds.Contains(x.SaleInvoiceId.Value)
                            && x.PaymentMethod == PaymentMethod.VietQr
                            && x.Status == FinancialTransactionStatus.Pending
                            && x.ExpiresAt.HasValue
                            && x.ExpiresAt.Value > now)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(cancellationToken))
                .GroupBy(x => x.SaleInvoiceId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

        var items = unpaidInvoices.Select(inv =>
        {
            pendingTxByInvoice.TryGetValue(inv.Id, out var activeTx);
            return new PosPendingInvoiceSummaryDto(
                SaleInvoiceId: inv.Id,
                InvoiceNumber: inv.InvoiceNumber,
                TotalAmount: inv.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture),
                PaymentStatus: inv.PaymentStatus.ToString(),
                CreatedAt: inv.CreatedAt,
                ActiveTransactionId: activeTx?.Id,
                ActivePaymentReference: activeTx?.PaymentReference,
                ActiveExpiresAt: activeTx?.ExpiresAt);
        }).ToArray();

        return Result.Success<IReadOnlyList<PosPendingInvoiceSummaryDto>>(items);
    }

    private async Task<string?> GenerateUniqueReferenceAsync(
        string? rawPrefix,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var prefix = VietQrQuickLinkAdapter.NormalizeTransferReference(rawPrefix);
        if (string.IsNullOrWhiteSpace(prefix))
        {
            prefix = "OCS";
        }
        else if (prefix.Length > 6)
        {
            prefix = prefix[..6];
        }

        var datePart = now.ToString("yyMMdd", CultureInfo.InvariantCulture);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var randomPart = GenerateRandomCode(6);
            var candidate = $"{prefix}{datePart}{randomPart}";

            var exists = await db.FinancialTransactions
                .AnyAsync(x => x.PaymentReference == candidate, cancellationToken);

            if (!exists)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string GenerateRandomCode(int length)
    {
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = ReferenceAlphabet[RandomNumberGenerator.GetInt32(ReferenceAlphabet.Length)];
        }
        return new string(chars);
    }

    private static VietQrPaymentSessionDto Map(
        FinancialTransaction transaction,
        SaleInvoice invoice,
        DateTimeOffset now)
    {
        var expiresAt = transaction.ExpiresAt ?? transaction.CreatedAt.AddMinutes(10);
        var remainingSeconds = transaction.Status == FinancialTransactionStatus.Pending && expiresAt > now
            ? (int)Math.Ceiling((expiresAt - now).TotalSeconds)
            : 0;
        var isExpired = transaction.Status != FinancialTransactionStatus.Success
            && (expiresAt <= now || transaction.Status == FinancialTransactionStatus.Failed);
        var reference = transaction.PaymentReference ?? string.Empty;

        return new VietQrPaymentSessionDto(
            TransactionId: transaction.Id,
            SaleInvoiceId: invoice.Id,
            InvoiceNumber: invoice.InvoiceNumber,
            Amount: transaction.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            PaymentReference: reference,
            TransferContent: reference,
            BankCode: transaction.BankCode ?? string.Empty,
            BankAccountNumber: transaction.BankAccountNumber ?? string.Empty,
            BankAccountHolderName: transaction.BankAccountHolderName ?? string.Empty,
            QrCodeUrl: transaction.QrCodeUrl ?? string.Empty,
            QrPayload: transaction.QrPayload ?? string.Empty,
            ProviderName: transaction.ProviderName ?? VietQrQuickLinkAdapter.AdapterProviderName,
            Status: transaction.Status.ToString(),
            InvoicePaymentStatus: invoice.PaymentStatus.ToString(),
            IsExpired: isExpired,
            CreatedAt: transaction.CreatedAt,
            ExpiresAt: expiresAt,
            RemainingSeconds: remainingSeconds);
    }
}
