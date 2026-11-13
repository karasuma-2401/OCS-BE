using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Contracts;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class WordDocumentsHttpTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task ManagerSavesTemplatesAndCreatesWordContractAndProductSlip()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "",
                ["Redis:ConnectionString"] = "",
                ["R2:ServiceUrl"] = "",
                ["Jwt:Issuer"] = "test",
                ["Jwt:Audience"] = "test",
                ["Jwt:Key"] = "test-only-key-with-at-least-32-bytes"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "WordTest"; options.DefaultChallengeScheme = "WordTest"; options.DefaultForbidScheme = "WordTest"; })
                    .AddScheme<AuthenticationSchemeOptions, TestHandler>("WordTest", _ => { });
            });
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Bypass-Csrf", "true");
        var brandId = Guid.NewGuid(); var categoryId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.AddRange(new Account { Id = UserId, FullName = "Quản lý", PhoneNumber = "0900000000" },
                new AccountRole { AccountId = UserId, RoleId = new Guid("20000000-0000-0000-0000-000000000001") },
                new EmployeeProfile { Id = UserId, JobTitle = "Quản lý" }, new Brand { Id = brandId, Name = "Nhãn", DefaultCommissionRate = 20 }, new OCS.Api.Entities.Category { Id = categoryId, Name = "Áo" });
            await db.SaveChangesAsync();
        }
        var contractTemplate = await client.GetByteArrayAsync("/api/word-template-samples/contract");
        using (var stream = new MemoryStream())
        {
            stream.Write(contractTemplate); stream.Position = 0;
            using (var word = WordprocessingDocument.Open(stream, true))
            {
                word.MainDocumentPart!.Document.Body!.Descendants<Text>().Single(x => x.Text == WordTemplateSamples.TermsInstruction).Text = "Điều khoản được quản lý xác nhận.";
                word.MainDocumentPart.Document.Save();
            }
            contractTemplate = stream.ToArray();
        }
        async Task SaveTemplate(string kind, byte[] bytes)
        {
            using var data = new MultipartFormDataContent();
            data.Add(new StringContent(Guid.Empty.ToString()), "version");
            data.Add(new ByteArrayContent(bytes), "file", "template.docx");
            using var result = await client.PutAsync($"/api/word-template-samples/{kind}/saved", data);
            Assert.True(result.StatusCode == HttpStatusCode.OK, await result.Content.ReadAsStringAsync());
        }
        await SaveTemplate("contract", contractTemplate);
        await SaveTemplate("product", await client.GetByteArrayAsync("/api/word-template-samples/product"));
        var template = await client.GetFromJsonAsync<ContractTemplateResponse>("/api/contracts/template");
        Assert.NotNull(template);
        var now = DateTimeOffset.UtcNow;
        var request = new CreateContractRequest
        {
            RequestId = Guid.NewGuid(),
            SignedAt = now,
            EndsAt = now.AddDays(30),
            ExpiryHandling = "Return",
            TemplateVersion = template.Version,
            Guest = new ContractGuestRequest { FullName = "Khách thử", PhoneNumber = "0901234567" },
            Products = [new ContractProductRequest { Name = "Áo mẫu", Barcode = "OCS-1234567890ABCDEF", Size = "M", BrandId = brandId, CategoryId = categoryId, ListedPrice = 150000, AgreedCommissionRate = 20 }]
        };
        using var preview = await client.PostAsJsonAsync("/api/contracts/preview", request);
        Assert.True(preview.IsSuccessStatusCode, await preview.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", preview.Content.Headers.ContentType?.MediaType);
        Assert.True((await preview.Content.ReadAsByteArrayAsync()).AsSpan().StartsWith("%PDF"u8));
        using var created = await client.PostAsJsonAsync("/api/contracts", request);
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var result = await created.Content.ReadFromJsonAsync<ContractResponse>();
        Assert.NotNull(result);
        using var documentResponse = await client.GetAsync($"/api/contracts/{result.Id}/document");
        Assert.Equal(HttpStatusCode.OK, documentResponse.StatusCode);
        Assert.Equal("application/pdf", documentResponse.Content.Headers.ContentType?.MediaType);
        Assert.True((await documentResponse.Content.ReadAsByteArrayAsync()).AsSpan().StartsWith("%PDF"u8));
        Guid productId;
        await using (var scope = factory.Services.CreateAsyncScope()) productId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Products.SingleAsync()).Id;
        using var slip = await client.PostAsJsonAsync("/api/product-documents/preview", new { productIds = new[] { productId } });
        Assert.True(slip.IsSuccessStatusCode, await slip.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", slip.Content.Headers.ContentType?.MediaType);
        Assert.True((await slip.Content.ReadAsByteArrayAsync()).AsSpan().StartsWith("%PDF"u8));
        using var slipWord = await client.PostAsJsonAsync("/api/product-documents/word", new { productIds = new[] { productId } });
        Assert.True(slipWord.IsSuccessStatusCode, await slipWord.Content.ReadAsStringAsync());
        using var slipWordDoc = WordprocessingDocument.Open(new MemoryStream(await slipWord.Content.ReadAsByteArrayAsync()), false);
        Assert.Contains("Áo mẫu", slipWordDoc.MainDocumentPart!.Document.Body!.InnerText);
        Assert.NotEmpty(slipWordDoc.MainDocumentPart.Document.Body.Descendants<Drawing>());

        var draft = Guid.NewGuid();
        using var requestResponse = await client.PostAsJsonAsync("/api/contract-terms-requests", new { draftKey = draft, reason = "Khách đề nghị bổ sung điều khoản", customerPhone = "0901234567" });
        Assert.True(requestResponse.IsSuccessStatusCode, await requestResponse.Content.ReadAsStringAsync());
        var termsRequest = await requestResponse.Content.ReadFromJsonAsync<OCS.Api.Controllers.TermsRequestResponse>();
        Assert.NotNull(termsRequest);
        var specialRequest = request with { RequestId = draft, Products = [] };
        using var pendingPreview = await client.PostAsJsonAsync("/api/contracts/preview", specialRequest);
        Assert.Equal(HttpStatusCode.Conflict, pendingPreview.StatusCode);
        var specialTemplate = contractTemplate.ToArray();
        using (var stream = new MemoryStream())
        {
            stream.Write(specialTemplate); stream.Position = 0;
            using (var word = WordprocessingDocument.Open(stream, true))
            {
                word.MainDocumentPart!.Document.Body!.Descendants<Text>().Single(x => x.Text == "Điều khoản được quản lý xác nhận.").Text = "Điều khoản riêng đã được duyệt.";
                word.MainDocumentPart.Document.Save();
            }
            specialTemplate = stream.ToArray();
        }
        using (var approval = new MultipartFormDataContent())
        {
            approval.Add(new StringContent(termsRequest.Version.ToString()), "version");
            approval.Add(new ByteArrayContent(specialTemplate), "file", "dieu-khoan-rieng.docx");
            using var decision = await client.PutAsync($"/api/contract-terms-requests/{termsRequest.Id}/approve", approval);
            Assert.True(decision.IsSuccessStatusCode, await decision.Content.ReadAsStringAsync());
        }
        using var approvedPreview = await client.PostAsJsonAsync("/api/contracts/preview", specialRequest with { TermsRequestId = termsRequest.Id });
        Assert.True(approvedPreview.IsSuccessStatusCode, await approvedPreview.Content.ReadAsStringAsync());
        Assert.Equal("application/pdf", approvedPreview.Content.Headers.ContentType?.MediaType);
        Assert.True((await approvedPreview.Content.ReadAsByteArrayAsync()).AsSpan().StartsWith("%PDF"u8));
        using var approvedWord = await client.PostAsJsonAsync("/api/contracts/preview?format=word", specialRequest with { TermsRequestId = termsRequest.Id });
        using (var word = WordprocessingDocument.Open(new MemoryStream(await approvedWord.Content.ReadAsByteArrayAsync()), false))
            Assert.Contains("Điều khoản riêng đã được duyệt.", word.MainDocumentPart!.Document.Body!.InnerText);
        using var unsigned = await client.PostAsJsonAsync("/api/contracts", specialRequest with { TermsRequestId = termsRequest.Id });
        Assert.Equal(HttpStatusCode.BadRequest, unsigned.StatusCode);
        var signature = new ContractImageRequest { ContentType = "image/png", Data = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=" };
        using var specialResult = await client.PostAsJsonAsync("/api/contracts", specialRequest with { TermsRequestId = termsRequest.Id, Signature = signature });
        Assert.True(specialResult.StatusCode == HttpStatusCode.Created, await specialResult.Content.ReadAsStringAsync());
    }

    private sealed class TestHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim(ClaimTypes.Role, "Admin")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
