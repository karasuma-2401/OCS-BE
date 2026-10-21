using OCS.Domain.Common;
using OCS.Domain.Enums;

namespace OCS.Domain.Entities;

public sealed class IncidentReport : BaseEntity
{
    public Guid ProductId { get; set; }

    public Guid CreatedByEmployeeId { get; set; }

    public Guid ApprovedByEmployeeId { get; set; }

    public IncidentType Type { get; set; }

    public decimal CompensationAmount { get; set; }

    public IncidentStatus Status { get; set; } = IncidentStatus.Pending;

    public Product Product { get; set; } = null!;

    public EmployeeProfile CreatedByEmployee { get; set; } = null!;

    public EmployeeProfile ApprovedByEmployee { get; set; } = null!;
}
