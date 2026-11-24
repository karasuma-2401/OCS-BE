using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class IncidentReport : BaseEntity
{
    public Guid ProductId { get; set; }

    public Guid CreatedByEmployeeId { get; set; }

    public Guid? ApprovedByEmployeeId { get; set; }

    public IncidentType Type { get; set; }

    public string Description { get; set; } = string.Empty;

    public string EvidenceUrls { get; set; } = "[]";

    public string? ReviewNote { get; set; }

    public decimal CompensationAmount { get; set; }

    public IncidentStatus Status { get; set; } = IncidentStatus.Pending;

    public DateTimeOffset? ApprovedAt { get; set; }

    public Product Product { get; set; } = null!;

    public EmployeeProfile CreatedByEmployee { get; set; } = null!;

    public EmployeeProfile? ApprovedByEmployee { get; set; }
}
