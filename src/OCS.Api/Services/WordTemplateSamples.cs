using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace OCS.Api.Services;

/// <summary>Tạo mẫu Word với các ô nội dung có tên ổn định, dùng để quản lý tự thay bố cục.</summary>
public static class WordTemplateSamples
{
    public const string Contract = "contract";
    public const string Product = "product";
    public const string TermsInstruction = "Quản lý cần thay nội dung này bằng điều khoản đã được cửa hàng phê duyệt trước khi sử dụng mẫu.";

    public static readonly IReadOnlyDictionary<string, string> ContractFields = new Dictionary<string, string>
    {
        ["contract.number"] = "Số hợp đồng",
        ["contract.signed_at"] = "Ngày ký",
        ["contract.signed_day"] = "Ngày",
        ["contract.signed_month"] = "Tháng",
        ["contract.signed_year"] = "Năm",
        ["contract.ends_at"] = "Ngày kết thúc",
        ["contract.expiry"] = "Xử lý hết hạn",
        ["contract.term_days"] = "Số ngày ký gửi",
        ["contract.product_count"] = "Số sản phẩm",
        ["contract.total_price"] = "Tổng giá niêm yết",
        ["contract.organization"] = "Tên cửa hàng",
        ["contract.store_address"] = "Địa chỉ cửa hàng",
        ["contract.title"] = "Tiêu đề hợp đồng",
        ["contract.header"] = "Nội dung đầu hợp đồng",
        ["contract.footer"] = "Nội dung cuối hợp đồng",
        ["contract.preview_notice"] = "Thông báo xem trước",
        ["employee.name"] = "Tên nhân viên",
        ["employee.title"] = "Chức vụ nhân viên",
        ["employee.phone"] = "Điện thoại nhân viên",
        ["customer.name"] = "Họ tên khách hàng",
        ["customer.phone"] = "Số điện thoại khách hàng",
        ["customer.email"] = "Email khách hàng",
        ["customer.identity"] = "CCCD / giấy tờ",
        ["customer.address"] = "Địa chỉ khách hàng",
        ["bank.code"] = "Ngân hàng",
        ["bank.number"] = "Số tài khoản",
        ["bank.holder"] = "Chủ tài khoản",
        ["customer.signature"] = "Chữ ký khách hàng",
        ["product.index"] = "STT",
        ["product.name"] = "Tên sản phẩm",
        ["product.barcode"] = "Mã sản phẩm",
        ["product.brand"] = "Thương hiệu",
        ["product.category"] = "Danh mục",
        ["product.size"] = "Kích thước",
        ["product.condition"] = "Độ mới",
        ["product.price"] = "Giá bán",
        ["product.commission"] = "Hoa hồng",
        ["product.description"] = "Mô tả sản phẩm",
        ["product.location"] = "Vị trí kho"
    };

    public static readonly IReadOnlyDictionary<string, string> ProductFields = new Dictionary<string, string>
    {
        ["product.name"] = "Tên sản phẩm",
        ["product.barcode"] = "Mã sản phẩm",
        ["product.barcode_image"] = "Hình barcode",
        ["product.brand"] = "Thương hiệu",
        ["product.category"] = "Danh mục",
        ["product.size"] = "Kích thước",
        ["product.condition"] = "Độ mới",
        ["product.price"] = "Giá bán",
        ["product.description"] = "Mô tả sản phẩm",
        ["product.status"] = "Trạng thái",
        ["product.location"] = "Vị trí kho",
        ["product.source"] = "Nguồn hàng"
    };

