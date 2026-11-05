using Microsoft.EntityFrameworkCore;
using Npgsql;
using OCS.Api.Common;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Models.Customers;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class CustomerProfileServiceTests
{
    [Fact]
    public async Task GetSelfProfileAsync_WhenCustomerExistsWithBankAccount_ReturnsDto()
    {
        var customerId = Guid.NewGuid();
        var account = CreateCustomerAccount(customerId, "Nguyen Van A", "0912345678", "VCB", "000000000001", "NGUYEN VAN A");
        var unitOfWork = new FakeCustomerUnitOfWork([account]);
        var service = new CustomerProfileService(unitOfWork);

        var result = await service.GetSelfProfileAsync(customerId);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(customerId, result.Data.Id);
        Assert.Equal("Nguyen Van A", result.Data.FullName);
        Assert.Equal("0912345678", result.Data.PhoneNumber);
        Assert.NotNull(result.Data.BankAccount);
        Assert.Equal("VCB", result.Data.BankAccount.BankCode);
        Assert.Equal("000000000001", result.Data.BankAccount.AccountNumber);
        Assert.Equal("NGUYEN VAN A", result.Data.BankAccount.AccountHolderName);
    }

    [Fact]
    public async Task GetSelfProfileAsync_WhenBankFieldsEmpty_ReturnsNullBankAccount()
    {
        var customerId = Guid.NewGuid();
        var account = CreateCustomerAccount(customerId, "Nguyen Van A", "0912345678", null, null, null);
        var unitOfWork = new FakeCustomerUnitOfWork([account]);
        var service = new CustomerProfileService(unitOfWork);

        var result = await service.GetSelfProfileAsync(customerId);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Null(result.Data.BankAccount);
    }

    [Fact]
    public async Task GetSelfProfileAsync_WhenProfileNotFound_ReturnsNotFound()
    {
        var unitOfWork = new FakeCustomerUnitOfWork([]);
        var service = new CustomerProfileService(unitOfWork);

        var result = await service.GetSelfProfileAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(CustomerErrorCodes.ProfileNotFound, result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task UpdateSelfProfileAsync_WhenValidInput_TrimsAndUppercasesValues()
    {
        var customerId = Guid.NewGuid();
        var account = CreateCustomerAccount(customerId, "Old Name", "0900000000", null, null, null);
        var unitOfWork = new FakeCustomerUnitOfWork([account]);
        var service = new CustomerProfileService(unitOfWork);

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "  Nguyen Van B  ",
            PhoneNumber = " 0987654321 ",
            BankAccount = new UpdateBankAccountRequest
            {
                BankCode = "  vcb  ",
                AccountNumber = "  1234567890  ",
                AccountHolderName = "  Nguyen Van B  "
            }
        };

        var result = await service.UpdateSelfProfileAsync(customerId, request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("Nguyen Van B", result.Data.FullName);
        Assert.Equal("0987654321", result.Data.PhoneNumber);
        Assert.NotNull(result.Data.BankAccount);
        Assert.Equal("VCB", result.Data.BankAccount.BankCode);
        Assert.Equal("1234567890", result.Data.BankAccount.AccountNumber);
        Assert.Equal("Nguyen Van B", result.Data.BankAccount.AccountHolderName);

        Assert.Equal("Nguyen Van B", account.FullName);
        Assert.Equal("0987654321", account.PhoneNumber);
        Assert.Equal("VCB", account.CustomerProfile!.BankCode);
        Assert.Equal("1234567890", account.CustomerProfile.BankAccountNumber);
        Assert.Equal("Nguyen Van B", account.CustomerProfile.BankAccountHolderName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateSelfProfileAsync_Validation_RejectsEmptyFullName(string invalidName)
    {
        var customerId = Guid.NewGuid();
        var account = CreateCustomerAccount(customerId, "Name", "0912345678", null, null, null);
        var unitOfWork = new FakeCustomerUnitOfWork([account]);
        var service = new CustomerProfileService(unitOfWork);

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = invalidName,
            PhoneNumber = "0912345678"
        };

        var result = await service.UpdateSelfProfileAsync(customerId, request);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("091234567")]
    [InlineData("09123456789")]
    [InlineData("abcdefghij")]
    public async Task UpdateSelfProfileAsync_Validation_RejectsInvalidPhoneNumber(string invalidPhone)
    {
        var customerId = Guid.NewGuid();
        var account = CreateCustomerAccount(customerId, "Name", "0912345678", null, null, null);
        var unitOfWork = new FakeCustomerUnitOfWork([account]);
        var service = new CustomerProfileService(unitOfWork);

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "Nguyen Van A",
            PhoneNumber = invalidPhone
        };

        var result = await service.UpdateSelfProfileAsync(customerId, request);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task UpdateSelfProfileAsync_Validation_RejectsIncompleteBankAccount()
    {
        var customerId = Guid.NewGuid();
        var account = CreateCustomerAccount(customerId, "Name", "0912345678", null, null, null);
        var unitOfWork = new FakeCustomerUnitOfWork([account]);
        var service = new CustomerProfileService(unitOfWork);

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "Nguyen Van A",
            PhoneNumber = "0912345678",
            BankAccount = new UpdateBankAccountRequest
            {
                BankCode = "   ",
                AccountNumber = "123456",
                AccountHolderName = "Nguyen Van A"
            }
        };

        var result = await service.UpdateSelfProfileAsync(customerId, request);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task UpdateSelfProfileAsync_WhenPhoneNumberTakenByAnotherCustomer_ReturnsConflict()
    {
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var accountA = CreateCustomerAccount(customerA, "Customer A", "0911111111", null, null, null);
        var accountB = CreateCustomerAccount(customerB, "Customer B", "0922222222", null, null, null);
        var unitOfWork = new FakeCustomerUnitOfWork([accountA, accountB]);
        var service = new CustomerProfileService(unitOfWork);

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "Customer A Updated",
            PhoneNumber = "0922222222"
        };

        var result = await service.UpdateSelfProfileAsync(customerA, request);

        Assert.False(result.IsSuccess);
        Assert.Equal(CustomerErrorCodes.PhoneNumberExists, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task UpdateSelfProfileAsync_WhenDbUniqueConstraintViolated_ReturnsConflict()
    {
        var customerId = Guid.NewGuid();
        var account = CreateCustomerAccount(customerId, "Customer", "0911111111", null, null, null);
        var unitOfWork = new FakeCustomerUnitOfWork([account])
        {
            SimulateUniqueViolationOnSave = true
        };
        var service = new CustomerProfileService(unitOfWork);

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "Customer Name",
            PhoneNumber = "0933333333"
        };

        var result = await service.UpdateSelfProfileAsync(customerId, request);

        Assert.False(result.IsSuccess);
        Assert.Equal(CustomerErrorCodes.PhoneNumberExists, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task UpdateSelfProfileAsync_WhenPendingPayoutExists_AndBankDetailsChanged_ReturnsConflict()
    {
        var customerId = Guid.NewGuid();
        var account = CreateCustomerAccount(customerId, "Customer", "0911111111", "VCB", "111111", "NGUYEN A");
        var unitOfWork = new FakeCustomerUnitOfWork([account])
        {
            HasPendingPayout = true
        };
        var service = new CustomerProfileService(unitOfWork);

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "Customer",
            PhoneNumber = "0911111111",
            BankAccount = new UpdateBankAccountRequest
            {
                BankCode = "MB",
                AccountNumber = "222222",
                AccountHolderName = "NGUYEN A"
            }
        };

        var result = await service.UpdateSelfProfileAsync(customerId, request);

        Assert.False(result.IsSuccess);
        Assert.Equal(CustomerErrorCodes.BankAccountLockedForPayout, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task UpdateSelfProfileAsync_WhenPendingPayoutExists_AndBankDetailsCleared_ReturnsConflict()
    {
        var customerId = Guid.NewGuid();
        var account = CreateCustomerAccount(customerId, "Customer", "0911111111", "VCB", "111111", "NGUYEN A");
        var unitOfWork = new FakeCustomerUnitOfWork([account])
        {
            HasPendingPayout = true
        };
        var service = new CustomerProfileService(unitOfWork);

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "Customer",
            PhoneNumber = "0911111111",
            BankAccount = null
        };

        var result = await service.UpdateSelfProfileAsync(customerId, request);

        Assert.False(result.IsSuccess);
        Assert.Equal(CustomerErrorCodes.BankAccountLockedForPayout, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task UpdateSelfProfileAsync_WhenPendingPayoutExists_AndBankDetailsUnchanged_AllowsNameAndPhoneUpdate()
    {
        var customerId = Guid.NewGuid();
        var account = CreateCustomerAccount(customerId, "Old Name", "0911111111", "VCB", "111111", "NGUYEN A");
        var unitOfWork = new FakeCustomerUnitOfWork([account])
        {
            HasPendingPayout = true
        };
        var service = new CustomerProfileService(unitOfWork);

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "New Name",
            PhoneNumber = "0922222222",
            BankAccount = new UpdateBankAccountRequest
            {
                BankCode = "vcb",
                AccountNumber = "111111",
                AccountHolderName = "NGUYEN A"
            }
        };

        var result = await service.UpdateSelfProfileAsync(customerId, request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("New Name", result.Data.FullName);
        Assert.Equal("0922222222", result.Data.PhoneNumber);
        Assert.Equal("VCB", result.Data.BankAccount!.BankCode);
    }

    [Fact]
    public async Task UpdateSelfProfileAsync_WhenPayoutCompletedOrTransferred_AllowsBankUpdate()
    {
        var customerId = Guid.NewGuid();
        var account = CreateCustomerAccount(customerId, "Customer", "0911111111", "VCB", "111111", "NGUYEN A");
        var unitOfWork = new FakeCustomerUnitOfWork([account])
        {
            HasPendingPayout = false // Only non-pending payouts exist
        };
        var service = new CustomerProfileService(unitOfWork);

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "Customer",
            PhoneNumber = "0911111111",
            BankAccount = new UpdateBankAccountRequest
            {
                BankCode = "TCB",
                AccountNumber = "888888",
                AccountHolderName = "NGUYEN A"
            }
        };

        var result = await service.UpdateSelfProfileAsync(customerId, request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("TCB", result.Data.BankAccount!.BankCode);
        Assert.Equal("888888", result.Data.BankAccount.AccountNumber);
    }

    [Fact]
    public async Task UpdateSelfProfileAsync_WhenNullBankAccountPassed_ClearsBankFields()
    {
        var customerId = Guid.NewGuid();
        var account = CreateCustomerAccount(customerId, "Customer", "0911111111", "VCB", "111111", "NGUYEN A");
        var unitOfWork = new FakeCustomerUnitOfWork([account]);
        var service = new CustomerProfileService(unitOfWork);

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "Customer",
            PhoneNumber = "0911111111",
            BankAccount = null
        };

        var result = await service.UpdateSelfProfileAsync(customerId, request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Null(result.Data.BankAccount);
        Assert.Null(account.CustomerProfile!.BankCode);
        Assert.Null(account.CustomerProfile.BankAccountNumber);
        Assert.Null(account.CustomerProfile.BankAccountHolderName);
    }

    [Fact]
    public async Task UpdateSelfProfileAsync_PreservesPhoneVerifiedAtAndStatusAndEmail()
    {
        var customerId = Guid.NewGuid();
        var phoneVerifiedAt = DateTimeOffset.UtcNow.AddDays(-10);
        var emailVerifiedAt = DateTimeOffset.UtcNow.AddDays(-10);
        var account = new Account
        {
            Id = customerId,
            FullName = "Original Name",
            PhoneNumber = "0911111111",
            Email = "original@example.com",
            AccountType = AccountType.Customer,
            Status = AccountStatus.Active,
            PhoneVerifiedAt = phoneVerifiedAt,
            EmailVerifiedAt = emailVerifiedAt,
            CustomerProfile = new CustomerProfile
            {
                Id = customerId,
                Status = CustomerStatus.Active
            }
        };

        var unitOfWork = new FakeCustomerUnitOfWork([account]);
        var service = new CustomerProfileService(unitOfWork);

        var request = new UpdateCustomerSelfProfileRequest
        {
            FullName = "New Name",
            PhoneNumber = "0922222222",
            BankAccount = null
        };

        var result = await service.UpdateSelfProfileAsync(customerId, request);

        Assert.True(result.IsSuccess);
        Assert.Equal(phoneVerifiedAt, account.PhoneVerifiedAt);
        Assert.Equal(emailVerifiedAt, account.EmailVerifiedAt);
        Assert.Equal("original@example.com", account.Email);
        Assert.Equal(AccountStatus.Active, account.Status);
    }

    private static Account CreateCustomerAccount(
        Guid id,
        string fullName,
        string phoneNumber,
        string? bankCode,
        string? bankAccountNumber,
        string? bankAccountHolderName)
    {
        var account = new Account
        {
            Id = id,
            FullName = fullName,
            PhoneNumber = phoneNumber,
            AccountType = AccountType.Customer,
            Status = AccountStatus.Active,
            CustomerProfile = new CustomerProfile
            {
                Id = id,
                BankCode = bankCode,
                BankAccountNumber = bankAccountNumber,
                BankAccountHolderName = bankAccountHolderName,
                Status = CustomerStatus.Active
            }
        };
        account.CustomerProfile.Account = account;
        return account;
    }

    private sealed class FakeCustomerUnitOfWork(List<Account> accounts) : IUnitOfWork
    {
        public List<Account> Accounts { get; } = accounts;
        public bool SimulateUniqueViolationOnSave { get; set; }
        public bool HasPendingPayout { get; set; }

        public IProductRepository ProductRepository => throw new NotSupportedException();
        public IAuthRepository AuthRepository => throw new NotSupportedException();
        public IAppointmentRepository AppointmentRepository => throw new NotSupportedException();
        public INotificationRepository NotificationRepository => throw new NotSupportedException();
        public ICustomerProfileRepository CustomerProfileRepository => new FakeCustomerProfileRepository(Accounts, () => HasPendingPayout);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (SimulateUniqueViolationOnSave)
            {
                throw new DbUpdateException("Duplicate key violation", new PostgresException("23505", "error", "error", "unique_violation"));
            }
            return Task.FromResult(1);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ReloadAccountAsync(Account account, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public void Dispose() { }
    }

    private sealed class FakeCustomerProfileRepository(List<Account> accounts, Func<bool> hasPendingPayout) : ICustomerProfileRepository
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
