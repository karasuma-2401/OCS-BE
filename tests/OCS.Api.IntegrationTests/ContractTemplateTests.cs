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
    public async Task WebTemplateFieldsAreSavedAndRenderedConsistently()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var service = new ContractTemplateService(db);
        var request = new UpdateContractTemplateRequest
        {
            Title = "HỢP ĐỒNG DỊCH VỤ KÝ GỬI THỜI TRANG",
            OrganizationName = "CỬA HÀNG OLD CLOTHES STORE (OCS)",
            HeaderText = "Đầu trang web",
            FooterText = "Chân trang web",
            DefaultTerms = "Điều khoản mặc định web",
            DefaultTermDays = 45,
            DefaultExpiryHandling = "Donate"
        };
        var preview = service.Preview(request);
        Assert.True(preview.IsSuccess);
        var saved = await service.UpdateAsync(request, Guid.NewGuid(), default);
        Assert.True(saved.IsSuccess);
        var fetched = await service.GetAsync(default);
        Assert.Equal("HỢP ĐỒNG DỊCH VỤ KÝ GỬI THỜI TRANG", fetched.Title);
        Assert.Equal("CỬA HÀNG OLD CLOTHES STORE (OCS)", fetched.OrganizationName);
        Assert.Equal(45, fetched.DefaultTermDays);
        Assert.Equal("Donate", fetched.DefaultExpiryHandling);
        Assert.Equal("Điều khoản mặc định web", fetched.DefaultTerms);
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
    public async Task NewContractsUseWebTemplateAndExistingSnapshotsStayUnchanged()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var employee = new EmployeeProfile { Id = Guid.NewGuid() };
        db.EmployeeProfiles.Add(employee);
        await db.SaveChangesAsync();
        var firstVersion = Guid.NewGuid();
        var template = new ContractTemplate { Id = 1, Title = "Mẫu đầu", DefaultTerms = "Điều khoản đầu", Version = firstVersion };
        db.ContractTemplates.Add(template);
        await db.SaveChangesAsync();
        var now = DateTimeOffset.UtcNow;
        var contractRequest = new CreateContractRequest
        {
            RequestId = Guid.NewGuid(),
            SignedAt = now,
            EndsAt = now.AddDays(30),
            ExpiryHandling = "Return",
            TemplateVersion = firstVersion,
            Signature = new ContractImageRequest { ContentType = "image/png", Data = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=" },
            Guest = new ContractGuestRequest { FullName = "Khách", PhoneNumber = "0901234567" }
        };
        var contracts = new ContractService(db);
        Assert.True((await contracts.CreateAsync(contractRequest, employee.Id, default)).IsSuccess);
        var document = await db.ContractDocuments.SingleAsync();
        var oldSnapshot = document.Content.ToArray();
        const string withdrawalFeeTerms = "Phí rút hàng sớm của mỗi sản phẩm bằng giá niêm yết nhân với tỷ lệ hoa hồng đã thỏa thuận của sản phẩm trên hợp đồng; làm tròn đến 2 chữ số thập phân.";
        var oldTerms = $"Điều khoản đầu\n{withdrawalFeeTerms}";
        Assert.Equal(oldTerms, document.Terms);
        Assert.Equal(oldTerms, (await db.ConsignmentContracts.SingleAsync()).TermsSnapshot);
        Assert.Equal(firstVersion.ToString("N"), document.TemplateVersion);
        template.Version = Guid.NewGuid();
        template.Title = "Mẫu mới";
        template.DefaultTerms = "Điều khoản mới";
        await db.SaveChangesAsync();
        Assert.Equal(oldSnapshot, (await db.ContractDocuments.SingleAsync()).Content);
        var stale = await contracts.PreviewWebAsync(contractRequest with { RequestId = Guid.NewGuid() }, employee.Id, default);
        Assert.Equal("CONTRACT_TEMPLATE.CHANGED", stale.Error!.Code);
        var next = await contracts.CreateAsync(contractRequest with { RequestId = Guid.NewGuid(), TemplateVersion = template.Version }, employee.Id, default);
        Assert.True(next.IsSuccess);
        var newTerms = $"Điều khoản mới\n{withdrawalFeeTerms}";
        Assert.Contains(await db.ContractDocuments.ToListAsync(), d => d.TemplateVersion == template.Version.ToString("N") && d.Terms == newTerms);
        Assert.Contains(await db.ConsignmentContracts.ToListAsync(), c => c.TermsSnapshot == newTerms);
        var unchanged = await db.ContractDocuments.AsNoTracking().SingleAsync(d => d.ContractId == document.ContractId);
        Assert.Equal(oldSnapshot, unchanged.Content);
        Assert.Equal(oldTerms, unchanged.Terms);
        Assert.Contains(await db.ConsignmentContracts.AsNoTracking().ToListAsync(), c => c.TermsSnapshot == oldTerms);
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
