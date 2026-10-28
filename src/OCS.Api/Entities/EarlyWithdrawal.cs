using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class EarlyWithdrawal : BaseEntity
{
    public Guid CustomerId { get; set; }

    public Guid ProcessedByEmployeeId { get; set; }

    public decimal TotalPenaltyAmount { get; set; }

    public InvoicePaymentStatus PaymentStatus { get; set; } = InvoicePaymentStatus.Unpaid;

    public CustomerProfile Customer { get; set; } = null!;

    public EmployeeProfile ProcessedByEmployee { get; set; } = null!;

    public ICollection<EarlyWithdrawalItem> Items { get; set; } = new List<EarlyWithdrawalItem>();
}
