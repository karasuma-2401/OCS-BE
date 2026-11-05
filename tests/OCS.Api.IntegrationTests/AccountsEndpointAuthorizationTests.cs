using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OCS.Api.IntegrationTests;

public sealed class AccountsEndpointAuthorizationTests
{
    [Theory]
    [InlineData("/api/accounts")]
    [InlineData("/api/accounts/00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/accounts/00000000-0000-0000-0000-000000000001/employee-profile")]
    public async Task CustomerCannotReadAccounts(string path)
    {
        await using var factory = CreateCustomerFactory();

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CustomerCannotCreateStaff()
    {
        await using var factory = CreateCustomerFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.PostAsync("/api/accounts/staff", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CustomerCannotChangeAccountLock()
    {
        await using var factory = CreateCustomerFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.PatchAsync(
            "/api/accounts/00000000-0000-0000-0000-000000000001/lock-status", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CustomerCannotAssignRoles()
    {
        await using var factory = CreateCustomerFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.PutAsync(
            "/api/accounts/00000000-0000-0000-0000-000000000001/roles", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/accounts/internal")]
    [InlineData("PUT", "/api/accounts/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/accounts/00000000-0000-0000-0000-000000000001/reset-password")]
    [InlineData("PATCH", "/api/accounts/00000000-0000-0000-0000-000000000001/status")]
    [InlineData("PATCH", "/api/accounts/00000000-0000-0000-0000-000000000001/employee-profile")]
    public async Task CustomerCannotUseNewManagementEndpoints(string method, string path)
    {
        await using var factory = CreateCustomerFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CustomerCannotReadAccountHistory()
    {
        await using var factory = CreateCustomerFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var response = await client.GetAsync("/api/accounts/history");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateCustomerFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "",
                    ["Redis:ConnectionString"] = "",
                    ["R2:ServiceUrl"] = ""
                }));
            builder.ConfigureTestServices(services => services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "TestCustomer";
                    options.DefaultChallengeScheme = "TestCustomer";
                    options.DefaultForbidScheme = "TestCustomer";
                })
                .AddScheme<AuthenticationSchemeOptions, CustomerAuthenticationHandler>("TestCustomer", _ => { }));
        });

    private sealed class CustomerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                    new Claim(ClaimTypes.Role, "Customer")
                ], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
