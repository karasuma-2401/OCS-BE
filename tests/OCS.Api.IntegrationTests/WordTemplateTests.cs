using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Context;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Services;
using System.Security.Claims;
using System.Buffers.Binary;
using System.IO.Compression;

namespace OCS.Api.IntegrationTests;

public sealed class WordTemplateTests
{
    [Theory]
    [InlineData(WordTemplateSamples.Contract)]
    [InlineData(WordTemplateSamples.Product)]
    public void SampleIsValidAndUsesOrdinaryText(string kind)
    {
        var bytes = WordTemplateSamples.Create(kind);
        Assert.Empty(WordTemplateValidator.Validate(bytes, kind));
        using var document = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(document.MainDocumentPart!.Document.Body!.Descendants<SdtRun>());
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    [Fact]
    public void ReportsMissingAndUnknownMarkers()
    {
        var bytes = WordTemplateSamples.Create(WordTemplateSamples.Contract);
        using var stream = new MemoryStream(); stream.Write(bytes); stream.Position = 0;
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            var text = document.MainDocumentPart!.Document.Body!.Descendants<Text>().Single(t => t.Text == "[Họ tên khách hàng]");
            text.Text = "[Tên sai]";
            document.MainDocumentPart.Document.Save();
        }
        var errors = WordTemplateValidator.Validate(stream.ToArray(), WordTemplateSamples.Contract);
        Assert.Contains(errors, message => message.Contains("Họ tên khách hàng", StringComparison.Ordinal));
        Assert.Contains(errors, message => message.Contains("Tên sai", StringComparison.Ordinal));
    }

    [Fact]
    public void FillsSplitMarkersAndRepeatsRows()
    {
        var template = WordTemplateSamples.Create(WordTemplateSamples.Contract);
        using var stream = new MemoryStream(); stream.Write(template); stream.Position = 0;
        using (var document = WordprocessingDocument.Open(stream, true))
        {
            var text = document.MainDocumentPart!.Document.Body!.Descendants<Text>().Single(t => t.Text == "[Họ tên khách hàng]");
            var run = (Run)text.Parent!; text.Text = "[Họ tên "; run.InsertAfterSelf(new Run(new Text("khách hàng]")));
            document.MainDocumentPart.Document.Save();
        }
        var values = new Dictionary<string, string?> { ["customer.name"] = "Trần Ánh" };
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows =
        [new Dictionary<string, string?> { ["product.index"] = "1", ["product.name"] = "Áo khoác" },
         new Dictionary<string, string?> { ["product.index"] = "2", ["product.name"] = "Váy đỏ" }];
        var result = WordTemplateFiller.Fill(stream.ToArray(), WordTemplateSamples.Contract, values, rows);
        using var filled = WordprocessingDocument.Open(new MemoryStream(result), false);
        var body = filled.MainDocumentPart!.Document.Body!;
        Assert.Contains("Trần Ánh", body.InnerText);
        Assert.Equal(3, body.Descendants<TableRow>().Count());
        Assert.Contains("Áo khoác", body.Descendants<TableRow>().ElementAt(1).InnerText);
        Assert.Contains("Váy đỏ", body.Descendants<TableRow>().ElementAt(2).InnerText);
    }

