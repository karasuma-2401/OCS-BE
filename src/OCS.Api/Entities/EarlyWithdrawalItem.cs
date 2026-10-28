namespace OCS.Api.Entities;

public sealed class EarlyWithdrawalItem
{
    public Guid EarlyWithdrawalId { get; set; }

    public Guid ProductId { get; set; }

    public decimal ListedPrice { get; set; }

    public decimal PenaltyAmount { get; set; }

    public EarlyWithdrawal EarlyWithdrawal { get; set; } = null!;

    public Product Product { get; set; } = null!;
}
