using OCS.Domain.Enums;

namespace OCS.Domain.Entities;

/// <summary>A recorded product lifecycle event; services add a new record for each transition.</summary>
public sealed class ProductStatusHistory
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ProductId { get; init; }

    public ProductStatus? PreviousStatus { get; init; }

    public ProductStatus NewStatus { get; init; }

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    public Guid? ChangedByAccountId { get; init; }

    public string? Reason { get; init; }

    public Product Product { get; private set; } = null!;

    public Account? ChangedByAccount { get; private set; }
}
