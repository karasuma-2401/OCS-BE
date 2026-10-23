using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OCS.Api.Contracts.Common;
using OCS.Api.Extensions;
using OCS.Application.Common;

namespace OCS.Api.IntegrationTests;

public sealed class ApiContractTests
{
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
            builder.ConfigureServices(services => services.AddControllers()
                .AddApplicationPart(typeof(ContractProbeController).Assembly));
        });

    [Theory]
    [InlineData("/missing-route", 404, "NOT_FOUND")]
    [InlineData("/__tests/contract/protected", 401, "UNAUTHORIZED")]
    [InlineData("/__tests/contract/failure", 409, "CATALOG.CONFLICT")]
    [InlineData("/__tests/contract/failure?type=NotFound", 404, "CATALOG.NOTFOUND")]
    [InlineData("/__tests/contract/failure?type=Validation", 400, "CATALOG.VALIDATION")]
    [InlineData("/__tests/contract/failure?type=Unauthorized", 401, "CATALOG.UNAUTHORIZED")]
    [InlineData("/__tests/contract/failure?type=Forbidden", 403, "CATALOG.FORBIDDEN")]
    [InlineData("/__tests/contract/paged?page=2147483647&pageSize=100", 400, "VALIDATION_FAILED")]
    [InlineData("/__tests/contract/paged?page=0", 400, "VALIDATION_FAILED")]
    [InlineData("/__tests/contract/crash", 500, "INTERNAL_ERROR")]
    public async Task ErrorsFollowTheSharedContract(string route, int status, string code)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync(route);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("Test exception", problem.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LivenessDoesNotRequireExternalServices()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/api/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void PaginationRejectsOffsetOverflowDuringValidation()
    {
        var request = new PagedRequest { Page = int.MaxValue, PageSize = 100 };
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), [], true));
        Assert.Throws<OverflowException>(() => request.Skip);
    }

    [Fact]
    public void ProductionRejectsMissingSecretsAtStartup()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "", ["Jwt:Key"] = "" }));
        });
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Production requires", exception.Message, StringComparison.Ordinal);
    }
}

// Registered by this test host only; never included in the deployed API assembly.
[ApiController]
[Route("__tests/contract")]
public sealed class ContractProbeController : ControllerBase
{
    [HttpGet("protected")]
    [Authorize]
    public IActionResult Protected() => Ok();

    [HttpGet("failure")]
    public IActionResult Failure([FromQuery] ErrorType type = ErrorType.Conflict) => Result.Failure<string>(
        new ApplicationError($"CATALOG.{type.ToString().ToUpperInvariant()}", "The product cannot be changed.", type)).ToActionResult(this);

    [HttpGet("paged")]
    public IActionResult Paged([FromQuery] PagedRequest request) => Ok(request.Skip);

    [HttpGet("crash")]
    public IActionResult Crash() => throw new InvalidOperationException($"Test exception for {HttpContext.TraceIdentifier}; must not appear in the HTTP response.");
}
