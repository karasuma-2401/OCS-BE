using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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


