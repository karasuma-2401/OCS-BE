using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Appointments;

namespace OCS.Api.Services;

public sealed class ManagedTimeSlotService(AppDbContext db) : IManagedTimeSlotService
{
    public async Task<IReadOnlyList<ManagedTimeSlotResponse>> ListAsync(CancellationToken cancellationToken) =>
        await db.AppointmentTimeSlots.AsNoTracking().OrderBy(x => x.StartTime).ThenBy(x => x.EndTime)
            .Select(x => new ManagedTimeSlotResponse(x.Id, x.StartTime, x.EndTime, x.MaxCustomers, x.IsActive, x.UpdatedAt,
                db.Appointments.Any(a => a.TimeSlotId == x.Id))).ToListAsync(cancellationToken);

    public async Task<ManagedTimeSlotResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.AppointmentTimeSlots.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new ManagedTimeSlotResponse(x.Id, x.StartTime, x.EndTime, x.MaxCustomers, x.IsActive, x.UpdatedAt,
                db.Appointments.Any(a => a.TimeSlotId == x.Id))).SingleOrDefaultAsync(cancellationToken);

    public Task<Result<ManagedTimeSlotResponse>> CreateAsync(SaveManagedTimeSlotRequest request, CancellationToken cancellationToken) => SaveAsync(null, request, cancellationToken);
    public Task<Result<ManagedTimeSlotResponse>> UpdateAsync(Guid id, SaveManagedTimeSlotRequest request, CancellationToken cancellationToken) => SaveAsync(id, request, cancellationToken);

    private async Task<Result<ManagedTimeSlotResponse>> SaveAsync(Guid? id, SaveManagedTimeSlotRequest request, CancellationToken cancellationToken)
    {
        if (request.EndTime <= request.StartTime || request.MaxCustomers is < 1 or > 200 ||
            request.StartTime.Second != 0 || request.EndTime.Second != 0 || request.StartTime.Millisecond != 0 || request.EndTime.Millisecond != 0)
            return Result.Failure<ManagedTimeSlotResponse>(ApplicationError.Validation("TIME_SLOT.INVALID", "Giờ kết thúc phải sau giờ bắt đầu; số khách từ 1 đến 200."));
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(72648531)", cancellationToken);
        AppointmentTimeSlot slot;
        var hasAppointments = false;
        if (id.HasValue)
        {
            if (db.Database.IsNpgsql())
                await db.AppointmentTimeSlots.FromSqlInterpolated($"SELECT * FROM \"AppointmentTimeSlots\" WHERE \"Id\" = {id.Value} FOR UPDATE").AsNoTracking().ToListAsync(cancellationToken);
            var existing = await db.AppointmentTimeSlots.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (existing is null) return Result.Failure<ManagedTimeSlotResponse>(ApplicationError.NotFound("TIME_SLOT.NOT_FOUND", "Không tìm thấy khung giờ."));
            slot = existing;
            if (slot.UpdatedAt != request.ExpectedUpdatedAt)
                return Result.Failure<ManagedTimeSlotResponse>(ApplicationError.Conflict("TIME_SLOT.CHANGED", "Khung giờ đã thay đổi. Vui lòng tải lại trước khi lưu."));
            hasAppointments = await db.Appointments.AnyAsync(x => x.TimeSlotId == slot.Id, cancellationToken);
            if (hasAppointments && (slot.StartTime != request.StartTime || slot.EndTime != request.EndTime))
                return Result.Failure<ManagedTimeSlotResponse>(ApplicationError.Conflict("TIME_SLOT.HAS_APPOINTMENTS", "Khung giờ đã có lịch hẹn nên không thể đổi giờ. Hãy ngừng áp dụng và tạo khung giờ mới."));
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            var largestBooking = await db.Appointments.Where(a => a.TimeSlotId == slot.Id && a.AppointmentDate >= today && a.Status != AppointmentStatus.Cancelled && a.Status != AppointmentStatus.NoShow)
                .GroupBy(a => a.AppointmentDate).Select(group => group.Count()).OrderByDescending(count => count).FirstOrDefaultAsync(cancellationToken);
            if (request.MaxCustomers < largestBooking)
                return Result.Failure<ManagedTimeSlotResponse>(ApplicationError.Conflict("TIME_SLOT.CAPACITY", $"Có ngày đã đặt {largestBooking} chỗ. Số khách tối đa không được nhỏ hơn con số này."));
        }
        else slot = new AppointmentTimeSlot();
        if (await db.AppointmentTimeSlots.AnyAsync(x => x.Id != slot.Id && x.StartTime == request.StartTime && x.EndTime == request.EndTime, cancellationToken))
            return Result.Failure<ManagedTimeSlotResponse>(ApplicationError.Conflict("TIME_SLOT.DUPLICATE", "Khung giờ này đã tồn tại."));
        if (request.IsActive && await db.AppointmentTimeSlots.AnyAsync(x => x.Id != slot.Id && x.IsActive && x.StartTime < request.EndTime && request.StartTime < x.EndTime, cancellationToken))
            return Result.Failure<ManagedTimeSlotResponse>(ApplicationError.Conflict("TIME_SLOT.OVERLAP", "Khung giờ đang hoạt động bị trùng với một khung giờ khác."));
        slot.StartTime = request.StartTime; slot.EndTime = request.EndTime; slot.MaxCustomers = request.MaxCustomers; slot.IsActive = request.IsActive;
        if (id.HasValue) slot.UpdatedAt = DateTimeOffset.UtcNow; else db.AppointmentTimeSlots.Add(slot);
        try { await db.SaveChangesAsync(cancellationToken); if (transaction is not null) await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Result.Failure<ManagedTimeSlotResponse>(ApplicationError.Conflict("TIME_SLOT.DUPLICATE", "Khung giờ này đã tồn tại.")); }
        return Result.Success(new ManagedTimeSlotResponse(slot.Id, slot.StartTime, slot.EndTime, slot.MaxCustomers, slot.IsActive, slot.UpdatedAt, hasAppointments));
    }
}
