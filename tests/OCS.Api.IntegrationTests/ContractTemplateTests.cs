using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Models.Contracts;
using OCS.Api.Services;
using PdfSharp.Pdf.IO;

namespace OCS.Api.IntegrationTests;

public sealed class ContractTemplateTests
{
    [Fact]
    public async Task CustomPdfMappingsAreSavedValidatedAndRenderedWithFullAppendix()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var service = new ContractTemplateService(db);
        var request = new UpdateContractTemplateRequest
        {
            Title = "Mẫu PDF", PdfBase64 = Convert.ToBase64String(ContractPdfRenderer.CreateSampleBackground()),
            FieldMappings = [new("customerName", 1, 35, 42, 50, 4), new("signature", 1, 60, 75, 30, 12)]
        };
        var preview = service.Preview(request);
        Assert.True(preview.IsSuccess);
        using var pdf = PdfReader.Open(new MemoryStream(preview.Data!), PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount >= 2);
        var saved = await service.UpdateAsync(request, Guid.NewGuid(), default);
        Assert.True(saved.IsSuccess);
        Assert.Equal(request.PdfBase64, (await service.GetAsync(default)).PdfBase64);
        Assert.Equal(2, (await service.GetAsync(default)).FieldMappings.Count);
        Assert.False(service.Preview(request with { FieldMappings = [new("customerName", 2, 0, 0, 50, 5)] }).IsSuccess);
        Assert.False(service.Preview(request with { FieldMappings = [new("customerName", 1, 90, 0, 20, 5)] }).IsSuccess);
        Assert.False(service.Preview(request with { PdfBase64 = "aW52YWxpZA==" }).IsSuccess);
    }

    [Fact]
    public void ContractListIsRestrictedToManagersAndAdmins()
    {
        var authorization = typeof(ContractManagementController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("Admin,Manager", authorization.Roles);
    }
    [Fact]
    public async Task TemplateCanBePreviewedAndSavedWithConcurrencyProtection()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var service = new ContractTemplateService(db);
        var initial = await service.GetAsync(default);
        Assert.Equal(Guid.Empty, initial.Version);
        var request = new UpdateContractTemplateRequest { Title = "Hợp đồng mẫu", OrganizationName = "Cửa hàng", HeaderText = "Đầu trang", FooterText = "Cuối trang", DefaultTerms = "Điều khoản ví dụ", Version = initial.Version };
        var preview = service.Preview(request);
        Assert.True(preview.IsSuccess);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(preview.Data!, 0, 5));
        Assert.Empty(db.ContractTemplates);
        var saved = await service.UpdateAsync(request, Guid.NewGuid(), default);
        Assert.True(saved.IsSuccess);
        Assert.NotEqual(Guid.Empty, saved.Data!.Version);
        Assert.Equal("Điều khoản ví dụ", saved.Data.DefaultTerms);
        var stale = await service.UpdateAsync(request with { Title = "Ghi đè" }, Guid.NewGuid(), default);
        Assert.Equal("CONTRACT_TEMPLATE.CHANGED", stale.Error!.Code);
        Assert.Equal("Hợp đồng mẫu", (await service.GetAsync(default)).Title);
    }

    [Fact]
    public async Task NewContractsUseDefaultsAndOldPdfStaysUnchanged()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var employee = new EmployeeProfile { Id = Guid.NewGuid() };
        db.EmployeeProfiles.Add(employee);
        await db.SaveChangesAsync();
        var templates = new ContractTemplateService(db);
        var request = new UpdateContractTemplateRequest { Title = "Hợp đồng A", DefaultTerms = "Điều khoản A" };
        var first = (await templates.UpdateAsync(request, Guid.NewGuid(), default)).Data!;
        var now = DateTimeOffset.UtcNow;
        var contractRequest = new CreateContractRequest { RequestId = Guid.NewGuid(), SignedAt = now, EndsAt = now.AddDays(30), ExpiryHandling = "Return", TemplateVersion = first.Version,
            Guest = new ContractGuestRequest { FullName = "Khách", PhoneNumber = "0901234567" } };
        var contracts = new ContractService(db);
        Assert.True((await contracts.CreateAsync(contractRequest, employee.Id, default)).IsSuccess);
        var document = await db.ContractDocuments.SingleAsync();
        var oldPdf = document.Content.ToArray();
        Assert.Equal("Điều khoản A", document.Terms);
        Assert.Equal(first.Version.ToString("N"), document.TemplateVersion);
        var second = await templates.UpdateAsync(request with { Title = "Hợp đồng B", DefaultTerms = "Điều khoản B", Version = first.Version }, Guid.NewGuid(), default);
        Assert.True(second.IsSuccess);
        Assert.Equal(oldPdf, (await db.ContractDocuments.SingleAsync()).Content);
        var stale = await contracts.PreviewAsync(contractRequest with { RequestId = Guid.NewGuid() }, employee.Id, default);
        Assert.Equal("CONTRACT_TEMPLATE.CHANGED", stale.Error!.Code);
        var next = await contracts.CreateAsync(contractRequest with { RequestId = Guid.NewGuid(), TemplateVersion = second.Data!.Version }, employee.Id, default);
        Assert.True(next.IsSuccess);
        Assert.Contains(await db.ContractDocuments.ToListAsync(), d => d.Terms == "Điều khoản B");
    }

    [Fact]
    public void ManagementControllerRestrictsAllActionsToAdminOrManager()
    {
        var authorization = typeof(ContractTemplateController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("Admin,Manager", authorization.Roles);
    }

    [Theory]
    [InlineData("Staff", false)]
    [InlineData("Customer", false)]
    [InlineData("Manager", true)]
    [InlineData("Admin", true)]
    public async Task ManagementPolicyAllowsOnlyManagersAndAdmins(string role, bool expected)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var data = typeof(ContractTemplateController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<IAuthorizeData>();
        var policy = await AuthorizationPolicy.CombineAsync(services.GetRequiredService<IAuthorizationPolicyProvider>(), data);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));
        var result = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy!);
        Assert.Equal(expected, result.Succeeded);
        await services.DisposeAsync();
    }
}
