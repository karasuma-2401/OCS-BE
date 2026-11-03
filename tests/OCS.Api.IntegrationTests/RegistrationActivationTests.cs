using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OCS.Api.Common;
using OCS.Api.Configuration;
using OCS.Api.Consts;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Auth;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class RegistrationActivationTests : IDisposable
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly TestInboxEmailSender _emailSender;
    private readonly TestAuthRepository _authRepository;
    private readonly TestUnitOfWork _unitOfWork;
    private readonly PasswordHasher _passwordHasher;
    private readonly TokenService _tokenService;
    private readonly OtpService _otpService;
    private readonly AuthService _authService;

    public RegistrationActivationTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero));
        _emailSender = new TestInboxEmailSender();
        _authRepository = new TestAuthRepository();
        _unitOfWork = new TestUnitOfWork(_authRepository);
        _passwordHasher = new PasswordHasher();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ocs-api",
            ["Jwt:Audience"] = "ocs-clients",
            ["Jwt:Key"] = "test-jwt-secret-key-that-is-at-least-32-bytes"
        }).Build();

        _tokenService = new TokenService(configuration);

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var otpOptions = Options.Create(new OtpOptions
        {
            ExpiryMinutes = 5,
            ResendCooldownSeconds = 60,
            MaxFailedAttempts = 5,
            ContinuationExpiryMinutes = 15,
            MaxSendsPerEmailPerHour = 5,
            MaxSendsPerIpPerHour = 20,
            SecretKey = "test-otp-secret-key-that-is-at-least-32-bytes"
        });

        var rateLimiter = new OtpRateLimiter(cache, otpOptions);

        _otpService = new OtpService(
            _unitOfWork,
            _emailSender,
            rateLimiter,
            otpOptions,
            configuration,
            _timeProvider,
            NullLogger<OtpService>.Instance);

        _authService = new AuthService(
            _unitOfWork,
            _passwordHasher,
            _tokenService,
            _otpService);
    }

    public void Dispose() => _unitOfWork.Dispose();

    private string ExtractOtpCodeFromEmail()
    {
        Assert.NotEmpty(_emailSender.SentMessages);
        var email = _emailSender.SentMessages.Last();
        var match = Regex.Match(email.Body, @"\b\d{6}\b");
        Assert.True(match.Success, "Expected 6-digit OTP in email body.");
        return match.Value;
    }

    [Fact]
    public async Task HappyPathRegistrationRequiresEmailOtpBeforeLogin()
    {
        var registerRequest = new RegisterRequestDto(
            FullName: "Nguyễn Văn A",
            Email: "nguyenvana@example.com",
            PhoneNumber: "0912345678",
            Password: "Password123@");

        // 1. Register creates pending account and sends OTP
        var registerResult = await _authService.RegisterAsync(registerRequest, "127.0.0.1");
        Assert.True(registerResult.IsSuccess);
        Assert.NotEqual(Guid.Empty, registerResult.Data!.ChallengeId);
        Assert.Equal("RegistrationActivation", registerResult.Data.Purpose);
        Assert.Equal("n***a@example.com", registerResult.Data.MaskedDestination);

        var account = await _authRepository.GetByEmailAsync("nguyenvana@example.com");
        Assert.NotNull(account);
        Assert.Equal(AccountStatus.PendingActivation, account.Status);
        Assert.Equal(AccountType.Customer, account.AccountType);
        Assert.Null(account.EmailVerifiedAt);
        Assert.Null(account.PhoneVerifiedAt);
        Assert.Null(account.ActivatedAt);
        Assert.NotNull(account.CustomerProfile);
        Assert.Single(account.AccountRoles);
        Assert.Equal(Role.CustomerRoleId, account.AccountRoles.First().RoleId);

        // 2. Pending account cannot log in
        var loginResult = await _authService.LoginAsync(new LoginRequestDto("nguyenvana@example.com", "Password123@"));
        Assert.False(loginResult.IsSuccess);
        Assert.Equal("AUTH.ACCOUNT_PENDING_ACTIVATION", loginResult.Error!.Code);
        Assert.NotNull(loginResult.Error.Extensions);
        var continuationChallengeId = (Guid)loginResult.Error.Extensions["challengeId"]!;

        // 3. Verify OTP
        var code = ExtractOtpCodeFromEmail();
        var verifyResult = await _authService.VerifyRegistrationOtpAsync(new VerifyOtpRequestDto(
            continuationChallengeId,
            code));

        Assert.True(verifyResult.IsSuccess);
        Assert.Contains("thành công", verifyResult.Data!.Message, StringComparison.OrdinalIgnoreCase);

        // 4. Account is now Active with EmailVerifiedAt, PhoneVerifiedAt remains null
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.NotNull(account.EmailVerifiedAt);
        Assert.NotNull(account.ActivatedAt);
        Assert.Null(account.PhoneVerifiedAt);
    }

    [Fact]
    public async Task RegistrationRejectsDuplicateEmailForActiveAccount()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            FullName = "Existing User",
            Email = "existing@example.com",
            AccountType = AccountType.Customer,
            Status = AccountStatus.Active
        };
        _authRepository.AddAccount(account);

        var request = new RegisterRequestDto(
            FullName: "Another User",
            Email: " EXISTING@example.com ",
            PhoneNumber: "0987654321",
            Password: "Password123@");

        var result = await _authService.RegisterAsync(request, "127.0.0.1");
        Assert.False(result.IsSuccess);
        Assert.Equal("AUTH.EMAIL_EXISTS", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task RegistrationForPendingAccountAllowsReissuingOtpIfPasswordMatches()
    {
        var password = "Password123@";
        var account = new Account
        {
            Id = Guid.NewGuid(),
            FullName = "Pending User",
            Email = "pending@example.com",
            PasswordHash = _passwordHasher.HashPassword(password),
            AccountType = AccountType.Customer,
            Status = AccountStatus.PendingActivation
        };
        _authRepository.AddAccount(account);

        var request = new RegisterRequestDto(
            FullName: "Pending User",
            Email: "pending@example.com",
            PhoneNumber: "0912345678",
            Password: password);

        var result = await _authService.RegisterAsync(request, "127.0.0.1");
        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Data!.ChallengeId);
        Assert.Single(_emailSender.SentMessages);
    }

    [Fact]
    public async Task RegistrationForPendingAccountRejectsIfPasswordDiffers()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            FullName = "Pending User",
            Email = "pending@example.com",
            PasswordHash = _passwordHasher.HashPassword("CorrectPassword1@"),
            AccountType = AccountType.Customer,
            Status = AccountStatus.PendingActivation
        };
        _authRepository.AddAccount(account);

        var request = new RegisterRequestDto(
            FullName: "Attacker",
            Email: "pending@example.com",
            PhoneNumber: "0912345678",
            Password: "DifferentPassword2@");

        var result = await _authService.RegisterAsync(request, "127.0.0.1");
        Assert.False(result.IsSuccess);
        Assert.Equal("AUTH.EMAIL_EXISTS", result.Error!.Code);
    }

    [Fact]
    public async Task InvalidEmailFormatIsRejected()
    {
        var request = new RegisterRequestDto(
            FullName: "Invalid Email",
            Email: "not-an-email",
            PhoneNumber: "0912345678",
            Password: "Password123@");

        var result = await _authService.RegisterAsync(request, "127.0.0.1");
        Assert.False(result.IsSuccess);
        Assert.Equal("VALIDATION_FAILED", result.Error!.Code);
    }

    [Fact]
    public async Task ResendOtpAllowsRetryAfterCooldown()
    {
        var registerRequest = new RegisterRequestDto(
            FullName: "Resend User",
            Email: "resend@example.com",
            PhoneNumber: "0912345678",
            Password: "Password123@");

        var registerResult = await _authService.RegisterAsync(registerRequest, "127.0.0.1");
        Assert.True(registerResult.IsSuccess);
        _emailSender.Clear();

        // Advance past 60s cooldown
        _timeProvider.Advance(TimeSpan.FromSeconds(65));

        var resendResult = await _authService.ResendRegistrationOtpAsync(
            new ResendOtpRequestDto(registerResult.Data!.ChallengeId),
            "127.0.0.1");

        Assert.True(resendResult.IsSuccess);
        Assert.NotEqual(registerResult.Data.ChallengeId, resendResult.Data!.ChallengeId);

        var newCode = ExtractOtpCodeFromEmail();

        var verifyResult = await _authService.VerifyRegistrationOtpAsync(new VerifyOtpRequestDto(
            resendResult.Data.ChallengeId,
            newCode));

        Assert.True(verifyResult.IsSuccess);
        var account = await _authRepository.GetByEmailAsync("resend@example.com");
        Assert.Equal(AccountStatus.Active, account!.Status);
    }

    [Fact]
    public async Task EmailDeliveryFailureKeepsAccountPendingAndReturnsDeliveryUnavailable()
    {
        var failingSender = new FailingEmailSender();
        var otpService = new OtpService(
            _unitOfWork,
            failingSender,
            new OtpRateLimiter(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), Options.Create(new OtpOptions())),
            Options.Create(new OtpOptions()),
            new ConfigurationBuilder().Build(),
            _timeProvider,
            NullLogger<OtpService>.Instance);

        var authService = new AuthService(_unitOfWork, _passwordHasher, _tokenService, otpService);

        var registerRequest = new RegisterRequestDto(
            FullName: "Failed Sender User",
            Email: "failed@example.com",
            PhoneNumber: "0912345678",
            Password: "Password123@");

        var result = await authService.RegisterAsync(registerRequest, "127.0.0.1");
        Assert.False(result.IsSuccess);
        Assert.Equal("AUTH.DELIVERY_UNAVAILABLE", result.Error!.Code);

        // Account is still saved in PendingActivation
        var account = await _authRepository.GetByEmailAsync("failed@example.com");
        Assert.NotNull(account);
        Assert.Equal(AccountStatus.PendingActivation, account.Status);
    }

    private sealed class FailingEmailSender : IEmailSender
    {
        public Task<Result<bool>> SendEmailAsync(EmailMessage message, CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Failure<bool>(ApplicationError.ServiceUnavailable(
                "AUTH.DELIVERY_UNAVAILABLE",
                "SMTP service unavailable.")));
    }

    private sealed class TestUnitOfWork(TestAuthRepository repository) : IUnitOfWork
    {
        public IProductRepository ProductRepository => null!;
        public IAuthRepository AuthRepository => repository;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class TestAuthRepository : IAuthRepository
    {
        public List<Account> Accounts { get; } = [];
        public List<OtpChallenge> Challenges { get; } = [];

        public Task<Account?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            var normalized = EmailIdentity.Normalize(email);
            return Task.FromResult(Accounts.FirstOrDefault(a => a.NormalizedEmail == normalized));
        }

        public Task<bool> ExistsEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            var normalized = EmailIdentity.Normalize(email);
            return Task.FromResult(Accounts.Any(a => a.NormalizedEmail == normalized));
        }

        public Task<Account?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
            => Task.FromResult(Accounts.FirstOrDefault(a => a.Username == username));

        public Task<Account?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default)
            => Task.FromResult(Accounts.FirstOrDefault(a => a.PhoneNumber == phone));

        public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Accounts.FirstOrDefault(a => a.Id == id));

        public Task<bool> ExistsUsernameAsync(string username, CancellationToken cancellationToken = default)
            => Task.FromResult(Accounts.Any(a => a.Username == username));

        public Task<bool> ExistsPhoneAsync(string phone, CancellationToken cancellationToken = default)
            => Task.FromResult(Accounts.Any(a => a.PhoneNumber == phone));

        public void AddAccount(Account account)
        {
            if (account.NormalizedEmail == null && account.Email != null)
            {
                typeof(Account).GetProperty(nameof(Account.NormalizedEmail))!
                    .SetValue(account, EmailIdentity.Normalize(account.Email));
            }
            Accounts.Add(account);
        }

        public Task<OtpChallenge?> GetOtpChallengeByIdAsync(Guid challengeId, CancellationToken cancellationToken = default)
            => Task.FromResult(Challenges.FirstOrDefault(c => c.Id == challengeId));

        public Task<int> CountOtpChallengesInWindowAsync(string normalizedEmail, DateTimeOffset windowStart, CancellationToken cancellationToken = default)
            => Task.FromResult(Challenges.Count(c => c.NormalizedEmail == normalizedEmail && c.CreatedAt >= windowStart));

        public void AddOtpChallenge(OtpChallenge challenge) => Challenges.Add(challenge);
    }

    private sealed class FakeTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialTime;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan timeSpan) => _utcNow += timeSpan;
    }
}

