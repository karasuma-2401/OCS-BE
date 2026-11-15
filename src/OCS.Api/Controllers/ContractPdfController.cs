using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OCS.Api.Authorization;
using OCS.Api.Interfaces.Services;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Extensions;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Consts;

namespace OCS.Api.Controllers;

/// <summary>Xem tài liệu hợp đồng của quản lý hoặc nhân viên tiếp nhận.</summary>
[ApiController]
[Route("api/contracts")]
[Authorize(Roles = "Admin,Manager,Staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ContractPdfController(IContractService contracts, IAuthorizationService authorization, AppDbContext db) : ControllerBase
{
    /// <summary>Dữ liệu để hiển thị hợp đồng trên web.</summary>
    [HttpGet("{id}/web")]
    public async Task<IActionResult> Web(string id, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(id, out var guid)) return await Web(guid, cancellationToken);
        var contract = await db.ConsignmentContracts.AsNoTracking().FirstOrDefaultAsync(x => x.ContractNumber == id, cancellationToken);
        if (contract is null) return NotFound();
        return await Web(contract.Id, cancellationToken);
    }

    [HttpGet("{id:guid}/web")]
    public async Task<IActionResult> Web(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("Staff") &&
            !(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded) return Forbid();
        var contract = await db.ConsignmentContracts.AsNoTracking()
            .Include(x => x.Customer).ThenInclude(x => x!.Account)
            .Include(x => x.Appointment).ThenInclude(x => x!.Customer).ThenInclude(x => x!.Account)
            .Include(x => x.Products).ThenInclude(x => x.Images)
            .Include(x => x.Products).ThenInclude(x => x.Category)
            .Include(x => x.Products).ThenInclude(x => x.Brand)
            .Include(x => x.CreatedByEmployee).ThenInclude(x => x.Account)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (contract is null) return NotFound();

        Account? directCustomerAccount = null;
        CustomerProfile? directCustomerProfile = null;
        if (contract.CustomerId.HasValue)
        {
            directCustomerAccount = contract.Customer?.Account ?? await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == contract.CustomerId.Value, cancellationToken);
            directCustomerProfile = contract.Customer ?? await db.CustomerProfiles.AsNoTracking().SingleOrDefaultAsync(cp => cp.Id == contract.CustomerId.Value, cancellationToken);
        }

        Account? directEmployeeAccount = null;
        EmployeeProfile? directEmployeeProfile = null;
        if (contract.CreatedByEmployeeId != Guid.Empty)
        {
            directEmployeeAccount = contract.CreatedByEmployee?.Account ?? await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == contract.CreatedByEmployeeId, cancellationToken);
            directEmployeeProfile = contract.CreatedByEmployee ?? await db.EmployeeProfiles.AsNoTracking().SingleOrDefaultAsync(ep => ep.Id == contract.CreatedByEmployeeId, cancellationToken);
        }

        var document = await db.ContractDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.ContractId == id, cancellationToken);
        System.Text.Json.JsonElement? template = null;
        if (document?.Content is { Length: > 0 } content && content[0] == (byte)'{')
        {
            try { template = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(content); }
            catch (System.Text.Json.JsonException) { /* Tài liệu lịch sử có thể không phải ảnh chụp mẫu web. */ }
        }
        string? title = contract.TemplateTitleSnapshot;
        string? organizationName = contract.OrganizationNameSnapshot;
        string? headerText = contract.HeaderTextSnapshot;
        string? footerText = contract.FooterTextSnapshot;
        string? snapCustName = null;
        string? snapCustPhone = null;
        if (template is { } t)
        {
            if (string.IsNullOrWhiteSpace(title) && (t.TryGetProperty("title", out var p1) || t.TryGetProperty("Title", out p1))) title = p1.GetString();
            if (string.IsNullOrWhiteSpace(organizationName) && (t.TryGetProperty("organizationName", out var p2) || t.TryGetProperty("OrganizationName", out p2))) organizationName = p2.GetString();
            if (string.IsNullOrWhiteSpace(headerText) && (t.TryGetProperty("headerText", out var p3) || t.TryGetProperty("HeaderText", out p3))) headerText = p3.GetString();
            if (string.IsNullOrWhiteSpace(footerText) && (t.TryGetProperty("footerText", out var p4) || t.TryGetProperty("FooterText", out p4))) footerText = p4.GetString();
            if (t.TryGetProperty("customerName", out var p5) || t.TryGetProperty("CustomerName", out p5)) snapCustName = p5.GetString();
            if (t.TryGetProperty("customerPhone", out var p6) || t.TryGetProperty("CustomerPhone", out p6)) snapCustPhone = p6.GetString();
        }

        var customerName = !string.IsNullOrWhiteSpace(contract.CustomerNameSnapshot)
            ? contract.CustomerNameSnapshot
            : (!string.IsNullOrWhiteSpace(directCustomerAccount?.FullName)
                ? directCustomerAccount!.FullName
                : (!string.IsNullOrWhiteSpace(contract.Customer?.Account?.FullName)
                    ? contract.Customer!.Account.FullName
                    : (!string.IsNullOrWhiteSpace(contract.Appointment?.Customer?.Account?.FullName)
                        ? contract.Appointment!.Customer!.Account.FullName
                        : (!string.IsNullOrWhiteSpace(snapCustName) ? snapCustName : "Khách hàng"))));

        var customerPhone = !string.IsNullOrWhiteSpace(contract.CustomerPhoneSnapshot)
            ? contract.CustomerPhoneSnapshot
            : (!string.IsNullOrWhiteSpace(directCustomerAccount?.PhoneNumber)
                ? directCustomerAccount!.PhoneNumber
                : (!string.IsNullOrWhiteSpace(contract.Customer?.Account?.PhoneNumber)
                    ? contract.Customer!.Account.PhoneNumber
                    : (!string.IsNullOrWhiteSpace(contract.Appointment?.Customer?.Account?.PhoneNumber)
                        ? contract.Appointment!.Customer!.Account.PhoneNumber
                        : (!string.IsNullOrWhiteSpace(snapCustPhone) ? snapCustPhone : ""))));

        var customerEmail = !string.IsNullOrWhiteSpace(contract.CustomerEmailSnapshot)
            ? contract.CustomerEmailSnapshot
            : (directCustomerAccount?.Email ?? contract.Customer?.Account?.Email ?? contract.Appointment?.Customer?.Account?.Email);

        var customerIdentity = !string.IsNullOrWhiteSpace(contract.CustomerIdentitySnapshot)
            ? contract.CustomerIdentitySnapshot
            : (directCustomerAccount?.IdentityNumber ?? contract.Customer?.Account?.IdentityNumber ?? contract.Appointment?.Customer?.Account?.IdentityNumber);

        var customerAddress = !string.IsNullOrWhiteSpace(contract.CustomerAddressSnapshot)
            ? contract.CustomerAddressSnapshot
            : (directCustomerProfile?.Address ?? contract.Customer?.Address ?? contract.Appointment?.Customer?.Address);

        var bankCode = !string.IsNullOrWhiteSpace(contract.BankCodeSnapshot)
            ? contract.BankCodeSnapshot
            : (directCustomerProfile?.BankCode ?? contract.Customer?.BankCode ?? contract.Appointment?.Customer?.BankCode);

        var bankAccountNumber = !string.IsNullOrWhiteSpace(contract.BankAccountNumberSnapshot)
            ? contract.BankAccountNumberSnapshot
            : (directCustomerProfile?.BankAccountNumber ?? contract.Customer?.BankAccountNumber ?? contract.Appointment?.Customer?.BankAccountNumber);

        var bankAccountHolder = !string.IsNullOrWhiteSpace(contract.BankAccountHolderSnapshot)
            ? contract.BankAccountHolderSnapshot
            : (directCustomerProfile?.BankAccountHolderName ?? contract.Customer?.BankAccountHolderName ?? contract.Appointment?.Customer?.BankAccountHolderName);

        var employeeName = !string.IsNullOrWhiteSpace(contract.EmployeeNameSnapshot)
            ? contract.EmployeeNameSnapshot
            : (!string.IsNullOrWhiteSpace(directEmployeeAccount?.FullName)
                ? directEmployeeAccount!.FullName
                : (!string.IsNullOrWhiteSpace(contract.CreatedByEmployee?.Account?.FullName)
                    ? contract.CreatedByEmployee.Account.FullName
                    : "Nhân viên tiếp nhận"));
        var employeeTitle = !string.IsNullOrWhiteSpace(contract.EmployeeTitleSnapshot)
            ? contract.EmployeeTitleSnapshot
            : (directEmployeeProfile?.JobTitle ?? contract.CreatedByEmployee?.JobTitle);
        var employeePhone = !string.IsNullOrWhiteSpace(contract.EmployeePhoneSnapshot)
            ? contract.EmployeePhoneSnapshot
            : (directEmployeeAccount?.PhoneNumber ?? contract.CreatedByEmployee?.Account?.PhoneNumber);

        return Ok(new
        {
            id = contract.Id,
            contractNumber = contract.ContractNumber,
            signedAt = contract.SignedAt,
            endsAt = contract.EndsAt,
            expiryHandling = contract.ExpiryHandling.ToString(),
            status = contract.Status.ToString(),
            customerName = customerName,
            customerPhone = customerPhone,
            customerEmail = customerEmail,
            customerIdentity = customerIdentity,
            customerAddress = customerAddress,
            bankCode = bankCode,
            bankAccountNumber = bankAccountNumber,
            bankAccountHolderName = bankAccountHolder,
            employeeName = employeeName,
            employeeTitle = employeeTitle,
            employeePhone = employeePhone,
            terms = !string.IsNullOrWhiteSpace(contract.TermsSnapshot) ? contract.TermsSnapshot : document?.Terms,
            title = title,
            organizationName = organizationName,
            headerText = headerText,
            footerText = footerText,
            note = template is { } noteSnapshot && (noteSnapshot.TryGetProperty("note", out var note) || noteSnapshot.TryGetProperty("Note", out note)) ? note.GetString() : null,
            template = template is not null ? new
            {
                title = title,
                organizationName = organizationName,
                headerText = headerText,
                footerText = footerText
            } : null,
            signature = document?.SignatureContent is null || document.SignatureContentType is null ? null : new { contentType = document.SignatureContentType, data = Convert.ToBase64String(document.SignatureContent) },
            customerSignatureDataUrl = document?.SignatureContent is null || document.SignatureContentType is null ? null : $"data:{document.SignatureContentType};base64,{Convert.ToBase64String(document.SignatureContent)}",
            products = template is { } snapshot && (snapshot.TryGetProperty("products", out var snapshotProducts) || snapshot.TryGetProperty("Products", out snapshotProducts)) && snapshotProducts.ValueKind == System.Text.Json.JsonValueKind.Array ? (object)snapshotProducts : contract.Products.Where(x => x.ApprovalStatus != ProductApprovalStatus.Rejected).OrderBy(x => x.CreatedAt).Select(x => new
            {
                id = x.Id,
                name = x.Name,
                barcode = x.Barcode,
                size = x.Size,
                brand = x.Brand?.Name,
                brandName = x.Brand?.Name,
                category = x.Category?.Name,
                categoryName = x.Category?.Name,
                material = x.Material,
                description = x.Description,
                conditionPercentage = x.ConditionPercentage,
                listedPrice = x.AgreedListedPrice ?? x.ListedPrice,
                commissionRate = x.AgreedCommissionRate ?? 0,
                agreedCommissionRate = x.AgreedCommissionRate ?? 0,
                images = x.Images.OrderBy(i => i.SortOrder).Select(i => i.Content != null && i.ContentType != null
                    ? $"data:{i.ContentType};base64,{Convert.ToBase64String(i.Content)}" : i.Url).ToArray()
            }).ToArray()
        });
    }
    /// <summary>Lấy PDF đã lưu.</summary>
    [HttpGet("{id:guid}/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("Staff") &&
            !(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded) return Forbid();
        var content = await contracts.GetPdfAsync(id, cancellationToken);
        return content is null || !content.AsSpan().StartsWith("%PDF"u8) ? NotFound() : File(content, "application/pdf");
    }

    /// <summary>Lấy tài liệu Word mới hoặc PDF lịch sử của hợp đồng.</summary>
    [HttpGet("{id:guid}/document")]
    public async Task<IActionResult> Document(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("Staff") &&
            !(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded) return Forbid();
        var content = await contracts.GetPdfAsync(id, cancellationToken);
        if (content is null) return NotFound();
        if (content.AsSpan().StartsWith("%PDF"u8)) return File(content, "application/pdf");
        if (content.AsSpan().StartsWith("PK"u8)) return File(content, "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        return NotFound();
    }

    /// <summary>Lấy sản phẩm của hợp đồng để xem trước và in nhãn.</summary>
    [HttpGet("{id:guid}/barcode-products")]
    public async Task<IActionResult> BarcodeProducts(Guid id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Manager") && !User.IsInRole("Staff") &&
            !(await authorization.AuthorizeAsync(User, PermissionCatalog.CustomersRead)).Succeeded) return Forbid();
        var products = await contracts.GetBarcodeProductsAsync(id, cancellationToken);
        return products is null ? NotFound() : Ok(products);
    }
}
