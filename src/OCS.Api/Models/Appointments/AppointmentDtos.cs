using System.ComponentModel.DataAnnotations;
using OCS.Api.Consts;

namespace OCS.Api.Models.Appointments;

public sealed class TimeSlotDto
{
    public Guid Id { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public int MaxCustomers { get; set; }
    public int BookedCount { get; set; }
    public int AvailableCount { get; set; }
    public bool IsAvailable { get; set; }
}

public sealed class CreateAppointmentRequest
{
    [Required]
    public DateOnly AppointmentDate { get; set; }

    [Required]
    public Guid TimeSlotId { get; set; }

    [Range(1, 50, ErrorMessage = "Số lượng đồ dự kiến phải từ 1 đến 50 món.")]
    public int EstimatedItemCount { get; set; }

    [MaxLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự.")]
    public string? Note { get; set; }

    public SubmissionMethod SubmissionMethod { get; set; } = SubmissionMethod.InPerson;
}

public sealed class CancelAppointmentRequest
{
    [MaxLength(500, ErrorMessage = "Lý do hủy không được vượt quá 500 ký tự.")]
    public string? Reason { get; set; }
}

public sealed class UpdateAppointmentStatusRequest
{
    [Required]
    public AppointmentStatus Status { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}

public sealed class AppointmentDto
{
    public Guid Id { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string AppointmentDate { get; set; } = string.Empty;
    public Guid TimeSlotId { get; set; }
    public string TimeSlotDisplay { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public int EstimatedItemCount { get; set; }
    public string SubmissionMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? CancellationReason { get; set; }
    public DateTimeOffset? ReminderSentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool CanCancel { get; set; }
}

public sealed class StartReceptionResponse
{
    public Guid AppointmentId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int EstimatedItemCount { get; set; }
    public string? Note { get; set; }
    public string Message { get; set; } = string.Empty;
}

