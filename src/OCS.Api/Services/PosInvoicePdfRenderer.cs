using System.Globalization;
using OCS.Api.Models.Pos;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace OCS.Api.Services;

/// <summary>
/// Xuất file PDF mẫu hóa đơn bán lẻ tại quầy POS.
/// Khổ giấy tiêu chuẩn 80mm (chiều rộng ~226pt) hoặc A5 / A4. Ở đây dùng chuẩn bill cuộn 80mm để in nhiệt hoặc A5.
/// </summary>
public static class PosInvoicePdfRenderer
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    public static byte[] Render(PosInvoiceDetailDto invoice)
    {
        ContractPdfRenderer.EnsureFontResolver();

        using var pdf = new PdfDocument();
        pdf.Info.Title = $"Hóa đơn {invoice.InvoiceNumber}";

        // Chuẩn khổ A5 (420 x 595 point) hoặc chuẩn Bill K80 (226 point width)
        // Dùng A5 cho rõ ràng, in trình duyệt hoặc máy in đều đẹp
        var page = pdf.AddPage();
        page.Width = XUnit.FromMillimeter(148);
        page.Height = XUnit.FromMillimeter(210);

        using var graphics = XGraphics.FromPdfPage(page);

        var titleFont = new XFont("Noto Sans", 14, XFontStyleEx.Bold);
        var subHeaderFont = new XFont("Noto Sans", 9, XFontStyleEx.Regular);
        var regularFont = new XFont("Noto Sans", 9, XFontStyleEx.Regular);
        var boldFont = new XFont("Noto Sans", 9, XFontStyleEx.Bold);
        var smallFont = new XFont("Noto Sans", 8, XFontStyleEx.Regular);
        var smallItalic = new XFont("Noto Sans", 8, XFontStyleEx.Italic);

        var linePen = new XPen(XColor.FromArgb(200, 200, 200), 0.8);
        var dashPen = new XPen(XColor.FromArgb(160, 160, 160), 0.8) { DashStyle = XDashStyle.Dash };

        double margin = 20;
        double width = page.Width.Point - (margin * 2);
        double y = 25;

        // 1. Tiêu đề cửa hàng
        graphics.DrawString("CỬA HÀNG THỜI TRANG KÝ GỬI OCS", titleFont, XBrushes.Black, new XRect(margin, y, width, 20), XStringFormats.TopCenter);
        y += 22;
        graphics.DrawString("HÓA ĐƠN BÁN LẺ", boldFont, XBrushes.Black, new XRect(margin, y, width, 16), XStringFormats.TopCenter);
        y += 20;

        // 2. Thông tin chung
        graphics.DrawString($"Số HĐ: {invoice.InvoiceNumber}", regularFont, XBrushes.Black, new XPoint(margin, y));
        graphics.DrawString($"Ngày: {invoice.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}", regularFont, XBrushes.Black, new XPoint(margin + (width / 2), y));
        y += 15;

        if (!string.IsNullOrWhiteSpace(invoice.CashierName))
        {
            graphics.DrawString($"Thu ngân: {invoice.CashierName}", regularFont, XBrushes.Black, new XPoint(margin, y));
        }
        graphics.DrawString($"PTTT: {invoice.PaymentMethod ?? "Tiền mặt"}", regularFont, XBrushes.Black, new XPoint(margin + (width / 2), y));
        y += 18;

        // Đường kẻ phân cách
        graphics.DrawLine(linePen, margin, y, margin + width, y);
        y += 8;

        // 3. Header bảng sản phẩm
        // Cột: STT (25), Tên SP (rộng nhất), Giá
        double sttWidth = 25;
        double priceWidth = 80;
        double nameWidth = width - sttWidth - priceWidth;

        graphics.DrawString("STT", boldFont, XBrushes.Black, new XPoint(margin, y + 10));
        graphics.DrawString("Tên sản phẩm", boldFont, XBrushes.Black, new XPoint(margin + sttWidth, y + 10));
        graphics.DrawString("Đơn giá", boldFont, XBrushes.Black, new XRect(margin + sttWidth + nameWidth, y, priceWidth, 14), XStringFormats.TopRight);
        y += 16;
        graphics.DrawLine(dashPen, margin, y, margin + width, y);
        y += 8;

        // 4. Danh sách sản phẩm
        int index = 1;
        foreach (var item in invoice.Items)
        {
            graphics.DrawString(index.ToString(CultureInfo.InvariantCulture), regularFont, XBrushes.Black, new XPoint(margin, y + 10));

            var productName = item.Name;
            if (!string.IsNullOrWhiteSpace(item.Size))
            {
                productName += $" ({item.Size})";
            }
            if (productName.Length > 32)
            {
                productName = productName[..29] + "...";
            }

            graphics.DrawString(productName, regularFont, XBrushes.Black, new XPoint(margin + sttWidth, y + 10));
            graphics.DrawString($"{item.Price.ToString("N0", Vietnamese)} đ", regularFont, XBrushes.Black, new XRect(margin + sttWidth + nameWidth, y, priceWidth, 14), XStringFormats.TopRight);

            y += 14;

            // Barcode dưới tên
            graphics.DrawString($"[{item.Barcode}]", smallItalic, XBrushes.Gray, new XPoint(margin + sttWidth, y + 8));
            y += 14;

            index++;
        }

        graphics.DrawLine(linePen, margin, y, margin + width, y);
        y += 12;

        // 5. Tổng kết tiền
        graphics.DrawString("Tổng tiền:", boldFont, XBrushes.Black, new XPoint(margin + sttWidth, y + 10));
        graphics.DrawString($"{invoice.TotalAmount.ToString("N0", Vietnamese)} đ", boldFont, XBrushes.Black, new XRect(margin + sttWidth + nameWidth, y, priceWidth, 14), XStringFormats.TopRight);
        y += 18;

        if (invoice.CashGiven.HasValue && invoice.CashGiven.Value > 0)
        {
            graphics.DrawString("Tiền khách đưa:", regularFont, XBrushes.Black, new XPoint(margin + sttWidth, y + 10));
            graphics.DrawString($"{invoice.CashGiven.Value.ToString("N0", Vietnamese)} đ", regularFont, XBrushes.Black, new XRect(margin + sttWidth + nameWidth, y, priceWidth, 14), XStringFormats.TopRight);
            y += 16;

            var change = invoice.ChangeAmount ?? (invoice.CashGiven.Value - invoice.TotalAmount);
            graphics.DrawString("Tiền thừa trả khách:", regularFont, XBrushes.Black, new XPoint(margin + sttWidth, y + 10));
            graphics.DrawString($"{change.ToString("N0", Vietnamese)} đ", regularFont, XBrushes.Black, new XRect(margin + sttWidth + nameWidth, y, priceWidth, 14), XStringFormats.TopRight);
            y += 20;
        }

        // 6. Chính sách không đổi trả & Cảm ơn
        graphics.DrawLine(dashPen, margin, y, margin + width, y);
        y += 12;

        graphics.DrawString("* CHÍNH SÁCH: Hàng ký gửi đã qua sử dụng, KHÔNG ĐỔI TRẢ dưới mọi hình thức.", smallItalic, XBrushes.Black, new XRect(margin, y, width, 14), XStringFormats.TopCenter);
        y += 15;
        graphics.DrawString("Quý khách vui lòng kiểm tra kỹ sản phẩm trước khi thanh toán và rời quầy.", smallItalic, XBrushes.Black, new XRect(margin, y, width, 14), XStringFormats.TopCenter);
        y += 18;
        graphics.DrawString("Xin cảm ơn và hẹn gặp lại Quý khách!", boldFont, XBrushes.Black, new XRect(margin, y, width, 14), XStringFormats.TopCenter);

        using var output = new MemoryStream();
        pdf.Save(output, false);
        return output.ToArray();
    }
}

