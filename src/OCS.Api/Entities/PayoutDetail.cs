using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class PayoutDetail : BaseEntity
{
    public Guid PayoutId { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? IncidentReportId { get; set; }

    public PayoutLineType Type { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public Payout Payout { get; set; } = null!;

    public Product? Product { get; set; }

    public IncidentReport? IncidentReport { get; set; }
}
