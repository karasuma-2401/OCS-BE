using Microsoft.Extensions.Logging;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Notifications;

namespace OCS.Api.Services;

public sealed partial class AppointmentReminderJob(
    IUnitOfWork unitOfWork,
    INotificationService notificationService,
    ILogger<AppointmentReminderJob> logger) : IAppointmentReminderJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow.AddHours(7); // Vietnam local time UTC+7
        var today = DateOnly.FromDateTime(now);
        var windowStart = TimeOnly.FromDateTime(now);
        var windowEnd = TimeOnly.FromDateTime(now.AddHours(2));

        LogJobTriggered(logger, now, windowStart, windowEnd);

        var upcomingAppointments = await unitOfWork.AppointmentRepository.GetUpcomingAppointmentsForReminderAsync(
            today,
            windowStart,
            windowEnd,
            cancellationToken);

        if (upcomingAppointments.Count == 0)
        {
            LogNoPendingAppointments(logger);
            return;
        }

        var sentCount = 0;
        foreach (var appointment in upcomingAppointments)
        {
            // Idempotency check: strictly skip if reminder was already sent
            if (appointment.ReminderSentAt.HasValue)
            {
                continue;
            }

            var startTimeStr = appointment.TimeSlot.StartTime.ToString("HH:mm");
            var endTimeStr = appointment.TimeSlot.EndTime.ToString("HH:mm");

            await notificationService.CreateAsync(new CreateNotificationDto
            {
                RecipientAccountId = appointment.CustomerId,
                Title = "Nhắc nhở: Lịch hẹn ký gửi sắp diễn ra",
                Content = $"Quý khách có lịch hẹn ký gửi mã {appointment.TrackingNumber} vào khung giờ {startTimeStr} - {endTimeStr} hôm nay tại cửa hàng OCS. Quý khách vui lòng chuẩn bị quần áo và đến đúng giờ để được nhân viên tiếp nhận tốt nhất.",
                Type = NotificationType.System
            }, cancellationToken);

            // Record sent timestamp in DB to guarantee no duplicate sending when job runs again
            appointment.ReminderSentAt = DateTimeOffset.UtcNow;
            appointment.UpdatedAt = DateTimeOffset.UtcNow;
            unitOfWork.AppointmentRepository.Update(appointment);
            sentCount++;
        }

        if (sentCount > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            LogRemindersSent(logger, sentCount);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "AppointmentReminderJob triggered at {Now} for appointments between {Start} and {End}")]
    private static partial void LogJobTriggered(ILogger logger, DateTime now, TimeOnly start, TimeOnly end);

    [LoggerMessage(Level = LogLevel.Information, Message = "No pending appointments need reminders at this time.")]
    private static partial void LogNoPendingAppointments(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully sent reminders for {SentCount} appointments.")]
    private static partial void LogRemindersSent(ILogger logger, int sentCount);
}
