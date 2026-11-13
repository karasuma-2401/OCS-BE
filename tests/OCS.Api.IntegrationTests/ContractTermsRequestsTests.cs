using System.Security.Claims;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OCS.Api.Context;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Models.Contracts;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class ContractTermsRequestsTests
{
    [Fact]
    public async Task RequestRequiresApprovalAndAppliesOnlyToItsOwnContract()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var employee = new EmployeeProfile { Id = Guid.NewGuid() };
        var other = new EmployeeProfile { Id = Guid.NewGuid() };
        var manager = Guid.NewGuid();
        var normal = WordTemplateSamples.Create(WordTemplateSamples.Contract);
        db.AddRange(employee, other, new WordDocumentTemplate { Kind = WordTemplateSamples.Contract, Content = normal, Version = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var key = Guid.NewGuid();
        var staff = Controller(db, employee.Id);
        var created = Assert.IsType<OkObjectResult>(await staff.Create(new CreateTermsRequest(key, "Khách đề nghị điều khoản riêng", "0901234567"), default));
        var pending = Assert.IsType<TermsRequestResponse>(created.Value);
        Assert.IsType<ConflictObjectResult>(await staff.Create(new CreateTermsRequest(key, "Gửi trùng", "0901234567"), default));
        var now = DateTimeOffset.UtcNow;
        var request = new CreateContractRequest { RequestId = key, SignedAt = now, EndsAt = now.AddDays(30), ExpiryHandling = "Return", Guest = new ContractGuestRequest { FullName = "Khách", PhoneNumber = "0901234567" } };
        var service = new ContractService(db);
        Assert.Equal("CONTRACT.TERMS_PENDING", (await service.PreviewAsync(request, employee.Id, default)).Error!.Code);

        var special = WithTerms(normal, "Điều khoản riêng đã duyệt.");
        IFormFile File() => new FormFile(new MemoryStream(special), 0, special.Length, "file", "rieng.docx");
        var managerController = Controller(db, manager);
        Assert.IsType<ConflictObjectResult>(await managerController.Approve(pending.Id, File(), Guid.Empty.ToString(), null, default));
        var approved = Assert.IsType<TermsRequestResponse>(Assert.IsType<OkObjectResult>(await managerController.Approve(pending.Id, File(), pending.Version.ToString(), "Đã trao đổi", default)).Value);
        Assert.Equal("Approved", approved.Status);
        Assert.Equal("CONTRACT.TERMS_CHANGED", (await service.PreviewAsync(request, employee.Id, default)).Error!.Code);
        Assert.Equal("CONTRACT.TERMS_CHANGED", (await service.PreviewAsync(request with { TermsRequestId = approved.Id }, other.Id, default)).Error!.Code);
        Assert.Equal("CONTRACT.TERMS_CUSTOMER_CHANGED", (await service.PreviewAsync(request with { TermsRequestId = approved.Id, Guest = request.Guest! with { PhoneNumber = "0909999999" } }, employee.Id, default)).Error!.Code);
        var preview = await service.PreviewAsync(request with { TermsRequestId = approved.Id }, employee.Id, default);
        Assert.True(preview.IsSuccess, preview.Error?.Message);
        Assert.True(preview.Data!.AsSpan().StartsWith("%PDF"u8));
        var previewWord = await service.PreviewWordAsync(request with { TermsRequestId = approved.Id }, employee.Id, default);
        Assert.True(previewWord.IsSuccess, previewWord.Error?.Message);
        using (var document = WordprocessingDocument.Open(new MemoryStream(previewWord.Data!), false))
            Assert.Contains("Điều khoản riêng đã duyệt.", document.MainDocumentPart!.Document.Body!.InnerText);
        Assert.Equal("CONTRACT.SIGNATURE_REQUIRED", (await service.CreateAsync(request with { TermsRequestId = approved.Id }, employee.Id, default)).Error!.Code);
        var signature = new ContractImageRequest { ContentType = "image/png", Data = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=" };
        var contract = await service.CreateAsync(request with { TermsRequestId = approved.Id, Signature = signature }, employee.Id, default);
        Assert.True(contract.IsSuccess, contract.Error?.Message);
        Assert.Equal("Used", (await db.ContractTermsRequests.SingleAsync()).Status);
        Assert.Equal(normal, (await db.WordDocumentTemplates.SingleAsync()).Content);
        Assert.IsType<ConflictObjectResult>(await staff.Create(new CreateTermsRequest(key, "Gửi sau khi tạo", "0901234567"), default));
        Assert.IsType<ConflictObjectResult>(await staff.Cancel(approved.Id, new CancelTermsRequest(approved.Version), default));
    }

    [Fact]
    public void ApprovalAndListRequireManagerRole()
    {
        foreach (var name in new[] { nameof(ContractTermsRequestsController.Pending), nameof(ContractTermsRequestsController.Approve), nameof(ContractTermsRequestsController.Reject) })
        {
            var attributes = typeof(ContractTermsRequestsController).GetMethod(name)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>();
            Assert.Contains(attributes, attribute => attribute.Roles == "Admin,Manager");
        }
    }

    private static ContractTermsRequestsController Controller(AppDbContext db, Guid actor) => new(db)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, actor.ToString())], "test")) } }
    };

    private static byte[] WithTerms(byte[] input, string terms)
    {
        using var output = new MemoryStream(); output.Write(input); output.Position = 0;
        using (var document = WordprocessingDocument.Open(output, true))
        {
            document.MainDocumentPart!.Document.Body!.Descendants<Text>().Single(text => text.Text == WordTemplateSamples.TermsInstruction).Text = terms;
            document.MainDocumentPart.Document.Save();
        }
        return output.ToArray();
    }
}
