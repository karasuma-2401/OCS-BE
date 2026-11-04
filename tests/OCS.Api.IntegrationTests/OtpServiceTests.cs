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

public sealed class OtpServiceTests : IDisposable
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly TestInboxEmailSender _emailSender;
    private readonly TestAuthRepository _authRepository;
    private readonly TestUnitOfWork _unitOfWork;
    private readonly OtpRateLimiter _rateLimiter;
    private readonly IOptions<OtpOptions> _otpOptions;
    private readonly IConfiguration _configuration;
    private readonly OtpService _otpService;

    public void Dispose() => _unitOfWork.Dispose();

    public OtpServiceTests()
    {
        _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero));
        _emailSender = new TestInboxEmailSender();
        _authRepository = new TestAuthRepository();
        _unitOfWork = new TestUnitOfWork(_authRepository);

        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var options = new OtpOptions
        {
            ExpiryMinutes = 5,
            ResendCooldownSeconds = 60,
            MaxFailedAttempts = 5,
            ContinuationExpiryMinutes = 15,
            MaxSendsPerEmailPerHour = 5,
            MaxSendsPerIpPerHour = 20,
            SecretKey = "test-otp-secret-key-that-is-at-least-32-bytes"
        };
        _otpOptions = Options.Create(options);

        _rateLimiter = new OtpRateLimiter(cache, _otpOptions);
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "test-jwt-secret-key-that-is-at-least-32-bytes"
        }).Build();

        _otpService = new OtpService(
            _unitOfWork,
            _emailSender,
            _rateLimiter,
            _otpOptions,
            _configuration,
            _timeProvider,
            NullLogger<OtpService>.Instance);
    }

    private static Account CreateTestAccount(string email = "customer@example.com") => new()
    {
        Id = Guid.NewGuid(),
        FullName = "Test Customer",
        Email = email,
        AccountType = AccountType.Customer,
        Status = AccountStatus.PendingActivation
    };

    private string ExtractOtpCodeFromEmail()
    {
        var email = Assert.Single(_emailSender.SentMessages);
        var match = Regex.Match(email.Body, @"\b\d{6}\b");
        Assert.True(match.Success, "Expected 6-digit OTP in email body.");
        return match.Value;
    }

    [Fact]
    public async Task IssueOtpSendsEmailAndReturnsMaskedMetadata()
    {
        var account = CreateTestAccount("nguyenvana@example.com");
        _authRepository.AddAccount(account);

        var result = await _otpService.IssueRegistrationOtpAsync(account, "192.168.1.1");

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Data!.ChallengeId);
        Assert.Equal("RegistrationActivation", result.Data.Purpose);
        Assert.Equal("n***a@example.com", result.Data.MaskedDestination);
        Assert.Equal(_timeProvider.GetUtcNow().AddMinutes(5), result.Data.ExpiresAt);
        Assert.Equal(_timeProvider.GetUtcNow().AddSeconds(60), result.Data.ResendAvailableAt);
        Assert.NotEmpty(result.Data.ContinuationToken);

        var sent = Assert.Single(_emailSender.SentMessages);
        Assert.Equal("nguyenvana@example.com", sent.To);
        Assert.Contains("[OCS] Mã xác thực", sent.Subject, StringComparison.Ordinal);

        var challenge = await _authRepository.GetOtpChallengeByIdAsync(result.Data.ChallengeId);
        Assert.NotNull(challenge);
        Assert.Equal("Sent", challenge.DeliveryStatus);
        Assert.NotNull(challenge.SentAt);
        Assert.Null(challenge.ConsumedAt);
        Assert.Null(challenge.InvalidatedAt);
    }

    [Fact]
    public async Task VerifyValidCodeMarksChallengeConsumed()
    {
        var account = CreateTestAccount();
        _authRepository.AddAccount(account);
        var issueResult = await _otpService.IssueRegistrationOtpAsync(account, "127.0.0.1");
        var code = ExtractOtpCodeFromEmail();

        var verifyResult = await _otpService.VerifyOtpAsync(
            issueResult.Data!.ChallengeId,
            code,
            "RegistrationActivation", issueResult.Data!.ContinuationToken);

        Assert.True(verifyResult.IsSuccess);
        Assert.Equal(account.Id, verifyResult.Data!.AccountId);
        Assert.Equal("CUSTOMER@EXAMPLE.COM", verifyResult.Data.NormalizedEmail);

        var challenge = await _authRepository.GetOtpChallengeByIdAsync(issueResult.Data.ChallengeId);
        Assert.NotNull(challenge!.ConsumedAt);
    }

    [Fact]
    public async Task VerifyWrongCodeIncrementsFailedAttemptsAndRejects()
    {
        var account = CreateTestAccount();
        _authRepository.AddAccount(account);
        var issueResult = await _otpService.IssueRegistrationOtpAsync(account, "127.0.0.1");

        var verifyResult = await _otpService.VerifyOtpAsync(
            issueResult.Data!.ChallengeId,
            "000000",
            "RegistrationActivation", issueResult.Data!.ContinuationToken);

        Assert.False(verifyResult.IsSuccess);
        Assert.Equal("AUTH.OTP_INVALID", verifyResult.Error!.Code);
        Assert.Contains("4 lần thử", verifyResult.Error.Message, StringComparison.Ordinal);

        var challenge = await _authRepository.GetOtpChallengeByIdAsync(issueResult.Data.ChallengeId);
        Assert.Equal(1, challenge!.FailedAttempts);
        Assert.Null(challenge.ConsumedAt);
        Assert.Null(challenge.InvalidatedAt);
    }

    [Fact]
    public async Task FifthWrongAttemptInvalidatesChallenge()
    {
        var account = CreateTestAccount();
        _authRepository.AddAccount(account);
        var issueResult = await _otpService.IssueRegistrationOtpAsync(account, "127.0.0.1");

        for (var i = 1; i <= 4; i++)
        {
            var res = await _otpService.VerifyOtpAsync(issueResult.Data!.ChallengeId, "000000", "RegistrationActivation", issueResult.Data!.ContinuationToken);
            Assert.False(res.IsSuccess);
            Assert.Equal("AUTH.OTP_INVALID", res.Error!.Code);
        }

        var fifthRes = await _otpService.VerifyOtpAsync(issueResult.Data!.ChallengeId, "000000", "RegistrationActivation", issueResult.Data!.ContinuationToken);
        Assert.False(fifthRes.IsSuccess);
        Assert.Equal("AUTH.OTP_ATTEMPTS_EXCEEDED", fifthRes.Error!.Code);

        var challenge = await _authRepository.GetOtpChallengeByIdAsync(issueResult.Data.ChallengeId);
        Assert.Equal(5, challenge!.FailedAttempts);
        Assert.NotNull(challenge.InvalidatedAt);

        // Subsequent verify attempts are also rejected
        var sixthRes = await _otpService.VerifyOtpAsync(issueResult.Data.ChallengeId, "000000", "RegistrationActivation", issueResult.Data!.ContinuationToken);
        Assert.False(sixthRes.IsSuccess);
        Assert.Equal("AUTH.CHALLENGE_NOT_FOUND", sixthRes.Error!.Code);
    }

    [Fact]
    public async Task VerifyExpiredOtpIsRejectedWithoutAdvancingClockInSleep()
    {
        var account = CreateTestAccount();
        _authRepository.AddAccount(account);
        var issueResult = await _otpService.IssueRegistrationOtpAsync(account, "127.0.0.1");
        var code = ExtractOtpCodeFromEmail();

        // Advance 5 minutes and 1 second using FakeTimeProvider
        _timeProvider.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));

        var verifyResult = await _otpService.VerifyOtpAsync(
            issueResult.Data!.ChallengeId,
            code,
            "RegistrationActivation", issueResult.Data!.ContinuationToken);

        Assert.False(verifyResult.IsSuccess);
        Assert.Equal("AUTH.OTP_EXPIRED", verifyResult.Error!.Code);
    }

    [Fact]
    public async Task ResendWithinCooldownIsRejectedWith429()
    {
        var account = CreateTestAccount();
        _authRepository.AddAccount(account);
        var issueResult = await _otpService.IssueRegistrationOtpAsync(account, "127.0.0.1");

        // Advance only 30 seconds
        _timeProvider.Advance(TimeSpan.FromSeconds(30));

        var resendResult = await _otpService.ResendOtpAsync(issueResult.Data!.ChallengeId, issueResult.Data.ContinuationToken, "127.0.0.1");

        Assert.False(resendResult.IsSuccess);
        Assert.Equal("AUTH.RESEND_COOLDOWN", resendResult.Error!.Code);
        Assert.Equal(ErrorType.RateLimited, resendResult.Error.Type);
        Assert.Equal(30, resendResult.Error.Extensions!["retryAfterSeconds"]);
    }

    [Fact]
    public async Task ResendAfterCooldownInvalidatesOldChallengeAndRejectsOldCode()
    {
        var account = CreateTestAccount();
        _authRepository.AddAccount(account);
        var issueResult = await _otpService.IssueRegistrationOtpAsync(account, "127.0.0.1");
        var oldCode = ExtractOtpCodeFromEmail();
        _emailSender.Clear();

        // Advance past 60s cooldown
        _timeProvider.Advance(TimeSpan.FromSeconds(65));

        var resendResult = await _otpService.ResendOtpAsync(issueResult.Data!.ChallengeId, issueResult.Data.ContinuationToken, "127.0.0.1");
        Assert.True(resendResult.IsSuccess);
        Assert.NotEqual(issueResult.Data.ChallengeId, resendResult.Data!.ChallengeId);

        var newCode = ExtractOtpCodeFromEmail();
        Assert.NotEmpty(newCode);

        // Old challenge is invalidated
        var oldChallenge = await _authRepository.GetOtpChallengeByIdAsync(issueResult.Data.ChallengeId);
        Assert.NotNull(oldChallenge!.InvalidatedAt);

        // Attempting to verify old challenge with old code fails
        var verifyOldResult = await _otpService.VerifyOtpAsync(
            issueResult.Data.ChallengeId,
            oldCode,
            "RegistrationActivation", issueResult.Data!.ContinuationToken);
        Assert.False(verifyOldResult.IsSuccess);
        Assert.Equal("AUTH.CHALLENGE_NOT_FOUND", verifyOldResult.Error!.Code);

        // Verifying new challenge with new code succeeds
        var verifyNewResult = await _otpService.VerifyOtpAsync(
            resendResult.Data.ChallengeId,
            newCode,
            "RegistrationActivation", resendResult.Data.ContinuationToken);
        Assert.True(verifyNewResult.IsSuccess);
    }

    [Fact]
    public async Task ConsumedChallengeCannotBeReplayed()
    {
        var account = CreateTestAccount();
        _authRepository.AddAccount(account);
        var issueResult = await _otpService.IssueRegistrationOtpAsync(account, "127.0.0.1");
        var code = ExtractOtpCodeFromEmail();

        var first = await _otpService.VerifyOtpAsync(issueResult.Data!.ChallengeId, code, "RegistrationActivation", issueResult.Data!.ContinuationToken);
        Assert.True(first.IsSuccess);

        // Replay attempt
        var second = await _otpService.VerifyOtpAsync(issueResult.Data.ChallengeId, code, "RegistrationActivation", issueResult.Data!.ContinuationToken);
        Assert.False(second.IsSuccess);
        Assert.Equal("AUTH.CHALLENGE_NOT_FOUND", second.Error!.Code);
    }

    [Fact]
    public async Task CrossPurposeVerificationIsRejected()
    {
        var account = CreateTestAccount();
        _authRepository.AddAccount(account);
        var issueResult = await _otpService.IssueRegistrationOtpAsync(account, "127.0.0.1");
        var code = ExtractOtpCodeFromEmail();

        var result = await _otpService.VerifyOtpAsync(issueResult.Data!.ChallengeId, code, "PasswordReset", issueResult.Data!.ContinuationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal("AUTH.CHALLENGE_NOT_FOUND", result.Error!.Code);
    }

    [Fact]
    public async Task HourlyEmailRateLimitRejectsExcessiveRequests()
    {
        var account = CreateTestAccount();
        _authRepository.AddAccount(account);

        // Perform 5 successful issues (with 65s gaps)
        for (var i = 0; i < 5; i++)
        {
            var res = await _otpService.IssueRegistrationOtpAsync(account, $"192.168.1.{i + 1}");
            Assert.True(res.IsSuccess);
            _timeProvider.Advance(TimeSpan.FromSeconds(65));
        }

        // 6th issue within the same hour fails
        var sixth = await _otpService.IssueRegistrationOtpAsync(account, "192.168.1.100");
        Assert.False(sixth.IsSuccess);
        Assert.Equal("AUTH.RATE_LIMITED", sixth.Error!.Code);
        Assert.Equal(ErrorType.RateLimited, sixth.Error.Type);
    }

    [Fact]
    public async Task HourlyIpRateLimitRejectsExcessiveRequestsFromSameIp()
    {
        for (var i = 0; i < 20; i++)
        {
            var account = CreateTestAccount($"user{i}@example.com");
            _authRepository.AddAccount(account);
            var res = await _otpService.IssueRegistrationOtpAsync(account, "10.0.0.1");
            Assert.True(res.IsSuccess);
        }

        // 21st request from same IP
        var extraAccount = CreateTestAccount("overflow@example.com");
        _authRepository.AddAccount(extraAccount);
        var rejected = await _otpService.IssueRegistrationOtpAsync(extraAccount, "10.0.0.1");
        Assert.False(rejected.IsSuccess);
        Assert.Equal("AUTH.RATE_LIMITED", rejected.Error!.Code);
    }

    [Fact]
    public async Task EmailDeliveryFailureMarksChallengeFailedAndReturnsServiceUnavailable()
    {
        var failingSender = new FailingEmailSender();
        var otpService = new OtpService(
            _unitOfWork,
            failingSender,
            _rateLimiter,
            _otpOptions,
            _configuration,
            _timeProvider,
            NullLogger<OtpService>.Instance);

        var account = CreateTestAccount();
        _authRepository.AddAccount(account);

        var result = await otpService.IssueRegistrationOtpAsync(account, "127.0.0.1");

        Assert.False(result.IsSuccess);
        Assert.Equal("AUTH.DELIVERY_UNAVAILABLE", result.Error!.Code);
        Assert.Equal(ErrorType.ServiceUnavailable, result.Error.Type);

        var challenge = Assert.Single(_authRepository.Challenges);
        Assert.Equal("Failed", challenge.DeliveryStatus);
        Assert.Null(challenge.SentAt);
        Assert.Null(challenge.ConsumedAt);
    }

    [Fact]
    public async Task FailedResendRestoresPreviousCode()
    {
        var account = CreateTestAccount();
        _authRepository.AddAccount(account);
        var first = await _otpService.IssueRegistrationOtpAsync(account, "127.0.0.1");
        var code = ExtractOtpCodeFromEmail();
        _timeProvider.Advance(TimeSpan.FromSeconds(65));
        var failingService = new OtpService(_unitOfWork, new FailingEmailSender(), _rateLimiter,
            _otpOptions, _configuration, _timeProvider, NullLogger<OtpService>.Instance);

        var failed = await failingService.ResendOtpAsync(first.Data!.ChallengeId, first.Data.ContinuationToken, "127.0.0.1");
        Assert.Equal("AUTH.DELIVERY_UNAVAILABLE", failed.Error!.Code);
        var verification = await _otpService.VerifyOtpAsync(first.Data.ChallengeId, code,
            "RegistrationActivation", first.Data.ContinuationToken);
        Assert.True(verification.IsSuccess);
    }

    [Fact]
    public async Task ParallelVerificationHandlesConcurrencyCleanly()
    {
        var account = CreateTestAccount();
        _authRepository.AddAccount(account);
        var issueResult = await _otpService.IssueRegistrationOtpAsync(account, "127.0.0.1");
        var code = ExtractOtpCodeFromEmail();

        // Configure unit of work to simulate concurrency conflict on second call
        _unitOfWork.SimulateConcurrencyExceptionOnNextSave = true;

        var result = await _otpService.VerifyOtpAsync(issueResult.Data!.ChallengeId, code, "RegistrationActivation", issueResult.Data!.ContinuationToken);
        Assert.False(result.IsSuccess);
        Assert.Equal("AUTH.OTP_CONCURRENT_CONSUMPTION", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
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
        public bool SimulateConcurrencyExceptionOnNextSave { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (SimulateConcurrencyExceptionOnNextSave)
            {
                SimulateConcurrencyExceptionOnNextSave = false;
                throw new DbUpdateConcurrencyException("Simulated concurrency conflict.");
            }
            return Task.FromResult(1);
        }

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
                // Simulate normalization
                typeof(Account).GetProperty(nameof(Account.NormalizedEmail))!
                    .SetValue(account, EmailIdentity.Normalize(account.Email));
            }
            Accounts.Add(account);
        }

        public Task<OtpChallenge?> GetOtpChallengeByIdAsync(Guid challengeId, CancellationToken cancellationToken = default)
            => Task.FromResult(Challenges.FirstOrDefault(c => c.Id == challengeId));

        public Task<int> CountOtpChallengesInWindowAsync(string normalizedEmail, DateTimeOffset windowStart, CancellationToken cancellationToken = default)
            => Task.FromResult(Challenges.Count(c => c.NormalizedEmail == normalizedEmail && c.CreatedAt >= windowStart));

        public Task<OtpChallenge?> GetLatestOtpChallengeAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Challenges.Where(c => c.AccountId == id && c.DeliveryStatus != "Failed").OrderByDescending(c => c.CreatedAt).FirstOrDefault());
        public Task<List<OtpChallenge>> GetActiveOtpChallengesAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Challenges.Where(c => c.AccountId == id && c.ConsumedAt == null && c.InvalidatedAt == null).ToList());
        public Task LockAccountAsync(Guid accountId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReloadChallengeAsync(OtpChallenge challenge, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
