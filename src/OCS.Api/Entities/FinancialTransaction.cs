using OCS.Api.Entities;
using OCS.Api.Consts;

namespace OCS.Api.Entities;

public sealed class FinancialTransaction : BaseEntity
{
    public Guid? CustomerId { get; set; }

    public FinancialTransactionType Type { get; set; }

    public Guid? SaleInvoiceId { get; set; }

    public Guid? PayoutId { get; set; }

    public Guid? BuyoutId { get; set; }

    public Guid? EarlyWithdrawalId { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public string? PaymentReference { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public string? QrCodeUrl { get; set; }

    public string? QrPayload { get; set; }

    public string? ProviderName { get; set; }

    public string? BankCode { get; set; }

    public string? BankAccountNumber { get; set; }

    public string? BankAccountHolderName { get; set; }

    public string? BankTransactionReference { get; set; }

    public string? ProofUrl { get; set; }

    public FinancialTransactionStatus Status { get; set; } = FinancialTransactionStatus.Pending;

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid PerformedByEmployeeId { get; set; }

    public CustomerProfile? Customer { get; set; }

    public SaleInvoice? SaleInvoice { get; set; }

    public Payout? Payout { get; set; }

    public Buyout? Buyout { get; set; }

    public EarlyWithdrawal? EarlyWithdrawal { get; set; }

    public EmployeeProfile PerformedByEmployee { get; set; } = null!;
}
