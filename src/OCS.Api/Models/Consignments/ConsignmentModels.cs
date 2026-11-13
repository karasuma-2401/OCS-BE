using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Consignments;

public sealed class ReceptionCustomerInfoDto
{
    [Required(ErrorMessage = "Tên khách hàng là bắt buộc.")]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Số điện thoại khách hàng là bắt buộc.")]
    [MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [MaxLength(254)]
    public string? Email { get; set; }
}

public sealed class CreateReceptionItemDto
{
    [Required(ErrorMessage = "Tên sản phẩm là bắt buộc.")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid CategoryId { get; set; }

    [Required]
    public Guid BrandId { get; set; }

    [MaxLength(30)]
    public string Size { get; set; } = "FreeSize";

    [MaxLength(100)]
    public string? Material { get; set; }

    [Range(0, 100)]
    public int ConditionPercentage { get; set; } = 95;

    public bool HasLabel { get; set; } = true;

    [MaxLength(500)]
    public string? DefectNotes { get; set; }

    public string ApprovalStatus { get; set; } = "Approved";

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    [Range(0, 999999999999)]
    public decimal ListedPrice { get; set; }

    [Range(0, 100)]
    public decimal? CommissionRate { get; set; }

    [Range(0, 999999999999)]
    public decimal? PurchasePrice { get; set; }

    public string? CoverImageUrl { get; set; }

    public List<string>? ImageUrls { get; set; }
}

public sealed class CreateReceptionRequest
{
    public Guid? AppointmentId { get; set; }

    public string IntakeType { get; set; } = "Consignment";

    [Required]
    public ReceptionCustomerInfoDto CustomerInfo { get; set; } = new();

    [Range(1, 500)]
    public int ActualItemCount { get; set; }

    public string ExpiryHandling { get; set; } = "Return";

    [Range(1, 60, ErrorMessage = "Thời gian ký gửi phải từ 1 đến tối đa 60 ngày.")]
    public int TermDays { get; set; } = 60;

    [MaxLength(1000)]
    public string? Note { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Cần ít nhất 1 sản phẩm để lập phiếu tiếp nhận.")]
    public List<CreateReceptionItemDto> Items { get; set; } = new();
}

public sealed class ReceptionCreatedProductDto
{
    public Guid Id { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ApprovalStatus { get; set; } = string.Empty;
    public decimal ListedPrice { get; set; }
    public decimal CommissionRate { get; set; }
    public decimal CustomerPayout { get; set; }
}

public sealed class ReceptionContractResponse
{
    public Guid ContractId { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string IntakeType { get; set; } = "Consignment";
    public DateTimeOffset SignedAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int TotalReceivedItems { get; set; }
    public int TotalApprovedItems { get; set; }
    public int TotalRejectedItems { get; set; }
    public decimal TotalEstimatedValue { get; set; }
    public decimal TotalCustomerPayout { get; set; }
    public string ExpiryHandling { get; set; } = "Return";
    public List<ReceptionCreatedProductDto> CreatedProducts { get; set; } = new();
}

public sealed class ConsignmentContractSummaryResponse
{
    public Guid Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public Guid? AppointmentId { get; set; }
    public DateTimeOffset SignedAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string ExpiryHandling { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public decimal TotalListedValue { get; set; }
}

public sealed class SaveReceptionDraftRequest
{
    public Guid? DraftId { get; set; }
    public Guid? AppointmentId { get; set; }
    public string IntakeType { get; set; } = "Consignment";
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public int TermDays { get; set; } = 60;
    public decimal CommissionShop { get; set; } = 20;
    public string? Note { get; set; }
    public List<CreateReceptionItemDto> Items { get; set; } = new();
}

public sealed class ReceptionDraftResponse
{
    public Guid DraftId { get; set; }
    public Guid? StaffId { get; set; }
    public Guid? AppointmentId { get; set; }
    public string IntakeType { get; set; } = "Consignment";
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public int TermDays { get; set; } = 60;
    public decimal CommissionShop { get; set; } = 20;
    public string? Note { get; set; }
    public List<CreateReceptionItemDto> Items { get; set; } = new();
    public DateTimeOffset SavedAt { get; set; }
}

