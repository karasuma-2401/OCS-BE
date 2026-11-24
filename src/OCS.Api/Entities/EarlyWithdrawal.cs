using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class EarlyWithdrawal : BaseEntity
{
    public Guid CustomerId { get; set; }

    public Guid? ProcessedByEmployeeId { get; set; }
    public string Status { get; set; } = "Pending";
    public string Reason { get; set; } = string.Empty;
    public string? DecisionNote { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public Guid? PaidByEmployeeId { get; set; }
    public DateTimeOffset? HandedOverAt { get; set; }
    public Guid? HandedOverByEmployeeId { get; set; }
    public string? HandoverEvidence { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();

    public decimal TotalPenaltyAmount { get; set; }

    public InvoicePaymentStatus PaymentStatus { get; set; } = InvoicePaymentStatus.Unpaid;

    public CustomerProfile Customer { get; set; } = null!;

    public EmployeeProfile? ProcessedByEmployee { get; set; }

    public ICollection<EarlyWithdrawalItem> Items { get; set; } = new List<EarlyWithdrawalItem>();
}