    public static byte[] Create(string kind, string? existingTerms = null)
    {
        if (kind is not (Contract or Product)) throw new ArgumentOutOfRangeException(nameof(kind));
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            main.Document = new Document(body);
            if (kind == Contract) BuildContract(body, main, existingTerms);
            else BuildProduct(body, main);
            // Present ordinary text markers that can be copied and moved in Word.
            foreach (var control in body.Descendants<SdtRun>().ToArray())
            {
                var label = control.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value ?? "";
                control.InsertBeforeSelf(new Run(new Text("[" + label + "]") { Space = SpaceProcessingModeValues.Preserve }));
                control.Remove();
            }
            body.Append(new SectionProperties(new PageSize { Width = 11906U, Height = 16838U },
                new PageMargin { Top = 1134, Right = 1134U, Bottom = 1134, Left = 1417U }));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static void BuildContract(Body body, MainDocumentPart main, string? existingTerms)
    {
        body.Append(Heading("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", true));
        body.Append(Heading("Độc lập - Tự do - Hạnh phúc", true));
        body.Append(new Paragraph(new Run(new Text("Hôm nay, ngày ") { Space = SpaceProcessingModeValues.Preserve }),
            Field("contract.signed_day", ContractFields["contract.signed_day"]), new Run(new Text(", tháng ") { Space = SpaceProcessingModeValues.Preserve }),
            Field("contract.signed_month", ContractFields["contract.signed_month"]), new Run(new Text(", năm ") { Space = SpaceProcessingModeValues.Preserve }),
            Field("contract.signed_year", ContractFields["contract.signed_year"]), new Run(new Text(", tại ") { Space = SpaceProcessingModeValues.Preserve }),
            Field("contract.store_address", ContractFields["contract.store_address"])));
        body.Append(Line(ContractFields, "contract.organization"));
        body.Append(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }), Field("contract.title", ContractFields["contract.title"])));
        body.Append(new Paragraph(Field("contract.header", ContractFields["contract.header"])));
        body.Append(new Paragraph(Field("contract.preview_notice", ContractFields["contract.preview_notice"])));
        foreach (var key in new[] { "contract.number", "contract.signed_at", "contract.ends_at", "contract.term_days", "contract.expiry", "contract.product_count", "contract.total_price" }) body.Append(Line(ContractFields, key));
        body.Append(Heading("NHÂN VIÊN LẬP HỢP ĐỒNG", false));
        foreach (var key in new[] { "employee.name", "employee.title", "employee.phone" }) body.Append(Line(ContractFields, key));
        body.Append(Heading("THÔNG TIN KHÁCH HÀNG VÀ NHẬN TIỀN", false));
        foreach (var key in new[] { "customer.name", "customer.phone", "customer.email", "customer.identity", "customer.address", "bank.code", "bank.number", "bank.holder" }) body.Append(Line(ContractFields, key));
        body.Append(Heading("DANH SÁCH SẢN PHẨM", false));
        var table = new Table(new TableProperties(new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new TableBorders(new TopBorder { Val = BorderValues.Single, Size = 4 }, new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 }, new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 }, new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));
        table.Append(new TableGrid(new GridColumn { Width = "500" }, new GridColumn { Width = "4200" },
            new GridColumn { Width = "1400" }, new GridColumn { Width = "1600" }, new GridColumn { Width = "1200" }));
        table.Append(new TableRow(new TableRowProperties(new TableHeader()), Cell("STT"), Cell("Sản phẩm / thông tin"), Cell("Độ mới"), Cell("Giá bán"), Cell("Hoa hồng")));
        var details = new TableCell();
        foreach (var key in new[] { "product.name", "product.barcode", "product.brand", "product.category", "product.size", "product.description", "product.location" }) details.Append(Line(ContractFields, key));
        table.Append(new TableRow(Cell(ContractFields, "product.index"), details, Cell(ContractFields, "product.condition"), Cell(ContractFields, "product.price"), Cell(ContractFields, "product.commission")));
        body.Append(table);
        body.Append(Heading("ĐIỀU KHOẢN KÝ GỬI", false));
        foreach (var line in (string.IsNullOrWhiteSpace(existingTerms) ? TermsInstruction : existingTerms).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
            body.Append(new Paragraph(new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve })));
        body.Append(new Paragraph(Field("contract.footer", ContractFields["contract.footer"])));
        body.Append(Heading("CHỮ KÝ KHÁCH HÀNG", false));
        body.Append(new Paragraph(Field("customer.signature", ContractFields["customer.signature"])));
    }

    private static void BuildProduct(Body body, MainDocumentPart main)
    {
        body.Append(Heading("THÔNG TIN SẢN PHẨM", true));
        foreach (var key in new[] { "product.name", "product.price", "product.barcode", "product.brand", "product.category", "product.size", "product.condition", "product.description", "product.status", "product.location", "product.source" }) body.Append(Line(ProductFields, key));
        body.Append(new Paragraph(Field("product.barcode_image", ProductFields["product.barcode_image"])));
    }

    private static Paragraph Heading(string text, bool centered) => new(new ParagraphProperties(
        new Justification { Val = centered ? JustificationValues.Center : JustificationValues.Left }),
        new Run(new RunProperties(new Bold()), new Text(text)));
    private static Paragraph Line(IReadOnlyDictionary<string, string> fields, string key) =>
        new(new Run(new Text(fields[key] + ": ") { Space = SpaceProcessingModeValues.Preserve }), Field(key, fields[key]));
    private static TableCell Cell(string text) => new(new Paragraph(new Run(new RunProperties(new Bold()), new Text(text))));
    private static TableCell Cell(IReadOnlyDictionary<string, string> fields, string key) => new(new Paragraph(Field(key, fields[key])));
    private static SdtRun Field(string key, string label) => new(
        new SdtProperties(new SdtAlias { Val = label }, new Tag { Val = "ocs." + key }, new SdtContentText()),
        new SdtContentRun(new Run(new Text("[" + label + "]"))));

    internal static Run CreatePictureRun(MainDocumentPart main, byte[] image, string label, long width, long height)
    {
        var isPng = image.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
        var isJpeg = image.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff });
        if (!isPng && !isJpeg) throw new ArgumentException("Ảnh phải là PNG hoặc JPEG.", nameof(image));
        var part = main.AddImagePart(isPng ? ImagePartType.Png : ImagePartType.Jpeg);
        using (var input = new MemoryStream(image)) part.FeedData(input);
        var relationship = main.GetIdOfPart(part);
        var drawing = new Drawing(new DW.Inline(
            new DW.Extent { Cx = width, Cy = height },
            new DW.DocProperties { Id = 1U, Name = label, Description = label },
            new A.Graphic(new A.GraphicData(new PIC.Picture(
                new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = 0U, Name = label }, new PIC.NonVisualPictureDrawingProperties()),
                new PIC.BlipFill(new A.Blip { Embed = relationship }, new A.Stretch(new A.FillRectangle())),
                new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = width, Cy = height }),
                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle },
                    new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = "888888" })) { Width = 6350 }))
            )
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
        ));
        return new Run(drawing);
    }
}
