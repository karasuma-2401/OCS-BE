namespace OCS.Api.Models.Customers;

public sealed record ConsignmentProductResponse(
    Guid Id,
    string Barcode,
    string Name,
    string Status,
    decimal ListedPrice,
    decimal? AgreedCommissionRate,
    decimal? EstimatedPayout);

public sealed record ConsignmentTrackingResponse(
    Guid ContractId,
    string ContractNumber,
    Guid? AppointmentId,
    DateTimeOffset SignedAt,
    DateTimeOffset EndsAt,
    string Status,
    string ExpiryHandling,
    IReadOnlyList<ConsignmentProductResponse> Products,
    decimal? EstimatedPayout,
    decimal SettledAmount);
