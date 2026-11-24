using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Pos;

namespace OCS.Api.Services;

public sealed partial class VietQrPaymentService
{
    public async Task<Result<WithdrawalVietQrSessionDto>> InitiateWithdrawalAsync(
        Guid withdrawalId, Guid actorAccountId, bool forceNew, CancellationToken ct = default)
    {
        if (!await IsWithdrawalCashierAsync(actorAccountId, ct))
            return Result.Failure<WithdrawalVietQrSessionDto>(ApplicationError.Forbidden("WITHDRAWAL.ACTOR", "Nhân viên không còn hoạt động."));
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var row = await db.EarlyWithdrawals.Include(w => w.Items).ThenInclude(i => i.Product)
            .SingleOrDefaultAsync(w => w.Id == withdrawalId, ct);
        if (row is null) return Result.Failure<WithdrawalVietQrSessionDto>(WithdrawalQrMissing());
        if (!CanCollectWithdrawal(row))
            return Result.Failure<WithdrawalVietQrSessionDto>(ApplicationError.Conflict("WITHDRAWAL.PAYMENT_STATE", "Yêu cầu phải được duyệt, còn giữ đủ hàng và chưa thu phí. Phiếu miễn phí không cần VietQR."));

        var now = timeProvider.GetUtcNow();
        var pending = await db.FinancialTransactions.Where(t => t.EarlyWithdrawalId == row.Id &&
            t.PaymentMethod == PaymentMethod.VietQr && t.Status == FinancialTransactionStatus.Pending)
            .OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        var active = !forceNew ? pending.FirstOrDefault(t => t.Amount == row.TotalPenaltyAmount && t.ExpiresAt > now &&
            !string.IsNullOrWhiteSpace(t.PaymentReference) && !string.IsNullOrWhiteSpace(t.QrCodeUrl)) : null;
        foreach (var old in pending.Where(t => t != active)) old.Status = FinancialTransactionStatus.Failed;
        if (active is null)
        {
            var reference = await GenerateUniqueReferenceAsync(options.Value.ReferencePrefix, now, ct);
            if (reference is null) return Result.Failure<WithdrawalVietQrSessionDto>(WithdrawalQrConflict());
            var expiry = now.AddMinutes(options.Value.ExpiryMinutes is > 0 and <= 120 ? options.Value.ExpiryMinutes : 10);
            var transactionId = Guid.NewGuid();
            // The existing adapter only uses the amount/reference/bank configuration to generate a QR.
            // No sale invoice is created: this transaction belongs exclusively to the withdrawal.
            var qr = await paymentGatewayAdapter.CreateQrPaymentAsync(new QrPaymentAdapterRequest(
                transactionId, Guid.Empty, WithdrawalNumber(row), row.TotalPenaltyAmount, reference, expiry), ct);
            if (!qr.IsSuccess) return Result.Failure<WithdrawalVietQrSessionDto>(qr.Error!);
            var data = qr.Data!;
            active = new FinancialTransaction
            {
                Id = transactionId, EarlyWithdrawalId = row.Id, CustomerId = row.CustomerId,
                Type = FinancialTransactionType.Income, PaymentMethod = PaymentMethod.VietQr,
                Amount = row.TotalPenaltyAmount, Status = FinancialTransactionStatus.Pending,
                PaymentReference = data.PaymentReference, QrCodeUrl = data.QrCodeUrl, QrPayload = data.QrPayload,
                ProviderName = data.ProviderName, BankCode = data.BankCode, BankAccountNumber = data.BankAccountNumber,
                BankAccountHolderName = data.BankAccountHolderName, ExpiresAt = expiry,
                CreatedAt = now, OccurredAt = now, PerformedByEmployeeId = actorAccountId
            };
            db.FinancialTransactions.Add(active);
        }
        // Serialize QR creation/rotation with another cashier, rejection and manual collection.
        row.Version = Guid.NewGuid();
        if (!await SaveWithdrawalPaymentAsync(tx, ct)) return Result.Failure<WithdrawalVietQrSessionDto>(WithdrawalQrConflict());
        return Result.Success(MapWithdrawalPayment(active, row, now));
    }

