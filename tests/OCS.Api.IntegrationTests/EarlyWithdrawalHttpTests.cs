using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Context.Interceptors;
using OCS.Api.Entities;
using OCS.Api.Models.Products;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class EarlyWithdrawalHttpTests
{
    [Fact]
    public async Task HttpWorkflowValidatesRecordBodiesAndCreatesApprovesCollectsHandsOverRejectsAndCancels()
    {
        var databaseName = Guid.NewGuid().ToString();
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "",
            ["Redis:ConnectionString"] = "",
            ["R2:ServiceUrl"] = "",
            ["Jwt:Issuer"] = "test",
            ["Jwt:Audience"] = "test",
            ["Jwt:Key"] = "test-only-key-with-at-least-32-bytes"
        };
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>((provider, options) => options.UseInMemoryDatabase(databaseName)
                    .AddInterceptors(provider.GetRequiredService<TimestampSaveChangesInterceptor>(), provider.GetRequiredService<AuditSaveChangesInterceptor>()));
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuer = "test";
                    options.TokenValidationParameters.ValidAudience = "test";
                    options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings["Jwt:Key"]!));
                });
            });
        });
        var customer = new Account
        {
            AccountType = AccountType.Customer,
            Status = AccountStatus.Active,
            EmailVerifiedAt = DateTimeOffset.UtcNow,
            Email = "withdraw-owner@example.com",
            FullName = "Khách rút hàng"
        };
        customer.CustomerProfile = new CustomerProfile { Id = customer.Id };
        var employee = new Account
        {
            AccountType = AccountType.Internal,
            Status = AccountStatus.Active,
            EmailVerifiedAt = DateTimeOffset.UtcNow,
            Email = "withdraw-staff@example.com",
            FullName = "Nhân viên"
        };
        employee.EmployeeProfile = new EmployeeProfile { Id = employee.Id };
        var customerSession = Guid.NewGuid(); var employeeSession = Guid.NewGuid();
        Guid[] productIds;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SystemAuditContext>().Initialize("WithdrawalHttpTest");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Accounts.AddRange(customer, employee);
            db.AccountRoles.AddRange(new AccountRole { AccountId = customer.Id, RoleId = Role.CustomerRoleId },
                new AccountRole { AccountId = employee.Id, RoleId = Guid.Parse("20000000-0000-0000-0000-000000000002") });
            db.AuthSessions.AddRange(new AuthSession { Id = customerSession, AccountId = customer.Id, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) },
                new AuthSession { Id = employeeSession, AccountId = employee.Id, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            var brand = new Brand { Name = "Nhãn", DefaultCommissionRate = 15 };
            var category = new Category { Name = "Áo" };
            var contract = new ConsignmentContract
            {
                ContractNumber = "HD-HTTP-01",
                CustomerId = customer.Id,
                CreatedByEmployeeId = employee.Id,
                SignedAt = DateTimeOffset.UtcNow.AddDays(-1),
                EndsAt = DateTimeOffset.UtcNow.AddMonths(2)
            };
            for (var i = 0; i < 3; i++)
            {
                var product = new Product { Name = $"Áo {i}", Barcode = $"HTTP-{i}", Brand = brand, Category = category };
                product.AssignConsignmentTerms(contract.Id, 20000, brand); product.OfferForSale(); contract.Products.Add(product);
            }
            productIds = contract.Products.Select(p => p.Id).ToArray();
            db.ConsignmentContracts.Add(contract); await db.SaveChangesAsync();
        }
        var tokenService = new TokenService(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Bypass-Csrf", "true");
        void AsCustomer() => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenService.GenerateAccessToken(customer, ["Customer"], customerSession));
        void AsStaff() => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenService.GenerateAccessToken(employee, ["Staff"], employeeSession));
        async Task<EarlyWithdrawalDto> Post(string path, object? body = null)
        {
            using var response = body is null ? await client.PostAsync(path, null) : await client.PostAsJsonAsync(path, body);
            Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return (await response.Content.ReadFromJsonAsync<EarlyWithdrawalDto>())!;
        }
        async Task Invalid(string path, object body)
        {
            using var response = await client.PostAsJsonAsync(path, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
        const string customerPath = "/api/customers/me/early-withdrawals";
        const string staffPath = "/api/early-withdrawals";
        AsCustomer();
        await Invalid(customerPath, new { productIds = Array.Empty<Guid>(), reason = "Lấy lại" });
        await Invalid(customerPath, new { productIds, reason = "" });
        await Invalid(customerPath, new { productIds, reason = new string('x', 1001) });
        await Invalid(customerPath, new { productIds = Enumerable.Repeat(productIds[0], 101).ToArray(), reason = "Lấy lại" });
        var created = await Post(customerPath, new { productIds = productIds.Take(1).ToArray(), reason = "Tôi muốn lấy lại sản phẩm để sử dụng." });
        Assert.Equal("Pending", created.Status); Assert.Equal(3000m, created.TotalPenaltyAmount);
        AsStaff();
        await Invalid($"{staffPath}/{created.Id}/reject", new { note = "" });
        await Invalid($"{staffPath}/{created.Id}/handover", new { evidence = new string('x', 1001) });
        await Invalid($"{staffPath}/{created.Id}/payment", new { amount = 3000, paymentMethod = "BankTransfer", reference = new string('x', 65) });
        Assert.Equal("Approved", (await Post($"{staffPath}/{created.Id}/approve")).Status);
        Assert.Equal("Paid", (await Post($"{staffPath}/{created.Id}/payment", new { amount = 3000, paymentMethod = "Cash" })).PaymentStatus);
        Assert.Equal("Completed", (await Post($"{staffPath}/{created.Id}/handover", new { evidence = "Khách đã nhận đủ món" })).Status);
        AsCustomer();
        var rejected = await Post(customerPath, new { productIds = productIds.Skip(1).Take(1).ToArray(), reason = "Thay đổi kế hoạch" });
        AsStaff();
        var decision = await Post($"{staffPath}/{rejected.Id}/reject", new { note = "Cần đối chiếu người nhận" });
        Assert.Equal("Rejected", decision.Status); Assert.Equal("Cần đối chiếu người nhận", decision.DecisionNote);
        AsCustomer();
        var cancelled = await Post(customerPath, new { productIds = productIds.Skip(2).ToArray(), reason = "Lấy lại" });
        Assert.Equal("Cancelled", (await Post($"{customerPath}/{cancelled.Id}/cancel")).Status);
        var again = await Post(customerPath, new { productIds = productIds.Skip(1).ToArray(), reason = "Gửi lại sau hủy và từ chối" });
        Assert.NotEqual(cancelled.Id, again.Id); Assert.NotEqual(rejected.Id, again.Id);
        Assert.Equal(6000m, again.TotalPenaltyAmount); Assert.Equal(2, again.Items.Count);
        using (var duplicate = await client.PostAsJsonAsync(customerPath, new { productIds = productIds.Skip(2).ToArray(), reason = "Gửi trùng" }))
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using (var oldCancel = await client.PostAsync($"{customerPath}/{cancelled.Id}/cancel", null))
            Assert.Equal(HttpStatusCode.Conflict, oldCancel.StatusCode);
        using (var completed = await client.PostAsJsonAsync(customerPath, new { productIds = productIds.Take(1).ToArray(), reason = "Đã nhận hàng" }))
            Assert.Equal(HttpStatusCode.Conflict, completed.StatusCode);
        Assert.Equal("Cancelled", (await client.GetFromJsonAsync<EarlyWithdrawalDto>($"{customerPath}/{cancelled.Id}"))!.Status);
        Assert.Equal("Rejected", (await client.GetFromJsonAsync<EarlyWithdrawalDto>($"{customerPath}/{rejected.Id}"))!.Status);
        using var verification = factory.Services.CreateScope();
        var resultDb = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(ProductStatus.EarlyWithdrawn, (await resultDb.Products.SingleAsync(p => p.Id == productIds[0])).Status);
        Assert.Equal(3000m, (await resultDb.FinancialTransactions.SingleAsync()).Amount);
        Assert.Contains(await resultDb.AuditLogs.ToArrayAsync(), log => log.EntityType == nameof(EarlyWithdrawal));
    }
}
