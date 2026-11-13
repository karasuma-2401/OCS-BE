using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Appointments;
using OCS.Api.Models.Notifications;

namespace OCS.Api.Services;

public sealed class AppointmentService(
    IUnitOfWork unitOfWork,
    INotificationService notificationService,
    AppDbContext dbContext,
    ILogger<AppointmentService> logger) : IAppointmentService
{
    public AppointmentService(
        IUnitOfWork unitOfWork,
        INotificationService notificationService)
        : this(unitOfWork, notificationService, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<AppointmentService>.Instance)
    {
    }

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SlotLocks = new();

    public async Task<Result<IReadOnlyList<TimeSlotDto>>> GetAvailableSlotsAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var today = GetCurrentVietnamDate();
        if (date < today)
        {
            return Result.Failure<IReadOnlyList<TimeSlotDto>>(
                ApplicationError.Validation("INVALID_DATE", "Không thể tra cứu khung giờ cho ngày trong quá khứ."));
        }

        var slots = await unitOfWork.AppointmentRepository.GetActiveTimeSlotsAsync(cancellationToken);
        var bookedCounts = await unitOfWork.AppointmentRepository.GetBookedCountsByDateAsync(date, cancellationToken);
        var currentTime = GetCurrentVietnamTime();

        var result = new List<TimeSlotDto>();
        foreach (var slot in slots)
        {
            var booked = bookedCounts.GetValueOrDefault(slot.Id, 0);
            var available = Math.Max(0, slot.MaxCustomers - booked);
            var isPastTime = date == today && slot.StartTime <= currentTime;

            result.Add(new TimeSlotDto
            {
                Id = slot.Id,
                StartTime = slot.StartTime.ToString("HH:mm"),
                EndTime = slot.EndTime.ToString("HH:mm"),
                MaxCustomers = slot.MaxCustomers,
                BookedCount = booked,
                AvailableCount = isPastTime ? 0 : available,
                IsAvailable = !isPastTime && available > 0
            });
        }

        return Result.Success<IReadOnlyList<TimeSlotDto>>(result);
    }

    public async Task<Result<AppointmentDto>> CreateAsync(
        Guid customerId,
        CreateAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var today = GetCurrentVietnamDate();
        if (request.AppointmentDate < today)
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.Validation("INVALID_DATE", "Không thể đặt lịch hẹn cho ngày trong quá khứ."));
        }

        if (request.AppointmentDate > today.AddDays(14))
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.Validation("DATE_OUT_OF_RANGE", "Chỉ có thể đặt lịch hẹn trước tối đa 14 ngày."));
        }

        if (request.EstimatedItemCount < 1 || request.EstimatedItemCount > 50)
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.Validation("INVALID_ITEM_COUNT", "Số lượng quần áo dự kiến phải từ 1 đến 50 món."));
        }

        var slot = await unitOfWork.AppointmentRepository.GetTimeSlotByIdAsync(request.TimeSlotId, cancellationToken);
        if (slot is null || !slot.IsActive)
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.NotFound("TIME_SLOT_NOT_FOUND", "Khung giờ hẹn không tồn tại hoặc đã ngừng áp dụng."));
        }

        if (request.AppointmentDate == today && slot.StartTime <= GetCurrentVietnamTime())
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.Validation("SLOT_EXPIRED", "Khung giờ này trong ngày hôm nay đã qua, vui lòng chọn khung giờ khác."));
        }

        var lockKey = $"{request.AppointmentDate:yyyy-MM-dd}_{request.TimeSlotId}";
        var slotLock = SlotLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        await slotLock.WaitAsync(cancellationToken);
        Appointment created;
        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);

            var currentBookings = await unitOfWork.AppointmentRepository.GetBookedCountAsync(
                request.AppointmentDate,
                request.TimeSlotId,
                cancellationToken);

            if (currentBookings >= slot.MaxCustomers)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return Result.Failure<AppointmentDto>(
                    ApplicationError.Conflict("SLOT_FULL", $"Khung giờ này đã đủ số lượng tiếp nhận ({slot.MaxCustomers}/{slot.MaxCustomers} khách). Vui lòng chọn khung giờ khác."));
            }

            var trackingNumber = $"APT-{DateTime.UtcNow:yyMMdd}-{Random.Shared.Next(1000, 9999)}";
            created = new Appointment
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                TimeSlotId = request.TimeSlotId,
                AppointmentDate = request.AppointmentDate,
                EstimatedItemCount = request.EstimatedItemCount,
                SubmissionMethod = request.SubmissionMethod,
                TrackingNumber = trackingNumber,
                Note = request.Note?.Trim(),
                Status = AppointmentStatus.Scheduled,
                CreatedAt = DateTimeOffset.UtcNow
            };

            unitOfWork.AppointmentRepository.Add(created);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await notificationService.CreateAsync(new CreateNotificationDto
            {
                RecipientAccountId = customerId,
                Title = "Đặt lịch hẹn ký gửi thành công",
                Content = $"Lịch hẹn ký gửi mã {created.TrackingNumber} đã được xác nhận vào ngày {created.AppointmentDate:dd/MM/yyyy} khung giờ {slot.StartTime:HH\\:mm} - {slot.EndTime:HH\\:mm}. Số lượng dự kiến: {created.EstimatedItemCount} món.",
                Type = NotificationType.System
            }, cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            slotLock.Release();
        }

        created.TimeSlot = slot;

        await NotifyStoreStaffAsync(
            customerId,
            "Có lịch hẹn ký gửi mới",
            customerDisplay => $"Khách hàng {customerDisplay} vừa đặt lịch hẹn ký gửi mã {created.TrackingNumber} vào ngày {created.AppointmentDate:dd/MM/yyyy} khung giờ {slot.StartTime:HH\\:mm} - {slot.EndTime:HH\\:mm} (Dự kiến {created.EstimatedItemCount} món).",
            cancellationToken);

        return Result.Success(MapToDto(created));
    }

    public async Task<Result<IReadOnlyList<AppointmentDto>>> GetCustomerHistoryAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var list = await unitOfWork.AppointmentRepository.GetByCustomerIdAsync(customerId, cancellationToken);
        var dtos = list.Select(MapToDto).ToList();
        return Result.Success<IReadOnlyList<AppointmentDto>>(dtos);
    }

    public async Task<Result<AppointmentDto>> GetByIdAsync(
        Guid id,
        Guid currentUserId,
        bool isStaff,
        CancellationToken cancellationToken = default)
    {
        var appointment = await unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(id, cancellationToken);
        if (appointment is null)
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.NotFound("APPOINTMENT_NOT_FOUND", "Không tìm thấy lịch hẹn."));
        }

        if (!isStaff && appointment.CustomerId != currentUserId)
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.Forbidden("FORBIDDEN", "Bạn không có quyền truy cập lịch hẹn này."));
        }

        return Result.Success(MapToDto(appointment));
    }

    public async Task<Result<AppointmentDto>> CancelByCustomerAsync(
        Guid id,
        Guid customerId,
        CancelAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var appointment = await unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(id, cancellationToken);
        if (appointment is null || appointment.CustomerId != customerId)
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.NotFound("APPOINTMENT_NOT_FOUND", "Không tìm thấy lịch hẹn hoặc bạn không có quyền hủy."));
        }

        if (appointment.Status != AppointmentStatus.Scheduled)
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.Conflict("CANNOT_CANCEL", $"Không thể hủy lịch hẹn đang ở trạng thái {appointment.Status}."));
        }

        var aptDateTime = appointment.AppointmentDate.ToDateTime(appointment.TimeSlot.StartTime);
        var currentDateTime = DateTime.UtcNow.AddHours(7);
        if (aptDateTime - currentDateTime < TimeSpan.FromHours(2))
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.Validation("CANCEL_TOO_LATE", "Chỉ có thể hủy lịch hẹn trước giờ bắt đầu tối thiểu 2 tiếng. Vui lòng liên hệ trực tiếp cửa hàng nếu cần hỗ trợ gấp."));
        }

        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancellationReason = string.IsNullOrWhiteSpace(request.Reason)
            ? "Khách hàng hủy trên ứng dụng."
            : request.Reason.Trim();
        appointment.UpdatedAt = DateTimeOffset.UtcNow;

        unitOfWork.AppointmentRepository.Update(appointment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notificationService.CreateAsync(new CreateNotificationDto
        {
            RecipientAccountId = customerId,
            Title = "Hủy lịch hẹn thành công",
            Content = $"Lịch hẹn ký gửi mã {appointment.TrackingNumber} ngày {appointment.AppointmentDate:dd/MM/yyyy} đã được hủy theo yêu cầu.",
            Type = NotificationType.System
        }, cancellationToken);

        await NotifyStoreStaffAsync(
            customerId,
            "Khách đã hủy lịch hẹn ký gửi",
            customerDisplay => $"Lịch hẹn ký gửi mã {appointment.TrackingNumber} ngày {appointment.AppointmentDate:dd/MM/yyyy} của khách {customerDisplay} đã bị hủy. Lý do: {appointment.CancellationReason}.",
            cancellationToken);

        return Result.Success(MapToDto(appointment));
    }

    public async Task<Result<IReadOnlyList<AppointmentDto>>> GetStaffAppointmentsAsync(
        DateOnly? date,
        Guid? timeSlotId,
        AppointmentStatus? status,
        string? searchKeyword,
        CancellationToken cancellationToken = default)
    {
        var list = await unitOfWork.AppointmentRepository.GetAppointmentsAsync(
            date,
            timeSlotId,
            status,
            searchKeyword,
            cancellationToken);

        var dtos = list.Select(MapToDto).ToList();
        return Result.Success<IReadOnlyList<AppointmentDto>>(dtos);
    }

    public async Task<Result<AppointmentDto>> UpdateStatusByStaffAsync(
        Guid id,
        UpdateAppointmentStatusRequest request,
        Guid staffId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(id, cancellationToken);
        if (appointment is null)
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.NotFound("APPOINTMENT_NOT_FOUND", "Không tìm thấy lịch hẹn."));
        }

        if (appointment.Status == AppointmentStatus.Completed)
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.Conflict("INVALID_TRANSITION", "Lịch hẹn đã hoàn tất tiếp nhận không thể thay đổi trạng thái."));
        }

        if (request.Status == AppointmentStatus.Completed)
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.Conflict("INVALID_TRANSITION", "Trạng thái hoàn tất được cập nhật tự động khi hoàn thành tiếp nhận ký gửi."));
        }

        if (request.Status == AppointmentStatus.CheckedIn &&
            appointment.Status != AppointmentStatus.Scheduled &&
            appointment.Status != AppointmentStatus.NoShow)
        {
            return Result.Failure<AppointmentDto>(
                ApplicationError.Conflict("INVALID_TRANSITION", $"Không thể xác nhận đã đến cho lịch hẹn đang ở trạng thái {appointment.Status}."));
        }

        appointment.Status = request.Status;
        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            appointment.CancellationReason = request.Reason.Trim();
        }
        appointment.UpdatedAt = DateTimeOffset.UtcNow;

        unitOfWork.AppointmentRepository.Update(appointment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var statusVi = GetStatusDisplayNameVi(request.Status);
        var statusMessage = request.Status switch
        {
            AppointmentStatus.CheckedIn => "Cửa hàng đã xác nhận quý khách đã có mặt tại quầy. Nhân viên đang chuẩn bị tiếp nhận đồ.",
            AppointmentStatus.NoShow => $"Lịch hẹn ký gửi ngày {appointment.AppointmentDate:dd/MM/yyyy} đã được ghi nhận: Không đến theo giờ hẹn.",
            AppointmentStatus.Cancelled => $"Lịch hẹn ký gửi ngày {appointment.AppointmentDate:dd/MM/yyyy} đã bị hủy bởi cửa hàng. Lý do: {request.Reason ?? "Không có"}.",
            AppointmentStatus.Completed => "Quá trình tiếp nhận và lập hợp đồng ký gửi cho lịch hẹn của quý khách đã hoàn tất.",
            AppointmentStatus.Scheduled => $"Lịch hẹn ký gửi ngày {appointment.AppointmentDate:dd/MM/yyyy} đã được chuyển về trạng thái: Đã đặt lịch.",
            _ => $"Trạng thái lịch hẹn ký gửi của bạn đã được cập nhật thành: {statusVi}."
        };

        try
        {
            await notificationService.CreateAsync(new CreateNotificationDto
            {
                RecipientAccountId = appointment.CustomerId,
                SenderAccountId = staffId != Guid.Empty ? staffId : null,
                Title = "Cập nhật trạng thái lịch hẹn",
                Content = statusMessage,
                Type = NotificationType.System
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send appointment status notification for appointment {AppointmentId}", appointment.Id);
        }

        return Result.Success(MapToDto(appointment));
    }

    public async Task<Result<StartReceptionResponse>> StartReceptionAsync(
        Guid id,
        Guid staffId,
        CancellationToken cancellationToken = default)
    {
        var appointment = await unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(id, cancellationToken);
        if (appointment is null)
        {
            return Result.Failure<StartReceptionResponse>(
                ApplicationError.NotFound("APPOINTMENT_NOT_FOUND", "Không tìm thấy lịch hẹn."));
        }

        if (appointment.Status != AppointmentStatus.Scheduled && appointment.Status != AppointmentStatus.CheckedIn)
        {
            return Result.Failure<StartReceptionResponse>(
                ApplicationError.Conflict("INVALID_STATUS", $"Lịch hẹn ở trạng thái '{appointment.Status}' không thể bắt đầu tiếp nhận."));
        }

        var customerName = appointment.Customer?.Account?.FullName ?? "Khách ký gửi";
        var customerPhone = appointment.Customer?.Account?.PhoneNumber ?? string.Empty;

        return Result.Success(new StartReceptionResponse
        {
            AppointmentId = appointment.Id,
            TrackingNumber = appointment.TrackingNumber ?? string.Empty,
            CustomerId = appointment.CustomerId,
            CustomerName = customerName,
            CustomerPhone = customerPhone,
            EstimatedItemCount = appointment.EstimatedItemCount,
            Note = appointment.Note,
            Message = "Lịch hẹn hợp lệ. Sẵn sàng lập hợp đồng tiếp nhận và kiểm định."
        });
    }

    private async Task NotifyStoreStaffAsync(
        Guid customerId,
        string title,
        Func<string, string> contentFactory,
        CancellationToken cancellationToken)
    {
        if (dbContext is null) return;

        try
        {
            var customerAccount = await dbContext.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == customerId, cancellationToken);
            var customerName = customerAccount?.FullName ?? "Khách hàng";
            var customerPhone = customerAccount?.PhoneNumber;
            var customerDisplay = !string.IsNullOrWhiteSpace(customerPhone)
                ? $"{customerName} ({customerPhone})"
                : customerName;

            var staffRoleIds = new[] { Role.StaffRoleId, Role.ManagerRoleId, Role.AdminRoleId };

            var roleStaffIds = await dbContext.AccountRoles
                .AsNoTracking()
                .Where(ar => staffRoleIds.Contains(ar.RoleId))
                .Select(ar => ar.AccountId)
                .ToListAsync(cancellationToken);

            var internalStaffIds = await dbContext.Accounts
                .AsNoTracking()
                .Where(a => a.Status == AccountStatus.Active &&
                            (a.AccountType == AccountType.Internal || a.EmployeeProfile != null))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);

            var recipientIds = roleStaffIds
                .Concat(internalStaffIds)
                .Distinct()
                .ToList();

            if (recipientIds.Count == 0)
            {
                recipientIds = await dbContext.Accounts
                    .AsNoTracking()
                    .Where(a => a.Status == AccountStatus.Active)
                    .Select(a => a.Id)
                    .ToListAsync(cancellationToken);
            }

            var content = contentFactory(customerDisplay);

            foreach (var staffAccountId in recipientIds)
            {
                await notificationService.CreateAsync(new CreateNotificationDto
                {
                    RecipientAccountId = staffAccountId,
                    SenderAccountId = customerId != staffAccountId ? customerId : null,
                    Title = title,
                    Content = content,
                    Type = NotificationType.System
                }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to notify store staff for customer appointment from customer {CustomerId}", customerId);
        }
    }

    private static string GetStatusDisplayNameVi(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Scheduled => "Đã đặt lịch",
        AppointmentStatus.CheckedIn => "Đã đến quầy",
        AppointmentStatus.Completed => "Đã tiếp nhận",
        AppointmentStatus.Cancelled => "Đã hủy",
        AppointmentStatus.NoShow => "Không đến",
        _ => status.ToString()
    };

    private static AppointmentDto MapToDto(Appointment a)
    {
        var startTime = a.TimeSlot != null ? a.TimeSlot.StartTime.ToString("HH:mm") : string.Empty;
        var endTime = a.TimeSlot != null ? a.TimeSlot.EndTime.ToString("HH:mm") : string.Empty;
        var slotDisplay = !string.IsNullOrEmpty(startTime) ? $"{startTime} - {endTime}" : string.Empty;
        var canCancel = a.Status == AppointmentStatus.Scheduled;

        return new AppointmentDto
        {
            Id = a.Id,
            TrackingNumber = a.TrackingNumber ?? string.Empty,
            CustomerId = a.CustomerId,
            CustomerName = a.Customer?.Account?.FullName ?? string.Empty,
            CustomerPhone = a.Customer?.Account?.PhoneNumber ?? string.Empty,
            AppointmentDate = a.AppointmentDate.ToString("yyyy-MM-dd"),
            TimeSlotId = a.TimeSlotId,
            TimeSlotDisplay = slotDisplay,
            StartTime = startTime,
            EndTime = endTime,
            EstimatedItemCount = a.EstimatedItemCount,
            SubmissionMethod = a.SubmissionMethod.ToString(),
            Status = a.Status.ToString(),
            Note = a.Note,
            CancellationReason = a.CancellationReason,
            ReminderSentAt = a.ReminderSentAt,
            CreatedAt = a.CreatedAt,
            CanCancel = canCancel
        };
    }

    private static DateOnly GetCurrentVietnamDate() =>
        DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    private static TimeOnly GetCurrentVietnamTime() =>
        TimeOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
}
