using System.ComponentModel.DataAnnotations;

namespace OCS.Api.Models.Contracts;

/// <summary>Cấu hình mẫu PDF và điều khoản mặc định hiện hành.</summary>
public sealed record ContractTemplateResponse(string Title, string OrganizationName, string HeaderText, string FooterText,
    string DefaultTerms, Guid Version, DateTimeOffset? UpdatedAt, string? PdfBase64, IReadOnlyList<ContractPdfFieldMapping> FieldMappings);

/// <summary>Vùng điền dữ liệu; tọa độ và kích thước tính theo phần trăm trang, gốc ở góc trên trái.</summary>
public sealed record ContractPdfFieldMapping(string Field, int Page, double X, double Y, double Width, double Height);

/// <summary>Thông tin mẫu PDF quản lý muốn lưu hoặc xem thử.</summary>
public sealed record UpdateContractTemplateRequest
{
    [Required, MaxLength(200)] public string Title { get; init; } = "";
    [MaxLength(200)] public string OrganizationName { get; init; } = "";
    [MaxLength(1000)] public string HeaderText { get; init; } = "";
    [MaxLength(1000)] public string FooterText { get; init; } = "";
    [MaxLength(6000)] public string DefaultTerms { get; init; } = "";
    public Guid Version { get; init; }
    public string? PdfBase64 { get; init; }
    public List<ContractPdfFieldMapping> FieldMappings { get; init; } = [];
}
