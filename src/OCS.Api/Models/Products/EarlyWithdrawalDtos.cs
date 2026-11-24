using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Products;

public sealed record CreateEarlyWithdrawalRequest(
    [property: Required, MinLength(1), MaxLength(100)] Guid[] ProductIds,
    [property: Required, MaxLength(1000)] string Reason);
public sealed record WithdrawalDecisionRequest([property: Required, MaxLength(1000)] string Note);
public sealed record WithdrawalPaymentRequest(decimal Amount, string PaymentMethod,
    [property: MaxLength(64)] string? Reference);
public sealed record WithdrawalHandoverRequest([property: Required, MaxLength(1000)] string Evidence);
public sealed record EarlyWithdrawalItemDto(Guid ProductId, string Name, Guid? ContractId,
    decimal ListedPrice, decimal PenaltyRate, decimal PenaltyAmount, string? ContractNumber = null);
public sealed record EarlyWithdrawalDto(Guid Id, Guid CustomerId, string Status, string Reason,
    decimal TotalPenaltyAmount, string PaymentStatus, DateTimeOffset RequestedAt,
    Guid? ProcessedByEmployeeId, DateTimeOffset? ApprovedAt, string? DecisionNote,
    DateTimeOffset? PaidAt, Guid? PaidByEmployeeId, DateTimeOffset? HandedOverAt,
    Guid? HandedOverByEmployeeId, string? HandoverEvidence, IReadOnlyList<EarlyWithdrawalItemDto> Items,
    string? CustomerName = null, string? CustomerPhone = null);
public sealed record WithdrawalEligibleProductDto(Guid ProductId, string Name, string Barcode,
    Guid ContractId, string ContractNumber, DateTimeOffset EndsAt, decimal ListedPrice,
    decimal PenaltyRate, decimal PenaltyAmount);
