using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Authorization;
using OCS.Api.Context;
using OCS.Api.Services;

namespace OCS.Api.Controllers;

/// <summary>Tạo phiếu sản phẩm và barcode theo mẫu phiếu của cửa hàng.</summary>
[ApiController]
[Route("api/product-documents")]
[Authorize(Roles = "Admin,Manager,Staff")]
[Authorize(Policy = PermissionCatalog.ProductsRead)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WordProductDocumentsController(AppDbContext db) : ControllerBase
{
    /// <summary>Xem trước phiếu sản phẩm dưới dạng PDF hoặc Word.</summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Preview([FromBody] WordProductDocumentRequest request, [FromQuery] string? format = null, CancellationToken cancellationToken = default)
    {
        if (request.ProductIds is null || request.ProductIds.Count is 0 or > 100 || request.ProductIds.Distinct().Count() != request.ProductIds.Count)
            return BadRequest(new ProblemDetails { Title = "Chọn từ 1 đến 100 sản phẩm khác nhau." });

        var template = await db.WordDocumentTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Kind == WordTemplateSamples.Product, cancellationToken);
        var templateBytes = template?.Content ?? WordTemplateSamples.Create(WordTemplateSamples.Product);

        var products = await db.Products.AsNoTracking().Include(x => x.Brand).Include(x => x.Category).Include(x => x.StorageLocation)
            .Where(x => request.ProductIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (products.Count != request.ProductIds.Count) return NotFound(new ProblemDetails { Title = "Một số sản phẩm không còn tồn tại." });

        try
        {
            var items = request.ProductIds.Select(id => products[id]).ToArray();
            var values = items.Select(item => WordTemplateData.Product(item, item.Brand.Name, item.Category.Name, item.StorageLocation?.Name)).ToArray();
            var images = items.Select(item => WordBarcodeImage.Create(item.Barcode)).ToArray();

            if (string.Equals(format, "word", StringComparison.OrdinalIgnoreCase))
            {
                var content = WordTemplateFiller.FillProducts(templateBytes, values, images);
                return File(content, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "phieu-san-pham.docx");
            }

            var pdfItems = items.Select(item => (item, item.Brand.Name, item.Category.Name, item.StorageLocation?.Name)).ToArray();
            var pdf = BarcodePdfRenderer.Render(pdfItems, images);
            return File(pdf, "application/pdf", "phieu-san-pham.pdf");
        }
        catch (ArgumentException)
        {
            return BadRequest(new ProblemDetails { Title = "Mẫu hoặc barcode của sản phẩm không hợp lệ." });
        }
    }

    /// <summary>Tải tệp Word gồm các phiếu sản phẩm được chọn.</summary>
    [HttpPost("word")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public async Task<IActionResult> DownloadWord([FromBody] WordProductDocumentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ProductIds is null || request.ProductIds.Count is 0 or > 100 || request.ProductIds.Distinct().Count() != request.ProductIds.Count)
            return BadRequest(new ProblemDetails { Title = "Chọn từ 1 đến 100 sản phẩm khác nhau." });

        var template = await db.WordDocumentTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Kind == WordTemplateSamples.Product, cancellationToken);
        var templateBytes = template?.Content ?? WordTemplateSamples.Create(WordTemplateSamples.Product);

        var products = await db.Products.AsNoTracking().Include(x => x.Brand).Include(x => x.Category).Include(x => x.StorageLocation)
            .Where(x => request.ProductIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        if (products.Count != request.ProductIds.Count) return NotFound(new ProblemDetails { Title = "Một số sản phẩm không còn tồn tại." });

        try
        {
            var items = request.ProductIds.Select(id => products[id]).ToArray();
            var values = items.Select(item => WordTemplateData.Product(item, item.Brand.Name, item.Category.Name, item.StorageLocation?.Name)).ToArray();
            var images = items.Select(item => WordBarcodeImage.Create(item.Barcode)).ToArray();
            var content = WordTemplateFiller.FillProducts(templateBytes, values, images);
            return File(content, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "phieu-san-pham.docx");
        }
        catch (ArgumentException)
        {
            return BadRequest(new ProblemDetails { Title = "Mẫu hoặc barcode của sản phẩm không hợp lệ." });
        }
    }
}

/// <summary>Sản phẩm cần tạo phiếu, theo thứ tự được chọn.</summary>
public sealed record WordProductDocumentRequest(List<Guid> ProductIds);
