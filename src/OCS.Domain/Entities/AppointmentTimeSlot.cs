using OCS.Domain.Common;

namespace OCS.Domain.Entities;

public sealed class AppointmentTimeSlot : BaseEntity
{
    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public int MaxCustomers { get; set; }

    public bool IsActive { get; set; } = true;
}