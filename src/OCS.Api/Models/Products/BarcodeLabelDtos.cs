namespace OCS.Api.Models.Products;

/// <summary>Thông tin cần thiết để in nhãn sản phẩm.</summary>
public sealed record BarcodeLabelProduct(Guid Id, string Name, string Barcode, decimal ListedPrice);

/// <summary>Kết quả cấp mã tạm cho một sản phẩm chưa lưu.</summary>
public sealed record GeneratedBarcodeResponse(string Barcode);

/// <summary>Thông số mẫu in nhãn trên giấy A4.</summary>
public sealed record BarcodeLabelTemplateDto(int WidthMm, int HeightMm, int Columns, int FontSizePt, Guid Version, DateTimeOffset? UpdatedAt);
