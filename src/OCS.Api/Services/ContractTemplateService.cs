using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Contracts;
using System.Text.Json;
using PdfSharp.Pdf.IO;

namespace OCS.Api.Services;

public sealed class ContractTemplateService(AppDbContext db) : IContractTemplateService
{
    public async Task<ContractTemplateResponse> GetAsync(CancellationToken cancellationToken) =>
        Map(await db.ContractTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken) ?? new ContractTemplate());

    public async Task<Result<ContractTemplateResponse>> UpdateAsync(UpdateContractTemplateRequest request, Guid actorId, CancellationToken cancellationToken)
    {
        var error = Validate(request);
        if (error is not null) return Result.Failure<ContractTemplateResponse>(error);
        var template = await db.ContractTemplates.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if ((template?.Version ?? Guid.Empty) != request.Version)
            return Result.Failure<ContractTemplateResponse>(ApplicationError.Conflict("CONTRACT_TEMPLATE.CHANGED", "Mẫu đã thay đổi. Hãy tải lại trước khi lưu."));
        if (template is null) { template = new ContractTemplate(); db.ContractTemplates.Add(template); }
        Apply(template, request);
        template.Version = Guid.NewGuid();
        template.UpdatedAt = DateTimeOffset.UtcNow;
        template.UpdatedBy = actorId;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Result.Failure<ContractTemplateResponse>(ApplicationError.Conflict("CONTRACT_TEMPLATE.CHANGED", "Mẫu đã thay đổi. Hãy tải lại trước khi lưu.")); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Result.Failure<ContractTemplateResponse>(ApplicationError.Conflict("CONTRACT_TEMPLATE.CHANGED", "Mẫu đã thay đổi. Hãy tải lại trước khi lưu.")); }
        return Result.Success(Map(template));
    }

    public Result<byte[]> Preview(UpdateContractTemplateRequest request)
    {
        var error = Validate(request);
        if (error is not null) return Result.Failure<byte[]>(error);
        var template = new ContractTemplate(); Apply(template, request);
        var contract = new ConsignmentContract { ContractNumber = "HD-MAU-001", CustomerNameSnapshot = "Khách hàng mẫu", CustomerPhoneSnapshot = "0900000000",
            SignedAt = DateTimeOffset.UtcNow, EndsAt = DateTimeOffset.UtcNow.AddMonths(3), ExpiryHandling = ExpiryHandling.Return };
        var brand = new Brand { Name = "Thương hiệu mẫu", DefaultCommissionRate = 20 };
        var product = new Product { Name = "Sản phẩm mẫu", Barcode = "MÃ SẢN PHẨM", Size = "M", ConditionPercentage = 90 };
        product.AssignConsignmentTerms(contract.Id, 250000, brand, 20);
        var products = new List<(Product Product, string Brand, string Category)> { (product, brand.Name, "Danh mục mẫu") };
        return Result.Success(ContractPdfRenderer.Render(contract, "Nhân viên mẫu", "Nhân viên", "0900000001", products, template.DefaultTerms, null, true, template));
    }

    private static ApplicationError? Validate(UpdateContractTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200 || request.OrganizationName is null || request.OrganizationName.Length > 200 ||
        request.HeaderText is null || request.HeaderText.Length > 1000 || request.FooterText is null || request.FooterText.Length > 1000 ||
        request.DefaultTerms is null || request.DefaultTerms.Length > 6000)
            return ApplicationError.Validation("CONTRACT_TEMPLATE.INVALID", "Vui lòng kiểm tra tiêu đề và độ dài nội dung mẫu.");
        if (request.FieldMappings is null || request.FieldMappings.Count > 40)
            return ApplicationError.Validation("CONTRACT_TEMPLATE.INVALID", "Tối đa 40 vùng điền dữ liệu.");
        if (string.IsNullOrEmpty(request.PdfBase64))
            return request.FieldMappings.Count == 0 ? null : ApplicationError.Validation("CONTRACT_TEMPLATE.INVALID", "Cần tải PDF nền trước khi thiết lập vùng dữ liệu.");
        if (request.PdfBase64.Length > 2_800_000)
            return ApplicationError.Validation("CONTRACT_TEMPLATE.INVALID", "Mẫu PDF không được vượt quá 2 MB.");
        try
        {
            var bytes = Convert.FromBase64String(request.PdfBase64);
            if (bytes.Length > 2 * 1024 * 1024) throw new FormatException();
            using var pdf = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
            if (pdf.PageCount is < 1 or > 20) throw new FormatException();
            foreach (var field in request.FieldMappings)
                if (field is null || !ContractPdfRenderer.Fields.Contains(field.Field) || field.Page < 1 || field.Page > pdf.PageCount ||
                    !double.IsFinite(field.X) || !double.IsFinite(field.Y) || !double.IsFinite(field.Width) || !double.IsFinite(field.Height) ||
                    field.X < 0 || field.Y < 0 || field.Width <= 0 || field.Height <= 0 || field.X + field.Width > 100 || field.Y + field.Height > 100)
                    return ApplicationError.Validation("CONTRACT_TEMPLATE.INVALID", "Vùng dữ liệu không hợp lệ hoặc nằm ngoài trang PDF.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ApplicationError.Validation("CONTRACT_TEMPLATE.INVALID", "PDF không hợp lệ, có mật khẩu, vượt quá 2 MB hoặc quá 20 trang.");
        }
        return null;
    }

    private static void Apply(ContractTemplate entity, UpdateContractTemplateRequest request)
    {
        entity.Title = request.Title.Trim(); entity.OrganizationName = request.OrganizationName.Trim();
        entity.HeaderText = request.HeaderText.Trim(); entity.FooterText = request.FooterText.Trim(); entity.DefaultTerms = request.DefaultTerms.Trim();
        entity.PdfBackground = string.IsNullOrEmpty(request.PdfBase64) ? null : Convert.FromBase64String(request.PdfBase64);
        entity.FieldMappingsJson = JsonSerializer.Serialize(request.FieldMappings);
    }

    private static ContractTemplateResponse Map(ContractTemplate x) => new(x.Title, x.OrganizationName, x.HeaderText, x.FooterText, x.DefaultTerms, x.Version, x.UpdatedAt,
        x.PdfBackground is null ? null : Convert.ToBase64String(x.PdfBackground), JsonSerializer.Deserialize<List<ContractPdfFieldMapping>>(x.FieldMappingsJson) ?? []);
}
