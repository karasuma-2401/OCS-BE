using OCS.Api.Consts;
using OCS.Api.Entities;

namespace OCS.Api.Interfaces.Repositories;

public interface IAppointmentRepository
{
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Appointment?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Appointment>> GetByCustomerIdAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Appointment>> GetAppointmentsAsync(
        DateOnly? date,
        Guid? timeSlotId,
        AppointmentStatus? status,
        string? searchKeyword,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppointmentTimeSlot>> GetActiveTimeSlotsAsync(CancellationToken cancellationToken = default);

    Task<AppointmentTimeSlot?> GetTimeSlotByIdAsync(Guid timeSlotId, CancellationToken cancellationToken = default);

    Task<int> GetBookedCountAsync(DateOnly date, Guid timeSlotId, CancellationToken cancellationToken = default);

    Task<Dictionary<Guid, int>> GetBookedCountsByDateAsync(DateOnly date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Appointment>> GetUpcomingAppointmentsForReminderAsync(
        DateOnly date,
        TimeOnly windowStart,
        TimeOnly windowEnd,
        CancellationToken cancellationToken = default);

    void Add(Appointment appointment);

    void Update(Appointment appointment);
}

