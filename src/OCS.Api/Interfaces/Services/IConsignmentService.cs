using OCS.Api.Common;
using OCS.Api.Models.Consignments;

namespace OCS.Api.Interfaces.Services;

public interface IConsignmentService
{
    Task<Result<ReceptionContractResponse>> CreateReceptionAsync(
        Guid staffId,
        CreateReceptionRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ConsignmentContractSummaryResponse>>> GetContractsAsync(
        CancellationToken cancellationToken = default);

    Task<Result<int>> GetCustomerItemHistoryCountAsync(
        Guid? customerId,
        string? phoneNumber,
        CancellationToken cancellationToken = default);

    Task<Result<ReceptionDraftResponse>> SaveDraftAsync(
        Guid staffId,
        SaveReceptionDraftRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ReceptionDraftResponse?>> GetDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default);

    Task<Result<ReceptionDraftResponse?>> GetDraftByAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    Task<Result<ReceptionDraftResponse?>> GetLatestWalkInDraftAsync(
        Guid staffId,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> DeleteDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> DeleteDraftByAppointmentAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> DeleteWalkInDraftAsync(
        Guid staffId,
        CancellationToken cancellationToken = default);
}

