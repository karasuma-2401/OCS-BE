namespace OCS.Api.Models.Pos;

public sealed record InitiateVietQrPaymentRequest(
    Guid SaleInvoiceId,
    bool ForceNew = false);

public sealed record VietQrPaymentSessionDto(
    Guid TransactionId,
    Guid SaleInvoiceId,
    string InvoiceNumber,
    string Amount,
    string PaymentReference,
    string TransferContent,
    string BankCode,
    string BankAccountNumber,
    string BankAccountHolderName,
    string QrCodeUrl,
    string QrPayload,
    string ProviderName,
    string Status,
    string InvoicePaymentStatus,
    bool IsExpired,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds);

public sealed record PosPendingInvoiceSummaryDto(
    Guid SaleInvoiceId,
    string InvoiceNumber,
    string TotalAmount,
    string PaymentStatus,
    DateTimeOffset CreatedAt,
    Guid? ActiveTransactionId,
    string? ActivePaymentReference,
    DateTimeOffset? ActiveExpiresAt);
