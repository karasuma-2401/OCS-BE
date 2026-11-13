namespace OCS.Api.Entities;

/// <summary>Cấu hình mẫu hợp đồng đang sử dụng; một bản cho toàn cửa hàng.</summary>
public sealed class ContractTemplate
{
    public int Id { get; set; } = 1;
    public string Title { get; set; } = "HỢP ĐỒNG KÝ GỬI";
    public string OrganizationName { get; set; } = "";
    public string HeaderText { get; set; } = "";
    public string FooterText { get; set; } = "";
    public string DefaultTerms { get; set; } = "";
    public byte[]? PdfBackground { get; set; }
    public string FieldMappingsJson { get; set; } = "[]";
    public Guid Version { get; set; } = Guid.Empty;
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
