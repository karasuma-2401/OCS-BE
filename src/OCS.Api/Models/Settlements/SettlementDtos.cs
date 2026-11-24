namespace OCS.Api.Models.Settlements;

public sealed record SettlementContractDto(
    Guid ContractId, string ContractNumber, string? CustomerName, string? CustomerPhone,
    string? BankCode, string? BankAccountNumber, string? BankAccountHolder,
    int ProductCount, int SoldProductCount, string ContractStatus);

public sealed record SettlementLineDto(
    string Type, string Description, decimal Amount, Guid? ProductId, Guid? IncidentReportId);

public sealed record SettlementPreviewDto(
    Guid ContractId, string ContractNumber, string? CustomerName,
    IReadOnlyList<SettlementLineDto> Lines, decimal TotalAmount, string? BlockReason);

public sealed record PayoutDto(
    Guid Id, string PayoutNumber, Guid ContractId, string ContractNumber,
    string? CustomerName, string? CustomerPhone, string? BankCode,
    string? BankAccountNumber, string? BankAccountHolder, decimal TotalAmount,
    string Status, string? ProofUrl, string? BankTransactionReference,
    DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt, DateTimeOffset? TransferredAt,
    DateTimeOffset? CustomerConfirmedAt, DateTimeOffset? CompletedAt,
    IReadOnlyList<SettlementLineDto> Lines);

public sealed record RecordPayoutTransferRequest(string ProofUrl, string BankTransactionReference);

public sealed record CustomerPayoutConfirmation(string ConfirmationNote);
