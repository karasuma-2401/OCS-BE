using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class Appointment : BaseEntity
{
    public Guid CustomerId { get; set; }

    public Guid TimeSlotId { get; set; }

    public DateOnly AppointmentDate { get; set; }

    public int EstimatedItemCount { get; set; }

    public SubmissionMethod SubmissionMethod { get; set; } = SubmissionMethod.InPerson;

    public string? TrackingNumber { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;

    public CustomerProfile Customer { get; set; } = null!;

    public AppointmentTimeSlot TimeSlot { get; set; } = null!;
}