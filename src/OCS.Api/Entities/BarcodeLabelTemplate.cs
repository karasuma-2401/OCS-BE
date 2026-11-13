namespace OCS.Api.Entities;

/// <summary>Cấu hình khổ nhãn barcode dùng chung cho cửa hàng.</summary>
public sealed class BarcodeLabelTemplate
{
    public int Id { get; set; } = 1;
    public int WidthMm { get; set; } = 60;
    public int HeightMm { get; set; } = 35;
    public int Columns { get; set; } = 3;
    public int FontSizePt { get; set; } = 8;
    public Guid Version { get; set; } = Guid.Empty;
    public DateTimeOffset? UpdatedAt { get; set; }
}
