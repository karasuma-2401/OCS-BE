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

public sealed record VietQrWebhookTransactionItemDto(
    object? Id = null,
    string? Tid = null,
    string? Description = null,
    string? Content = null,
    decimal? Amount = null,
    decimal? TransferAmount = null,
    string? TransferType = null,
    string? Code = null,
    string? PaymentReference = null,
    string? ReferenceCode = null,
    string? BankTransactionReference = null,
    string? When = null,
    string? TransactionDate = null);

public sealed record VietQrWebhookPayloadDto(
    object? Id = null,
    string? Gateway = null,
    string? TransactionDate = null,
    string? AccountNumber = null,
    string? SubAccount = null,
    string? Code = null,
    string? Content = null,
    string? Description = null,
    string? TransferType = null,
    decimal? TransferAmount = null,
    decimal? Amount = null,
    string? PaymentReference = null,
    string? ReferenceCode = null,
    string? BankTransactionReference = null,
    IReadOnlyList<VietQrWebhookTransactionItemDto>? Data = null);

public sealed record VietQrWebhookProcessResultDto(
    bool Processed,
    bool AlreadyPaid,
    string PaymentReference,
    Guid? TransactionId,
    Guid? SaleInvoiceId,
    string? InvoiceNumber,
    string? InvoicePaymentStatus,
    string Message);
