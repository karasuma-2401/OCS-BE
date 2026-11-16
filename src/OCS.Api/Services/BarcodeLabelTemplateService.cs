using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Products;

namespace OCS.Api.Services;

public sealed class BarcodeLabelTemplateService(AppDbContext db) : IBarcodeLabelTemplateService
{
    public async Task<BarcodeLabelTemplateDto> GetAsync(CancellationToken cancellationToken) =>
        Map(await db.BarcodeLabelTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken) ?? new BarcodeLabelTemplate());

    public async Task<Result<BarcodeLabelTemplateDto>> UpdateAsync(BarcodeLabelTemplateDto request, CancellationToken cancellationToken)
    {
        if (request.WidthMm is < 40 or > 90 || request.HeightMm is < 25 or > 60 || request.Columns is < 1 or > 4 ||
            request.FontSizePt is < 6 or > 12 || request.Columns * request.WidthMm + (request.Columns - 1) * 3 > 198)
            return Result.Failure<BarcodeLabelTemplateDto>(ApplicationError.Validation("BARCODE_TEMPLATE.INVALID", "Kích thước hoặc số cột không phù hợp với giấy A4."));
        var template = await db.BarcodeLabelTemplates.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if ((template?.Version ?? Guid.Empty) != request.Version)
            return Result.Failure<BarcodeLabelTemplateDto>(ApplicationError.Conflict("BARCODE_TEMPLATE.CHANGED", "Mẫu in đã thay đổi. Hãy tải lại trước khi lưu."));
        if (template is null) { template = new BarcodeLabelTemplate(); db.BarcodeLabelTemplates.Add(template); }
        template.WidthMm = request.WidthMm; template.HeightMm = request.HeightMm; template.Columns = request.Columns; template.FontSizePt = request.FontSizePt;
        template.Version = Guid.NewGuid(); template.UpdatedAt = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Result.Failure<BarcodeLabelTemplateDto>(ApplicationError.Conflict("BARCODE_TEMPLATE.CHANGED", "Mẫu in đã thay đổi. Hãy tải lại trước khi lưu.")); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Result.Failure<BarcodeLabelTemplateDto>(ApplicationError.Conflict("BARCODE_TEMPLATE.CHANGED", "Mẫu in đã thay đổi. Hãy tải lại trước khi lưu.")); }
        return Result.Success(Map(template));
    }

    private static BarcodeLabelTemplateDto Map(BarcodeLabelTemplate x) => new(x.WidthMm, x.HeightMm, x.Columns, x.FontSizePt, x.Version, x.UpdatedAt);
}
