using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OCS.Api.Common;
using OCS.Api.Configuration;
using OCS.Api.Consts;
using OCS.Api.Controllers;
using OCS.Api.Entities;
using OCS.Api.Interfaces.Repositories;
using OCS.Api.Interfaces.Services;
using OCS.Api.Middleware;
using OCS.Api.Models.Auth;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class SessionValidationTests : IDisposable
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
    private readonly IConfiguration _configuration;

    public SessionValidationTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero));
        _emailSender = new TestInboxEmailSender();
        _authRepository = new TestAuthRepository();
        _unitOfWork = new TestUnitOfWork(_authRepository);
        _passwordHasher = new PasswordHasher();

        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ocs-api",
            ["Jwt:Audience"] = "ocs-clients",
            ["Jwt:Key"] = "test-jwt-secret-key-that-is-at-least-32-bytes"
        }).Build();

        var authOptions = Options.Create(new AuthOptions
        {
            MaxFailedLoginAttempts = 5,
            LockoutDurationMinutes = 15,
            MaxLoginAttemptsPerIpPerHour = 60,
            SessionExpirySeconds = 28800
        });

        _tokenService = new TokenService(_configuration, _timeProvider, authOptions);

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
            _configuration,
            _timeProvider,
            NullLogger<OtpService>.Instance);

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
        string roleCode = "Customer")
    {
        var accountId = Guid.NewGuid();
        var account = new Account
        {
            Id = accountId,
            FullName = "Session User",
            Email = email,
            PhoneNumber = "0912345678",
            PasswordHash = _passwordHasher.HashPassword(password),
            AccountType = AccountType.Customer,
            Status = status,
            EmailVerifiedAt = status == AccountStatus.Active ? _timeProvider.GetUtcNow() : null,
            ActivatedAt = status == AccountStatus.Active ? _timeProvider.GetUtcNow() : null
        };

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Code = roleCode,
            Name = "Khách hàng"
        };

        account.AccountRoles.Add(new AccountRole
        {
            AccountId = accountId,
            Account = account,
            RoleId = role.Id,
            Role = role
        });

        _authRepository.AddAccount(account);
        return account;
    }

    [Fact]
    public async Task LoginPersistsAuthSessionAndEmbedsSessionIdInToken()
    {
        var account = CreateAccount("sessionuser@example.com", "Password123@");

        var loginResult = await _authService.LoginAsync(
            new LoginRequestDto("sessionuser@example.com", "Password123@"),
            "127.0.0.1");

        Assert.True(loginResult.IsSuccess);
        Assert.NotNull(loginResult.Data);

        var session = Assert.Single(_authRepository.Sessions);
        Assert.Equal(account.Id, session.AccountId);
        Assert.Equal(_timeProvider.GetUtcNow(), session.CreatedAt);
        Assert.Equal(_timeProvider.GetUtcNow().AddSeconds(28800), session.ExpiresAt);
        Assert.Null(session.RevokedAt);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(loginResult.Data.AccessToken);
        var sidClaim = token.Claims.FirstOrDefault(c => c.Type == "sid")?.Value;
        Assert.NotNull(sidClaim);
        Assert.Equal(session.Id.ToString(), sidClaim);
    }

    [Fact]
    public async Task GetCurrentUserReturnsDynamicRemainingTtlFromSession()
    {
        var account = CreateAccount("dynamic@example.com", "Password123@");

        var loginResult = await _authService.LoginAsync(
            new LoginRequestDto("dynamic@example.com", "Password123@"),
            "127.0.0.1");
        Assert.True(loginResult.IsSuccess);

        var session = _authRepository.Sessions.Single();

        // Advance 2 hours
        _timeProvider.Advance(TimeSpan.FromHours(2));

        var meResult = await _authService.GetCurrentUserAsync(account.Id, session.Id);
        Assert.True(meResult.IsSuccess);
        Assert.NotNull(meResult.Data);

        // 8 hours - 2 hours = 6 hours (21600 seconds)
        Assert.Equal(21600, meResult.Data.ExpiresIn);
        Assert.Equal(session.ExpiresAt, meResult.Data.ExpiresAt);
        Assert.Equal(account.Email, meResult.Data.User.Email);
        Assert.Contains("Customer", meResult.Data.User.Roles);
    }

    [Fact]
    public async Task LogoutRevokesSessionAndSubsequentRequestsFail()
    {
        var account = CreateAccount("logout@example.com", "Password123@");

        var loginResult = await _authService.LoginAsync(
            new LoginRequestDto("logout@example.com", "Password123@"),
            "127.0.0.1");
        Assert.True(loginResult.IsSuccess);

        var session = _authRepository.Sessions.Single();

        var logoutResult = await _authService.LogoutAsync(session.Id);
        Assert.True(logoutResult.IsSuccess);
        Assert.NotNull(session.RevokedAt);
        Assert.Equal(_timeProvider.GetUtcNow(), session.RevokedAt);

        var meResult = await _authService.GetCurrentUserAsync(account.Id, session.Id);
        Assert.False(meResult.IsSuccess);
        Assert.Equal("UNAUTHORIZED", meResult.Error!.Code);
        Assert.Equal(ErrorType.Unauthorized, meResult.Error.Type);
    }

    [Fact]
    public async Task ExpiredSessionReturnsUnauthorized()
    {
        var account = CreateAccount("expiredsession@example.com", "Password123@");

        var loginResult = await _authService.LoginAsync(
            new LoginRequestDto("expiredsession@example.com", "Password123@"),
            "127.0.0.1");
        Assert.True(loginResult.IsSuccess);

        var session = _authRepository.Sessions.Single();

        // Advance past 8 hours
        _timeProvider.Advance(TimeSpan.FromHours(8).Add(TimeSpan.FromSeconds(1)));

        var meResult = await _authService.GetCurrentUserAsync(account.Id, session.Id);
        Assert.False(meResult.IsSuccess);
        Assert.Equal("UNAUTHORIZED", meResult.Error!.Code);
        Assert.Equal(ErrorType.Unauthorized, meResult.Error.Type);
    }

    [Fact]
    public async Task AccountDeactivatedAfterLoginRejectsSubsequentAccess()
    {
        var account = CreateAccount("deactivatedafter@example.com", "Password123@");

        var loginResult = await _authService.LoginAsync(
            new LoginRequestDto("deactivatedafter@example.com", "Password123@"),
            "127.0.0.1");
        Assert.True(loginResult.IsSuccess);

        var session = _authRepository.Sessions.Single();

        // Admin deactivates account after login
        account.Status = AccountStatus.Deactivated;

        var meResult = await _authService.GetCurrentUserAsync(account.Id, session.Id);
        Assert.False(meResult.IsSuccess);
        Assert.Equal("UNAUTHORIZED", meResult.Error!.Code);
        Assert.Equal(ErrorType.Unauthorized, meResult.Error.Type);
    }

    [Fact]
    public async Task AccountLockedByAdminAfterLoginRejectsSubsequentAccess()
    {
        var account = CreateAccount("lockedbyadmin@example.com", "Password123@");

        var loginResult = await _authService.LoginAsync(
            new LoginRequestDto("lockedbyadmin@example.com", "Password123@"),
            "127.0.0.1");
        Assert.True(loginResult.IsSuccess);

        var session = _authRepository.Sessions.Single();

        // Admin locks account
        account.Status = AccountStatus.Locked;

        var meResult = await _authService.GetCurrentUserAsync(account.Id, session.Id);
        Assert.False(meResult.IsSuccess);
        Assert.Equal("UNAUTHORIZED", meResult.Error!.Code);
    }

    [Fact]
    public async Task TemporaryLockoutDoesNotRevokeActiveSession()
    {
        var account = CreateAccount("templock@example.com", "Password123@");

        var loginResult = await _authService.LoginAsync(
            new LoginRequestDto("templock@example.com", "Password123@"),
            "127.0.0.1");
        Assert.True(loginResult.IsSuccess);

        var session = _authRepository.Sessions.Single();

        // Attacker attempts 5 wrong passwords from different IP
        for (var i = 1; i <= 5; i++)
        {
            await _authService.LoginAsync(new LoginRequestDto("templock@example.com", "WrongPassword!"), "10.0.0.99");
        }

        Assert.NotNull(account.LockoutEndAt);

        // Active session for the legitimate user is NOT revoked by temporary password lockout
        var meResult = await _authService.GetCurrentUserAsync(account.Id, session.Id);
        Assert.True(meResult.IsSuccess);
    }

    [Fact]
    public async Task ControllerLogoutDeletesSessionCookieAndSetsNoStore()
    {
        var account = CreateAccount("cookiedelete@example.com", "Password123@");

        var loginResult = await _authService.LoginAsync(
            new LoginRequestDto("cookiedelete@example.com", "Password123@"),
            "127.0.0.1");

        var session = _authRepository.Sessions.Single();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim("sid", session.Id.ToString())
        };
        _authController.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

        var result = await _authController.Logout(CancellationToken.None);
        Assert.IsType<NoContentResult>(result);

        Assert.Equal("no-store", _authController.Response.Headers.CacheControl.ToString());

        var setCookie = _authController.Response.Headers.SetCookie.ToString();
        Assert.Contains("ocs_session=;", setCookie);
        Assert.Contains("path=/api", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CsrfEndpointReturnsTokenAndSetsNoStore()
    {
        var mockAntiforgery = new TestAntiforgery();
        var result = _authController.GetCsrfToken(mockAntiforgery);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CsrfTokenResponseDto>(okResult.Value);

        Assert.Equal("test-request-token", response.Token);
        Assert.Equal("no-store", _authController.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task CsrfMiddlewareRejectsMutationsWithoutCsrfHeader()
    {
        var mockAntiforgery = new TestFailingAntiforgery();
        var hostEnvironment = new TestHostEnvironment("Production");
        var middleware = new CsrfValidationMiddleware(
            _ => Task.CompletedTask,
            mockAntiforgery,
            hostEnvironment);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/auth/logout";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task CsrfMiddlewarePassesSafeGetRequestsWithoutCsrf()
    {
        var mockAntiforgery = new TestFailingAntiforgery();
        var hostEnvironment = new TestHostEnvironment("Production");
        var nextExecuted = false;

        var middleware = new CsrfValidationMiddleware(
            _ =>
            {
                nextExecuted = true;
                return Task.CompletedTask;
            },
            mockAntiforgery,
            hostEnvironment);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/api/auth/me";

        await middleware.InvokeAsync(context);

        Assert.True(nextExecuted);
    }

    [Fact]
    public async Task CsrfMiddlewareAllowsTestBypassInNonProduction()
    {
        var mockAntiforgery = new TestFailingAntiforgery();
        var hostEnvironment = new TestHostEnvironment("Testing");
        var nextExecuted = false;

        var middleware = new CsrfValidationMiddleware(
            _ =>
            {
                nextExecuted = true;
                return Task.CompletedTask;
            },
            mockAntiforgery,
            hostEnvironment);

        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/auth/login";
        context.Request.Headers["X-Test-Bypass-Csrf"] = "true";

        await middleware.InvokeAsync(context);

        Assert.True(nextExecuted);
    }

    [Fact]
    public void TokenValidationStrictClockSkewRejectsExpiredToken()
    {
        var account = CreateAccount("skewtest@example.com", "Password123@");
        var sessionId = Guid.NewGuid();
        var token = _tokenService.GenerateAccessToken(account, ["Customer"], sessionId);

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "ocs-api",
            ValidateAudience = true,
            ValidAudience = "ocs-clients",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-jwt-secret-key-that-is-at-least-32-bytes")),
            ClockSkew = TimeSpan.Zero,
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var currentTime = _timeProvider.GetUtcNow().UtcDateTime;
                if (notBefore.HasValue && notBefore.Value > currentTime) return false;
                if (expires.HasValue && expires.Value <= currentTime) return false;
                return true;
            }
        };

        var handler = new JwtSecurityTokenHandler();

        // Valid initially
        var principal = handler.ValidateToken(token, tokenValidationParameters, out var validatedToken);
        Assert.NotNull(principal);
        Assert.NotNull(validatedToken);

        // Advance time past 8 hours
        _timeProvider.Advance(TimeSpan.FromHours(8).Add(TimeSpan.FromSeconds(1)));

        // Re-validating token with expired time and ClockSkew=0 throws SecurityTokenInvalidLifetimeException
        Assert.Throws<SecurityTokenInvalidLifetimeException>(() =>
        {
            handler.ValidateToken(token, tokenValidationParameters, out _);
        });
    }

    private sealed class TestAntiforgery : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext)
            => new("test-request-token", "test-cookie-token", "X-XSRF-TOKEN", "ocs_antiforgery");

        public AntiforgeryTokenSet GetTokens(HttpContext httpContext)
            => new("test-request-token", "test-cookie-token", "X-XSRF-TOKEN", "ocs_antiforgery");

        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);
        public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
        public void SetCookieTokenAndHeader(HttpContext httpContext) { }
    }

    internal sealed class TestFailingAntiforgery : IAntiforgery
    {
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext)
            => new("test-request-token", "test-cookie-token", "X-XSRF-TOKEN", "ocs_antiforgery");

        public AntiforgeryTokenSet GetTokens(HttpContext httpContext)
            => new("test-request-token", "test-cookie-token", "X-XSRF-TOKEN", "ocs_antiforgery");

        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(false);
        public Task ValidateRequestAsync(HttpContext httpContext) => throw new AntiforgeryValidationException("CSRF failed.");
        public void SetCookieTokenAndHeader(HttpContext httpContext) { }
    }

    internal sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "OCS.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
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
        public Task<PagedResponse<Account>> GetPagedAccountsAsync(
            PagedRequest paging, string? search, AccountType? accountType, AccountStatus? status, bool staffOnly, Guid? roleId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResponse<AuditLog>> GetPagedAccountAuditLogsAsync(PagedRequest paging,
            Guid? accountId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void AddAuditLog(AuditLog auditLog) => throw new NotSupportedException();

        public List<Account> Accounts { get; } = [];
        public List<OtpChallenge> Challenges { get; } = [];
        public List<AuthSession> Sessions { get; } = [];

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

        public void ReplaceAccountRoles(Account account, IReadOnlyCollection<Guid> roleIds) => throw new NotSupportedException();

        public Task<OtpChallenge?> GetOtpChallengeByIdAsync(Guid challengeId, CancellationToken cancellationToken = default)
            => Task.FromResult(Challenges.FirstOrDefault(c => c.Id == challengeId));

        public Task<int> CountOtpChallengesInWindowAsync(string normalizedEmail, DateTimeOffset windowStart, CancellationToken cancellationToken = default)
            => Task.FromResult(Challenges.Count(c => c.NormalizedEmail == normalizedEmail && c.CreatedAt >= windowStart));

        public Task LockAccountAsync(Guid accountId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LockRoleAsync(Guid roleId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> RevokeSessionsAsync(Guid accountId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReloadChallengeAsync(OtpChallenge challenge, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OtpChallenge?> GetLatestOtpChallengeAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Challenges.Where(c => c.AccountId == id && c.DeliveryStatus != "Failed").OrderByDescending(c => c.CreatedAt).FirstOrDefault());
        public Task<List<OtpChallenge>> GetActiveOtpChallengesAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Challenges.Where(c => c.AccountId == id && !c.InvalidatedAt.HasValue && !c.ConsumedAt.HasValue).ToList());
        public void AddOtpChallenge(OtpChallenge challenge) => Challenges.Add(challenge);

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
