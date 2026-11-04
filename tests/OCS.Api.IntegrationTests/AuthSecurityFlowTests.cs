using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using OCS.Api.Consts;
using OCS.Api.Context;
using OCS.Api.Interfaces.Services;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class AuthSecurityFlowTests
{
    private sealed class FakeTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private static WebApplicationFactory<Program> CreateFactory(TestDatabase database, FakeTimeProvider clock) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders().AddConsole());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString,
                ["Redis:ConnectionString"] = "",
                ["Email:Provider"] = "TestSink",
                ["Auth:SessionExpirySeconds"] = "60"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(database.ConnectionString));
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<TestInboxEmailSender>();
                services.AddSingleton<IEmailSender>(provider => provider.GetRequiredService<TestInboxEmailSender>());
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Bypass-Csrf", "true");
        return client;
    }

    private static string Code(TestInboxEmailSender inbox) =>
        Regex.Match(inbox.SentMessages.Last().Body, @"\b[0-9]{6}\b").Value;

    [PostgresFact]
    public async Task HttpFlowEnforcesContinuationResendSessionAndConcurrentLockout()
    {
        await using var database = await TestDatabase.CreateAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = CreateFactory(database, clock);
        using var client = CreateClient(factory);
        const string email = "flow@example.com";
        const string password = "Password123@";
        var oversized = await client.PostAsJsonAsync("/api/auth/login", new { email, password = new string('a', 73) });
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        var registration = await client.PostAsJsonAsync("/api/auth/register", new { fullName = "Test User", email, phoneNumber = "0912345678", password });
        Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
        var challenge = await registration.Content.ReadFromJsonAsync<JsonElement>();
        var id = challenge.GetProperty("challengeId").GetGuid();
        var token = challenge.GetProperty("continuationToken").GetString()!;
        var inbox = factory.Services.GetRequiredService<TestInboxEmailSender>();
        var code = Code(inbox);
        Assert.Equal(6, code.Length);
        var missingToken = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = id, code });
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        var wrongCode = code == "000000" ? "000001" : "000000";
        for (var i = 0; i < 5; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = id, code = wrongCode, continuationToken = token });
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }
        var tooSoon = await client.PostAsJsonAsync("/api/auth/otp/resend", new { challengeId = id, continuationToken = token });
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
        clock.Advance(TimeSpan.FromSeconds(61));
        var resend = await client.PostAsJsonAsync("/api/auth/otp/resend", new { challengeId = id, continuationToken = token });
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
        var replacement = await resend.Content.ReadFromJsonAsync<JsonElement>();
        var newId = replacement.GetProperty("challengeId").GetGuid();
        var newToken = replacement.GetProperty("continuationToken").GetString()!;
        var stale = await client.PostAsJsonAsync("/api/auth/otp/resend", new { challengeId = id, continuationToken = token });
        Assert.Equal(HttpStatusCode.NotFound, stale.StatusCode);
        var verify = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = newId, code = Code(inbox), continuationToken = newToken });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(60, session.GetProperty("expiresIn").GetInt32());
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), value => value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/products", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        // An old Active account without email proof must complete OTP before a new session.
        await using (var legacy = database.Context())
        {
            var existing = await legacy.Accounts.SingleAsync(a => a.Email == email);
            existing.EmailVerifiedAt = null;
            await legacy.SaveChangesAsync();
        }
        clock.Advance(TimeSpan.FromSeconds(61));
        var pending = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.Forbidden, pending.StatusCode);
        var pendingBody = await pending.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AUTH.ACCOUNT_PENDING_ACTIVATION", pendingBody.GetProperty("code").GetString());
        var legacyVerify = await client.PostAsJsonAsync("/api/auth/otp/verify", new
        {
            challengeId = pendingBody.GetProperty("challengeId").GetGuid(),
            code = Code(inbox),
            continuationToken = pendingBody.GetProperty("continuationToken").GetString()
        });
        Assert.Equal(HttpStatusCode.OK, legacyVerify.StatusCode);

        var badLogins = Enumerable.Range(0, 10).Select(_ => client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong" }));
        var results = await Task.WhenAll(badLogins);
        Assert.All(results, result => Assert.True(result.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden));
        await using var db = database.Context();
        var account = await db.Accounts.SingleAsync(a => a.Email == email);
        Assert.Equal(5, account.FailedLoginAttempts);
        Assert.NotNull(account.LockoutEndAt);
        Assert.NotNull(account.EmailVerifiedAt);
        Assert.Null(account.PhoneVerifiedAt);
    }

    [PostgresFact]
    public async Task OtpCannotUnlockAccountOrVerifyChangedEmail()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var factory = CreateFactory(database, new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var client = CreateClient(factory);
        var inbox = factory.Services.GetRequiredService<TestInboxEmailSender>();
        foreach (var locked in new[] { true, false })
        {
            var email = locked ? "locked@example.com" : "old@example.com";
            var registration = await client.PostAsJsonAsync("/api/auth/register", new { fullName = "Test User", email, phoneNumber = locked ? "0912345678" : "0912345679", password = "Password123@" });
            Assert.Equal(HttpStatusCode.OK, registration.StatusCode);
            var challenge = await registration.Content.ReadFromJsonAsync<JsonElement>();
            var code = Code(inbox);
            await using (var db = database.Context())
            {
                var account = await db.Accounts.SingleAsync(a => a.Email == email);
                if (locked) account.Status = AccountStatus.Locked;
                else account.Email = "new@example.com";
                await db.SaveChangesAsync();
            }
            var verify = await client.PostAsJsonAsync("/api/auth/otp/verify", new { challengeId = challenge.GetProperty("challengeId").GetGuid(), code, continuationToken = challenge.GetProperty("continuationToken").GetString() });
            Assert.Equal(HttpStatusCode.Forbidden, verify.StatusCode);
            await using var check = database.Context();
            var current = await check.Accounts.SingleAsync(a => a.Email == (locked ? email : "new@example.com"));
            Assert.Null(current.EmailVerifiedAt);
            Assert.Equal(locked ? AccountStatus.Locked : AccountStatus.PendingActivation, current.Status);
        }
    }
}
