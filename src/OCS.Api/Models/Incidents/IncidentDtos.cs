using System.Text.Json.Serialization;
using OCS.Api.Consts;

namespace OCS.Api.Models.Incidents;

public sealed record IncidentQuery(int Page = 1, int PageSize = 20, string? Q = null, string? Status = null);

public sealed record CreateIncidentRequest(
    Guid ProductId,
    [property: JsonConverter(typeof(JsonStringEnumConverter<IncidentType>))] IncidentType Type,
    string Description,
    IReadOnlyList<string> EvidenceUrls);

public sealed record ResubmitIncidentRequest(string Description, IReadOnlyList<string>? EvidenceUrls);

public sealed record ApproveIncidentRequest(decimal CompensationAmount, string? Note);

public sealed record RequestIncidentRecheckRequest(string Reason);

public sealed record IncidentProductLookup(
    Guid Id, string Barcode, string Name, string Status, decimal ListedPrice,
    string? ContractNumber, string? CustomerName, IReadOnlyList<string> Images);

public sealed record IncidentAuditEvent(
    Guid Id, string Action, string? ActorAccountId, string ActorType,
    string? Reason, string Changes, DateTimeOffset OccurredAt);

public sealed record IncidentResponse(
    Guid Id, Guid ProductId, string ProductBarcode, string ProductName,
    string? ContractNumber, string? CustomerName, string Type, string Status,
    string Description, IReadOnlyList<string> EvidenceUrls, decimal AgreedPrice,
    decimal CompensationAmount, Guid CreatedByEmployeeId, Guid? ApprovedByEmployeeId, string? ApprovedByName,
    DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt, string? ReviewNote,
    IReadOnlyList<IncidentAuditEvent>? History = null,
    Guid? ContractId = null, Guid? PayoutId = null, string? PayoutNumber = null, string? PayoutStatus = null);
