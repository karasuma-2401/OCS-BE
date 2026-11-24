using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class Payout : BaseEntity
{
    public string PayoutNumber { get; set; } = string.Empty;

    public Guid ConsignmentContractId { get; set; }

    public Guid? CreatedByEmployeeId { get; set; }

    public Guid? ApprovedByEmployeeId { get; set; }
    public Guid? RejectedByEmployeeId { get; set; }

    public decimal TotalAmount { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? BankCode { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankAccountHolder { get; set; }

    public DateTimeOffset? TransferredAt { get; set; }

    public DateTimeOffset? CustomerConfirmedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? DisputedAt { get; set; }
    public string? DisputeReason { get; set; }
    public PayoutDisputeStatus? DisputeStatus { get; set; }
    public DateTimeOffset? DisputeResolvedAt { get; set; }
    public Guid? DisputeResolvedByEmployeeId { get; set; }
    public string? DisputeResolutionNote { get; set; }

    public string? ProofUrl { get; set; }

    public string? BankTransactionReference { get; set; }

    public PayoutStatus Status { get; set; } = PayoutStatus.Pending;

    public ConsignmentContract ConsignmentContract { get; set; } = null!;

    public EmployeeProfile? CreatedByEmployee { get; set; }

    public EmployeeProfile? ApprovedByEmployee { get; set; }
    public EmployeeProfile? RejectedByEmployee { get; set; }
    public EmployeeProfile? DisputeResolvedByEmployee { get; set; }

    public ICollection<PayoutDetail> Details { get; set; } = new List<PayoutDetail>();
}
