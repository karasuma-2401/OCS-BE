using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OCS.Api.Auditing;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Context.Interceptors;
using OCS.Api.Entities;
using OCS.Api.Models.Customers;
using OCS.Api.Repositories;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class CustomerSelfProfileRealIntegrationTests
{
    private const string TestJwtKey = "ocs-super-secret-key-that-is-at-least-32-bytes-long";
    private static (AppDbContext Context, CustomerProfileService Service, SystemAuditContext AuditContext) CreateService(TestDatabase database)
    {
        var auditContext = new SystemAuditContext();
        auditContext.Initialize("RealIntegrationTest");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.ConnectionString)
            .AddInterceptors(new TimestampSaveChangesInterceptor(), new AuditSaveChangesInterceptor(auditContext))
            .Options;

        var context = new AppDbContext(options);
        var customerRepo = new CustomerProfileRepository(context);
        var authRepo = new AuthRepository(context);
        var productRepo = new ProductRepository(context);
        var appointmentRepo = new AppointmentRepository(context);
        var notificationRepo = new NotificationRepository(context);
        var uow = new UnitOfWork(context, productRepo, authRepo, appointmentRepo, notificationRepo, customerRepo);
        var service = new CustomerProfileService(uow);

        return (context, service, auditContext);
    }

    private static async Task<Account> SeedCustomerAsync(
        AppDbContext context,
        Guid id,
        string fullName,
        string phoneNumber,
        string email,
        string? bankCode = null,
        string? bankAccountNumber = null,
        string? bankAccountHolderName = null,
        bool createProfile = true)
    {
        var account = new Account
        {
            Id = id,
            FullName = fullName,
            PhoneNumber = phoneNumber,
            Email = email,
            AccountType = AccountType.Customer,
            Status = AccountStatus.Active,
            EmailVerifiedAt = DateTimeOffset.UtcNow,
            ActivatedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.Accounts.Add(account);

        if (createProfile)
        {
            var profile = new CustomerProfile
            {
                Id = id,
                BankCode = bankCode,
                BankAccountNumber = bankAccountNumber,
                BankAccountHolderName = bankAccountHolderName,
                Status = CustomerStatus.Active
            };
            context.CustomerProfiles.Add(profile);
        }

        context.AccountRoles.Add(new AccountRole
        {
            AccountId = id,
            RoleId = Role.CustomerRoleId
        });

        await context.SaveChangesAsync();
        return account;
    }

    private static async Task SeedPayoutAsync(
        AppDbContext context,
        Guid customerId,
        PayoutStatus status)
    {
        var employeeId = Guid.NewGuid();
        var employeeAccount = new Account
        {
            Id = employeeId,
            FullName = "Employee Tester",
            PhoneNumber = "098" + Random.Shared.Next(1000000, 9999999).ToString("D7"),
            Username = "emp_" + employeeId.ToString("N")[..8],
            Email = $"emp_{employeeId:N}@example.com",
            PasswordHash = "$2a$11$LVrkKmxPZJm4JnKXZsgMjuGNMXKczO5VF71yH10t42unyeBUM6Kb.",
            AccountType = AccountType.Internal,
            Status = AccountStatus.Active
        };
        context.Accounts.Add(employeeAccount);
        context.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = employeeId,
            JobTitle = "Staff",
            HiredOn = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = EmployeeStatus.Working
        });

        var contractId = Guid.NewGuid();
        context.ConsignmentContracts.Add(new ConsignmentContract
        {
            Id = contractId,
            ContractNumber = "HD-" + contractId.ToString("N")[..8],
            CustomerId = customerId,
            CreatedByEmployeeId = employeeId,
            SignedAt = DateTimeOffset.UtcNow,
            EndsAt = DateTimeOffset.UtcNow.AddDays(30),
            ExpiryHandling = ExpiryHandling.Donate,
            Status = ConsignmentContractStatus.Active
        });

        var payoutId = Guid.NewGuid();
        context.Payouts.Add(new Payout
        {
            Id = payoutId,
            PayoutNumber = "PO-" + payoutId.ToString("N")[..8],
            ConsignmentContractId = contractId,
            ApprovedByEmployeeId = employeeId,
            TotalAmount = 1000000,
            Status = status
        });

        await context.SaveChangesAsync();
    }

    [PostgresFact]
    public async Task GetSelfProfileAsync_WithRealPostgreSql_ReturnsCorrectDtoAndNullBankDetails()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (context, service, _) = CreateService(database);
        await using (context)
        {
            var customerId = Guid.NewGuid();
            await SeedCustomerAsync(context, customerId, "Nguyen Van A", "0901234567", "a@example.com");

            var result = await service.GetSelfProfileAsync(customerId);

            Assert.True(result.IsSuccess);
            var data = result.Data!;
            Assert.Equal(customerId, data.Id);
            Assert.Equal("Nguyen Van A", data.FullName);
            Assert.Equal("0901234567", data.PhoneNumber);
            Assert.Null(data.BankAccount);
        }
    }

    [PostgresFact]
    public async Task GetSelfProfileAsync_WithPopulatedBankDetails_ReturnsMappedDto()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (context, service, _) = CreateService(database);
        await using (context)
        {
            var customerId = Guid.NewGuid();
            await SeedCustomerAsync(context, customerId, "Nguyen Van B", "0901234568", "b@example.com", "VCB", "0011002233", "NGUYEN VAN B");

            var result = await service.GetSelfProfileAsync(customerId);

            Assert.True(result.IsSuccess);
            var data = result.Data!;
            Assert.Equal(customerId, data.Id);
            Assert.NotNull(data.BankAccount);
            Assert.Equal("VCB", data.BankAccount.BankCode);
            Assert.Equal("0011002233", data.BankAccount.AccountNumber);
            Assert.Equal("NGUYEN VAN B", data.BankAccount.AccountHolderName);
        }
    }

    [PostgresFact]
    public async Task GetSelfProfileAsync_WhenCustomerHasNoProfile_ReturnsProfileNotFound()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (context, service, _) = CreateService(database);
        await using (context)
        {
            var customerId = Guid.NewGuid();
            await SeedCustomerAsync(context, customerId, "Legacy No Profile", "0901234569", "legacy@example.com", createProfile: false);

            var result = await service.GetSelfProfileAsync(customerId);

            Assert.False(result.IsSuccess);
            Assert.Equal(CustomerErrorCodes.ProfileNotFound, result.Error!.Code);
        }
    }

    [PostgresFact]
    public async Task UpdateSelfProfileAsync_WithRealPostgreSql_SavesAccountAndProfileAtomically()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (context, service, _) = CreateService(database);
        await using (context)
        {
            var customerId = Guid.NewGuid();
            await SeedCustomerAsync(context, customerId, "Nguyen Van C", "0901234570", "c@example.com");

            var request = new UpdateCustomerSelfProfileRequest
            {
                FullName = "Nguyen Van C Updated",
                PhoneNumber = "0909999888",
                BankAccount = new UpdateBankAccountRequest
                {
                    BankCode = "mb",
                    AccountNumber = "0987654321",
                    AccountHolderName = "NGUYEN VAN C"
                }
            };

            var result = await service.UpdateSelfProfileAsync(customerId, request);

            Assert.True(result.IsSuccess);
            var data = result.Data!;
            Assert.Equal("Nguyen Van C Updated", data.FullName);
            Assert.Equal("0909999888", data.PhoneNumber);
            Assert.NotNull(data.BankAccount);
            Assert.Equal("MB", data.BankAccount.BankCode); // Trimmed and uppercase
            Assert.Equal("0987654321", data.BankAccount.AccountNumber);

            // Re-read directly from PostgreSQL
            await using var verifyContext = database.Context();
            var accountDb = await verifyContext.Accounts.Include(a => a.CustomerProfile).FirstAsync(a => a.Id == customerId);
            Assert.Equal("Nguyen Van C Updated", accountDb.FullName);
            Assert.Equal("0909999888", accountDb.PhoneNumber);
            Assert.Equal("MB", accountDb.CustomerProfile!.BankCode);
            Assert.Equal("0987654321", accountDb.CustomerProfile.BankAccountNumber);
        }
    }

    [PostgresFact]
    public async Task UpdateSelfProfileAsync_WhenBankDetailsCleared_PersistsNullValuesInPostgreSql()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (context, service, _) = CreateService(database);
        await using (context)
        {
            var customerId = Guid.NewGuid();
            await SeedCustomerAsync(context, customerId, "Nguyen Van D", "0901234571", "d@example.com", "ACB", "11223344", "NGUYEN VAN D");

            var request = new UpdateCustomerSelfProfileRequest
            {
                FullName = "Nguyen Van D",
                PhoneNumber = "0901234571",
                BankAccount = null
            };

            var result = await service.UpdateSelfProfileAsync(customerId, request);

            Assert.True(result.IsSuccess);
            Assert.Null(result.Data!.BankAccount);

            // Verify in PostgreSQL
            await using var verifyContext = database.Context();
            var profileDb = await verifyContext.CustomerProfiles.FirstAsync(cp => cp.Id == customerId);
            Assert.Null(profileDb.BankCode);
            Assert.Null(profileDb.BankAccountNumber);
            Assert.Null(profileDb.BankAccountHolderName);
        }
    }

    [PostgresFact]
    public async Task UpdateSelfProfileAsync_WhenDuplicatePhone_RollsBackTransactionAndLeavesDatabaseUnchanged()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (context, service, _) = CreateService(database);
        await using (context)
        {
            var customer1 = Guid.NewGuid();
            var customer2 = Guid.NewGuid();
            await SeedCustomerAsync(context, customer1, "Customer 1", "0901111111", "c1@example.com", "VCB", "1111", "HOLDER 1");
            await SeedCustomerAsync(context, customer2, "Customer 2", "0902222222", "c2@example.com", "ACB", "2222", "HOLDER 2");

            // Customer 1 attempts to take Customer 2's phone number
            var request = new UpdateCustomerSelfProfileRequest
            {
                FullName = "Attempted Malicious Update",
                PhoneNumber = "0902222222",
                BankAccount = new UpdateBankAccountRequest
                {
                    BankCode = "TPB",
                    AccountNumber = "9999",
                    AccountHolderName = "ATTEMPT"
                }
            };

            var result = await service.UpdateSelfProfileAsync(customer1, request);

            Assert.False(result.IsSuccess);
            Assert.Equal(CustomerErrorCodes.PhoneNumberExists, result.Error!.Code);

            // Verify Atomic Rollback: Customer 1 data must remain unchanged in PostgreSQL
            await using var verifyContext = database.Context();
            var c1Db = await verifyContext.Accounts.Include(a => a.CustomerProfile).FirstAsync(a => a.Id == customer1);
            Assert.Equal("Customer 1", c1Db.FullName);
            Assert.Equal("0901111111", c1Db.PhoneNumber);
            Assert.Equal("VCB", c1Db.CustomerProfile!.BankCode);
            Assert.Equal("1111", c1Db.CustomerProfile.BankAccountNumber);
        }
    }

    [PostgresFact]
    public async Task UpdateSelfProfileAsync_WhenPendingPayoutExists_RejectsBankAccountChanges()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (context, service, _) = CreateService(database);
        await using (context)
        {
            var customerId = Guid.NewGuid();
            await SeedCustomerAsync(context, customerId, "Customer Pending Payout", "0903333333", "payout@example.com", "VCB", "1000", "CUSTOMER");
            await SeedPayoutAsync(context, customerId, PayoutStatus.Pending);

            // Attempt to change bank account
            var changeBankRequest = new UpdateCustomerSelfProfileRequest
            {
                FullName = "Customer Pending Payout",
                PhoneNumber = "0903333333",
                BankAccount = new UpdateBankAccountRequest
                {
                    BankCode = "MB",
                    AccountNumber = "2000",
                    AccountHolderName = "CUSTOMER"
                }
            };

            var result = await service.UpdateSelfProfileAsync(customerId, changeBankRequest);

            Assert.False(result.IsSuccess);
            Assert.Equal(CustomerErrorCodes.BankAccountLockedForPayout, result.Error!.Code);

            // Attempt to clear bank account
            var clearBankRequest = new UpdateCustomerSelfProfileRequest
            {
                FullName = "Customer Pending Payout",
                PhoneNumber = "0903333333",
                BankAccount = null
            };

            var clearResult = await service.UpdateSelfProfileAsync(customerId, clearBankRequest);

            Assert.False(clearResult.IsSuccess);
            Assert.Equal(CustomerErrorCodes.BankAccountLockedForPayout, clearResult.Error!.Code);
        }
    }

    [PostgresFact]
    public async Task UpdateSelfProfileAsync_WhenPendingPayoutExists_PermitsNameAndPhoneUpdate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (context, service, _) = CreateService(database);
        await using (context)
        {
            var customerId = Guid.NewGuid();
            await SeedCustomerAsync(context, customerId, "Original Name", "0904444444", "payout_permit@example.com", "VCB", "1000", "CUSTOMER");
            await SeedPayoutAsync(context, customerId, PayoutStatus.Pending);

            // Leaving bank info identical, updating only name and phone
            var contactUpdateRequest = new UpdateCustomerSelfProfileRequest
            {
                FullName = "New Name Allowed",
                PhoneNumber = "0904444999",
                BankAccount = new UpdateBankAccountRequest
                {
                    BankCode = "VCB",
                    AccountNumber = "1000",
                    AccountHolderName = "CUSTOMER"
                }
            };

            var result = await service.UpdateSelfProfileAsync(customerId, contactUpdateRequest);

            Assert.True(result.IsSuccess);
            Assert.Equal("New Name Allowed", result.Data!.FullName);
            Assert.Equal("0904444999", result.Data.PhoneNumber);
            Assert.Equal("VCB", result.Data.BankAccount!.BankCode);
        }
    }

    [PostgresFact]
    public async Task UpdateSelfProfileAsync_WhenOnlyTransferredOrCompletedPayoutsExist_AllowsBankUpdate()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (context, service, _) = CreateService(database);
        await using (context)
        {
            var customerId = Guid.NewGuid();
            await SeedCustomerAsync(context, customerId, "Customer Transferred", "0905555555", "transferred@example.com", "ACB", "5000", "CUSTOMER");
            await SeedPayoutAsync(context, customerId, PayoutStatus.Transferred);
            await SeedPayoutAsync(context, customerId, PayoutStatus.Completed);

            // Modifying bank account should succeed
            var updateBankRequest = new UpdateCustomerSelfProfileRequest
            {
                FullName = "Customer Transferred",
                PhoneNumber = "0905555555",
                BankAccount = new UpdateBankAccountRequest
                {
                    BankCode = "TPB",
                    AccountNumber = "7777",
                    AccountHolderName = "CUSTOMER NEW"
                }
            };

            var result = await service.UpdateSelfProfileAsync(customerId, updateBankRequest);

            Assert.True(result.IsSuccess);
            Assert.Equal("TPB", result.Data!.BankAccount!.BankCode);
            Assert.Equal("7777", result.Data.BankAccount.AccountNumber);
        }
    }

    [PostgresFact]
    public async Task UpdateSelfProfileAsync_WithRealAuditInterceptor_MasksSensitiveFieldsInAuditLogs()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (context, service, _) = CreateService(database);
        await using (context)
        {
            var customerId = Guid.NewGuid();
            await SeedCustomerAsync(context, customerId, "Initial Name", "0906666666", "audit@example.com", "BIDV", "666677778888", "INITIAL HOLDER");

            var request = new UpdateCustomerSelfProfileRequest
            {
                FullName = "Updated Name",
                PhoneNumber = "0906666999",
                BankAccount = new UpdateBankAccountRequest
                {
                    BankCode = "VCB",
                    AccountNumber = "1234567890",
                    AccountHolderName = "NEW HOLDER"
                }
            };

            var result = await service.UpdateSelfProfileAsync(customerId, request);
            Assert.True(result.IsSuccess);

            // Query AuditLogs in PostgreSQL
            await using var verifyContext = database.Context();
            var auditEntries = await verifyContext.AuditLogs
                .Where(a => a.EntityId == customerId.ToString())
                .ToListAsync();

            Assert.NotEmpty(auditEntries);

            // Raw sensitive values should never appear unmasked in any audit log
            foreach (var entry in auditEntries)
            {
                var changesJson = entry.Changes ?? string.Empty;
                Assert.DoesNotContain("0906666666", changesJson);
                Assert.DoesNotContain("0906666999", changesJson);
                Assert.DoesNotContain("666677778888", changesJson);
                Assert.DoesNotContain("1234567890", changesJson);
            }

            // Verify specific update entries have REDACTED names and masked numbers
            var accountUpdate = Assert.Single(auditEntries, a => a.EntityType == "Account" && a.Action == "AccountUpdated");
            Assert.Contains("[REDACTED]", accountUpdate.Changes);
            Assert.Contains("****6999", accountUpdate.Changes);

            var profileUpdate = Assert.Single(auditEntries, a => a.EntityType == "CustomerProfile" && a.Action == "CustomerProfileUpdated");
            Assert.Contains("[REDACTED]", profileUpdate.Changes);
            Assert.Contains("****7890", profileUpdate.Changes);
        }
    }

    [PostgresFact]
    public async Task Ownership_Isolation_CustomerACannotAccessOrModifyCustomerB()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (context, service, _) = CreateService(database);
        await using (context)
        {
            var customerA = Guid.NewGuid();
            var customerB = Guid.NewGuid();
            await SeedCustomerAsync(context, customerA, "Customer A", "0907777111", "a_iso@example.com", "VCB", "1111", "HOLDER A");
            await SeedCustomerAsync(context, customerB, "Customer B", "0907777222", "b_iso@example.com", "ACB", "2222", "HOLDER B");

            // Service method only accepts current user's ID
            var profileA = await service.GetSelfProfileAsync(customerA);
            var profileB = await service.GetSelfProfileAsync(customerB);

            Assert.Equal("Customer A", profileA.Data!.FullName);
            Assert.Equal("1111", profileA.Data.BankAccount!.AccountNumber);

            Assert.Equal("Customer B", profileB.Data!.FullName);
            Assert.Equal("2222", profileB.Data.BankAccount!.AccountNumber);

            // Customer A cannot change Customer B's data
            var updateRequest = new UpdateCustomerSelfProfileRequest
            {
                FullName = "Malicious A",
                PhoneNumber = "0907777111",
                BankAccount = null
            };
            await service.UpdateSelfProfileAsync(customerA, updateRequest);

            await using var verifyContext = database.Context();
            var bDb = await verifyContext.Accounts.Include(a => a.CustomerProfile).FirstAsync(a => a.Id == customerB);
            Assert.Equal("Customer B", bDb.FullName);
            Assert.Equal("2222", bDb.CustomerProfile!.BankAccountNumber);
        }
    }

    private static async Task SeedSessionAsync(AppDbContext context, Guid accountId, Guid sessionId)
    {
        var session = new AuthSession
        {
            Id = sessionId,
            AccountId = accountId,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(8)
        };
        context.AuthSessions.Add(session);
        await context.SaveChangesAsync();
    }

    private static string GenerateJwt(Guid accountId, string name, string phone, string email, string role, Guid sessionId)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ocs-api",
            ["Jwt:Audience"] = "ocs-clients",
            ["Jwt:Key"] = TestJwtKey
        }).Build();
        var account = new Account { Id = accountId, FullName = name, PhoneNumber = phone, Email = email };
        return new TokenService(configuration).GenerateAccessToken(account, [role], sessionId);
    }

    private static WebApplicationFactory<Program> CreateHttpFactory(TestDatabase database) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "",
                ["Redis:ConnectionString"] = "",
                ["Email:Provider"] = "TestSink",
                ["Jwt:Issuer"] = "ocs-api",
                ["Jwt:Audience"] = "ocs-clients",
                ["Jwt:Key"] = TestJwtKey,
                ["Auth:SessionExpirySeconds"] = "28800"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(database.ConnectionString));
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuer = "ocs-api";
                    options.TokenValidationParameters.ValidAudience = "ocs-clients";
                    options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtKey));
                });
            });
        });

    [PostgresFact]
    public async Task HttpPipeline_EndToEndFlow_WithRealPostgreSql_VerifiesAllSecurityAndDataContracts()
    {
        await using var database = await TestDatabase.CreateAsync();
        var customerA = Guid.NewGuid();
        var sessionA = Guid.NewGuid();
        var customerPending = Guid.NewGuid();
        var sessionPending = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var sessionStaff = Guid.NewGuid();

        await using (var context = database.Context())
        {
            await SeedCustomerAsync(context, customerA, "Customer A", "0908888111", "cust_a@example.com", "VCB", "11112222", "HOLDER A");
            await SeedSessionAsync(context, customerA, sessionA);

            await SeedCustomerAsync(context, customerPending, "Customer Pending", "0908888222", "cust_pending@example.com", "BIDV", "33334444", "HOLDER PENDING");
            await SeedSessionAsync(context, customerPending, sessionPending);
            await SeedPayoutAsync(context, customerPending, PayoutStatus.Pending);

            var staffAccount = new Account
            {
                Id = staffId,
                FullName = "Staff Member",
                Username = "staff_pipeline",
                Email = "staff_pipeline@example.com",
                PhoneNumber = "0908888555",
                AccountType = AccountType.Internal,
                Status = AccountStatus.Active,
                PasswordHash = "$2a$11$LVrkKmxPZJm4JnKXZsgMjuGNMXKczO5VF71yH10t42unyeBUM6Kb.",
                EmailVerifiedAt = DateTimeOffset.UtcNow,
                ActivatedAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.Accounts.Add(staffAccount);
            context.AccountRoles.Add(new AccountRole { AccountId = staffId, RoleId = Role.StaffRoleId });
            await SeedSessionAsync(context, staffId, sessionStaff);
        }

        var tokenA = GenerateJwt(customerA, "Customer A", "0908888111", "cust_a@example.com", "Customer", sessionA);
        var tokenPending = GenerateJwt(customerPending, "Customer Pending", "0908888222", "cust_pending@example.com", "Customer", sessionPending);
        var tokenStaff = GenerateJwt(staffId, "Staff Member", "0908888555", "staff_pipeline@example.com", "Staff", sessionStaff);

        await using var factory = CreateHttpFactory(database);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

        // 1. Unauthenticated request -> 401 Unauthorized
        using (var unauthRes = await client.GetAsync("/api/customers/me"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);
        }

        // 2. Staff user -> 403 Forbidden
        using (var staffReq = new HttpRequestMessage(HttpMethod.Get, "/api/customers/me"))
        {
            staffReq.Headers.Add("Cookie", $"ocs_session={tokenStaff}");
            using var staffRes = await client.SendAsync(staffReq);
            Assert.Equal(HttpStatusCode.Forbidden, staffRes.StatusCode);
        }

        // 3. Customer A GET Profile -> 200 OK with redacted sensitive internal fields
        using (var getReq = new HttpRequestMessage(HttpMethod.Get, "/api/customers/me"))
        {
            getReq.Headers.Add("Cookie", $"ocs_session={tokenA}");
            using var getRes = await client.SendAsync(getReq);
            Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

            var json = await getRes.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("Customer A", root.GetProperty("fullName").GetString());
            Assert.Equal("0908888111", root.GetProperty("phoneNumber").GetString());
            Assert.Equal("VCB", root.GetProperty("bankAccount").GetProperty("bankCode").GetString());

            Assert.False(root.TryGetProperty("email", out _));
            Assert.False(root.TryGetProperty("passwordHash", out _));
            Assert.False(root.TryGetProperty("status", out _));
            Assert.False(root.TryGetProperty("roles", out _));
        }

        // 4. Customer A PUT Profile without CSRF -> 403 Forbidden with AUTH.CSRF_INVALID
        var updatePayload = new
        {
            fullName = "Customer A Updated",
            phoneNumber = "0908888999",
            bankAccount = new
            {
                bankCode = "tpb",
                accountNumber = "99990000",
                accountHolderName = "holder updated"
            }
        };

        using (var noCsrfReq = new HttpRequestMessage(HttpMethod.Put, "/api/customers/me"))
        {
            noCsrfReq.Headers.Add("Cookie", $"ocs_session={tokenA}");
            noCsrfReq.Content = JsonContent.Create(updatePayload);
            using var noCsrfRes = await client.SendAsync(noCsrfReq);
            Assert.Equal(HttpStatusCode.Forbidden, noCsrfRes.StatusCode);
            var errJson = await noCsrfRes.Content.ReadAsStringAsync();
            Assert.Contains("AUTH.CSRF_INVALID", errJson, StringComparison.Ordinal);
        }

        // 5. Customer A PUT Profile with CSRF bypass -> 200 OK and persists to PostgreSQL
        using (var validPutReq = new HttpRequestMessage(HttpMethod.Put, "/api/customers/me"))
        {
            validPutReq.Headers.Add("Cookie", $"ocs_session={tokenA}");
            validPutReq.Headers.Add("X-Test-Bypass-Csrf", "true");
            validPutReq.Content = JsonContent.Create(updatePayload);
            using var validPutRes = await client.SendAsync(validPutReq);
            Assert.Equal(HttpStatusCode.OK, validPutRes.StatusCode);

            var resJson = await validPutRes.Content.ReadAsStringAsync();
            using var resDoc = JsonDocument.Parse(resJson);
            Assert.Equal("Customer A Updated", resDoc.RootElement.GetProperty("fullName").GetString());
            Assert.Equal("0908888999", resDoc.RootElement.GetProperty("phoneNumber").GetString());
            Assert.Equal("TPB", resDoc.RootElement.GetProperty("bankAccount").GetProperty("bankCode").GetString());
        }

        // 6. Verify database updated
        await using (var verifyContext = database.Context())
        {
            var accountDb = await verifyContext.Accounts.Include(a => a.CustomerProfile).FirstAsync(a => a.Id == customerA);
            Assert.Equal("Customer A Updated", accountDb.FullName);
            Assert.Equal("0908888999", accountDb.PhoneNumber);
            Assert.Equal("TPB", accountDb.CustomerProfile!.BankCode);
            Assert.Equal("99990000", accountDb.CustomerProfile.BankAccountNumber);
            Assert.Equal("holder updated", accountDb.CustomerProfile.BankAccountHolderName);
        }

        // 7. Customer Pending PUT changing bank -> 409 Conflict with CUSTOMER.BANK_ACCOUNT_LOCKED_FOR_PAYOUT
        using (var lockReq = new HttpRequestMessage(HttpMethod.Put, "/api/customers/me"))
        {
            lockReq.Headers.Add("Cookie", $"ocs_session={tokenPending}");
            lockReq.Headers.Add("X-Test-Bypass-Csrf", "true");
            lockReq.Content = JsonContent.Create(new
            {
                fullName = "Customer Pending",
                phoneNumber = "0908888222",
                bankAccount = new
                {
                    bankCode = "MB",
                    accountNumber = "77778888",
                    accountHolderName = "HOLDER PENDING"
                }
            });
            using var lockRes = await client.SendAsync(lockReq);
            Assert.Equal(HttpStatusCode.Conflict, lockRes.StatusCode);
            var lockBody = await lockRes.Content.ReadAsStringAsync();
            Assert.Contains("CUSTOMER.BANK_ACCOUNT_LOCKED_FOR_PAYOUT", lockBody, StringComparison.Ordinal);
        }

        // 8. Customer Pending PUT only updating contact name -> 200 OK
        using (var contactOnlyReq = new HttpRequestMessage(HttpMethod.Put, "/api/customers/me"))
        {
            contactOnlyReq.Headers.Add("Cookie", $"ocs_session={tokenPending}");
            contactOnlyReq.Headers.Add("X-Test-Bypass-Csrf", "true");
            contactOnlyReq.Content = JsonContent.Create(new
            {
                fullName = "Customer Pending Name Updated",
                phoneNumber = "0908888222",
                bankAccount = new
                {
                    bankCode = "BIDV",
                    accountNumber = "33334444",
                    accountHolderName = "HOLDER PENDING"
                }
            });
            using var contactOnlyRes = await client.SendAsync(contactOnlyReq);
            Assert.Equal(HttpStatusCode.OK, contactOnlyRes.StatusCode);
        }
    }
}
