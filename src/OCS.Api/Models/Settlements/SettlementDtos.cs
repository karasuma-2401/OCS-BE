namespace OCS.Api.Models.Settlements;

public sealed record SettlementContractDto(
    Guid ContractId, string ContractNumber, string? CustomerName, string? CustomerPhone,
    string? BankCode, string? BankAccountNumber, string? BankAccountHolder,
    int ProductCount, int SoldProductCount, string ContractStatus,
    DateTimeOffset EndsAt = default,
    int ActiveProductCount = 0,
    int CompensatedProductCount = 0,
    int ApprovedIncidentCount = 0,
    decimal UnsettledSalesNetAmount = 0,
    decimal UnsettledCompensationAmount = 0,
    decimal EligiblePayoutAmount = 0,
    Guid? OpenPayoutId = null,
    string? OpenPayoutNumber = null,
    string? OpenPayoutStatus = null,
    decimal? OpenPayoutAmount = null,
    string? BlockReason = null,
    string? EligibilityKind = null,
    string? EligibilityReason = null,
    int OtherProductCount = 0,
    int CompletedPayoutCount = 0,
    decimal CompletedPayoutAmount = 0,
    IReadOnlyList<string>? EligibilityFactors = null);

public sealed record SettlementLineDto(
    string Type, string Description, decimal Amount, Guid? ProductId, Guid? IncidentReportId);

public sealed record SettlementPreviewDto(
    Guid ContractId, string ContractNumber, string? CustomerName,
    IReadOnlyList<SettlementLineDto> Lines, decimal TotalAmount, string? BlockReason,
    string? CustomerPhone = null, string? BankCode = null, string? BankAccountNumber = null, string? BankAccountHolder = null,
    DateTimeOffset? EndsAt = null,
    IReadOnlyList<SettlementLineDto>? DeferredLines = null,
    decimal DeferredAmount = 0,
    Guid? OpenPayoutId = null, string? OpenPayoutNumber = null, string? OpenPayoutStatus = null, decimal? OpenPayoutAmount = null,
    bool IsCompensationOnly = false,
    bool IsMidTerm = false,
    string? EligibilityKind = null,
    string? EligibilityReason = null,
    int TotalProductCount = 0,
    int SoldProductCount = 0,
    int ActiveProductCount = 0,
    int CompensatedProductCount = 0,
    int OtherProductCount = 0,
    int CompletedPayoutCount = 0,
    decimal CompletedPayoutAmount = 0,
    IReadOnlyList<string>? EligibilityFactors = null);

public sealed record PayoutDto(
    Guid Id, string PayoutNumber, Guid ContractId, string ContractNumber,
    string? CustomerName, string? CustomerPhone, string? BankCode,
    string? BankAccountNumber, string? BankAccountHolder, decimal TotalAmount,
    string Status, string? ProofUrl, string? BankTransactionReference,
    DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt, DateTimeOffset? TransferredAt,
    DateTimeOffset? CustomerConfirmedAt, DateTimeOffset? CompletedAt,
    IReadOnlyList<SettlementLineDto> Lines,
    Guid? CreatedByEmployeeId, Guid? ApprovedByEmployeeId, Guid? RejectedByEmployeeId,
    DateTimeOffset? RejectedAt, string? RejectionReason,
    string? CreatedByName, string? ApprovedByName, string? RejectedByName,
    DateTimeOffset? DisputedAt = null,
    string? DisputeReason = null,
    string? DisputeStatus = null,
    DateTimeOffset? DisputeResolvedAt = null,
    string? DisputeResolutionNote = null,
    Guid? DisputeResolvedByEmployeeId = null,
    string? DisputeResolvedByName = null);

public sealed record CreatePayoutRequest(decimal ServiceFee, string? ServiceFeeDescription);
public sealed record UpdatePayoutServiceFeeRequest(decimal ServiceFee, string? Description, string Reason);
public sealed record UpdatePayoutPaymentRequest(string BankCode, string BankAccountNumber, string BankAccountHolder, string Reason);
public sealed record RejectPayoutRequest(string Reason);
public sealed record PayoutHistoryDto(string Action, string Changes, string? Reason, Guid? ActorAccountId, string? ActorName, DateTimeOffset OccurredAt);

public sealed record RecordPayoutTransferRequest(string ProofUrl, string BankTransactionReference);

public sealed record CustomerPayoutConfirmation(string? ConfirmationNote);
public sealed record SubmitPayoutDisputeRequest(string Reason);
public sealed record ResolvePayoutDisputeRequest(string Resolution, string? Note);
