using OCS.Api.Common;
using OCS.Api.Consts;

namespace OCS.Api.Interfaces.Services;

public sealed record QrPaymentAdapterRequest(
    Guid TransactionId,
    Guid SaleInvoiceId,
    string InvoiceNumber,
    decimal Amount,
    string PaymentReference,
    DateTimeOffset ExpiresAt);

public sealed record QrPaymentAdapterResult(
    string ProviderName,
    string PaymentReference,
    string TransferContent,
    string BankCode,
    string BankAccountNumber,
    string BankAccountHolderName,
    string QrCodeUrl,
    string QrPayload);

public sealed record QrPaymentProviderStatusResult(
    string ProviderName,
    string PaymentReference,
    FinancialTransactionStatus Status,
    string? BankTransactionReference = null,
    DateTimeOffset? PaidAt = null);

public interface IPaymentGatewayAdapter
{
    string ProviderName { get; }

    Task<Result<QrPaymentAdapterResult>> CreateQrPaymentAsync(
        QrPaymentAdapterRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<QrPaymentProviderStatusResult>> QueryPaymentStatusAsync(
        string paymentReference,
        CancellationToken cancellationToken = default);
}
