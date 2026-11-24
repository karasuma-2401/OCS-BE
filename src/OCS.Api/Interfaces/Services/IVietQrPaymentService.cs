using OCS.Api.Common;
using OCS.Api.Models.Pos;

namespace OCS.Api.Interfaces.Services;

public interface IVietQrPaymentService
{
    Task<Result<WithdrawalVietQrSessionDto>> InitiateWithdrawalAsync(Guid withdrawalId, Guid actorAccountId, bool forceNew, CancellationToken ct = default);
    Task<Result<WithdrawalVietQrSessionDto>> GetWithdrawalStatusAsync(Guid withdrawalId, Guid transactionId, Guid actorAccountId, CancellationToken ct = default);
    Task<Result<VietQrPaymentSessionDto>> InitiateAsync(
        Guid actorAccountId,
        InitiateVietQrPaymentRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<VietQrPaymentSessionDto>> GetStatusAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<PosPendingInvoiceSummaryDto>>> GetPendingInvoicesAsync(
        CancellationToken cancellationToken = default);

    Task<Result<VietQrWebhookProcessResultDto>> ProcessWebhookAsync(
        VietQrWebhookPayloadDto payload,
        CancellationToken cancellationToken = default);

    Task<Result<VietQrPaymentSessionDto>> SimulateWebhookAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);
}