    public async Task<Result<WithdrawalVietQrSessionDto>> GetWithdrawalStatusAsync(
        Guid withdrawalId, Guid transactionId, Guid actorAccountId, CancellationToken ct = default)
    {
        if (!await IsWithdrawalCashierAsync(actorAccountId, ct))
            return Result.Failure<WithdrawalVietQrSessionDto>(ApplicationError.Forbidden("WITHDRAWAL.ACTOR", "Nhân viên không còn hoạt động."));
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var payment = await db.FinancialTransactions.Include(t => t.EarlyWithdrawal)
            .SingleOrDefaultAsync(t => t.Id == transactionId && t.EarlyWithdrawalId == withdrawalId && t.PaymentMethod == PaymentMethod.VietQr, ct);
        if (payment?.EarlyWithdrawal is not { } row) return Result.Failure<WithdrawalVietQrSessionDto>(WithdrawalQrMissing());
        var now = timeProvider.GetUtcNow();
        // QuickLink confirms receipt via authenticated webhook, never by opening/scanning the QR.
        if (payment.Status == FinancialTransactionStatus.Pending &&
            (payment.ExpiresAt <= now || row.Status != "Approved" || row.PaymentStatus == InvoicePaymentStatus.Paid))
        {
            payment.Status = FinancialTransactionStatus.Failed;
            row.Version = Guid.NewGuid();
            if (!await SaveWithdrawalPaymentAsync(tx, ct)) return Result.Failure<WithdrawalVietQrSessionDto>(WithdrawalQrConflict());
        }
        return Result.Success(MapWithdrawalPayment(payment, row, now));
    }

    private async Task<Result<VietQrWebhookProcessResultDto>> ReconcileWithdrawalWebhookAsync(
        FinancialTransaction payment, VietQrWebhookTransactionItemDto item, DateTimeOffset now, CancellationToken ct)
    {
        var row = payment.EarlyWithdrawal!;
        if (payment.Status == FinancialTransactionStatus.Success && row.PaymentStatus == InvoicePaymentStatus.Paid)
            return Result.Success(WithdrawalWebhookResult(payment, row, true));
        if (!CanCollectWithdrawal(row) || payment.Amount != row.TotalPenaltyAmount)
            return Result.Failure<VietQrWebhookProcessResultDto>(ApplicationError.Conflict("WITHDRAWAL.PAYMENT_RECONCILIATION",
                "Đã nhận thông báo chuyển khoản cho phiếu đã thu phí, kết thúc hoặc hàng không còn hợp lệ. Cần đối soát thủ công."));
        if ((item.TransferAmount ?? item.Amount ?? 0m) != payment.Amount)
            return Result.Failure<VietQrWebhookProcessResultDto>(ApplicationError.Validation("WITHDRAWAL.PAYMENT_AMOUNT",
                "Số tiền nhận được không khớp phí rút hàng. Cần đối soát thủ công."));
        var bankRef = FirstNonEmpty(item.BankTransactionReference, item.ReferenceCode, item.Tid, item.Id?.ToString());
        if (bankRef?.Length > 100)
            return Result.Failure<VietQrWebhookProcessResultDto>(ApplicationError.Validation("WITHDRAWAL.BANK_REFERENCE", "Mã giao dịch ngân hàng không hợp lệ."));
        if (bankRef is not null && await db.FinancialTransactions.AnyAsync(t => t.Id != payment.Id &&
            t.BankTransactionReference == bankRef && t.BankCode == payment.BankCode && t.Status == FinancialTransactionStatus.Success, ct))
            return Result.Failure<VietQrWebhookProcessResultDto>(ApplicationError.Conflict("WITHDRAWAL.BANK_REFERENCE", "Giao dịch ngân hàng đã được đối soát cho phiếu khác."));

        // A late callback for an expired/rotated QR may still settle this same unpaid, approved request.
        // The withdrawal concurrency token prevents a second payment from settling it again.
        payment.Status = FinancialTransactionStatus.Success;
        payment.BankTransactionReference = bankRef;
        payment.OccurredAt = now;
        row.PaymentStatus = InvoicePaymentStatus.Paid;
        row.PaidAt = now;
        row.PaidByEmployeeId = payment.PerformedByEmployeeId;
        row.Version = Guid.NewGuid();
        var others = await db.FinancialTransactions.Where(t => t.EarlyWithdrawalId == row.Id && t.Id != payment.Id &&
            t.PaymentMethod == PaymentMethod.VietQr && t.Status == FinancialTransactionStatus.Pending).ToListAsync(ct);
        foreach (var other in others) other.Status = FinancialTransactionStatus.Failed;
        return Result.Success(WithdrawalWebhookResult(payment, row, false));
    }

