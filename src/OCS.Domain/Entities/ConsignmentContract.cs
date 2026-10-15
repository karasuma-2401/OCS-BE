using OCS.Domain.Common;
using OCS.Domain.Enums;

namespace OCS.Domain.Entities;

public sealed class ConsignmentContract : BaseEntity
{
    public string ContractNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public Guid? AppointmentId { get; set; }

    public Guid CreatedByEmployeeId { get; set; }

    public DateTimeOffset SignedAt { get; set; }

    public DateTimeOffset EndsAt { get; set; }

    public ExpiryHandling ExpiryHandling { get; set; }

    public ConsignmentContractStatus Status { get; set; } = ConsignmentContractStatus.Active;

    public CustomerProfile Customer { get; set; } = null!;

    public Appointment? Appointment { get; set; }

    public EmployeeProfile CreatedByEmployee { get; set; } = null!;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}