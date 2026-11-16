using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Controllers;
using OCS.Api.Contracts.Categories;
using OCS.Api.Entities;
using OCS.Api.Models.Buyouts;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class BuyoutWorkflowTests
{
    [Fact]
    public async Task NewBuyoutsUseVietnamDateAndDistinctSequenceNumbers()
    {
        await using var db = NewDb();
        var (staff, _, customer, brand, category, _) = await SeedAsync(db);
        var service = new BuyoutService(db, TimeProvider.System);
        var first = await service.InspectAsync(staff, new(customer, "Jacket", "M", null, 90, brand, category, "Passed", true), default);
        var second = await service.InspectAsync(staff, new(customer, "Bag", "M", null, 90, brand, category, "Passed", true), default);
        var a = await service.CreateAsync(staff, new(first.Data!.Id, 100, 200), default);
        var b = await service.CreateAsync(staff, new(second.Data!.Id, 100, 200), default);
        Assert.True(a.IsSuccess);
        Assert.True(b.IsSuccess);
        var date = a.Data!.CreatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"BO-{date}-0001", a.Data.BuyoutNumber);
        Assert.Equal($"BO-{date}-0002", b.Data!.BuyoutNumber);
    }

    [Theory]
    [InlineData(999999, 1000000, "Approved")]
    [InlineData(1000000, 1000000, "Approved")]
    [InlineData(1000001, 1200000, "Pending")]
    [InlineData(1000000, 999999, "Pending")]
    public async Task DefaultApprovalThresholdAppliesToNewProposals(int purchasePrice, int listedPrice, string status)
    {
        await using var db = NewDb();
        var (staff, _, customer, brand, category, _) = await SeedAsync(db);
        var service = new BuyoutService(db, TimeProvider.System);
        var config = await service.GetApprovalConfigAsync(default);
        Assert.True(config.Enabled);
        Assert.Equal(1000000m, config.Threshold);
        var inspection = await service.InspectAsync(staff, new(customer, "Jacket", "M", null, 90, brand, category, "Passed", true), default);
        var created = await service.CreateAsync(staff, new(inspection.Data!.Id, purchasePrice, listedPrice), default);
        Assert.Equal(status, created.Data!.Status);
        Assert.Equal(1000000m, created.Data.AppliedApprovalThreshold);
        Assert.Null(created.Data.ApprovalConfigurationVersion);
        Assert.Empty(db.Products);
        Assert.Empty(db.FinancialTransactions);
    }

    [Fact]
    public async Task ApprovalConfigRejectsInvalidValuesAndDisabledOrMalformedConfigDoesNotAutoApprove()
    {
        await using var db = NewDb();
        var (staff, manager, customer, brand, category, _) = await SeedAsync(db);
        var service = new BuyoutService(db, TimeProvider.System);
        Assert.False((await service.UpdateApprovalConfigAsync(manager, new(true, 0, null, null), default)).IsSuccess);
        Assert.False((await service.UpdateApprovalConfigAsync(manager, new(true, 1.001m, null, null), default)).IsSuccess);
        var config = await service.UpdateApprovalConfigAsync(manager, new(false, null, null, "Off"), default);
        Assert.True(config.IsSuccess);
        var inspection = await service.InspectAsync(staff, new(customer, "Jacket", "M", null, 90, brand, category, "Passed", true), default);
        var proposal = await service.CreateAsync(staff, new(inspection.Data!.Id, 100, 200), default);
        Assert.Equal("Pending", proposal.Data!.Status);
        Assert.Equal("Disabled", proposal.Data.ApprovalDecisionReason);
        var updated = await service.UpdateApprovalConfigAsync(manager, new(true, 500, config.Data!.Version, "On"), default);
        Assert.True(updated.IsSuccess);
        Assert.Equal("Pending", (await service.GetAsync(proposal.Data.Id, default)).Data!.Status);
        Assert.Equal(config.Data.Version, (await service.GetAsync(proposal.Data.Id, default)).Data!.ApprovalConfigurationVersion);
        var parameter = await db.SystemParameters.SingleAsync(p => p.Key == "buyout.auto_approval");
        parameter.Value = "not-json";
        await db.SaveChangesAsync();
        Assert.False((await service.GetApprovalConfigAsync(default)).Enabled);
    }

    [Theory]
    [InlineData(300000, 600000, "Approved", "WithinThreshold")]
    [InlineData(500000, 500000, "Approved", "WithinThreshold")]
    [InlineData(700000, 800000, "Pending", "AboveThreshold")]
    [InlineData(300000, 250000, "Pending", "BelowPurchasePrice")]
    public async Task AutoApprovalUsesConfiguredThresholdAndPreservesPaymentWorkflow(int purchasePrice, int listedPrice, string status, string reason)
    {
        await using var db = NewDb();
        var (staff, manager, customer, brand, category, location) = await SeedAsync(db);
        var service = new BuyoutService(db, TimeProvider.System);
        var defaultConfig = await service.GetApprovalConfigAsync(default);
        Assert.True(defaultConfig.Enabled);
        Assert.Equal(1000000m, defaultConfig.Threshold);
        var configured = await service.UpdateApprovalConfigAsync(manager, new(true, 500000, null, "Threshold"), default);
        Assert.True(configured.IsSuccess);
        Assert.False((await service.UpdateApprovalConfigAsync(manager, new(true, 400000, null, null), default)).IsSuccess);
        var inspection = await service.InspectAsync(staff, new(customer, "Jacket", "M", null, 90, brand, category, "Passed", true), default);
        var created = await service.CreateAsync(staff, new(inspection.Data!.Id, purchasePrice, listedPrice), default);
        Assert.True(created.IsSuccess);
        Assert.Equal(status, created.Data!.Status);
        Assert.Equal(reason, created.Data.ApprovalDecisionReason);
        Assert.Equal(configured.Data!.Version, created.Data.ApprovalConfigurationVersion);
        Assert.Empty(db.Products);
        Assert.Empty(db.FinancialTransactions);
        if (status == "Approved")
        {
            Assert.True(created.Data.IsAutoApproved);
            Assert.Null(created.Data.ApprovedByEmployeeId);
            Assert.DoesNotContain(db.Notifications, n => n.Title == "Phiếu mua đứt chờ duyệt");
            Assert.Contains(db.AuditLogs, a => a.ActorType == AuditActorType.System && a.Action == "BuyoutAutoApproved");
            var paid = await service.PayAsync(created.Data.Id, staff, new(created.Data.Version, location, "Cash", null, true), default);
            Assert.True(paid.IsSuccess);
            Assert.True((await service.GetReceiptAsync(created.Data.Id, default)).IsSuccess);
            var product = Assert.Single(db.Products);
            var priceChange = await new InventoryService(db).ChangePriceAsync(product.Id, staff, new(product.InventoryVersion, 900000, "Adjustment"), default);
            Assert.True(priceChange.IsSuccess);
        }
        else Assert.False(created.Data.IsAutoApproved);
    }

    [Fact]
    public async Task CustomerLookupRequiresQueryAndOnlyReturnsActiveCustomers()
    {
        await using var db = NewDb();
        var (staff, _, customer, _, _, _) = await SeedAsync(db);
        var inactive = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Locked, FullName = "Customer inactive", PhoneNumber = "0900000005" };
        db.CustomerProfiles.Add(new CustomerProfile { Id = inactive.Id, Account = inactive });
        await db.SaveChangesAsync();
        var service = new BuyoutService(db, TimeProvider.System);
        Assert.False((await service.FindCustomersAsync(Guid.NewGuid(), "customer", default)).IsSuccess);
        Assert.False((await service.FindCustomersAsync(staff, "c", default)).IsSuccess);
        var byName = await service.FindCustomersAsync(staff, "CUSTOMER", default);
        Assert.Equal(customer, Assert.Single(byName.Data!).Id);
        var byPhone = await service.FindCustomersAsync(staff, "0900000003", default);
        Assert.Equal(customer, Assert.Single(byPhone.Data!).Id);
        var attributes = typeof(BuyoutsController).GetMethod(nameof(BuyoutsController.Customers))!.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>();
        Assert.Contains(attributes, a => a.Policy == "appointments.staff.reception");
    }
    [Fact]
    public async Task ApprovedPaymentCreatesOneStoreProductAndExpense()
    {
        await using var db = NewDb();
        var (staff, manager, customer, brand, category, location) = await SeedAsync(db);
        var service = new BuyoutService(db, TimeProvider.System);
        Assert.True((await service.UpdateApprovalConfigAsync(manager, new(false, null, null, null), default)).IsSuccess);
        var inspect = await service.InspectAsync(staff, new(customer, "Jacket", "M", null, 90, brand, category, "Passed inspection", true), default);
        Assert.True(inspect.IsSuccess);
        var created = await service.CreateAsync(staff, new(inspect.Data!.Id, 100000, 200000), default);
        Assert.True(created.IsSuccess);
        Assert.Equal("Pending", created.Data!.Status);
        Assert.Equal("Customer", created.Data.CustomerName);
        Assert.Equal("Brand", created.Data.Inspection.BrandName);
        Assert.Equal("Category", created.Data.Inspection.CategoryName);
        Assert.False((await service.CreateAsync(staff, new(inspect.Data.Id, 100000, 200000), default)).IsSuccess);
        Assert.Equal(created.Data.Id, Assert.Single((await service.ListAsync(default)).Data!).Id);
        Assert.Empty(db.Products);
        Assert.Empty(db.FinancialTransactions);
        Assert.False((await service.GetReceiptAsync(created.Data.Id, default)).IsSuccess);
        var payBeforeApproval = await service.PayAsync(created.Data.Id, staff, new(created.Data.Version, location, "Cash", null, true), default);
        Assert.False(payBeforeApproval.IsSuccess);
        var review = await service.ReviewAsync(created.Data.Id, manager, new(created.Data.Version, true, null), default);
        Assert.True(review.IsSuccess);
        Assert.Equal("Approved", review.Data!.Status);
        Assert.Empty(db.Products);
        var stale = await service.PayAsync(created.Data.Id, staff, new(created.Data.Version, location, "Cash", null, true), default);
        Assert.False(stale.IsSuccess);
        var paid = await service.PayAsync(created.Data.Id, staff, new(review.Data.Version, location, "Cash", null, true), default);
        Assert.True(paid.IsSuccess);
        Assert.Equal("Paid", paid.Data!.Status);
        var product = Assert.Single(db.Products);
        Assert.Equal(ProductSourceType.Buyout, product.SourceType);
        Assert.Equal(brand, product.BrandId);
        Assert.Equal(category, product.CategoryId);
        Assert.Equal(created.Data.Id, product.BuyoutId);
        Assert.Equal(100000m, product.PurchasePrice);
        var inventoryDetail = await new InventoryService(db).DetailAsync(product.Id, default);
        Assert.True(inventoryDetail.IsSuccess);
        Assert.Equal(100000m, inventoryDetail.Data!.PurchasePrice);
        Assert.Equal(created.Data.BuyoutNumber, inventoryDetail.Data.Product.SourceReference);
        Assert.Equal(product.Barcode, inventoryDetail.Data.Product.Barcode);
        Assert.Equal(200000m, product.ListedPrice);
        Assert.Equal(ProductStatus.OnSale, product.Status);
        Assert.Equal(ProductApprovalStatus.Approved, product.ApprovalStatus);
        var expense = Assert.Single(db.FinancialTransactions);
        Assert.Equal(FinancialTransactionType.Expense, expense.Type);
        Assert.Equal(FinancialTransactionStatus.Success, expense.Status);
        Assert.Equal(100000m, expense.Amount);
        var receipt = await service.GetReceiptAsync(created.Data.Id, default);
        Assert.True(receipt.IsSuccess);
        Assert.Equal(expense.Id, receipt.Data!.TransactionId);
        Assert.Equal("Cash", receipt.Data.PaymentMethod);
        Assert.Equal("Customer", receipt.Data.CustomerName);
        Assert.Equal("Staff", receipt.Data.PaidByEmployeeName);
        Assert.Equal(product.Barcode, receipt.Data.ProductBarcode);
        Assert.Single(db.ProductStatusHistories);
        var duplicate = await service.PayAsync(created.Data.Id, staff, new(paid.Data.Version, location, "Cash", null, true), default);
        Assert.False(duplicate.IsSuccess);
        Assert.Single(db.Products);
        Assert.Single(db.FinancialTransactions);
        var inspections = await service.ListInspectionsAsync(customer, default);
        Assert.False(Assert.Single(inspections.Data!).Available);
    }

    [Fact]
    public async Task BuyoutNotificationsFollowSuccessfulStateChangesAndReviewerPermissions()
    {
        await using var db = NewDb();
        var (staff, manager, customer, brand, category, _) = await SeedAsync(db);
        var role = new Role { Id = Role.ManagerRoleId, Code = "Manager", Name = "Manager" };
        var permission = new Permission { Code = "products.approve", Name = "Approve" };
        var adminRole = new Role { Id = Role.AdminRoleId, Code = "Admin", Name = "Admin" };
        var noPermission = new Account { AccountType = AccountType.Internal, Status = AccountStatus.Active, Username = "admin_" + Guid.NewGuid().ToString("N"), FullName = "Admin", PhoneNumber = "0900000004" };
        db.EmployeeProfiles.Add(new EmployeeProfile { Id = noPermission.Id, Account = noPermission, Status = EmployeeStatus.Working });
        db.AddRange(role, adminRole, permission, new RolePermission { Role = role, Permission = permission }, new AccountRole { AccountId = manager, Role = role }, new AccountRole { AccountId = noPermission.Id, Role = adminRole });
        await db.SaveChangesAsync();
        var service = new BuyoutService(db, TimeProvider.System);
        Assert.True((await service.UpdateApprovalConfigAsync(manager, new(false, null, null, null), default)).IsSuccess);
        var inspection = await service.InspectAsync(staff, new(customer, "Jacket", "M", null, 90, brand, category, "Passed", true), default);
        var proposal = await service.CreateAsync(staff, new(inspection.Data!.Id, 100, 200), default);
        var requestNotification = Assert.Single(db.Notifications);
        Assert.Equal(manager, requestNotification.RecipientAccountId);
        Assert.Contains(proposal.Data!.BuyoutNumber, requestNotification.Content);
        var reviewed = await service.ReviewAsync(proposal.Data.Id, manager, new(proposal.Data.Version, true, null), default);
        Assert.True(reviewed.IsSuccess);
        var decisionNotification = Assert.Single(db.Notifications.Where(n => n.RecipientAccountId == staff));
        Assert.Contains(proposal.Data.BuyoutNumber, decisionNotification.Content);
        Assert.False((await service.ReviewAsync(proposal.Data.Id, manager, new(proposal.Data.Version, true, null), default)).IsSuccess);
        Assert.Equal(2, db.Notifications.Count());
        var secondInspection = await service.InspectAsync(staff, new(customer, "Bag", "S", null, 90, brand, category, "Passed", true), default);
        var secondProposal = await service.CreateAsync(staff, new(secondInspection.Data!.Id, 100, 200), default);
        var rejected = await service.ReviewAsync(secondProposal.Data!.Id, manager, new(secondProposal.Data.Version, false, "Không phù hợp"), default);
        Assert.True(rejected.IsSuccess);
        Assert.Equal(2, db.Notifications.Count(n => n.RecipientAccountId == staff));
        Assert.Contains(db.Notifications, n => n.RecipientAccountId == staff && n.Content.Contains("từ chối"));
    }

    [Fact]
    public async Task RejectedProposalReleasesInspectionWithoutProductOrPayment()
    {
        await using var db = NewDb();
        var (staff, manager, customer, brand, category, location) = await SeedAsync(db);
        var service = new BuyoutService(db, TimeProvider.System);
        Assert.True((await service.UpdateApprovalConfigAsync(manager, new(false, null, null, null), default)).IsSuccess);
        var failedInspection = await service.InspectAsync(staff, new(customer, "Jacket", "M", null, 90, brand, category, "Failed", false), default);
        Assert.False(failedInspection.IsSuccess);
        var inspection = await service.InspectAsync(staff, new(customer, "Jacket", "M", null, 90, brand, category, "Passed", true), default);
        var proposal = await service.CreateAsync(staff, new(inspection.Data!.Id, 100, 200), default);
        var missingReason = await service.ReviewAsync(proposal.Data!.Id, manager, new(proposal.Data.Version, false, " "), default);
        Assert.False(missingReason.IsSuccess);
        var rejected = await service.ReviewAsync(proposal.Data.Id, manager, new(proposal.Data.Version, false, "Price not approved"), default);
        Assert.True(rejected.IsSuccess);
        Assert.Equal("Rejected", rejected.Data!.Status);
        Assert.Null(rejected.Data.ApprovedByEmployeeId);
        Assert.True(Assert.Single((await service.ListInspectionsAsync(customer, default)).Data!).Available);
        Assert.False((await service.PayAsync(proposal.Data.Id, staff, new(rejected.Data.Version, location, "Cash", null, true), default)).IsSuccess);
        Assert.True((await service.CreateAsync(staff, new(inspection.Data.Id, 120, 200), default)).IsSuccess);
        Assert.Empty(db.Products);
        Assert.Empty(db.FinancialTransactions);
    }

    [Fact]
    public async Task TransferRequiresReferenceAndActiveLocation()
    {
        await using var db = NewDb();
        var (staff, manager, customer, brand, category, location) = await SeedAsync(db);
        var service = new BuyoutService(db, TimeProvider.System);
        Assert.True((await service.UpdateApprovalConfigAsync(manager, new(false, null, null, null), default)).IsSuccess);
        var inspection = await service.InspectAsync(staff, new(customer, "Bag", "M", null, 95, brand, category, "Passed", true), default);
        var proposal = await service.CreateAsync(staff, new(inspection.Data!.Id, 100m, 200m), default);
        var approved = await service.ReviewAsync(proposal.Data!.Id, manager, new(proposal.Data.Version, true, null), default);
        Assert.False((await service.PayAsync(proposal.Data.Id, staff, new(approved.Data!.Version, location, "BankTransfer", " ", true), default)).IsSuccess);
        Assert.False((await service.PayAsync(proposal.Data.Id, staff, new(approved.Data.Version, Guid.NewGuid(), "Cash", null, true), default)).IsSuccess);
        Assert.False((await service.PayAsync(proposal.Data.Id, staff, new(approved.Data.Version, location, "Cash", null, false), default)).IsSuccess);
        var paid = await service.PayAsync(proposal.Data.Id, staff, new(approved.Data.Version, location, "BankTransfer", "BANK-123", true), default);
        Assert.True(paid.IsSuccess);
        Assert.Equal("BANK-123", Assert.Single(db.FinancialTransactions).BankTransactionReference);
        var receipt = await service.GetReceiptAsync(proposal.Data.Id, default);
        Assert.True(receipt.IsSuccess);
        Assert.Equal("BankTransfer", receipt.Data!.PaymentMethod);
        Assert.Equal("BANK-123", receipt.Data.BankTransactionReference);
    }

    [PostgresFact]
    public async Task ConcurrentProposalsAndPaymentsAreAtomicInPostgres()
    {
        await using var database = await TestDatabase.CreateAsync();
        Guid staff, manager, customer, brand, category, location, inspectionId;
        await using (var setup = database.Context())
        {
            (staff, manager, customer, brand, category, location) = await SeedAsync(setup);
            Assert.True((await new BuyoutService(setup, TimeProvider.System).UpdateApprovalConfigAsync(manager, new(false, null, null, null), default)).IsSuccess);
            var inspected = await new BuyoutService(setup, TimeProvider.System).InspectAsync(staff,
                new(customer, "Jacket", "M", null, 90, brand, category, "Passed", true), default);
            Assert.True(inspected.IsSuccess);
            inspectionId = inspected.Data!.Id;
        }
        await using var first = database.Context();
        await using var second = database.Context();
        var left = new BuyoutService(first, TimeProvider.System);
        var right = new BuyoutService(second, TimeProvider.System);
        var proposals = await Task.WhenAll(left.CreateAsync(staff, new(inspectionId, 100, 200), default),
            right.CreateAsync(staff, new(inspectionId, 100, 200), default));
        Assert.Single(proposals, x => x.IsSuccess);
        var proposal = proposals.Single(x => x.IsSuccess).Data!;
        await using var reviewContext = database.Context();
        var approved = await new BuyoutService(reviewContext, TimeProvider.System).ReviewAsync(proposal.Id, manager,
            new(proposal.Version, true, null), default);
        Assert.True(approved.IsSuccess);
        await using var payFirst = database.Context();
        await using var paySecond = database.Context();
        var payment = new PayBuyoutRequest(approved.Data!.Version, location, "Cash", null, true);
        var outcomes = await Task.WhenAll(new BuyoutService(payFirst, TimeProvider.System).PayAsync(proposal.Id, staff, payment, default),
            new BuyoutService(paySecond, TimeProvider.System).PayAsync(proposal.Id, staff, payment, default));
        Assert.Single(outcomes, x => x.IsSuccess);
        await using var verify = database.Context();
        Assert.Equal(1, await verify.Products.CountAsync(p => p.BuyoutId == proposal.Id));
        Assert.Equal(1, await verify.FinancialTransactions.CountAsync(t => t.BuyoutId == proposal.Id));
        var one = await new BuyoutService(verify, TimeProvider.System).InspectAsync(staff, new(customer, "Coat", "M", null, 90, brand, category, "Passed", true), default);
        var two = await new BuyoutService(verify, TimeProvider.System).InspectAsync(staff, new(customer, "Bag", "M", null, 90, brand, category, "Passed", true), default);
        await using var createFirst = database.Context();
        await using var createSecond = database.Context();
        var both = await Task.WhenAll(new BuyoutService(createFirst, TimeProvider.System).CreateAsync(staff, new(one.Data!.Id, 100, 200), default),
            new BuyoutService(createSecond, TimeProvider.System).CreateAsync(staff, new(two.Data!.Id, 100, 200), default));
        Assert.All(both, result => Assert.True(result.IsSuccess));
        Assert.NotEqual(both[0].Data!.BuyoutNumber, both[1].Data!.BuyoutNumber);
    }

    [Fact]
    public async Task AnonymousCallerCannotReadBuyouts()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "",
                ["Redis:ConnectionString"] = "",
                ["R2:ServiceUrl"] = ""
            }));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/buyouts")).StatusCode);
    }

    [Fact]
    public async Task ReviewHttpPipelineRejectsStaffAndAllowsManagerToReachEndpoint()
    {
        var dbName = Guid.NewGuid().ToString();
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
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuer = "test";
                    options.TokenValidationParameters.ValidAudience = "test";
                    options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings["Jwt:Key"]!));
                });
            });
        });
        var staffSession = Guid.NewGuid();
        var managerSession = Guid.NewGuid();
        var customerSession = Guid.NewGuid();
        var activeCategoryId = Guid.NewGuid();
        var inactiveCategoryId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        Account staff, manager, customer;
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SystemAuditContext>().Initialize("BuyoutHttpTest");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            staff = new Account { AccountType = AccountType.Internal, Status = AccountStatus.Active, Email = "staff@buyout.test", EmailVerifiedAt = DateTimeOffset.UtcNow };
            manager = new Account { AccountType = AccountType.Internal, Status = AccountStatus.Active, Email = "manager@buyout.test", EmailVerifiedAt = DateTimeOffset.UtcNow };
            db.EmployeeProfiles.AddRange(new EmployeeProfile { Id = staff.Id, Account = staff, Status = EmployeeStatus.Working }, new EmployeeProfile { Id = manager.Id, Account = manager, Status = EmployeeStatus.Working });
            db.AccountRoles.AddRange(new AccountRole { AccountId = staff.Id, RoleId = Role.StaffRoleId }, new AccountRole { AccountId = manager.Id, RoleId = Role.ManagerRoleId });
            db.AuthSessions.AddRange(new AuthSession { Id = staffSession, AccountId = staff.Id, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) }, new AuthSession { Id = managerSession, AccountId = manager.Id, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            db.Categories.AddRange(new Category { Id = activeCategoryId, Name = "Active", IsActive = true }, new Category { Id = inactiveCategoryId, Name = "Inactive", IsActive = false });
            db.Brands.Add(new Brand { Id = brandId, Name = "Brand", IsActive = true });
            customer = new Account { Id = customerId, AccountType = AccountType.Customer, Status = AccountStatus.Active, FullName = "Customer", PhoneNumber = "0900000004", Email = "customer@buyout.test", EmailVerifiedAt = DateTimeOffset.UtcNow };
            db.CustomerProfiles.Add(new CustomerProfile { Id = customerId, Account = customer });
            db.AccountRoles.Add(new AccountRole { AccountId = customerId, RoleId = Role.CustomerRoleId });
            db.AuthSessions.Add(new AuthSession { Id = customerSession, AccountId = customerId, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            await db.SaveChangesAsync();
        }
        var tokens = new TokenService(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Bypass-Csrf", "true");
        var route = $"/api/buyouts/{Guid.NewGuid()}/review";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/buyouts/approval-config")).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<CategoryResponse[]>("/api/categories"))!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateAccessToken(customer, ["Customer"], customerSession));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/buyouts/approval-config")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/categories?activeOnly=true")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateAccessToken(staff, ["Staff"], staffSession));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/buyouts/approval-config")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/buyouts/approval-config", new UpdateBuyoutApprovalConfigRequest(true, 500000, null, null))).StatusCode);
        var categories = await client.GetFromJsonAsync<CategoryResponse[]>("/api/categories?activeOnly=true");
        var activeCategory = Assert.Single(categories!);
        Assert.Equal(activeCategoryId, activeCategory.Id);
        Assert.Equal(2, (await client.GetFromJsonAsync<CategoryResponse[]>("/api/categories?activeOnly=false"))!.Length);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/buyouts/inspections", new CreateBuyoutInspectionRequest(customerId, "Jacket", "M", null, 90, brandId, activeCategory.Id, "Passed", true))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<SystemAuditContext>().Initialize("BuyoutHttpTest");
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grant = await db.RolePermissions.SingleAsync(p => p.RoleId == Role.StaffRoleId && p.Permission.Code == "appointments.staff.reception");
            db.RolePermissions.Remove(grant);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/categories?activeOnly=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, new ReviewBuyoutRequest(Guid.NewGuid(), true, null))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GenerateAccessToken(manager, ["Manager"], managerSession));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/buyouts/approval-config", new UpdateBuyoutApprovalConfigRequest(true, 500000, null, "Configuration"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(route, new ReviewBuyoutRequest(Guid.NewGuid(), true, null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/categories?activeOnly=true")).StatusCode);
    }

    [Fact]
    public async Task CategoryListReturnsEmptyArrayWhenNoCategoriesMatch()
    {
        await using var db = NewDb();
        db.Categories.Add(new Category { Name = "Inactive", IsActive = false });
        await db.SaveChangesAsync();
        var result = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await new CategoriesController(db).Get(true));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<CategoryResponse>>(result.Value));
    }

    [Fact]
    public void ReviewEndpointRequiresManagerOrAdminAndApprovalPermission()
    {
        var method = typeof(BuyoutsController).GetMethod(nameof(BuyoutsController.Review))!;
        var attributes = method.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().ToArray();
        Assert.Contains(attributes, a => a.Roles == "Admin,Manager");
        Assert.Contains(attributes, a => a.Policy == "products.approve");
    }

    private static AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static async Task<(Guid Staff, Guid Manager, Guid Customer, Guid Brand, Guid Category, Guid Location)> SeedAsync(AppDbContext db)
    {
        var staff = new Account { AccountType = AccountType.Internal, Status = AccountStatus.Active, Username = "staff_" + Guid.NewGuid().ToString("N"), PasswordHash = "hash", FullName = "Staff", PhoneNumber = "0900000001" };
        var manager = new Account { AccountType = AccountType.Internal, Status = AccountStatus.Active, Username = "manager_" + Guid.NewGuid().ToString("N"), PasswordHash = "hash", FullName = "Manager", PhoneNumber = "0900000002" };
        var customer = new Account { AccountType = AccountType.Customer, Status = AccountStatus.Active, FullName = "Customer", PhoneNumber = "0900000003" };
        db.EmployeeProfiles.AddRange(new EmployeeProfile { Id = staff.Id, Account = staff, Status = EmployeeStatus.Working }, new EmployeeProfile { Id = manager.Id, Account = manager, Status = EmployeeStatus.Working });
        db.CustomerProfiles.Add(new CustomerProfile { Id = customer.Id, Account = customer });
        var brand = new Brand { Name = "Brand", DefaultCommissionRate = 20 };
        var category = new Category { Name = "Category" };
        var location = new StorageLocation { Code = "A1", Name = "A1", IsActive = true };
        db.AddRange(brand, category, location);
        await db.SaveChangesAsync();
        return (staff.Id, manager.Id, customer.Id, brand.Id, category.Id, location.Id);
    }
}
