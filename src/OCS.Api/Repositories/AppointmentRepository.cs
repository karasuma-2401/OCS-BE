using Microsoft.EntityFrameworkCore;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;

namespace OCS.Api.Repositories;

public sealed class AppointmentRepository(AppDbContext dbContext) : IAppointmentRepository
{
    public Task<Appointment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Appointments
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<Appointment?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Appointments
            .Include(a => a.TimeSlot)
            .Include(a => a.Customer)
                .ThenInclude(c => c.Account)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Appointment>> GetByCustomerIdAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var items = await dbContext.Appointments
            .Include(a => a.TimeSlot)
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.AppointmentDate)
            .ThenByDescending(a => a.TimeSlot.StartTime)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return items;
    }

    public async Task<IReadOnlyList<Appointment>> GetAppointmentsAsync(
        DateOnly? date,
        Guid? timeSlotId,
        AppointmentStatus? status,
        string? searchKeyword,
        DateOnly? fromDate,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Appointments
            .Include(a => a.TimeSlot)
            .Include(a => a.Customer)
                .ThenInclude(c => c.Account)
            .AsNoTracking()
            .AsQueryable();

        if (date.HasValue)
        {
            query = query.Where(a => a.AppointmentDate == date.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(a => a.AppointmentDate >= fromDate.Value);
        }

        if (timeSlotId.HasValue)
        {
            query = query.Where(a => a.TimeSlotId == timeSlotId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(a => a.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchKeyword))
        {
            var keyword = searchKeyword.Trim().ToLower();
            query = query.Where(a =>
                a.Customer.Account.FullName.ToLower().Contains(keyword) ||
                a.Customer.Account.PhoneNumber.Contains(keyword) ||
                (a.TrackingNumber != null && a.TrackingNumber.ToLower().Contains(keyword)));
        }

        return await query
            .OrderBy(a => a.AppointmentDate)
            .ThenBy(a => a.TimeSlot.StartTime)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AppointmentTimeSlot>> GetActiveTimeSlotsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.AppointmentTimeSlots
            .Where(s => s.IsActive)
            .OrderBy(s => s.StartTime)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public Task<AppointmentTimeSlot?> GetTimeSlotByIdAsync(Guid timeSlotId, CancellationToken cancellationToken = default) =>
        dbContext.AppointmentTimeSlots
            .SingleOrDefaultAsync(s => s.Id == timeSlotId, cancellationToken);

    public async Task<AppointmentTimeSlot?> GetTimeSlotForUpdateAsync(Guid timeSlotId, CancellationToken cancellationToken = default)
    {
        if (!dbContext.Database.IsNpgsql()) return await GetTimeSlotByIdAsync(timeSlotId, cancellationToken);
        var rows = await dbContext.AppointmentTimeSlots.FromSqlInterpolated($"SELECT * FROM \"AppointmentTimeSlots\" WHERE \"Id\" = {timeSlotId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<int> GetBookedCountAsync(DateOnly date, Guid timeSlotId, CancellationToken cancellationToken = default) =>
        dbContext.Appointments
            .CountAsync(a => a.AppointmentDate == date &&
                             a.TimeSlotId == timeSlotId &&
                             a.Status != AppointmentStatus.Cancelled &&
                             a.Status != AppointmentStatus.NoShow,
                        cancellationToken);

    public async Task<Dictionary<Guid, int>> GetBookedCountsByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var list = await dbContext.Appointments
            .Where(a => a.AppointmentDate == date &&
                        a.Status != AppointmentStatus.Cancelled &&
                        a.Status != AppointmentStatus.NoShow)
            .GroupBy(a => a.TimeSlotId)
            .Select(g => new { TimeSlotId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return list.ToDictionary(x => x.TimeSlotId, x => x.Count);
    }

    public async Task<IReadOnlyList<Appointment>> GetUpcomingAppointmentsForReminderAsync(
        DateOnly date,
        TimeOnly windowStart,
        TimeOnly windowEnd,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Appointments
            .Include(a => a.TimeSlot)
            .Include(a => a.Customer)
                .ThenInclude(c => c.Account)
            .Where(a => a.AppointmentDate == date &&
                        a.Status == AppointmentStatus.Scheduled &&
                        a.ReminderSentAt == null &&
                        a.TimeSlot.StartTime >= windowStart &&
                        a.TimeSlot.StartTime <= windowEnd)
            .ToListAsync(cancellationToken);
    }

    public void Add(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        dbContext.Appointments.Add(appointment);
    }

    public void Update(Appointment appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);
        dbContext.Appointments.Update(appointment);
    }
}
