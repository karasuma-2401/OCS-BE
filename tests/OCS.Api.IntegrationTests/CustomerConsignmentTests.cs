using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Models.Customers;
using OCS.Api.Repositories;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class CustomerConsignmentTests
{
    [Fact]
    public async Task Get_OnlyReturnsOwnersContracts_AndCompletedSettlements()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        db.Accounts.AddRange(Customer(owner), Customer(other));
        var contract = new ConsignmentContract { CustomerId = owner, ContractNumber = "OWN", SignedAt = DateTimeOffset.UtcNow, EndsAt = DateTimeOffset.UtcNow.AddDays(30) };
        var otherContract = new ConsignmentContract { CustomerId = other, ContractNumber = "OTHER" };
        var brand = new Brand { DefaultCommissionRate = 20 };
        var product = new Product { Name = "Shirt" };
        product.AssignConsignmentTerms(contract.Id, 1000, brand);
        product.Status = ProductStatus.Sold;
        contract.Products.Add(product);
        contract.Payouts.Add(new Payout { TotalAmount = 300, Status = PayoutStatus.Completed });
        contract.Payouts.Add(new Payout { TotalAmount = 500, Status = PayoutStatus.Pending });
        contract.Payouts.Add(new Payout { TotalAmount = 700, Status = PayoutStatus.Transferred });
        db.ConsignmentContracts.AddRange(contract, otherContract);
        await db.SaveChangesAsync();
        var controller = CreateController(db, owner);

        var result = Assert.IsType<OkObjectResult>(await controller.Get(default));
        var response = Assert.Single(Assert.IsType<ConsignmentTrackingResponse[]>(result.Value));
        Assert.Equal("OWN", response.ContractNumber);
        Assert.Equal(800m, response.EstimatedPayout);
        Assert.Equal(300m, response.SettledAmount);
        Assert.Equal("Sold", Assert.Single(response.Products).Status);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Get_InactiveCustomer_IsForbidden()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var id = Guid.NewGuid();
        var account = Customer(id);
        account.CustomerProfile!.Status = CustomerStatus.Suspended;
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        Assert.IsType<ForbidResult>(await CreateController(db, id).Get(default));
    }

    [Fact]
    public async Task Get_WithoutAuthentication_ReturnsUnauthorized()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "", ["Redis:ConnectionString"] = "", ["R2:ServiceUrl"] = ""
            }));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customers/me/consignments")).StatusCode);
        var authorization = Assert.Single(typeof(CustomerConsignmentsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Customer", authorization.Roles);
    }

    [Fact]
    public async Task Get_AuthenticatedHttpRequest_ReturnsOnlyCurrentCustomerData()
    {
        var databaseName = Guid.NewGuid().ToString();
        var jwtSettings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "", ["Redis:ConnectionString"] = "",
            ["R2:ServiceUrl"] = "", ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test",
            ["Jwt:Key"] = "test-only-key-with-at-least-32-bytes"
        };
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(jwtSettings));
            builder.ConfigureServices(services =>
            {
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuer = "test";
                    options.TokenValidationParameters.ValidAudience = "test";
                    options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["Jwt:Key"]!));
                });
            });
        });

        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        Account owner;
        Guid ownedContractId;
        Guid foreignContractId;
        Guid ownedProductId;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SystemAuditContext>().Initialize("ConsignmentTest");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            owner = Customer(ownerId);
            owner.Email = "owner@example.com";
            var other = Customer(otherId);
            other.Email = "other@example.com";
            db.Accounts.AddRange(owner, other);
            db.AccountRoles.Add(new AccountRole { AccountId = ownerId, RoleId = Role.CustomerRoleId });
            db.AuthSessions.Add(new AuthSession { Id = sessionId, AccountId = ownerId, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            var contract = new ConsignmentContract { CustomerId = ownerId, ContractNumber = "HTTP-OWN", SignedAt = DateTimeOffset.UtcNow, EndsAt = DateTimeOffset.UtcNow.AddDays(30) };
            var brand = new Brand { Name = "HTTP Brand", DefaultCommissionRate = 20m };
            var product = new Product { Name = "Shirt", Brand = brand, Category = new Category { Name = "HTTP Category" } };
            product.AssignConsignmentTerms(contract.Id, 1000m, brand);
            product.Status = ProductStatus.Sold;
            contract.Products.Add(product);
            contract.Payouts.Add(new Payout { TotalAmount = 300m, Status = PayoutStatus.Completed });
            var foreign = new ConsignmentContract { CustomerId = otherId, ContractNumber = "HTTP-OTHER" };
            ownedContractId = contract.Id; foreignContractId = foreign.Id; ownedProductId = product.Id;
            db.ConsignmentContracts.AddRange(contract, foreign);
            db.ConsignmentDocuments.Add(new ConsignmentDocument { ContractId = contract.Id, FileName = "contract.pdf", ContentType = "application/pdf", UploadedAt = DateTimeOffset.UtcNow, Content = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4") });
            await db.SaveChangesAsync();
        }
        var token = new TokenService(new ConfigurationBuilder().AddInMemoryCollection(jwtSettings).Build())
            .GenerateAccessToken(owner, ["Customer"], sessionId);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.GetAsync("/api/customers/me/consignments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var contracts = await response.Content.ReadFromJsonAsync<ConsignmentTrackingResponse[]>();
        var item = Assert.Single(contracts!);
        Assert.Equal("HTTP-OWN", item.ContractNumber);
        Assert.Equal(800m, item.EstimatedPayout);
        Assert.Equal(300m, item.SettledAmount);
        Assert.Equal("Sold", Assert.Single(item.Products).Status);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/customers/me/consignments/{ownedContractId}/products/{ownedProductId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/customers/me/consignments/{foreignContractId}/products/{ownedProductId}")).StatusCode);
        using var documentResponse = await client.GetAsync($"/api/customers/me/consignments/{ownedContractId}/document");
        Assert.Equal(HttpStatusCode.OK, documentResponse.StatusCode);
        Assert.Equal("application/pdf", documentResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF-1.4", await documentResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/customers/me/consignments/{foreignContractId}/document")).StatusCode);
    }

    [Fact]
    public async Task DocumentsAndDetails_RequireOwnershipAndReturnStoredContent()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        db.Accounts.AddRange(Customer(owner), Customer(other));
        var contract = new ConsignmentContract { CustomerId = owner, ContractNumber = "OWN-DOC" };
        var foreign = new ConsignmentContract { CustomerId = other, ContractNumber = "OTHER-DOC" };
        var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20m };
        var category = new Category { Name = "Category" };
        var product = new Product { Name = "Shirt", Brand = brand, Category = category, Size = "M", ConditionPercentage = 90 };
        product.AssignConsignmentTerms(contract.Id, 1000m, brand);
        contract.Products.Add(product);
        db.ConsignmentContracts.AddRange(contract, foreign);
        await db.SaveChangesAsync();
        var uploader = new ConsignmentDocumentsController(db) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF");
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "../../unsafe.pdf")
        { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        Assert.IsType<NoContentResult>(await uploader.Put(contract.Id, file, default));
        var customer = CreateController(db, owner);
        var response = Assert.IsType<FileContentResult>(await customer.GetDocument(contract.Id, default));
        Assert.Equal(bytes, response.FileContents);
        Assert.Equal("application/pdf", response.ContentType);
        Assert.Contains("inline", customer.Response.Headers.ContentDisposition.ToString());
        Assert.Equal("nosniff", customer.Response.Headers.XContentTypeOptions.ToString());
        Assert.IsType<NotFoundResult>(await CreateController(db, other).GetDocument(contract.Id, default));
        Assert.IsType<NotFoundResult>(await customer.GetDocument(foreign.Id, default));
        var detail = Assert.IsType<ConsignmentProductDetailResponse>(Assert.IsType<OkObjectResult>(await customer.GetProduct(contract.Id, product.Id, default)).Value);
        Assert.Equal("Brand", detail.BrandName);
        Assert.Equal("90%", detail.Condition);
        Assert.Equal(800m, detail.EstimatedPayout);
        Assert.Empty(detail.ImageUrls);
        Assert.IsType<NotFoundResult>(await customer.GetProduct(foreign.Id, product.Id, default));
        Assert.IsType<NotFoundResult>(await CreateController(db, other).GetProduct(contract.Id, product.Id, default));
        var listed = Assert.Single(Assert.IsType<ConsignmentTrackingResponse[]>(Assert.IsType<OkObjectResult>(await customer.Get(default)).Value));
        Assert.NotNull(listed.ContractDocument);
        Assert.Equal("application/pdf", listed.ContractDocument.ContentType);
        Assert.True(Assert.Single(listed.Products).DetailAvailable);
    }

    [Theory]
    [InlineData("image/jpeg", "FFD8FF00")]
    [InlineData("image/png", "89504E470D0A1A0A")]
    [InlineData("image/webp", "524946460400000057454250")]
    public async Task Upload_AcceptsSupportedImageSignaturesAndPreservesBytes(string contentType, string hex)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var owner = Guid.NewGuid();
        db.Accounts.Add(Customer(owner));
        var contract = new ConsignmentContract { CustomerId = owner, ContractNumber = "IMAGE" };
        db.ConsignmentContracts.Add(contract);
        await db.SaveChangesAsync();
        var bytes = Convert.FromHexString(hex);
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "x") { Headers = new HeaderDictionary(), ContentType = contentType };
        Assert.IsType<NoContentResult>(await new ConsignmentDocumentsController(db).Put(contract.Id, file, default));
        var download = Assert.IsType<FileContentResult>(await CreateController(db, owner).GetDocument(contract.Id, default));
        Assert.Equal(bytes, download.FileContents);
        Assert.Equal(contentType, download.ContentType);
    }

    [Theory]
    [InlineData("image/png", "not an image")]
    [InlineData("application/pdf", "not a pdf")]
    [InlineData("image/jpeg", "%PDF-1.4")]
    public async Task Upload_RejectsMismatchedContent(string contentType, string content)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var contract = new ConsignmentContract { ContractNumber = "BAD-CONTENT" };
        db.ConsignmentContracts.Add(contract);
        await db.SaveChangesAsync();
        var uploader = new ConsignmentDocumentsController(db);
        var bytes = System.Text.Encoding.ASCII.GetBytes(content);
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "file") { Headers = new HeaderDictionary(), ContentType = contentType };
        Assert.IsType<BadRequestObjectResult>(await uploader.Put(contract.Id, file, default));
        Assert.Empty(db.ConsignmentDocuments);
    }

    [Fact]
    public void ImageStore_OnlyAcceptsUrlsInsideConfiguredR2Base()
    {
        using var client = new Amazon.S3.AmazonS3Client(new Amazon.Runtime.BasicAWSCredentials("key", "secret"),
            new Amazon.S3.AmazonS3Config { ServiceURL = "https://storage.example.com", ForcePathStyle = true });
        using var provider = new ServiceCollection().AddSingleton<Amazon.S3.IAmazonS3>(client).BuildServiceProvider();
        var store = new PrivateProductImageStore(provider, Microsoft.Extensions.Options.Options.Create(
            new OCS.Api.Storage.R2Options { BucketName = "private", PublicBaseUrl = "https://cdn.example.com/items" }));
        Assert.Equal("folder/image.png", store.GetKey("https://cdn.example.com/items/folder/image.png"));
        Assert.Equal("folder/image.png", store.GetKey("r2://private/folder/image.png"));
        Assert.Null(store.GetKey("r2://other/folder/image.png"));
        Assert.Null(store.GetKey("r2://private/../image.png"));
        Assert.Null(store.GetKey("https://evil.example.com/items/folder/image.png"));
        Assert.Null(store.GetKey("https://cdn.example.com/items-elsewhere/image.png"));
        Assert.Null(store.GetKey("https://cdn.example.com/items/image.png?token=secret"));
        Assert.Null(store.GetKey("https://cdn.example.com/items/%2e%2e/private.png"));
    }

    private static Account Customer(Guid id) => new()
    {
        Id = id, AccountType = AccountType.Customer, Status = AccountStatus.Active,
        EmailVerifiedAt = DateTimeOffset.UtcNow, CustomerProfile = new CustomerProfile { Id = id }
    };

    private static CustomerConsignmentsController CreateController(AppDbContext db, Guid id) => new(new CustomerConsignmentService(db, new CustomerProfileRepository(db)))
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test"))
        } }
    };
}
