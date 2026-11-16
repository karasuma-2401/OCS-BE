using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using System.Text.RegularExpressions;
using System.Globalization;
using PdfSharp.Drawing;

namespace OCS.Api.Services;

/// <summary>Điền tham số văn bản trong bản sao mẫu Word.</summary>
public static class WordTemplateFiller
{
    public static byte[] FillProducts(byte[] source, IReadOnlyList<IReadOnlyDictionary<string, string?>> products, IReadOnlyList<byte[]> barcodes)
    {
        var errors = WordTemplateValidator.Validate(source, WordTemplateSamples.Product);
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors), nameof(source));
        if (products.Count == 0 || products.Count != barcodes.Count) throw new ArgumentException("Danh sách sản phẩm không hợp lệ.", nameof(products));
        using var output = new MemoryStream(); output.Write(source); output.Position = 0;
        using (var document = WordprocessingDocument.Open(output, true))
        {
            var main = document.MainDocumentPart!;
            var body = main.Document.Body!;
            var sourceElements = body.ChildElements.Where(element => element is not SectionProperties).Select(element => element.CloneNode(true)).ToArray();
            var section = body.GetFirstChild<SectionProperties>()?.CloneNode(true);
            body.RemoveAllChildren();
            for (var index = 0; index < products.Count; index++)
            {
                if (index != 0) body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                var elements = sourceElements.Select(element => element.CloneNode(true)).ToArray();
                foreach (var element in elements) body.Append(element);
                var paragraphs = elements.SelectMany(element => element is Paragraph paragraph ? new[] { paragraph }.Concat(paragraph.Descendants<Paragraph>()) : element.Descendants<Paragraph>()).ToArray();
                foreach (var paragraph in paragraphs)
                {
                    if (paragraph.InnerText.Trim() == "[" + WordTemplateSamples.ProductFields["product.barcode_image"] + "]")
                    {
                        paragraph.RemoveAllChildren<Run>();
                        paragraph.Append(Picture(main, paragraph, barcodes[index], "Barcode", 2743200L));
                    }
                    else FillText([paragraph], WordTemplateSamples.ProductFields, products[index]);
                }
            }
            if (section is not null) body.Append(section);
            AssignDrawingIds(body);
            main.Document.Save();
        }
        return output.ToArray();
    }

    public static byte[] Fill(byte[] source, string kind, IReadOnlyDictionary<string, string?> values,
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? productRows = null,
        IReadOnlyDictionary<string, byte[]>? images = null)
    {
        var errors = WordTemplateValidator.Validate(source, kind);
        if (errors.Count != 0) throw new ArgumentException(string.Join(" ", errors), nameof(source));
        using var output = new MemoryStream();
        output.Write(source);
        output.Position = 0;
        using (var document = WordprocessingDocument.Open(output, true))
        {
            var main = document.MainDocumentPart!;
            var body = main.Document.Body!;
            var fields = kind == WordTemplateSamples.Contract ? WordTemplateSamples.ContractFields : WordTemplateSamples.ProductFields;
            if (kind == WordTemplateSamples.Contract)
            {
                var marker = "[" + fields["product.index"] + "]";
                var original = body.Descendants<Paragraph>().Single(paragraph => paragraph.InnerText.Contains(marker, StringComparison.Ordinal)).Ancestors<TableRow>().First();
                foreach (var row in productRows ?? [])
                {
                    var clone = (TableRow)original.CloneNode(true);
                    FillText(clone.Descendants<Paragraph>(), fields, row);
                    original.InsertBeforeSelf(clone);
                }
                original.Remove();
            }
            foreach (var paragraph in WordTemplateValidator.Paragraphs(main).ToArray())
            {
                var pictureKey = kind == WordTemplateSamples.Contract ? "customer.signature" : "product.barcode_image";
                var pictureLabel = fields[pictureKey];
                if (paragraph.InnerText.Trim() == "[" + pictureLabel + "]")
                {
                    paragraph.RemoveAllChildren<Run>();
                    if (images is not null && images.TryGetValue(pictureKey, out var bytes))
                        paragraph.Append(Picture(main, paragraph, bytes, pictureLabel,
                            pictureKey == "customer.signature" ? 1828800L : 2743200L));
                    continue;
                }
                FillText([paragraph], fields, values);
            }
            AssignDrawingIds(body);
            main.Document.Save();
        }
        return output.ToArray();
    }

    private static void AssignDrawingIds(Body body)
    {
        uint id = 1;
        foreach (var properties in body.Descendants<DW.DocProperties>()) properties.Id = id++;
    }

    private static Run Picture(MainDocumentPart main, Paragraph paragraph, byte[] bytes, string label, long defaultWidth)
    {
        var width = defaultWidth;
        var cellWidth = paragraph.Ancestors<TableCell>().FirstOrDefault()?.TableCellProperties?.TableCellWidth;
        if (cellWidth?.Type?.Value == TableWidthUnitValues.Dxa && long.TryParse(cellWidth.Width?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var twips) && twips > 200)
            width = Math.Min(5486400L, twips * 635L - 127000L);
        using var image = XImage.FromStream(new MemoryStream(bytes));
        var height = Math.Max(1L, (long)(width * (double)image.PixelHeight / image.PixelWidth));
        const long maximumHeight = 914400L;
        if (height > maximumHeight) { width = Math.Max(1L, width * maximumHeight / height); height = maximumHeight; }
        return WordTemplateSamples.CreatePictureRun(main, bytes, label, width, height);
    }

    private static void FillText(IEnumerable<Paragraph> paragraphs, IReadOnlyDictionary<string, string> fields, IReadOnlyDictionary<string, string?> values)
    {
        var labels = fields.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
        foreach (var paragraph in paragraphs)
        {
            var nodes = paragraph.Descendants<Text>().ToArray();
            var text = string.Concat(nodes.Select(node => node.Text));
            var matches = Regex.Matches(text, @"\[\[([^\[\]\r\n]+)\]\]|\[([^\[\]\r\n]+)\]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Cast<Match>().ToArray();
            foreach (var match in matches.Reverse())
            {
                string replacement;
                if (match.Groups[1].Success) replacement = "[" + match.Groups[1].Value + "]";
                else
                {
                    if (!labels.TryGetValue(match.Groups[2].Value, out var key) || !values.TryGetValue(key, out var value)) continue;
                    var optional = key is "contract.organization" or "contract.header" or "contract.footer" or "contract.preview_notice";
                    replacement = string.IsNullOrWhiteSpace(value) ? (optional ? "" : "Chưa cung cấp") : value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                }
                var offset = 0;
                for (var index = 0; index < nodes.Length; index++)
                {
                    var node = nodes[index];
                    var start = offset;
                    var end = start + node.Text.Length;
                    offset = end;
                    var overlapStart = Math.Max(start, match.Index);
                    var overlapEnd = Math.Min(end, match.Index + match.Length);
                    if (overlapStart >= overlapEnd) continue;
                    var prefix = node.Text[..(overlapStart - start)];
                    var suffix = node.Text[(overlapEnd - start)..];
                    node.Text = prefix + (overlapStart == match.Index ? replacement : "") + suffix;
                    node.Space = SpaceProcessingModeValues.Preserve;
                }
            }
            foreach (var node in nodes.Where(node => node.Text.Contains('\n')))
            {
                var lines = node.Text.Split('\n');
                node.Text = lines[0];
                OpenXmlElement current = node;
                foreach (var line in lines.Skip(1))
                {
                    var lineBreak = new Break(); current.InsertAfterSelf(lineBreak);
                    var next = new Text(line) { Space = SpaceProcessingModeValues.Preserve }; lineBreak.InsertAfterSelf(next);
                    current = next;
                }
            }
        }
    }
}