    [Fact]
    public void InsertsSignatureIntoWord()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        var result = WordTemplateFiller.Fill(WordTemplateSamples.Create(WordTemplateSamples.Contract), WordTemplateSamples.Contract,
            new Dictionary<string, string?>(), images: new Dictionary<string, byte[]> { ["customer.signature"] = png });
        using var document = WordprocessingDocument.Open(new MemoryStream(result), false);
        Assert.Single(document.MainDocumentPart!.Document.Body!.Descendants<Drawing>());
        Assert.DoesNotContain("[Chữ ký khách hàng]", document.MainDocumentPart.Document.Body.InnerText);
    }

    [Fact]
    public void ProductDocumentContainsSeparateProductsAndScannableCode128()
    {
        const string code1 = "OCS-1234567890ABCDEF";
        const string code2 = "OCS-FEDCBA0987654321";
        var image1 = WordBarcodeImage.Create(code1);
        var image2 = WordBarcodeImage.Create(code2);
        Assert.Equal(code1, DecodePng(image1));
        Assert.Equal(code2, DecodePng(image2));
        var first = new Product { Name = "Áo khoác", Barcode = code1, Size = "M", ListedPrice = 125000, ConditionPercentage = 90 };
        var second = new Product { Name = "Váy đỏ", Barcode = code2, Size = "S", ListedPrice = 250000, ConditionPercentage = 95 };
        var result = WordTemplateFiller.FillProducts(WordTemplateSamples.Create(WordTemplateSamples.Product),
            [WordTemplateData.Product(first, "Nhãn A", "Áo", "Kệ 1"), WordTemplateData.Product(second, "Nhãn B", "Váy", "Kệ 2")], [image1, image2]);
        using var document = WordprocessingDocument.Open(new MemoryStream(result), false);
        var body = document.MainDocumentPart!.Document.Body!;
        Assert.Contains("Áo khoác", body.InnerText);
        Assert.Contains("Váy đỏ", body.InnerText);
        Assert.Equal(2, body.Descendants<Drawing>().Count());
        Assert.DoesNotContain("[Hình barcode]", body.InnerText);
        Assert.Empty(new OpenXmlValidator().Validate(document));
    }

    private static string? DecodePng(byte[] png)
    {
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        using var compressed = new MemoryStream();
        for (var offset = 8; offset < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT") compressed.Write(png, offset + 8, length);
            offset += length + 12;
        }
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream(); zlib.CopyTo(raw);
        var scanlines = raw.ToArray();
        var gray = new byte[width * height];
        for (var row = 0; row < height; row++) Array.Copy(scanlines, row * (width + 1) + 1, gray, row * width, width);
        return new ZXing.BarcodeReaderGeneric().Decode(gray, width, height, ZXing.RGBLuminanceSource.BitmapFormat.Gray8)?.Text;
    }

    [Fact]
    public async Task DraftSaveRejectsInvalidFilesAndStaleVersions()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var controller = new WordTemplateSamplesController(db) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "test")) } } };
        var bytes = WordTemplateSamples.Create(WordTemplateSamples.Contract);
        using (var stream = new MemoryStream())
        {
            stream.Write(bytes); stream.Position = 0;
            using (var word = WordprocessingDocument.Open(stream, true))
            {
                word.MainDocumentPart!.Document.Body!.Descendants<Text>().Single(text => text.Text == WordTemplateSamples.TermsInstruction).Text = "Điều khoản mẫu để kiểm thử.";
                word.MainDocumentPart.Document.Save();
            }
            bytes = stream.ToArray();
        }
        IFormFile FileFrom(byte[] content) => new FormFile(new MemoryStream(content), 0, content.Length, "file", "mau.docx");
        Assert.IsType<OkObjectResult>(await controller.Save(WordTemplateSamples.Contract, FileFrom(bytes), Guid.Empty.ToString(), CancellationToken.None));
        var saved = await db.WordDocumentTemplates.SingleAsync();
        Assert.Equal(bytes, saved.Content);
        Assert.IsType<ConflictObjectResult>(await controller.Save(WordTemplateSamples.Contract, FileFrom(bytes), Guid.Empty.ToString(), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Save(WordTemplateSamples.Contract, FileFrom([1, 2, 3]), saved.Version.ToString(), CancellationToken.None));
        Assert.Equal(bytes, (await db.WordDocumentTemplates.SingleAsync()).Content);
    }

    [Fact]
    public async Task ProductPreviewUsesStoredValuesAndRendersPdfAndWord()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var brand = new Brand { Name = "Thương hiệu" };
        var category = new OCS.Api.Entities.Category { Name = "Áo" };
        var product = new Product { Name = "Áo xanh", Barcode = "OCS-1234567890ABCDEF", Size = "M", ListedPrice = 180000, Brand = brand, Category = category };
        db.AddRange(brand, category, product);
        await db.SaveChangesAsync();
        var controller = new WordProductDocumentsController(db);
        var request = new WordProductDocumentRequest([product.Id]);
        
        // Preview defaults to PDF (even with sample fallback when template is not yet in DB)
        var pdfResponse = Assert.IsType<FileContentResult>(await controller.Preview(request));
        Assert.Equal("application/pdf", pdfResponse.ContentType);
        Assert.True(pdfResponse.FileContents.AsSpan().StartsWith("%PDF"u8));

        db.WordDocumentTemplates.Add(new WordDocumentTemplate { Kind = WordTemplateSamples.Product, Content = WordTemplateSamples.Create(WordTemplateSamples.Product), Version = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var wordResponse = Assert.IsType<FileContentResult>(await controller.DownloadWord(request));
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", wordResponse.ContentType);
        using var document = WordprocessingDocument.Open(new MemoryStream(wordResponse.FileContents), false);
        Assert.Contains("Áo xanh", document.MainDocumentPart!.Document.Body!.InnerText);
        Assert.Contains("180.000 đ", document.MainDocumentPart.Document.Body.InnerText);
        Assert.Single(document.MainDocumentPart.Document.Body.Descendants<Drawing>());
        Assert.IsType<NotFoundObjectResult>(await controller.Preview(new WordProductDocumentRequest([Guid.NewGuid()])));
    }
}
