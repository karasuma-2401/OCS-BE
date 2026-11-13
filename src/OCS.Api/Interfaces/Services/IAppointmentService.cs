using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Models.Appointments;

namespace OCS.Api.Interfaces.Services;

public interface IAppointmentService
{
    Task<Result<IReadOnlyList<TimeSlotDto>>> GetAvailableSlotsAsync(DateOnly appointmentDate, CancellationToken cancellationToken = default);

    Task<Result<AppointmentDto>> CreateAsync(Guid customerId, CreateAppointmentRequest request, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<AppointmentDto>>> GetCustomerHistoryAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<Result<AppointmentDto>> GetByIdAsync(Guid id, Guid currentUserId, bool isStaff, CancellationToken cancellationToken = default);

    Task<Result<AppointmentDto>> CancelByCustomerAsync(Guid id, Guid customerId, CancelAppointmentRequest request, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<AppointmentDto>>> GetStaffAppointmentsAsync(
        DateOnly? appointmentDate,
        Guid? timeSlotId,
        AppointmentStatus? status,
        string? searchKeyword,
        DateOnly? fromDate,
        CancellationToken cancellationToken = default);

    Task<Result<AppointmentDto>> UpdateStatusByStaffAsync(
        Guid id,
        UpdateAppointmentStatusRequest request,
        Guid staffId,
        CancellationToken cancellationToken = default);

    Task<Result<StartReceptionResponse>> StartReceptionAsync(Guid id, Guid staffId, CancellationToken cancellationToken = default);
}
