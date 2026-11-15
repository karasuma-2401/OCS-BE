using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Buyouts;

public sealed record BuyoutCustomerResponse(Guid Id, string FullName, string PhoneNumber);

public sealed record CreateBuyoutInspectionRequest(
    [Required] Guid CustomerId,
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [Required, StringLength(30, MinimumLength = 1)] string Size,
    [StringLength(2000)] string? Description,
    [Range(0, 100)] int ConditionPercentage,
    [Required] Guid BrandId,
    [Required] Guid CategoryId,
    [Required, StringLength(1000, MinimumLength = 1)] string InspectionNotes,
    bool Passed);

public sealed record CreateBuyoutRequest(Guid InspectionId, decimal PurchasePrice, decimal ListedPrice);
public sealed record ReviewBuyoutRequest(Guid ExpectedVersion, bool Approve, string? Reason);
public sealed record PayBuyoutRequest(Guid ExpectedVersion, Guid StorageLocationId, string PaymentMethod, string? BankTransactionReference, bool CustomerAgreed);

public sealed record BuyoutInspectionResponse(Guid Id, Guid CustomerId, string Name, string Size, string? Description,
    int ConditionPercentage, Guid BrandId, Guid CategoryId, string InspectionNotes, Guid InspectedByEmployeeId, DateTimeOffset CreatedAt, bool Available, string CustomerName, string BrandName, string CategoryName);
public sealed record BuyoutInspectionSnapshot(Guid Id, Guid CustomerId, string Name, string Size, string? Description,
    int ConditionPercentage, Guid BrandId, Guid CategoryId, string InspectionNotes, Guid InspectedByEmployeeId, DateTimeOffset CreatedAt, string BrandName, string CategoryName);
public sealed record BuyoutResponse(Guid Id, string BuyoutNumber, Guid CustomerId, Guid InspectionId,
    Guid CreatedByEmployeeId, Guid? ApprovedByEmployeeId, string Status, decimal PurchasePrice, decimal ListedPrice,
    Guid Version, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt, Guid? PaidByEmployeeId, string? RejectionReason, Guid? ProductId, string? ProductBarcode, BuyoutInspectionSnapshot Inspection, string CustomerName);

public sealed record BuyoutReceiptResponse(Guid BuyoutId, string BuyoutNumber, Guid CustomerId, string CustomerName,
    string ProductName, string Size, int ConditionPercentage, decimal PurchasePrice, decimal ListedPrice,
    Guid TransactionId, string PaymentMethod, string? BankTransactionReference, DateTimeOffset PaidAt,
    Guid PaidByEmployeeId, string PaidByEmployeeName, string? ProductBarcode);
