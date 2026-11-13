using System.Globalization;
using System.Text;
using OCS.Api.Consts;
using OCS.Api.Entities;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace OCS.Api.Services;

/// <summary>Xuất PDF phiếu sản phẩm và barcode theo mẫu phiếu.</summary>
public static class BarcodePdfRenderer
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static string Money(decimal value) => value.ToString(value == decimal.Truncate(value) ? "N0" : "N2", Vietnamese) + " đ";

    public static byte[] Render(IReadOnlyList<(Product Product, string Brand, string Category, string? Location)> products, IReadOnlyList<byte[]> barcodes)
    {
        ContractPdfRenderer.EnsureFontResolver();

        using var pdf = new PdfDocument();
        pdf.Info.Title = "Phiếu thông tin sản phẩm và Barcode";

        var regular = new XFont("Noto Sans", 10, XFontStyleEx.Regular);
        var bold = new XFont("Noto Sans", 10, XFontStyleEx.Bold);
        var titleFont = new XFont("Noto Sans", 15, XFontStyleEx.Bold);
        var barcodeFont = new XFont("Noto Sans", 11, XFontStyleEx.Bold);
        var boxPen = new XPen(XColor.FromArgb(200, 200, 200), 1);
        var headerBrush = new XSolidBrush(XColor.FromArgb(245, 245, 245));

        for (var i = 0; i < products.Count; i++)
        {
            var (product, brand, category, location) = products[i];
            var barcodeImage = i < barcodes.Count ? barcodes[i] : [];
            var page = pdf.AddPage();
            using var graphics = XGraphics.FromPdfPage(page);

            var width = page.Width.Point - 140; // margin 70 on each side
            var y = 50d;

            // Title
            graphics.DrawString("THÔNG TIN SẢN PHẨM & MÃ VẠCH (BARCODE)", titleFont, XBrushes.Black, new XRect(70, y, width, 24), XStringFormats.TopCenter);
            y += 35;

            // Information Card Header
            var boxTop = y;
            graphics.DrawRectangle(boxPen, headerBrush, new XRect(70, y, width, 26));
            graphics.DrawString("CHI TIẾT SẢN PHẨM", bold, XBrushes.Black, new XPoint(82, y + 17));
            y += 26;

            var fields = new (string Label, string Value)[]
            {
                ("Tên sản phẩm:", product.Name),
                ("Giá niêm yết:", Money(product.ListedPrice)),
                ("Mã sản phẩm (Barcode):", product.Barcode ?? "Chưa có mã"),
                ("Thương hiệu:", brand),
                ("Danh mục:", category),
                ("Kích thước:", product.Size),
                ("Độ mới:", $"{product.ConditionPercentage}%"),
                ("Tỷ lệ hoa hồng:", product.AgreedCommissionRate.HasValue ? $"{product.AgreedCommissionRate.Value:0.##}%" : "Chưa có"),
                ("Vị trí kho:", location ?? "Chưa xếp vị trí"),
                ("Nguồn hàng:", product.SourceType == ProductSourceType.Consignment ? "Ký gửi" : "Thu mua"),
                ("Trạng thái:", GetStatusText(product.Status)),
                ("Mô tả:", string.IsNullOrWhiteSpace(product.Description) ? "Không có mô tả" : product.Description)
            };

            var lineY = y + 15;
            const double labelWidth = 160d;
            var valueX = 70 + labelWidth;
            var maxValueWidth = width - labelWidth - 20;

            foreach (var (label, val) in fields)
            {
                graphics.DrawString(label, bold, XBrushes.Black, new XPoint(85, lineY));

                var remaining = val.Normalize(NormalizationForm.FormC).Replace("\r", "");
                var isFirst = true;
                foreach (var paragraph in remaining.Split('\n'))
                {
                    var text = paragraph.Length == 0 ? " " : paragraph;
                    while (text.Length > 0)
                    {
                        var length = text.Length;
                        while (length > 1 && graphics.MeasureString(text[..length], regular).Width > maxValueWidth) length--;
                        if (length < text.Length)
                        {
                            var space = text.LastIndexOf(' ', length - 1, length);
                            if (space > 0) length = space;
                        }
                        graphics.DrawString(text[..length], regular, XBrushes.Black, new XPoint(valueX, lineY));
                        lineY += 18;
                        text = text[length..].TrimStart();
                        isFirst = false;
                    }
                }
                if (isFirst) lineY += 18;
            }

            var boxHeight = lineY - boxTop + 10;
            graphics.DrawRectangle(boxPen, 70, boxTop, width, boxHeight);
            y = boxTop + boxHeight + 25;

            // Barcode Section
            var barcodeBoxTop = y;
            graphics.DrawRectangle(boxPen, headerBrush, new XRect(70, y, width, 26));
            graphics.DrawString("MÃ VẠCH QUÉT (BARCODE CODE 128)", bold, XBrushes.Black, new XRect(70, y + 5, width, 18), XStringFormats.TopCenter);
            y += 36;

            if (barcodeImage.Length > 0)
            {
                try
                {
                    using var stream = new MemoryStream(barcodeImage);
                    using var image = XImage.FromStream(stream);
                    const double imgWidth = 280d;
                    var imgHeight = Math.Min(80d, imgWidth * image.PixelHeight / image.PixelWidth);
                    var imgX = 70 + (width - imgWidth) / 2;
                    graphics.DrawImage(image, imgX, y, imgWidth, imgHeight);
                    y += imgHeight + 8;
                }
                catch
                {
                    y += 10;
                }
            }

            graphics.DrawString(product.Barcode ?? "", barcodeFont, XBrushes.Black, new XRect(70, y, width, 18), XStringFormats.TopCenter);
            y += 24;

            graphics.DrawRectangle(boxPen, 70, barcodeBoxTop, width, y - barcodeBoxTop + 12);
            y += 25;

            var note = "In trên khổ A4 với tỷ lệ 100%. Quét thử barcode bằng máy quét trước khi dán lên sản phẩm.";
            graphics.DrawString(note, regular, XBrushes.Gray, new XRect(70, y, width, 16), XStringFormats.TopCenter);
        }

        using var output = new MemoryStream();
        pdf.Save(output, false);
        return output.ToArray();
    }

    private static string GetStatusText(ProductStatus status) => status switch
    {
        ProductStatus.PendingProcessing => "Chờ xử lý",
        ProductStatus.OnSale => "Đang bán",
        ProductStatus.Sold => "Đã bán",
        ProductStatus.Expired => "Hết hạn",
        ProductStatus.Donated => "Đã quyên góp",
        ProductStatus.Returned => "Đã trả",
        ProductStatus.EarlyWithdrawn => "Đã rút trước hạn",
        ProductStatus.Compensated => "Đã bồi thường",
        ProductStatus.Cancelled => "Đã hủy",
        _ => "Không xác định"
    };
}

