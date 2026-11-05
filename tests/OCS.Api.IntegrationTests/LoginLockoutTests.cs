using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OCS.Api.Common;
using OCS.Api.Configuration;
using OCS.Api.Consts;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Models.Auth;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class LoginLockoutTests : IDisposable
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly TestInboxEmailSender _emailSender;
    private readonly TestAuthRepository _authRepository;
    private readonly TestUnitOfWork _unitOfWork;
    private readonly PasswordHasher _passwordHasher;
    private readonly TokenService _tokenService;
    private readonly OtpService _otpService;
    private readonly LoginRateLimiter _loginRateLimiter;
    private readonly AuthService _authService;
    private readonly AuthController _authController;

    public LoginLockoutTests()
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

        _tokenService = new TokenService(configuration, _timeProvider);

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

        var otpRateLimiter = new OtpRateLimiter(cache, otpOptions);

        _otpService = new OtpService(
            _unitOfWork,
            _emailSender,
            otpRateLimiter,
            otpOptions,
            configuration,
            _timeProvider,
            NullLogger<OtpService>.Instance);

        var authOptions = Options.Create(new AuthOptions
        {
            MaxFailedLoginAttempts = 5,
            LockoutDurationMinutes = 15,
            MaxLoginAttemptsPerIpPerHour = 60,
            SessionExpirySeconds = 28800
        });

        _loginRateLimiter = new LoginRateLimiter(cache, authOptions);

        _authService = new AuthService(
            _unitOfWork,
            _passwordHasher,
            _tokenService,
            _otpService,
            _loginRateLimiter,
            _timeProvider,
            NullLogger<AuthService>.Instance);

        _authController = new AuthController(_authService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    public void Dispose()
    {
        _unitOfWork.Dispose();
    }

    private Account CreateAccount(
        string email,
        string password,
        AccountStatus status = AccountStatus.Active,
        string roleCode = "Customer",
        string roleName = "Khách hàng")
    {
        var accountId = Guid.NewGuid();
        var account = new Account
        {
            Id = accountId,
            FullName = "Test User",
            Email = email,
            PhoneNumber = "0912345678",
            PasswordHash = _passwordHasher.HashPassword(password),
            AccountType = AccountType.Customer,
            Status = status,
            EmailVerifiedAt = status == AccountStatus.Active ? _timeProvider.GetUtcNow() : null,
            ActivatedAt = status == AccountStatus.Active ? _timeProvider.GetUtcNow() : null
        };

        if (!string.IsNullOrWhiteSpace(roleCode))
        {
            var role = new Role
            {
                Id = Guid.NewGuid(),
                Code = roleCode,
                Name = roleName
            };

            account.AccountRoles.Add(new AccountRole
            {
                AccountId = accountId,
                Account = account,
                RoleId = role.Id,
                Role = role
            });
        }

        _authRepository.AddAccount(account);
        return account;
    }

    [Fact]
    public async Task SuccessfulLoginSetsHttpOnlyCookieAndReturnsResponseWithoutRawJwt()
    {
        CreateAccount("user@example.com", "Password123@");

        var request = new LoginRequestDto("user@example.com", "Password123@");
        var result = await _authController.Login(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<LoginResponseDto>(okResult.Value);

        Assert.Equal(28800, response.ExpiresIn);
        Assert.Equal(_timeProvider.GetUtcNow().AddSeconds(28800), response.ExpiresAt);
        Assert.Equal("user@example.com", response.User.Email);
        Assert.Contains("Customer", response.User.Roles);

        var setCookie = _authController.Response.Headers.SetCookie.ToString();
        Assert.Contains("ocs_session=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonExistentEmailAndWrongPasswordReturnIdenticalUnauthorizedResponse()
    {
        CreateAccount("realuser@example.com", "CorrectPassword123@");

        var nonExistentResult = await _authService.LoginAsync(
            new LoginRequestDto("nonexistent@example.com", "SomePassword123@"),
            "127.0.0.1");

        var wrongPasswordResult = await _authService.LoginAsync(
            new LoginRequestDto("realuser@example.com", "WrongPassword123@"),
            "127.0.0.1");

        Assert.False(nonExistentResult.IsSuccess);
        Assert.False(wrongPasswordResult.IsSuccess);

        Assert.Equal(nonExistentResult.Error!.Type, wrongPasswordResult.Error!.Type);
        Assert.Equal(ErrorType.Unauthorized, nonExistentResult.Error.Type);
        Assert.Equal("AUTH.INVALID_CREDENTIALS", nonExistentResult.Error.Code);
        Assert.Equal("AUTH.INVALID_CREDENTIALS", wrongPasswordResult.Error.Code);
        Assert.Equal("Email hoặc mật khẩu không chính xác.", nonExistentResult.Error.Message);
        Assert.Equal("Email hoặc mật khẩu không chính xác.", wrongPasswordResult.Error.Message);
    }

    [Fact]
    public async Task EmailNormalizationSupportsCaseAndWhitespace()
    {
        CreateAccount("normalized@example.com", "Password123@");

        var request = new LoginRequestDto("   NORMALIZED@EXAMPLE.COM   ", "Password123@");
        var result = await _authService.LoginAsync(request, "127.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.Equal("normalized@example.com", result.Data!.User.Email);
    }

    [Fact]
    public async Task FailedAttemptsFromFirstToFourthIncrementCounterAndReturnUnauthorized()
    {
        var account = CreateAccount("counter@example.com", "CorrectPassword123@");

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var result = await _authService.LoginAsync(
                new LoginRequestDto("counter@example.com", "WrongPassword123@"),
                "127.0.0.1");

            Assert.False(result.IsSuccess);
            Assert.Equal("AUTH.INVALID_CREDENTIALS", result.Error!.Code);
            Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
            Assert.Equal(attempt, account.FailedLoginAttempts);
            Assert.Null(account.LockoutEndAt);
        }
    }

    [Fact]
    public async Task FifthFailedAttemptLocksAccountFor15MinutesAndReturnsForbiddenWithExtensions()
    {
        var account = CreateAccount("lockout@example.com", "CorrectPassword123@");

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await _authService.LoginAsync(new LoginRequestDto("lockout@example.com", "WrongPassword123@"), "127.0.0.1");
        }

        var fifthResult = await _authService.LoginAsync(
            new LoginRequestDto("lockout@example.com", "WrongPassword123@"),
            "127.0.0.1");

        Assert.False(fifthResult.IsSuccess);
        Assert.Equal("AUTH.ACCOUNT_LOCKED", fifthResult.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, fifthResult.Error.Type);
        Assert.Equal(5, account.FailedLoginAttempts);
        Assert.NotNull(account.LockoutEndAt);
        Assert.Equal(_timeProvider.GetUtcNow().AddMinutes(15), account.LockoutEndAt);

        Assert.NotNull(fifthResult.Error.Extensions);
        Assert.Equal(account.LockoutEndAt.Value, fifthResult.Error.Extensions["lockoutEndAt"]);
        Assert.Equal(900, Convert.ToInt32(fifthResult.Error.Extensions["retryAfterSeconds"], CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task SubsequentAttemptsDuringActiveLockoutDoNotExtendLockoutDuration()
    {
        var account = CreateAccount("locked@example.com", "CorrectPassword123@");

        for (var i = 1; i <= 5; i++)
        {
            await _authService.LoginAsync(new LoginRequestDto("locked@example.com", "WrongPassword123@"), "127.0.0.1");
        }

        var initialLockoutEnd = account.LockoutEndAt!.Value;

        // Advance 5 minutes into the 15-minute lockout
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        var lockedAttempt = await _authService.LoginAsync(
            new LoginRequestDto("locked@example.com", "CorrectPassword123@"),
            "127.0.0.1");

        Assert.False(lockedAttempt.IsSuccess);
        Assert.Equal("AUTH.ACCOUNT_LOCKED", lockedAttempt.Error!.Code);
        Assert.Equal(initialLockoutEnd, account.LockoutEndAt);

        // Remaining seconds should be 10 minutes (600s), not reset to 15m (900s)
        var remainingSeconds = Convert.ToInt32(lockedAttempt.Error.Extensions!["retryAfterSeconds"], CultureInfo.InvariantCulture);
        Assert.Equal(600, remainingSeconds);
    }

    [Fact]
    public async Task After15MinutesCorrectPasswordSucceedsAndResetsCounters()
    {
        var account = CreateAccount("expiredlock@example.com", "CorrectPassword123@");

        for (var i = 1; i <= 5; i++)
        {
            await _authService.LoginAsync(new LoginRequestDto("expiredlock@example.com", "WrongPassword123@"), "127.0.0.1");
        }

        Assert.NotNull(account.LockoutEndAt);

        // Advance 16 minutes (lockout expired)
        _timeProvider.Advance(TimeSpan.FromMinutes(16));

        var result = await _authService.LoginAsync(
            new LoginRequestDto("expiredlock@example.com", "CorrectPassword123@"),
            "127.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.Equal(0, account.FailedLoginAttempts);
        Assert.Null(account.LockoutEndAt);
    }

    [Fact]
    public async Task After15MinutesWrongPasswordRestartsCounterAtOne()
    {
        var account = CreateAccount("retrywrong@example.com", "CorrectPassword123@");

        for (var i = 1; i <= 5; i++)
        {
            await _authService.LoginAsync(new LoginRequestDto("retrywrong@example.com", "WrongPassword123@"), "127.0.0.1");
        }

        // Advance 16 minutes
        _timeProvider.Advance(TimeSpan.FromMinutes(16));

        var result = await _authService.LoginAsync(
            new LoginRequestDto("retrywrong@example.com", "StillWrongPassword123@"),
            "127.0.0.1");

        Assert.False(result.IsSuccess);
        Assert.Equal("AUTH.INVALID_CREDENTIALS", result.Error!.Code);
        Assert.Equal(1, account.FailedLoginAttempts);
        Assert.Null(account.LockoutEndAt);
    }

    [Fact]
    public async Task DeactivatedAccountReturnsForbiddenDeactivatedOnlyIfPasswordIsCorrect()
    {
        CreateAccount("deactivated@example.com", "CorrectPassword123@", AccountStatus.Deactivated);

        // Wrong password -> generic 401 (anti-enumeration)
        var wrongResult = await _authService.LoginAsync(
            new LoginRequestDto("deactivated@example.com", "WrongPassword123@"),
            "127.0.0.1");
        Assert.False(wrongResult.IsSuccess);
        Assert.Equal("AUTH.INVALID_CREDENTIALS", wrongResult.Error!.Code);

        // Correct password -> 403 Deactivated
        var correctResult = await _authService.LoginAsync(
            new LoginRequestDto("deactivated@example.com", "CorrectPassword123@"),
            "127.0.0.1");
        Assert.False(correctResult.IsSuccess);
        Assert.Equal("AUTH.ACCOUNT_DEACTIVATED", correctResult.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, correctResult.Error.Type);
    }

    [Fact]
    public async Task AdminLockedAccountReturnsForbiddenLockedOnlyIfPasswordIsCorrect()
    {
        CreateAccount("adminlocked@example.com", "CorrectPassword123@", AccountStatus.Locked);

        // Wrong password -> generic 401
        var wrongResult = await _authService.LoginAsync(
            new LoginRequestDto("adminlocked@example.com", "WrongPassword123@"),
            "127.0.0.1");
        Assert.False(wrongResult.IsSuccess);
        Assert.Equal("AUTH.INVALID_CREDENTIALS", wrongResult.Error!.Code);

        // Correct password -> 403 Locked
        var correctResult = await _authService.LoginAsync(
            new LoginRequestDto("adminlocked@example.com", "CorrectPassword123@"),
            "127.0.0.1");
        Assert.False(correctResult.IsSuccess);
        Assert.Equal("AUTH.ACCOUNT_LOCKED", correctResult.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, correctResult.Error.Type);
    }

    [Fact]
    public async Task PendingAccountReturnsPendingActivationOnlyIfPasswordIsCorrect()
    {
        CreateAccount("pending@example.com", "CorrectPassword123@", AccountStatus.PendingActivation);

        // Wrong password -> generic 401
        var wrongResult = await _authService.LoginAsync(
            new LoginRequestDto("pending@example.com", "WrongPassword123@"),
            "127.0.0.1");
        Assert.False(wrongResult.IsSuccess);
        Assert.Equal("AUTH.INVALID_CREDENTIALS", wrongResult.Error!.Code);
        Assert.Empty(_emailSender.SentMessages);

        // Correct password -> 403 PendingActivation with challenge extensions and sent OTP
        var correctResult = await _authService.LoginAsync(
            new LoginRequestDto("pending@example.com", "CorrectPassword123@"),
            "127.0.0.1");

        Assert.False(correctResult.IsSuccess);
        Assert.Equal("AUTH.ACCOUNT_PENDING_ACTIVATION", correctResult.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, correctResult.Error.Type);
        Assert.NotNull(correctResult.Error.Extensions);
        Assert.True(correctResult.Error.Extensions.ContainsKey("challengeId"));
        Assert.Single(_emailSender.SentMessages);
    }

    [Fact]
    public async Task AccountWithoutConfiguredRolesReturnsForbiddenRoleNotConfiguredNoSilentFallback()
    {
        CreateAccount("norole@example.com", "CorrectPassword123@", roleCode: string.Empty);

        var result = await _authService.LoginAsync(
            new LoginRequestDto("norole@example.com", "CorrectPassword123@"),
            "127.0.0.1");

        Assert.False(result.IsSuccess);
        Assert.Equal("AUTH.ROLE_NOT_CONFIGURED", result.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
    }

    [Fact]
    public async Task RoleCodeIsUsedInTokenAndUserDtoNotRoleName()
    {
        CreateAccount(
            "admin@example.com",
            "CorrectPassword123@",
            roleCode: "Admin",
            roleName: "Quản trị viên");

        var result = await _authService.LoginAsync(
            new LoginRequestDto("admin@example.com", "CorrectPassword123@"),
            "127.0.0.1");

        Assert.True(result.IsSuccess);
        var role = Assert.Single(result.Data!.User.Roles);
        Assert.Equal("Admin", role);
        Assert.DoesNotContain("Quản trị viên", result.Data.User.Roles);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.Data.AccessToken);
        var roleClaims = token.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Contains("Admin", roleClaims);
        Assert.DoesNotContain("Quản trị viên", roleClaims);
    }

    [Fact]
    public async Task IpRateLimitingExceedingLimitReturnsRateLimited()
    {
        const string clientIp = "192.168.1.100";

        for (var i = 1; i <= 60; i++)
        {
            await _authService.LoginAsync(
                new LoginRequestDto($"spray{i}@example.com", "WrongPassword123@"),
                clientIp);
        }

        var blockedResult = await _authService.LoginAsync(
            new LoginRequestDto("spray61@example.com", "CorrectPassword123@"),
            clientIp);

        Assert.False(blockedResult.IsSuccess);
        Assert.Equal("AUTH.RATE_LIMITED", blockedResult.Error!.Code);
        Assert.Equal(ErrorType.RateLimited, blockedResult.Error.Type);
    }

    private sealed class TestUnitOfWork(TestAuthRepository repository) : IUnitOfWork
    {
        public IProductRepository ProductRepository => null!;
        public IAppointmentRepository AppointmentRepository => throw new NotSupportedException();
        public INotificationRepository NotificationRepository => throw new NotSupportedException();
        public ICustomerProfileRepository CustomerProfileRepository => throw new NotSupportedException();
        public IAuthRepository AuthRepository => repository;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ReloadAccountAsync(Account account, CancellationToken cancellationToken = default) => Task.FromResult(repository.Accounts.Contains(account));
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

        public Task LockAccountAsync(Guid accountId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReloadChallengeAsync(OtpChallenge challenge, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OtpChallenge?> GetLatestOtpChallengeAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Challenges.Where(c => c.AccountId == id && c.DeliveryStatus != "Failed").OrderByDescending(c => c.CreatedAt).FirstOrDefault());
        public Task<List<OtpChallenge>> GetActiveOtpChallengesAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Challenges.Where(c => c.AccountId == id && !c.InvalidatedAt.HasValue && !c.ConsumedAt.HasValue).ToList());
        public void AddOtpChallenge(OtpChallenge challenge) => Challenges.Add(challenge);

        public List<AuthSession> Sessions { get; } = [];

        public Task<AuthSession?> GetSessionByIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(Sessions.FirstOrDefault(s => s.Id == sessionId));

        public void AddSession(AuthSession session) => Sessions.Add(session);
    }

    private sealed class FakeTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialTime;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan timeSpan) => _utcNow += timeSpan;
    }
}
