using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Contracts;

/// <summary>Thông tin cơ bản để lập hợp đồng từ lịch hẹn hoặc khách đến trực tiếp.</summary>
public sealed record CreateContractRequest
{
    public Guid? AppointmentId { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? RequestId { get; init; }
    public ContractGuestRequest? Guest { get; init; }
    public ContractCustomerDetailsRequest? CustomerDetails { get; init; }
    public bool SaveCustomer { get; init; }
    public List<ContractProductRequest> Products { get; init; } = [];
    [Required] public DateTimeOffset? SignedAt { get; init; }
    [Required] public DateTimeOffset? EndsAt { get; init; }
    [Required, MaxLength(20)] public string ExpiryHandling { get; init; } = string.Empty;
    [MaxLength(6000)] public string? Terms { get; init; }
    public Guid? TemplateVersion { get; init; }
    public Guid? TermsRequestId { get; init; }
    public ContractImageRequest? Signature { get; init; }
}

/// <summary>Thông tin bổ sung được ghi riêng cho hợp đồng, không tự sửa hồ sơ khách.</summary>
public sealed record ContractCustomerDetailsRequest
{
    [MaxLength(254), EmailAddress] public string? Email { get; init; }
    [MaxLength(20)] public string? IdentityNumber { get; init; }
    [MaxLength(500)] public string? Address { get; init; }
    [MaxLength(50)] public string? BankCode { get; init; }
    [MaxLength(50)] public string? BankAccountNumber { get; init; }
    [MaxLength(200)] public string? BankAccountHolderName { get; init; }
}

/// <summary>Thông tin tối thiểu để nhân viên chọn đúng hồ sơ khách.</summary>
public sealed record ContractCustomerResponse(Guid Id, string FullName, string PhoneNumber, string? Email,
    string? IdentityNumber, string? Address, string? BankCode, string? BankAccountNumber, string? BankAccountHolderName, uint Version, uint ProfileVersion);

/// <summary>Thông tin nhân viên lập hợp đồng từ phiên hiện tại.</summary>
public sealed record ContractEmployeeResponse(string FullName, string PhoneNumber, string JobTitle);

/// <summary>Các trường có thể cập nhật trên hồ sơ khách.</summary>
public sealed record UpdateContractCustomerRequest
{
    [MaxLength(254), EmailAddress] public string? Email { get; init; }
    [MaxLength(20)] public string? IdentityNumber { get; init; }
    [MaxLength(500)] public string? Address { get; init; }
    [MaxLength(50)] public string? BankCode { get; init; }
    [MaxLength(50)] public string? BankAccountNumber { get; init; }
    [MaxLength(200)] public string? BankAccountHolderName { get; init; }
    public uint Version { get; init; }
    public uint ProfileVersion { get; init; }
}

/// <summary>Thông tin khách vãng lai ghi trên hợp đồng.</summary>
public sealed record ContractGuestRequest
{
    [Required, MaxLength(200)] public string FullName { get; init; } = string.Empty;
    [Required, RegularExpression("^0[0-9]{9}$")] public string PhoneNumber { get; init; } = string.Empty;
    [MaxLength(254), EmailAddress] public string? Email { get; init; }
    [MaxLength(20)] public string? IdentityNumber { get; init; }
    [MaxLength(500)] public string? Address { get; init; }
    [MaxLength(50)] public string? BankCode { get; init; }
    [MaxLength(50)] public string? BankAccountNumber { get; init; }
    [MaxLength(200)] public string? BankAccountHolderName { get; init; }
}

/// <summary>Một sản phẩm ký gửi được tiếp nhận cùng hợp đồng.</summary>
public sealed record ContractProductRequest
{
    [Required, MaxLength(200)] public string Name { get; init; } = string.Empty;
    [Required, MaxLength(30)] public string Size { get; init; } = string.Empty;
    [MaxLength(2000)] public string? Description { get; init; }
    [MaxLength(100)] public string? Barcode { get; init; }
    [Range(0, 100)] public int ConditionPercentage { get; init; }
    [Range(typeof(decimal), "0", "9999999999999999")] public decimal ListedPrice { get; init; }
    [Required, Range(typeof(decimal), "0", "100")] public decimal? AgreedCommissionRate { get; init; }
    public Guid BrandId { get; init; }
    public Guid CategoryId { get; init; }
    public Guid? StorageLocationId { get; init; }
    public List<ContractImageRequest> Images { get; init; } = [];
}

/// <summary>Ảnh sản phẩm PNG hoặc JPEG được mã hóa base64.</summary>
public sealed record ContractImageRequest
{
    [Required] public string ContentType { get; init; } = string.Empty;
    [Required] public string Data { get; init; } = string.Empty;
}

/// <summary>Nội dung ảnh và điều kiện hiển thị công khai.</summary>
public sealed record ContractImageContent(byte[] Content, string ContentType, bool IsPublic);

/// <summary>Danh mục dùng trong form tiếp nhận.</summary>
public sealed record ContractOption(Guid Id, string Name, decimal? CommissionRate = null);
/// <summary>Các danh mục đang hoạt động.</summary>
public sealed record ContractOptionsResponse(IReadOnlyList<ContractOption> Brands, IReadOnlyList<ContractOption> Categories, IReadOnlyList<ContractOption> Locations);

/// <summary>Thông tin cơ bản của hợp đồng đã lập.</summary>
public sealed record ContractResponse(
    Guid Id, string ContractNumber, Guid? CustomerId, Guid? AppointmentId,
    Guid CreatedByEmployeeId, DateTimeOffset SignedAt, DateTimeOffset EndsAt,
    string ExpiryHandling, string Status);

public sealed record ContractListItem(Guid Id, string ContractNumber, string? CustomerName, DateTimeOffset SignedAt, bool HasPdf);
