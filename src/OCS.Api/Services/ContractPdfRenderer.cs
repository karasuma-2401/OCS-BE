using System.Globalization;
using System.Text;
using System.Text.Json;
using OCS.Api.Entities;
using OCS.Api.Models.Contracts;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace OCS.Api.Services;

public static class ContractPdfRenderer
{
    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        "contractNumber", "signedAt", "endsAt", "expiryHandling", "employeeName", "employeeTitle", "employeePhone",
        "customerName", "customerPhone", "customerEmail", "identityNumber", "address", "bankCode", "bankAccountNumber",
        "bankAccountHolder", "products", "terms", "signature"
    };
    private sealed class FontResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(isBold ? "noto-bold" : "noto-regular");
        public byte[] GetFont(string faceName)
        {
            var name = faceName == "noto-bold" ? "NotoSans-Bold.ttf" : "NotoSans-Regular.ttf";
            using var stream = typeof(ContractPdfRenderer).Assembly.GetManifestResourceStream($"OCS.Api.Assets.Fonts.{name}")
                ?? throw new InvalidOperationException("Không tìm thấy phông chữ PDF.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

    private static readonly object FontLock = new();

    public static byte[] CreateSampleBackground()
    {
        lock (FontLock) { GlobalFontSettings.FontResolver ??= new FontResolver(); }
        using var pdf = new PdfDocument();
        var page = pdf.AddPage();
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            var font = new XFont("Noto Sans", 10, XFontStyleEx.Regular);
            var bold = new XFont("Noto Sans", 11, XFontStyleEx.Bold);
            var title = new XFont("Noto Sans", 15, XFontStyleEx.Bold);
            graphics.DrawString("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", bold, XBrushes.Black, new XRect(85, 60, 450, 20), XStringFormats.TopCenter);
            graphics.DrawString("Độc lập - Tự do - Hạnh phúc", bold, XBrushes.Black, new XRect(85, 80, 450, 20), XStringFormats.TopCenter);
            graphics.DrawString("HỢP ĐỒNG KÝ GỬI", title, XBrushes.Black, new XRect(85, 120, 450, 25), XStringFormats.TopCenter);
            var labels = new (string Label, double Y)[] { ("Số hợp đồng:", 20), ("Ngày ký:", 23), ("Ngày kết thúc:", 26),
                ("Nhân viên lập:", 31), ("Chức vụ:", 34), ("Điện thoại nhân viên:", 37), ("Họ tên khách hàng:", 42),
                ("Điện thoại khách hàng:", 45), ("CCCD / giấy tờ:", 48), ("Địa chỉ:", 51), ("Ngân hàng:", 54),
                ("Số tài khoản:", 57), ("Chủ tài khoản:", 60) };
            foreach (var (label, y) in labels) graphics.DrawString(label, font, XBrushes.Black, new XPoint(85, page.Height.Point * y / 100 + 11));
            graphics.DrawString("Chi tiết sản phẩm và điều khoản tại phụ lục kèm theo.", font, XBrushes.Black, new XPoint(85, page.Height.Point * 67 / 100));
            graphics.DrawString("KHÁCH HÀNG", bold, XBrushes.Black, new XRect(page.Width.Point * .60, page.Height.Point * .71, page.Width.Point * .30, 20), XStringFormats.TopCenter);
        }
        using var output = new MemoryStream();
        pdf.Save(output, false);
        return output.ToArray();
    }

    public static bool IsValidSignature(byte[] data, string contentType)
    {
        var png = data.Length >= 8 && data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var jpeg = data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff;
        if (!(contentType == "image/png" ? png : jpeg)) return false;
        try
        {
            using var stream = new MemoryStream(data);
            using var image = XImage.FromStream(stream);
            return image.PixelWidth is > 0 and <= 3000 && image.PixelHeight is > 0 and <= 3000;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    public static byte[] Render(ConsignmentContract contract, string employeeName, string employeeTitle, string employeePhone,
        IReadOnlyList<(Product Product, string Brand, string Category)> products, string? terms, byte[]? signature, bool preview = false, ContractTemplate? template = null)
    {
        lock (FontLock)
        {
            GlobalFontSettings.FontResolver ??= new FontResolver();
        }
        if (template?.PdfBackground is not null)
            return RenderCustom(contract, employeeName, employeeTitle, employeePhone, products, terms, signature, preview, template);
        using var pdf = new PdfDocument();
        pdf.Info.Title = $"Hợp đồng {contract.ContractNumber}";
        var regular = new XFont("Noto Sans", 11, XFontStyleEx.Regular);
        var bold = new XFont("Noto Sans", 11, XFontStyleEx.Bold);
        var title = new XFont("Noto Sans", 15, XFontStyleEx.Bold);
        XGraphics? graphics = null;
        double y = 0;
        double width = 0;
        void NewPage()
        {
            graphics?.Dispose();
            var page = pdf.AddPage();
            graphics = XGraphics.FromPdfPage(page);
            width = page.Width.Point - 142;
            y = 70;
        }
        void Line(string? input, XFont? font = null)
        {
            font ??= regular;
            foreach (var paragraph in (input ?? "").Normalize(NormalizationForm.FormC).Replace("\r", "").Split('\n'))
            {
                var remaining = paragraph.Length == 0 ? " " : paragraph;
                while (remaining.Length > 0)
                {
                    if (y > 770) NewPage();
                    var length = remaining.Length;
                    while (length > 1 && graphics!.MeasureString(remaining[..length], font).Width > width) length--;
                    if (length < remaining.Length)
                    {
                        var space = remaining.LastIndexOf(' ', length - 1, length);
                        if (space > 0) length = space;
                    }
                    graphics!.DrawString(remaining[..length], font, XBrushes.Black, new XPoint(85, y));
                    y += 16;
                    remaining = remaining[length..].TrimStart();
                }
            }
        }
        void ProductsTable()
        {
            // Rows may span pages; repeat the table heading without dropping long descriptions.
            var tableFont = new XFont("Noto Sans", 8, XFontStyleEx.Regular);
            var tableBold = new XFont("Noto Sans", 8, XFontStyleEx.Bold);
            var pen = new XPen(XColor.FromArgb(185, 185, 185), 0.5);
            var headerBrush = new XSolidBrush(XColor.FromArgb(242, 242, 242));
            var columns = new[] { 25d, width - 238d, 55d, 95d, 63d };
            var headings = new[] { "STT", "Sản phẩm / thông tin", "Độ mới", "Giá bán (đ)", "Hoa hồng" };
            List<string> Wrap(string? content, double columnWidth)
            {
                var lines = new List<string>();
                foreach (var paragraph in (content ?? "").Normalize(NormalizationForm.FormC).Replace("\r", "").Split('\n'))
                {
                    var remaining = paragraph.Length == 0 ? " " : paragraph;
                    while (remaining.Length > 0)
                    {
                        var low = 1;
                        var boundaries = StringInfo.ParseCombiningCharacters(remaining);
                        var high = boundaries.Length;
                        while (low < high)
                        {
                            var middle = low + (high - low + 1) / 2;
                            var end = middle == boundaries.Length ? remaining.Length : boundaries[middle];
                            if (graphics!.MeasureString(remaining[..end], tableFont).Width <= columnWidth - 8) low = middle;
                            else high = middle - 1;
                        }
                        var length = low == boundaries.Length ? remaining.Length : boundaries[low];
                        if (length < remaining.Length) { var space = remaining.LastIndexOf(' ', length - 1, length); if (space > 0) length = space; }
                        lines.Add(remaining[..length]);
                        remaining = remaining[length..].TrimStart();
                    }
                }
                return lines;
            }
            void Header()
            {
                if (y > 730) NewPage();
                var top = y - 10;
                var x = 85d;
                for (var column = 0; column < columns.Length; column++)
                {
                    graphics!.DrawRectangle(pen, headerBrush, new XRect(x, top, columns[column], 24));
                    graphics.DrawString(headings[column], tableBold, XBrushes.Black, new XPoint(x + 4, top + 15));
                    x += columns[column];
                }
                y = top + 24;
            }
            Header();
            for (var index = 0; index < products.Count; index++)
            {
                var item = products[index]; var p = item.Product;
                var detail = $"{p.Name}\nMã: {p.Barcode}\n{item.Brand} | {item.Category} | Cỡ: {p.Size}\nMô tả: {Value(p.Description)}";
                var price = p.ListedPrice.ToString(p.ListedPrice == decimal.Truncate(p.ListedPrice) ? "N0" : "N2", CultureInfo.GetCultureInfo("vi-VN"));
                var values = new[] { (index + 1).ToString(CultureInfo.InvariantCulture), detail, $"{p.ConditionPercentage}%", price, $"{p.AgreedCommissionRate:0.##}%" };
                var cellLines = values.Select((value, column) => Wrap(value, columns[column])).ToArray();
                var lineCount = cellLines.Max(lines => lines.Count);
                for (var offset = 0; offset < lineCount;)
                {
                    var availableLines = (int)((770 - y - 8) / 12);
                    if (availableLines < 1) { NewPage(); Header(); continue; }
                    var count = Math.Min(availableLines, lineCount - offset);
                    var height = count * 12 + 8;
                    var x = 85d;
                    for (var column = 0; column < columns.Length; column++)
                    {
                        graphics!.DrawRectangle(pen, x, y, columns[column], height);
                        for (var line = 0; line < count && offset + line < cellLines[column].Count; line++)
                            graphics.DrawString(cellLines[column][offset + line], tableFont, XBrushes.Black, new XPoint(x + 4, y + 12 + line * 12));
                        x += columns[column];
                    }
                    y += height;
                    offset += count;
                }
            }
            y += 16;
        }
        static string Value(string? s) => string.IsNullOrWhiteSpace(s) ? "Chưa cung cấp" : s.Trim();
        static string Date(DateTimeOffset date) => date.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        NewPage();
        template ??= new ContractTemplate();
        var heading = new XFont("Noto Sans", 11, XFontStyleEx.Bold);
        graphics!.DrawString("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", heading, XBrushes.Black, new XRect(85, y - 12, width, 18), XStringFormats.TopCenter);
        y += 18;
        graphics.DrawString("Độc lập - Tự do - Hạnh phúc", heading, XBrushes.Black, new XRect(85, y - 12, width, 18), XStringFormats.TopCenter);
        y += 25;
        if (!string.IsNullOrWhiteSpace(template.OrganizationName)) Line(template.OrganizationName, bold);
        Line(template.Title, title);
        if (!string.IsNullOrWhiteSpace(template.HeaderText)) Line(template.HeaderText);
        if (preview) Line("BẢN XEM TRƯỚC - CHƯA LƯU HỢP ĐỒNG", bold);
        Line("BẢN DỰ THẢO - CHƯA PHÊ DUYỆT", bold);
        Line($"Mã hợp đồng: {contract.ContractNumber}");
        Line($"Ngày ký theo thông tin nhập: {Date(contract.SignedAt)}");
        Line($"Ngày kết thúc: {Date(contract.EndsAt)}");
        Line($"Xử lý khi hết hạn: {(contract.ExpiryHandling == OCS.Api.Consts.ExpiryHandling.Return ? "Trả lại khách hàng" : "Quyên góp")}");
        Line("NHÂN VIÊN LẬP HỢP ĐỒNG", bold);
        Line($"Họ tên: {Value(employeeName)} | Chức vụ: {Value(employeeTitle)} | Số điện thoại: {Value(employeePhone)}");
        Line("THÔNG TIN KHÁCH HÀNG VÀ NHẬN TIỀN", bold);
        Line($"Họ tên: {Value(contract.CustomerNameSnapshot)} | Số điện thoại: {Value(contract.CustomerPhoneSnapshot)}");
        Line($"Email: {Value(contract.CustomerEmailSnapshot)} | CCCD / giấy tờ: {Value(contract.CustomerIdentitySnapshot)}");
        Line($"Địa chỉ: {Value(contract.CustomerAddressSnapshot)}");
        Line($"Mã ngân hàng: {Value(contract.BankCodeSnapshot)} | Số tài khoản: {Value(contract.BankAccountNumberSnapshot)}");
        Line($"Tên chủ tài khoản: {Value(contract.BankAccountHolderSnapshot)}");
        Line("DANH SÁCH SẢN PHẨM", bold);
        if (products.Count == 0) Line("Chưa có sản phẩm.");
        else ProductsTable();
        Line("ĐIỀU KHOẢN ĐƯỢC NHẬP", bold);
        Line(string.IsNullOrWhiteSpace(terms) ? "Chưa có nội dung điều khoản được cung cấp." : terms);
        Line("CHỮ KÝ KHÁCH HÀNG", bold);
        if (signature is null) Line("Chưa có ảnh chữ ký.");
        else
        {
            if (y > 680) NewPage();
            using var signatureStream = new MemoryStream(signature);
            using var image = XImage.FromStream(signatureStream);
            var scale = Math.Min(180.0 / image.PixelWidth, 90.0 / image.PixelHeight);
            graphics!.DrawImage(image, 85, y, image.PixelWidth * scale, image.PixelHeight * scale);
            y += image.PixelHeight * scale + 8;
        }
        if (!string.IsNullOrWhiteSpace(template.FooterText)) Line(template.FooterText);
        graphics?.Dispose();
        using var output = new MemoryStream();
        pdf.Save(output, false);
        return output.ToArray();
    }

    private static byte[] RenderCustom(ConsignmentContract contract, string employeeName, string employeeTitle, string employeePhone,
        IReadOnlyList<(Product Product, string Brand, string Category)> products, string? terms, byte[]? signature, bool preview, ContractTemplate template)
    {
        static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "Chưa cung cấp" : value.Trim();
        static string Date(DateTimeOffset value) => value.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var values = new Dictionary<string, string>
        {
            ["contractNumber"] = contract.ContractNumber,
            ["signedAt"] = Date(contract.SignedAt),
            ["endsAt"] = Date(contract.EndsAt),
            ["expiryHandling"] = contract.ExpiryHandling == OCS.Api.Consts.ExpiryHandling.Return ? "Trả lại khách hàng" : "Quyên góp",
            ["employeeName"] = Value(employeeName),
            ["employeeTitle"] = Value(employeeTitle),
            ["employeePhone"] = Value(employeePhone),
            ["customerName"] = Value(contract.CustomerNameSnapshot),
            ["customerPhone"] = Value(contract.CustomerPhoneSnapshot),
            ["customerEmail"] = Value(contract.CustomerEmailSnapshot),
            ["identityNumber"] = Value(contract.CustomerIdentitySnapshot),
            ["address"] = Value(contract.CustomerAddressSnapshot),
            ["bankCode"] = Value(contract.BankCodeSnapshot),
            ["bankAccountNumber"] = Value(contract.BankAccountNumberSnapshot),
            ["bankAccountHolder"] = Value(contract.BankAccountHolderSnapshot),
            ["terms"] = Value(terms),
            ["products"] = products.Count == 0 ? "Chưa có sản phẩm." : string.Join("\n", products.Select((item, index) =>
                $"{index + 1}. {item.Product.Name} | Mã: {item.Product.Barcode} | {item.Brand} | {item.Category} | Size: {item.Product.Size} | Độ mới: {item.Product.ConditionPercentage}% | Giá: {item.Product.ListedPrice.ToString("N2", CultureInfo.GetCultureInfo("vi-VN"))} đ | Hoa hồng: {item.Product.AgreedCommissionRate:0.##}% | {Value(item.Product.Description)}"))
        };
        using var pdf = PdfReader.Open(new MemoryStream(template.PdfBackground!), PdfDocumentOpenMode.Modify);
        pdf.Info.Title = $"Hợp đồng {contract.ContractNumber}";
        var font = new XFont("Noto Sans", 10, XFontStyleEx.Regular);
        using (var graphics = XGraphics.FromPdfPage(pdf.Pages[0], XGraphicsPdfPageOptions.Append))
        {
            var label = preview ? "BẢN XEM TRƯỚC - CHƯA LƯU" : "BẢN DỰ THẢO - CHƯA PHÊ DUYỆT";
            graphics.DrawString(label, new XFont("Noto Sans", 8, XFontStyleEx.Bold), XBrushes.DarkRed,
                new XRect(10, 4, pdf.Pages[0].Width.Point - 20, 14), XStringFormats.TopRight);
        }
        var mappings = JsonSerializer.Deserialize<List<ContractPdfFieldMapping>>(template.FieldMappingsJson) ?? [];
        foreach (var group in mappings.GroupBy(x => x.Page))
        {
            var page = pdf.Pages[group.Key - 1];
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            foreach (var mapping in group)
            {
                var rect = new XRect(page.Width.Point * mapping.X / 100, page.Height.Point * mapping.Y / 100,
                    page.Width.Point * mapping.Width / 100, page.Height.Point * mapping.Height / 100);
                if (mapping.Field == "signature")
                {
                    if (signature is null) continue;
                    using var image = XImage.FromStream(new MemoryStream(signature));
                    var scale = Math.Min(rect.Width / image.PixelWidth, rect.Height / image.PixelHeight);
                    graphics.DrawImage(image, rect.X, rect.Y, image.PixelWidth * scale, image.PixelHeight * scale);
                    continue;
                }
                var remaining = values[mapping.Field].Normalize(NormalizationForm.FormC).Replace("\r", "");
                var lines = new List<string>();
                foreach (var paragraph in remaining.Split('\n'))
                {
                    var text = paragraph.Length == 0 ? " " : paragraph;
                    while (text.Length > 0)
                    {
                        var length = text.Length;
                        while (length > 1 && graphics.MeasureString(text[..length], font).Width > rect.Width) length--;
                        if (length < text.Length) { var space = text.LastIndexOf(' ', length - 1, length); if (space > 0) length = space; }
                        lines.Add(text[..length]);
                        text = text[length..].TrimStart();
                    }
                }
                var maxLines = Math.Max(0, (int)(rect.Height / 14));
                for (var i = 0; i < Math.Min(lines.Count, maxLines); i++)
                {
                    var line = lines[i];
                    if (i == maxLines - 1 && lines.Count > maxLines)
                    {
                        while (line.Length > 0 && graphics.MeasureString(line + "…", font).Width > rect.Width) line = line[..^1];
                        line += "…";
                    }
                    graphics.DrawString(line, font, XBrushes.Black, new XPoint(rect.X, rect.Y + 11 + i * 14));
                }
            }
        }
        // Bản chi tiết luôn được đính kèm để vùng điền ngắn không làm mất dữ liệu hoặc chữ ký.
        var appendixTemplate = new ContractTemplate
        {
            Title = "PHỤ LỤC THÔNG TIN HỢP ĐỒNG",
            OrganizationName = template.OrganizationName,
            HeaderText = template.HeaderText,
            FooterText = template.FooterText,
            DefaultTerms = template.DefaultTerms
        };
        var appendix = Render(contract, employeeName, employeeTitle, employeePhone, products, terms, signature, preview, appendixTemplate);
        using var appendixPdf = PdfReader.Open(new MemoryStream(appendix), PdfDocumentOpenMode.Import);
        foreach (var page in appendixPdf.Pages) pdf.AddPage(page);
        using var output = new MemoryStream();
        pdf.Save(output, false);
        return output.ToArray();
    }
}
