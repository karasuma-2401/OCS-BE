using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class EmployeeProfile
{
    public Guid Id { get; set; }

    public string JobTitle { get; set; } = string.Empty;

    public DateOnly HiredOn { get; set; }

    public DateOnly? LeftOn { get; set; }

    public EmployeeStatus Status { get; set; }
        = EmployeeStatus.Working;

    public Account Account { get; set; } = null!;
}
