using System.IO.Compression;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace OCS.Api.Services;

/// <summary>Kiểm tra tệp Word và các tham số văn bản trước khi lưu mẫu.</summary>
public static class WordTemplateValidator
{
    public const int MaximumFileBytes = 2 * 1024 * 1024;
    private const long MaximumExpandedBytes = 20 * 1024 * 1024;
    internal static readonly Regex Marker = new(@"(?<!\[)\[([^\[\]\r\n]+)\](?!\])", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static IEnumerable<Paragraph> Paragraphs(MainDocumentPart main) =>
        main.Document.Body!.Descendants<Paragraph>()
            .Concat(main.HeaderParts.SelectMany(part => part.Header?.Descendants<Paragraph>() ?? []))
            .Concat(main.FooterParts.SelectMany(part => part.Footer?.Descendants<Paragraph>() ?? []));

    public static IReadOnlyList<string> Validate(byte[] content, string kind)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length is 0 or > MaximumFileBytes) return ["Tệp Word phải có dung lượng không quá 2 MB."];
        var fields = kind switch { WordTemplateSamples.Contract => WordTemplateSamples.ContractFields, WordTemplateSamples.Product => WordTemplateSamples.ProductFields, _ => null };
        if (fields is null) return ["Loại mẫu không hợp lệ."];
        try
        {
            using var stream = new MemoryStream(content, false);
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                if (archive.Entries.Count > 1000 || archive.Entries.Sum(entry => entry.Length) > MaximumExpandedBytes)
                    return ["Tệp Word chứa quá nhiều dữ liệu sau giải nén."];
                if (archive.Entries.Any(entry => entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
                    return ["Mẫu Word không được chứa macro."];
            }
            stream.Position = 0;
            using var document = WordprocessingDocument.Open(stream, false);
            var main = document.MainDocumentPart;
            if (main?.Document?.Body is null || document.DocumentType != DocumentFormat.OpenXml.WordprocessingDocumentType.Document)
                return ["Vui lòng chọn tệp .docx hợp lệ, không có mật khẩu."];
            var pending = new Queue<OpenXmlPart>();
            var visited = new HashSet<OpenXmlPart>();
            pending.Enqueue(main);
            while (pending.TryDequeue(out var part))
            {
                if (!visited.Add(part)) continue;
                if (part.ExternalRelationships.Any() || part.HyperlinkRelationships.Any(link => link.IsExternal))
                    return ["Mẫu Word không được chứa liên kết hoặc tài nguyên bên ngoài. Vui lòng xóa liên kết rồi thử lại."];
                foreach (var child in part.Parts) pending.Enqueue(child.OpenXmlPart);
            }
            var paragraphs = Paragraphs(main).ToArray();
            var markers = paragraphs.SelectMany(paragraph => Marker.Matches(paragraph.InnerText).Select(match => (Paragraph: paragraph, Name: match.Groups[1].Value))).ToArray();
            var names = markers.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
            var errors = new List<string>();
            var allowed = fields.Values.ToHashSet(StringComparer.Ordinal);
            foreach (var unknown in names.Where(name => !allowed.Contains(name))) errors.Add($"Tham số không hợp lệ: [{unknown}].");
            var required = kind == WordTemplateSamples.Contract
                ? new[] { "contract.number", "customer.name", "customer.phone", "contract.ends_at", "contract.expiry", "customer.signature", "product.index", "product.name", "product.barcode", "product.price", "product.commission" }
                : new[] { "product.name", "product.price", "product.barcode", "product.barcode_image" };
            foreach (var key in required.Where(key => !names.Contains(fields[key]))) errors.Add($"Thiếu tham số: [{fields[key]}].");
            if (kind == WordTemplateSamples.Contract && !names.Contains(fields["contract.signed_at"]) &&
                new[] { "contract.signed_day", "contract.signed_month", "contract.signed_year" }.Any(key => !names.Contains(fields[key])))
                errors.Add("Cần [Ngày ký] hoặc đủ cả [Ngày], [Tháng] và [Năm].");
            var pictureName = fields[kind == WordTemplateSamples.Contract ? "customer.signature" : "product.barcode_image"];
            if (markers.Any(item => item.Name == pictureName && item.Paragraph.InnerText.Trim() != $"[{pictureName}]"))
                errors.Add($"Đặt [{pictureName}] riêng trên một dòng hoặc trong một ô bảng.");
            if (markers.Any(item => item.Name == pictureName && !item.Paragraph.Ancestors<Body>().Any()))
                errors.Add($"Đặt [{pictureName}] trong nội dung chính của tài liệu.");
            if (kind == WordTemplateSamples.Contract)
            {
                var productNames = fields.Where(field => field.Key.StartsWith("product.", StringComparison.Ordinal)).Select(field => field.Value).ToHashSet(StringComparer.Ordinal);
                var indices = markers.Where(item => item.Name == fields["product.index"]).ToArray();
                var row = indices.Length == 1 ? indices[0].Paragraph.Ancestors<TableRow>().FirstOrDefault() : null;
                if (row is null || markers.Where(item => productNames.Contains(item.Name)).Any(item => item.Paragraph.Ancestors<TableRow>().FirstOrDefault() != row))
                    errors.Add("Giữ một dòng mẫu sản phẩm và đặt các tham số sản phẩm trong dòng đó.");
            }
            else if (markers.Any(item => !item.Paragraph.Ancestors<Body>().Any()))
                errors.Add("Đặt các tham số sản phẩm trong nội dung chính, không đặt ở đầu trang hoặc chân trang.");
            return errors;
        }
        catch (Exception ex) when (ex is InvalidDataException or OpenXmlPackageException or IOException or ArgumentException or RegexMatchTimeoutException)
        {
            return ["Không thể đọc tệp .docx. Hãy lưu lại bằng Word rồi thử lại."];
        }
    }
}
