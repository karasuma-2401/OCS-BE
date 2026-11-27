using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
using OCS.Api.Authorization;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Context.Interceptors;
using OCS.Api.Entities;
using OCS.Api.Models.Contracts;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.SystemConfig;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class SystemConfigAndAuditLogTests
{
    private sealed class TestUserAuditContext(Guid accountId) : IAuditContext
    {
        public Guid? ActorAccountId => accountId;
        public AuditActorType ActorType => AuditActorType.User;
        public string Source => "SystemConfigController.Update";
        public string? IpAddress => "127.0.0.1";
        public string? CorrelationId => "corr-test";
    }

    [Fact]
    public async Task SystemConfigValidatesBoundsChecksVersionConcurrencyAndWritesAuditLogsAtomically()
    {
        var adminId = Guid.NewGuid();
        var auditContext = new TestUserAuditContext(adminId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(auditContext))
            .Options);

        await using (db)
        {
            var adminAccount = new Account
            {
                Id = adminId,
                AccountType = AccountType.Internal,
                FullName = "Quản trị viên",
                Username = "admin_cfg"
            };
            db.Accounts.Add(adminAccount);
            await db.SaveChangesAsync();

            var configService = new SystemConfigService(db);
            var auditService = new AuditLogQueryService(db);

            // 1. Default values
            var initial = (await configService.GetAsync(default)).Data!;
            Assert.Equal(30, initial.ConsignmentDurationDays);
            Assert.Equal(48, initial.PayoutAutoConfirmHours);
            Assert.Equal(10m, initial.EarlyWithdrawalFeeRate);
            Assert.Equal(10m, initial.CommissionTierRates.NoBrand);
            Assert.Equal(12m, initial.CommissionTierRates.LocalBrand);
            Assert.Equal(15m, initial.CommissionTierRates.Commercial);
            Assert.Equal(20m, initial.CommissionTierRates.Luxury);

            // 2. Validation bounds
            var badDuration = await configService.UpdateAsync(
                new UpdateSystemConfigRequest(0, initial.CommissionTierRates, 10m, 48, initial.Version), default);
            Assert.False(badDuration.IsSuccess);

            var badHours = await configService.UpdateAsync(
                new UpdateSystemConfigRequest(30, initial.CommissionTierRates, 10m, 721, initial.Version), default);
            Assert.False(badHours.IsSuccess);

            var badFee = await configService.UpdateAsync(
                new UpdateSystemConfigRequest(30, initial.CommissionTierRates, 105m, 48, initial.Version), default);
            Assert.False(badFee.IsSuccess);

            var badTier = await configService.UpdateAsync(
                new UpdateSystemConfigRequest(30, initial.CommissionTierRates with { Luxury = -1m }, 10m, 48, initial.Version), default);
            Assert.False(badTier.IsSuccess);

            // 3. Optimistic concurrency conflict
            var stale = await configService.UpdateAsync(
                new UpdateSystemConfigRequest(45, initial.CommissionTierRates, 12m, 24, "STALE_VERSION"), default);
            Assert.False(stale.IsSuccess);

            // 4. Valid update writes SystemParameters and triggers AuditSaveChangesInterceptor (4 SystemParameter logs)
            var nextTiers = new CommissionTierRatesDto(28m, 22m, 18m, 12m);
            var updated = await configService.UpdateAsync(
                new UpdateSystemConfigRequest(45, nextTiers, 12.5m, 24, initial.Version), default);
            Assert.True(updated.IsSuccess);
            Assert.Equal(45, updated.Data!.ConsignmentDurationDays);
            Assert.Equal(24, updated.Data.PayoutAutoConfirmHours);
            Assert.Equal(12.5m, updated.Data.EarlyWithdrawalFeeRate);
            Assert.Equal(12m, updated.Data.CommissionTierRates.Luxury);
            Assert.NotEqual(initial.Version, updated.Data.Version);

            // 5. Query audit logs
            var logs = (await auditService.SearchAsync(
                new AuditLogFilterRequest(EntityType: nameof(SystemParameter), ActorType: "User", Search: "payout.auto_confirm_hours"),
                default)).Data!;
            var autoConfirmLog = Assert.Single(logs.Items);
            Assert.Equal(adminId, autoConfirmLog.ActorAccountId);
            Assert.Equal("Quản trị viên", autoConfirmLog.ActorName);
            Assert.Contains("24", autoConfirmLog.Changes);
        }
    }

    [Fact]
    public async Task SystemConfigAndAuditLogEndpointsEnforceAdminPermissions()
    {
        Assert.Contains(PermissionCatalog.SettingsRead, PermissionCatalog.DefaultsFor("Admin"));
        Assert.Contains(PermissionCatalog.SettingsUpdate, PermissionCatalog.DefaultsFor("Admin"));
        Assert.Contains(PermissionCatalog.AuditLogsRead, PermissionCatalog.DefaultsFor("Admin"));
        Assert.DoesNotContain(PermissionCatalog.SettingsUpdate, PermissionCatalog.DefaultsFor("Manager"));
        Assert.DoesNotContain(PermissionCatalog.AuditLogsRead, PermissionCatalog.DefaultsFor("Staff"));

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
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "CfgTest";
                    options.DefaultChallengeScheme = "CfgTest";
                    options.DefaultForbidScheme = "CfgTest";
                }).AddScheme<AuthenticationSchemeOptions, RoleHeaderAuthHandler>("CfgTest", _ => { });
            });
        });

        var managerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.AddRange(
                new Account { Id = managerId, FullName = "Quản lý", PhoneNumber = "0900000011" },
                new AccountRole { AccountId = managerId, RoleId = new Guid("20000000-0000-0000-0000-000000000001") },
                new Account { Id = adminId, FullName = "Admin", PhoneNumber = "0900000012" },
                new AccountRole { AccountId = adminId, RoleId = new Guid("20000000-0000-0000-0000-000000000003") });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Bypass-Csrf", "true");

        // Manager lacks settings.read & audit_logs.read -> 403, but has appointments.staff.reception -> 200 on defaults
        using var mgrCfg = new HttpRequestMessage(HttpMethod.Get, "/api/system-config");
        mgrCfg.Headers.Add("X-Test-UserId", managerId.ToString());
        mgrCfg.Headers.Add("X-Test-Role", "Manager");
        using var mgrCfgRes = await client.SendAsync(mgrCfg);
        Assert.Equal(HttpStatusCode.Forbidden, mgrCfgRes.StatusCode);

        using var mgrDefaults = new HttpRequestMessage(HttpMethod.Get, "/api/system-config/defaults");
        mgrDefaults.Headers.Add("X-Test-UserId", managerId.ToString());
        mgrDefaults.Headers.Add("X-Test-Role", "Manager");
        using var mgrDefaultsRes = await client.SendAsync(mgrDefaults);
        Assert.Equal(HttpStatusCode.OK, mgrDefaultsRes.StatusCode);
        var defaultsPayload = await mgrDefaultsRes.Content.ReadAsStringAsync();
        Assert.DoesNotContain("payoutAutoConfirmHours", defaultsPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("version", defaultsPayload, StringComparison.OrdinalIgnoreCase);

        // User without appointments.staff.reception (e.g. Customer) -> 403 on defaults
        using var custDefaults = new HttpRequestMessage(HttpMethod.Get, "/api/system-config/defaults");
        custDefaults.Headers.Add("X-Test-UserId", Guid.NewGuid().ToString());
        custDefaults.Headers.Add("X-Test-Role", "Customer");
        using var custDefaultsRes = await client.SendAsync(custDefaults);
        Assert.Equal(HttpStatusCode.Forbidden, custDefaultsRes.StatusCode);

        using var mgrLogs = new HttpRequestMessage(HttpMethod.Get, "/api/audit-logs");
        mgrLogs.Headers.Add("X-Test-UserId", managerId.ToString());
        mgrLogs.Headers.Add("X-Test-Role", "Manager");
        using var mgrLogsRes = await client.SendAsync(mgrLogs);
        Assert.Equal(HttpStatusCode.Forbidden, mgrLogsRes.StatusCode);

        // Admin succeeds on GET, PUT, and audit-logs
        using var admCfg = new HttpRequestMessage(HttpMethod.Get, "/api/system-config");
        admCfg.Headers.Add("X-Test-UserId", adminId.ToString());
        admCfg.Headers.Add("X-Test-Role", "Admin");
        using var admCfgRes = await client.SendAsync(admCfg);
        Assert.Equal(HttpStatusCode.OK, admCfgRes.StatusCode);
        var current = await admCfgRes.Content.ReadFromJsonAsync<SystemConfigDto>();
        Assert.NotNull(current);

        using var putReq = new HttpRequestMessage(HttpMethod.Put, "/api/system-config")
        {
            Content = JsonContent.Create(new UpdateSystemConfigRequest(60, current!.CommissionTierRates, 15m, 36, current.Version))
        };
        putReq.Headers.Add("X-Test-UserId", adminId.ToString());
        putReq.Headers.Add("X-Test-Role", "Admin");
        using var putRes = await client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        using var admLogs = new HttpRequestMessage(HttpMethod.Get, "/api/audit-logs?entityType=SystemParameter");
        admLogs.Headers.Add("X-Test-UserId", adminId.ToString());
        admLogs.Headers.Add("X-Test-Role", "Admin");
        using var admLogsRes = await client.SendAsync(admLogs);
        Assert.Equal(HttpStatusCode.OK, admLogsRes.StatusCode);
    }

    [Fact]
    public async Task EarlyWithdrawalUsesContractFeeRateAndSystemConfigDrivesTierDefaults()
    {
        var now = new DateTimeOffset(2026, 11, 27, 8, 0, 0, TimeSpan.Zero);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        await using (db)
        {
            var configService = new SystemConfigService(db);
            var initial = (await configService.GetAsync(default)).Data!;
            var updated = (await configService.UpdateAsync(
                new UpdateSystemConfigRequest(45, new CommissionTierRatesDto(22m, 18m, 16m, 12m), 8m, 36, initial.Version),
                default)).Data!;

            var effective = await SystemConfigService.ReadEffectiveAsync(db, default);
            Assert.Equal(45, effective.ConsignmentDurationDays);
            Assert.Equal(12m, SystemConfigService.GetTierCommissionRate(effective.CommissionTierRates, BrandTier.Luxury));
            Assert.Equal(22m, SystemConfigService.GetTierCommissionRate(effective.CommissionTierRates, BrandTier.NoBrand));

            var customerAccount = new Account { AccountType = AccountType.Customer, FullName = "Khách", Username = "cust_ew", Status = AccountStatus.Active };
            var customer = new CustomerProfile { Id = customerAccount.Id, Account = customerAccount, Status = CustomerStatus.Active };
            var brand = new Brand { Name = "Luxury Brand", Tier = BrandTier.Luxury, DefaultCommissionRate = 0m };
            var category = new Category { Name = "Bag" };
            var contract = new ConsignmentContract
            {
                ContractNumber = "KG-CFG-1",
                CustomerId = customer.Id,
                SignedAt = now.AddDays(-5),
                EndsAt = now.AddDays(effective.ConsignmentDurationDays),
                EarlyWithdrawalFeeRate = effective.EarlyWithdrawalFeeRate,
                Status = ConsignmentContractStatus.Active
            };
            var product = new Product { Name = "Túi", Barcode = "BC-CFG-1", Size = "M", BrandId = brand.Id, CategoryId = category.Id };
            var tierRate = SystemConfigService.GetTierCommissionRate(effective.CommissionTierRates, brand.Tier);
            product.AssignConsignmentTerms(contract.Id, 1_000_000m, brand, tierRate);
            product.OfferForSale();

            db.AddRange(customerAccount, customer, brand, category, contract, product);
            await db.SaveChangesAsync();

            var ewService = new EarlyWithdrawalService(db, TimeProvider.System);
            var eligible = (await ewService.EligibleAsync(customer.Id, default)).Data!;
            var item = Assert.Single(eligible);
            Assert.Equal(8m, item.PenaltyRate);
            Assert.Equal(80_000m, item.PenaltyAmount); // 8% from SystemConfig snapshot, NOT 12% commission
            Assert.Equal(8m, contract.EarlyWithdrawalFeeRate);

            var customBrand = new Brand { Name = "Custom Brand", Tier = BrandTier.Commercial, DefaultCommissionRate = 19m };
            db.Brands.Add(customBrand);
            await db.SaveChangesAsync();

            var contractService = new ContractService(db);
            var options = await contractService.GetOptionsAsync(default);
            Assert.Equal(45, options.DefaultTermDays);
            Assert.Equal(8m, options.DefaultEarlyWithdrawalFeeRate);
            Assert.Equal(12m, Assert.Single(options.Brands, b => b.Id == brand.Id).CommissionRate);
            Assert.Equal(19m, Assert.Single(options.Brands, b => b.Id == customBrand.Id).CommissionRate);
        }
    }

    [Fact]
    public void ContractTermsFormatter_RejectsMismatchedRateAndTooLongTerms()
    {
        var mismatch = ContractTermsFormatter.BuildEffectiveTerms(
            "Điều khoản riêng có tỷ lệ phí rút sớm 15% trên hợp đồng.",
            8m,
            isCustomApprovedTerms: true);
        Assert.False(mismatch.IsSuccess);
        Assert.Equal("CONTRACT.TERMS_FEE_RATE_MISMATCH", mismatch.Error!.Code);

        var matching = ContractTermsFormatter.BuildEffectiveTerms(
            "Điều khoản riêng có tỷ lệ phí rút sớm 8% trên hợp đồng.",
            8m,
            isCustomApprovedTerms: true);
        Assert.True(matching.IsSuccess);

        var tooLong = ContractTermsFormatter.BuildEffectiveTerms(
            new string('a', 5950),
            8m,
            isCustomApprovedTerms: false);
        Assert.False(tooLong.IsSuccess);
        Assert.Equal("CONTRACT.TERMS_TOO_LONG", tooLong.Error!.Code);

        var legacyCustom = ContractTermsFormatter.BuildEffectiveTerms(
            "Phí rút hàng sớm bằng giá niêm yết nhân với tỷ lệ hoa hồng đã thỏa thuận.",
            8m,
            isCustomApprovedTerms: true);
        Assert.False(legacyCustom.IsSuccess);
        Assert.Equal("CONTRACT.TERMS_FEE_RATE_MISMATCH", legacyCustom.Error!.Code);

        var legacyTemplate = ContractTermsFormatter.BuildEffectiveTerms(
            "Điều khoản chung.\nPhí rút hàng sớm của mỗi sản phẩm bằng giá niêm yết nhân với tỷ lệ hoa hồng đã thỏa thuận.",
            8m,
            isCustomApprovedTerms: false);
        Assert.True(legacyTemplate.IsSuccess);
        Assert.DoesNotContain("hoa hồng", legacyTemplate.Data!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tỷ lệ phí rút sớm 8% trên hợp đồng", legacyTemplate.Data!);

        var staticTemplateMismatch = ContractTermsFormatter.BuildEffectiveTerms(
            "Điều khoản chung.\nPhí rút sớm 15% giá niêm yết.",
            10m,
            isCustomApprovedTerms: false);
        Assert.True(staticTemplateMismatch.IsSuccess);
        Assert.DoesNotContain("15%", staticTemplateMismatch.Data!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10%", staticTemplateMismatch.Data!);

        var staticTemplateSameRate = ContractTermsFormatter.BuildEffectiveTerms(
            "Điều khoản chung.\nPhí rút hàng sớm 10%.",
            10m,
            isCustomApprovedTerms: false);
        Assert.True(staticTemplateSameRate.IsSuccess);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(staticTemplateSameRate.Data!, @"10%"));

        var customStaticMismatch = ContractTermsFormatter.BuildEffectiveTerms(
            "Điều khoản riêng có phí rút sớm 15%.",
            8m,
            isCustomApprovedTerms: true);
        Assert.False(customStaticMismatch.IsSuccess);
        Assert.Equal("CONTRACT.TERMS_FEE_RATE_MISMATCH", customStaticMismatch.Error!.Code);

        var customNumberBeforeKeyword = ContractTermsFormatter.BuildEffectiveTerms(
            "Khách chịu phí 15% khi rút hàng sớm.",
            10m,
            isCustomApprovedTerms: true);
        Assert.False(customNumberBeforeKeyword.IsSuccess);
        Assert.Equal("CONTRACT.TERMS_FEE_RATE_MISMATCH", customNumberBeforeKeyword.Error!.Code);

        var templateNumberBeforeKeyword = ContractTermsFormatter.BuildEffectiveTerms(
            "Điều khoản chung.\nKhách chịu phí 15% khi rút hàng sớm.",
            10m,
            isCustomApprovedTerms: false);
        Assert.True(templateNumberBeforeKeyword.IsSuccess);
        Assert.DoesNotContain("15%", templateNumberBeforeKeyword.Data!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10%", templateNumberBeforeKeyword.Data!);

        var multiClauseTemplate = ContractTermsFormatter.BuildEffectiveTerms(
            "Rút hàng sớm chịu phí 15% và phải báo trước 3 ngày.",
            10m,
            isCustomApprovedTerms: false);
        Assert.True(multiClauseTemplate.IsSuccess);
        Assert.DoesNotContain("15%", multiClauseTemplate.Data!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10%", multiClauseTemplate.Data!);
        Assert.Contains("phải báo trước 3 ngày", multiClauseTemplate.Data!);

        var multiClauseLegacyTemplate = ContractTermsFormatter.BuildEffectiveTerms(
            "Rút hàng sớm chịu phí theo tỷ lệ hoa hồng đã thỏa thuận và phải báo trước 3 ngày.",
            8m,
            isCustomApprovedTerms: false);
        Assert.True(multiClauseLegacyTemplate.IsSuccess);
        Assert.DoesNotContain("hoa hồng", multiClauseLegacyTemplate.Data!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tỷ lệ phí rút sớm 8% trên hợp đồng", multiClauseLegacyTemplate.Data!);
        Assert.Contains("phải báo trước 3 ngày", multiClauseLegacyTemplate.Data!);

        var customUnrecognizedFee = ContractTermsFormatter.BuildEffectiveTerms(
            "Phí rút hàng sớm tính theo thỏa thuận riêng.",
            10m,
            isCustomApprovedTerms: true);
        Assert.False(customUnrecognizedFee.IsSuccess);
        Assert.Equal("CONTRACT.TERMS_FEE_RATE_MISMATCH", customUnrecognizedFee.Error!.Code);

        var nonFeePercentagePreserved = ContractTermsFormatter.BuildEffectiveTerms(
            "Chỉ được rút khi đã bán 50% sản phẩm.",
            10m,
            isCustomApprovedTerms: false);
        Assert.True(nonFeePercentagePreserved.IsSuccess);
        Assert.Contains("50% sản phẩm", nonFeePercentagePreserved.Data!);
        Assert.Contains("tỷ lệ phí rút sớm 10% trên hợp đồng", nonFeePercentagePreserved.Data!);

        var mixedNonFeeAndFeePercentage = ContractTermsFormatter.BuildEffectiveTerms(
            "Chỉ được rút khi đã bán 50% sản phẩm và chịu phí 15% khi rút hàng sớm.",
            10m,
            isCustomApprovedTerms: false);
        Assert.True(mixedNonFeeAndFeePercentage.IsSuccess);
        Assert.Contains("50% sản phẩm", mixedNonFeeAndFeePercentage.Data!);
        Assert.DoesNotContain("15%", mixedNonFeeAndFeePercentage.Data!);
        Assert.Contains("phí 10%", mixedNonFeeAndFeePercentage.Data!);

        var customMixedMatching = ContractTermsFormatter.BuildEffectiveTerms(
            "Chỉ được rút khi đã bán 50% sản phẩm và chịu phí 8% khi rút hàng sớm.",
            8m,
            isCustomApprovedTerms: true);
        Assert.True(customMixedMatching.IsSuccess);
        Assert.Contains("50% sản phẩm", customMixedMatching.Data!);

        var unrelatedFeePercentagePreserved = ContractTermsFormatter.BuildEffectiveTerms(
            "Chỉ được rút hàng sớm khi khách đã thanh toán 50% phí vận chuyển.",
            10m,
            isCustomApprovedTerms: false);
        Assert.True(unrelatedFeePercentagePreserved.IsSuccess);
        Assert.Contains("50% phí vận chuyển", unrelatedFeePercentagePreserved.Data!);
        Assert.Contains("tỷ lệ phí rút sớm 10% trên hợp đồng", unrelatedFeePercentagePreserved.Data!);
    }

    [Fact]
    public async Task ContractService_RejectsTamperedEarlyWithdrawalFeeRate()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var employee = new EmployeeProfile { Id = Guid.NewGuid() };
        db.EmployeeProfiles.Add(employee);
        await db.SaveChangesAsync();

        var service = new ContractService(db);
        var now = DateTimeOffset.UtcNow;
        var req = new CreateContractRequest
        {
            RequestId = Guid.NewGuid(),
            SignedAt = now,
            EndsAt = now.AddDays(30),
            ExpiryHandling = "Return",
            EarlyWithdrawalFeeRate = 0m,
            Guest = new ContractGuestRequest { FullName = "Khách", PhoneNumber = "0901234567" },
            Signature = new ContractImageRequest { ContentType = "image/png", Data = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=" }
        };

        var result = await service.CreateAsync(req, employee.Id, default);
        Assert.False(result.IsSuccess);
        Assert.Equal("CONTRACT.SYSTEM_CONFIG_CHANGED", result.Error!.Code);
    }

    private sealed class RoleHeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Request.Headers.TryGetValue("X-Test-UserId", out var uid) ? uid.ToString() : Guid.NewGuid().ToString();
            var role = Request.Headers.TryGetValue("X-Test-Role", out var r) ? r.ToString() : "Manager";
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role)],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
