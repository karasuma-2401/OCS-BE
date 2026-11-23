namespace OCS.Api.Models.Products;

public sealed record ExpiryQuery(int Page = 1, int PageSize = 20, string? Q = null,
    string? Status = null, string? Type = null, DateTimeOffset? DeadlineFrom = null,
    DateTimeOffset? DeadlineTo = null, Guid? CustomerId = null, string? CustomerQuery = null);

public sealed record ExpiryProductDto(Guid Id, string Barcode, string Name, string Status,
    decimal ListedPrice, Guid? HandledByEmployeeId, DateTimeOffset? HandledAt,
    string? RecipientOrganization, string? Evidence, string? Note);

public sealed record ExpiryDto(Guid Id, Guid ContractId, string ContractNumber, string? CustomerName,
    string? CustomerPhone, DateTimeOffset EndsAt, string Type, string Status,
    DateTimeOffset? CollectionDeadline, string? RecipientOrganization, string? Evidence,
    string? Note, DateTimeOffset? ConfirmedAt, DateTimeOffset? UnclaimedApprovedAt,
    string? UnclaimedBasis, IReadOnlyList<ExpiryProductDto> Products);

public sealed record ConfirmReturnRequest(IReadOnlyList<string>? Barcodes);
public sealed record ConfirmDonationRequest(IReadOnlyList<string>? Barcodes,
    string? RecipientOrganization, string? Evidence, string? Note);
public sealed record MarkUnclaimedRequest(string? Basis);

public sealed record DonationReceiptProductDto(Guid ProductId, string Barcode, string Name,
    decimal ListedPrice);
public sealed record DonationReceiptDto(Guid Id, string ReceiptNumber, Guid ExpiryProcessingId,
    string ContractNumber, string? CustomerName, string RecipientOrganization, string Evidence,
    string? Note, Guid HandledByEmployeeId, DateTimeOffset HandledAt,
    IReadOnlyList<DonationReceiptProductDto> Products);
