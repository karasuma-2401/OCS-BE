using OCS.Domain.Common;
using OCS.Domain.Enums;

namespace OCS.Domain.Entities;

public sealed class Payout : BaseEntity
{
    public string PayoutNumber { get; set; } = string.Empty;

    public Guid ConsignmentContractId { get; set; }

    public Guid ApprovedByEmployeeId { get; set; }

    public decimal TotalAmount { get; set; }

    public string? ProofUrl { get; set; }

    public string? BankTransactionReference { get; set; }

    public PayoutStatus Status { get; set; } = PayoutStatus.Pending;

    public ConsignmentContract ConsignmentContract { get; set; } = null!;

    public EmployeeProfile ApprovedByEmployee { get; set; } = null!;

    public ICollection<PayoutDetail> Details { get; set; } = new List<PayoutDetail>();
}