    private Task<bool> IsWithdrawalCashierAsync(Guid actor, CancellationToken ct) => db.EmployeeProfiles.AnyAsync(e =>
        e.Id == actor && e.Status == EmployeeStatus.Working && e.Account.Status == AccountStatus.Active && e.Account.AccountType == AccountType.Internal, ct);

    private static bool CanCollectWithdrawal(EarlyWithdrawal row) => row.Status == "Approved" &&
        row.PaymentStatus == InvoicePaymentStatus.Unpaid && !row.PaidAt.HasValue && row.TotalPenaltyAmount > 0 &&
        row.Items.Count > 0 && row.Items.All(i => i.Product.Status == ProductStatus.OnSale && i.Product.IsReservedForWithdrawal);

    private async Task<bool> SaveWithdrawalPaymentAsync(IDbContextTransaction? tx, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException ||
            ex is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation } ||
            ex is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation } })
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private static ApplicationError WithdrawalQrConflict() => ApplicationError.Conflict("WITHDRAWAL.PAYMENT_CONFLICT", "Dữ liệu thanh toán vừa thay đổi. Vui lòng kiểm tra lại yêu cầu.");
    private static ApplicationError WithdrawalQrMissing() => ApplicationError.NotFound("WITHDRAWAL.PAYMENT_NOT_FOUND", "Không tìm thấy giao dịch VietQR của yêu cầu này.");
    private static string WithdrawalNumber(EarlyWithdrawal row) => $"RS-{row.Id.ToString()[..8].ToUpperInvariant()}";
    private static VietQrWebhookProcessResultDto WithdrawalWebhookResult(FinancialTransaction payment, EarlyWithdrawal row, bool alreadyPaid) =>
        new(true, alreadyPaid, payment.PaymentReference!, payment.Id, null, null, null,
            alreadyPaid ? "Phí rút hàng đã được đối soát trước đó." : "Đã thu đủ phí rút hàng qua VietQR.", row.Id);

    private static WithdrawalVietQrSessionDto MapWithdrawalPayment(FinancialTransaction payment, EarlyWithdrawal row, DateTimeOffset now)
    {
        var expires = payment.ExpiresAt ?? payment.CreatedAt.AddMinutes(10);
        return new(payment.Id, row.Id, WithdrawalNumber(row), payment.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            payment.PaymentReference ?? "", payment.PaymentReference ?? "", payment.BankCode ?? "", payment.BankAccountNumber ?? "",
            payment.BankAccountHolderName ?? "", payment.QrCodeUrl ?? "", payment.QrPayload ?? "", payment.ProviderName ?? "",
            payment.Status.ToString(), row.PaymentStatus.ToString(), payment.Status != FinancialTransactionStatus.Success &&
                (expires <= now || payment.Status == FinancialTransactionStatus.Failed), payment.CreatedAt, expires,
            payment.Status == FinancialTransactionStatus.Pending && expires > now ? (int)Math.Ceiling((expires - now).TotalSeconds) : 0);
    }
}
