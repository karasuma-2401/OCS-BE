using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Middleware;
using OCS.Api.Models.Customers;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class CustomerSelfProfileApiTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "",
                    ["Redis:ConnectionString"] = "",
                    ["Jwt:Issuer"] = "test",
                    ["Jwt:Audience"] = "test",
                    ["Jwt:Key"] = "test-only-key-with-at-least-32-bytes",
                    ["R2:ServiceUrl"] = ""
                }));
        });

    [Fact]
    public void Controller_RequiresCustomerRole()
    {
        var authorize = typeof(CustomersController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        Assert.NotNull(authorize);
        Assert.Equal("Customer", authorize.Roles);
    }

    [Fact]
    public async Task GetSelfProfile_WithoutAuthentication_Returns401Unauthorized()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/api/customers/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UNAUTHORIZED", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task PutSelfProfile_WithoutAuthentication_Returns401Unauthorized()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var requestBody = new UpdateCustomerSelfProfileRequest
        {
            FullName = "Nguyen Van A",
            PhoneNumber = "0912345678"
        };

        using var response = await client.PutAsJsonAsync("/api/customers/me", requestBody);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UNAUTHORIZED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PutSelfProfile_WithoutCsrf_InProduction_Returns403CsrfInvalid()
    {
        var mockAntiforgery = new SessionValidationTests.TestFailingAntiforgery();
        var hostEnvironment = new SessionValidationTests.TestHostEnvironment("Production");
        var middleware = new CsrfValidationMiddleware(
            _ => Task.CompletedTask,
            mockAntiforgery,
            hostEnvironment);

        var context = new DefaultHttpContext();
        using var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = "/api/customers/me";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);

        bodyStream.Seek(0, SeekOrigin.Begin);
        var problem = await JsonSerializer.DeserializeAsync<JsonElement>(bodyStream);
        Assert.Equal("AUTH.CSRF_INVALID", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PutSelfProfile_WithCsrfBypassHeader_InNonProduction_PassesMiddleware()
    {
        var mockAntiforgery = new SessionValidationTests.TestFailingAntiforgery();
        var hostEnvironment = new SessionValidationTests.TestHostEnvironment("Testing");
        var nextExecuted = false;

        var middleware = new CsrfValidationMiddleware(
            _ =>
            {
                nextExecuted = true;
                return Task.CompletedTask;
            },
            mockAntiforgery,
            hostEnvironment);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = "/api/customers/me";
        context.Request.Headers["X-Test-Bypass-Csrf"] = "true";

        await middleware.InvokeAsync(context);

        Assert.True(nextExecuted);
    }

    [Fact]
    public async Task GetSelfProfile_CustomerA_OnlyReturnsCustomerAData_NeverCustomerB()
    {
        var customerAId = Guid.NewGuid();
        var customerBId = Guid.NewGuid();

        var accountA = new Account
        {
            Id = customerAId,
            FullName = "Customer A",
            PhoneNumber = "0911111111",
            Email = "customera@example.com",
            AccountType = AccountType.Customer,
            CustomerProfile = new CustomerProfile
            {
                Id = customerAId,
                BankCode = "VCB",
                BankAccountNumber = "1111111111",
                BankAccountHolderName = "CUSTOMER A"
            }
        };

        var accountB = new Account
        {
            Id = customerBId,
            FullName = "Customer B",
            PhoneNumber = "0922222222",
            Email = "customerb@example.com",
            AccountType = AccountType.Customer,
            CustomerProfile = new CustomerProfile
            {
                Id = customerBId,
                BankCode = "MB",
                BankAccountNumber = "2222222222",
                BankAccountHolderName = "CUSTOMER B"
            }
        };

        var unitOfWork = new FakeApiCustomerUnitOfWork([accountA, accountB]);
        var service = new CustomerProfileService(unitOfWork);
        var controller = new CustomersController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContextWithUser(customerAId, "Customer")
            }
        };

        var actionResult = await controller.GetSelfProfile(CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<CustomerSelfProfileResponse>(okResult.Value);

        Assert.Equal(customerAId, response.Id);
        Assert.Equal("Customer A", response.FullName);
        Assert.Equal("0911111111", response.PhoneNumber);
        Assert.Equal("VCB", response.BankAccount?.BankCode);
        Assert.Equal("1111111111", response.BankAccount?.AccountNumber);
        Assert.NotEqual(customerBId, response.Id);
    }

    [Fact]
    public async Task PutSelfProfile_WhenSuccessful_Returns200WithUpdatedResponse()
    {
        var customerId = Guid.NewGuid();
        var account = new Account
        {
            Id = customerId,
            FullName = "Original Name",
            PhoneNumber = "0900000000",
            AccountType = AccountType.Customer,
            CustomerProfile = new CustomerProfile
            {
                Id = customerId,
                BankCode = "VCB",
                BankAccountNumber = "123456",
                BankAccountHolderName = "ORIGINAL NAME"
            }
        };

        var unitOfWork = new FakeApiCustomerUnitOfWork([account]);
        var service = new CustomerProfileService(unitOfWork);
        var controller = new CustomersController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContextWithUser(customerId, "Customer")
            }
        };

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "Updated Name",
            PhoneNumber = "0987654321",
            BankAccount = new UpdateBankAccountRequest
            {
                BankCode = "tcb",
                AccountNumber = "654321",
                AccountHolderName = "UPDATED NAME"
            }
        };

        var actionResult = await controller.UpdateSelfProfile(request, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var response = Assert.IsType<CustomerSelfProfileResponse>(okResult.Value);

        Assert.Equal(customerId, response.Id);
        Assert.Equal("Updated Name", response.FullName);
        Assert.Equal("0987654321", response.PhoneNumber);
        Assert.NotNull(response.BankAccount);
        Assert.Equal("TCB", response.BankAccount.BankCode);
        Assert.Equal("654321", response.BankAccount.AccountNumber);
        Assert.Equal("UPDATED NAME", response.BankAccount.AccountHolderName);
    }

    [Fact]
    public async Task PutSelfProfile_WhenPhoneAlreadyExists_Returns409ConflictWithCode()
    {
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var accountA = new Account
        {
            Id = customerA,
            FullName = "Customer A",
            PhoneNumber = "0911111111",
            AccountType = AccountType.Customer,
            CustomerProfile = new CustomerProfile { Id = customerA }
        };
        var accountB = new Account
        {
            Id = customerB,
            FullName = "Customer B",
            PhoneNumber = "0922222222",
            AccountType = AccountType.Customer,
            CustomerProfile = new CustomerProfile { Id = customerB }
        };

        var unitOfWork = new FakeApiCustomerUnitOfWork([accountA, accountB]);
        var service = new CustomerProfileService(unitOfWork);
        var controller = new CustomersController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContextWithUser(customerA, "Customer")
            }
        };

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "Customer A",
            PhoneNumber = "0922222222" // Duplicate with Customer B
        };

        var actionResult = await controller.UpdateSelfProfile(request, CancellationToken.None);
        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(CustomerErrorCodes.PhoneNumberExists, problem.Extensions["code"]);
    }

    [Fact]
    public async Task PutSelfProfile_WhenPendingPayoutExists_AndBankDetailsChanged_Returns409Conflict()
    {
        var customerId = Guid.NewGuid();
        var account = new Account
        {
            Id = customerId,
            FullName = "Customer",
            PhoneNumber = "0911111111",
            AccountType = AccountType.Customer,
            CustomerProfile = new CustomerProfile
            {
                Id = customerId,
                BankCode = "VCB",
                BankAccountNumber = "111111",
                BankAccountHolderName = "CUSTOMER"
            }
        };

        var unitOfWork = new FakeApiCustomerUnitOfWork([account])
        {
            HasPendingPayout = true
        };
        var service = new CustomerProfileService(unitOfWork);
        var controller = new CustomersController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContextWithUser(customerId, "Customer")
            }
        };

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "Customer",
            PhoneNumber = "0911111111",
            BankAccount = new UpdateBankAccountRequest
            {
                BankCode = "MB",
                AccountNumber = "999999",
                AccountHolderName = "CUSTOMER"
            }
        };

        var actionResult = await controller.UpdateSelfProfile(request, CancellationToken.None);
        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(CustomerErrorCodes.BankAccountLockedForPayout, problem.Extensions["code"]);
    }

    [Fact]
    public void ResponsePayload_ShapeDoesNotContainInternalSensitiveFields()
    {
        var response = new CustomerSelfProfileResponse(
            Guid.NewGuid(),
            "Nguyen Van A",
            "0912345678",
            new BankAccountResponse("VCB", "000000000001", "NGUYEN VAN A"));

        var json = JsonSerializer.Serialize(response, WebOptions);
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.True(element.TryGetProperty("id", out _));
        Assert.True(element.TryGetProperty("fullName", out _));
        Assert.True(element.TryGetProperty("phoneNumber", out _));
        Assert.True(element.TryGetProperty("bankAccount", out var bankElement));
        Assert.True(bankElement.TryGetProperty("bankCode", out _));
        Assert.True(bankElement.TryGetProperty("accountNumber", out _));
        Assert.True(bankElement.TryGetProperty("accountHolderName", out _));

        // Must not expose internal fields
        Assert.False(element.TryGetProperty("email", out _));
        Assert.False(element.TryGetProperty("passwordHash", out _));
        Assert.False(element.TryGetProperty("status", out _));
        Assert.False(element.TryGetProperty("roles", out _));
        Assert.False(element.TryGetProperty("address", out _));
    }

    [Fact]
    public async Task OpenApiDocument_IncludesCustomerSelfProfileEndpoints()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = "",
                        ["Redis:ConnectionString"] = "",
                        ["Jwt:Issuer"] = "test",
                        ["Jwt:Audience"] = "test",
                        ["Jwt:Key"] = "test-only-key-with-at-least-32-bytes",
                        ["R2:ServiceUrl"] = ""
                    }));
            });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/customers/me", json, StringComparison.Ordinal);
        Assert.Contains("CustomerSelfProfileResponse", json, StringComparison.Ordinal);
        Assert.Contains("UpdateCustomerSelfProfileRequest", json, StringComparison.Ordinal);
    }

    private static DefaultHttpContext CreateHttpContextWithUser(Guid userId, string role)
    {
        var context = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    private sealed class FakeApiCustomerUnitOfWork(List<Account> accounts) : IUnitOfWork
    {
        public List<Account> Accounts { get; } = accounts;
        public bool HasPendingPayout { get; set; }

        public IProductRepository ProductRepository => throw new NotSupportedException();
        public IAuthRepository AuthRepository => throw new NotSupportedException();
        public IAppointmentRepository AppointmentRepository => throw new NotSupportedException();
        public INotificationRepository NotificationRepository => throw new NotSupportedException();
        public ICustomerProfileRepository CustomerProfileRepository => new FakeApiCustomerProfileRepository(Accounts, () => HasPendingPayout);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ReloadAccountAsync(Account account, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public void Dispose() { }
    }

    private sealed class FakeApiCustomerProfileRepository(List<Account> accounts, Func<bool> hasPendingPayout) : ICustomerProfileRepository
    {
        public Task<Account?> GetSelfProfileAsync(Guid customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(accounts.FirstOrDefault(a => a.Id == customerId && a.AccountType == AccountType.Customer && a.CustomerProfile != null));

        public Task<Account?> GetSelfProfileForUpdateAsync(Guid customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(accounts.FirstOrDefault(a => a.Id == customerId && a.AccountType == AccountType.Customer && a.CustomerProfile != null));

        public Task<bool> IsPhoneNumberTakenAsync(string phoneNumber, Guid excludeAccountId, CancellationToken cancellationToken = default) =>
            Task.FromResult(accounts.Any(a => a.PhoneNumber == phoneNumber && a.AccountType == AccountType.Customer && a.Id != excludeAccountId));

        public Task<bool> HasPendingPayoutAsync(Guid customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(hasPendingPayout());
    }
}
