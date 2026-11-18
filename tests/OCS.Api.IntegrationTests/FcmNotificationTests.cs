using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OCS.Api.Configuration;
using OCS.Api.Context;
using OCS.Api.Entities;
using OCS.Api.Models.Notifications;
using OCS.Api.Repositories;
using OCS.Api.Services;

namespace OCS.Api.IntegrationTests;

public sealed class FcmNotificationTests
{
    private static DbContextOptions<AppDbContext> DatabaseOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static FcmNotificationService Service(AppDbContext db, MockHandler? handler = null, bool configured = true)
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        cache.SetString("fcm:oauth:access_token", "mock-access-token");
        return new(db, cache, new Factory(handler ?? new()), Options.Create(configured
            ? new FirebaseOptions { ProjectId = "test", ClientEmail = "test@example.invalid", PrivateKey = "unused-mock" }
            : new FirebaseOptions()), NullLogger<FcmNotificationService>.Instance);
    }

    [Fact]
    public async Task RegistrationSurvivesNewContextAndCacheAndTransfersOwnership()
    {
        var options = DatabaseOptions();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await using (var db = new AppDbContext(options))
            await Service(db).RegisterTokenAsync(a, "browser-token");
        await using var reopened = new AppDbContext(options);
        var service = Service(reopened);
        Assert.Single(await service.GetTokensForAccountAsync(a));
        await service.RegisterTokenAsync(b, "browser-token");
        Assert.Empty(await service.GetTokensForAccountAsync(a));
        Assert.Single(await service.GetTokensForAccountAsync(b));
        // A delayed logout from the former owner must not remove B's registration.
        await service.UnregisterTokenAsync(a, "browser-token");
        Assert.Single(await service.GetTokensForAccountAsync(b));
    }

    [Theory]
    [InlineData("{\"error\":{\"status\":\"INVALID_ARGUMENT\",\"message\":\"Message too big\"}}", 400, false)]
    [InlineData("{\"error\":{\"status\":\"NOT_FOUND\"}}", 404, false)]
    [InlineData("not json", 500, false)]
    [InlineData("{\"error\":{\"details\":[{\"@type\":\"type.googleapis.com/google.firebase.fcm.v1.FcmError\",\"errorCode\":\"UNREGISTERED\"}]}}", 404, true)]
    public async Task OnlyConfirmedUnregisteredErrorsRemoveTokens(string error, int status, bool remove)
    {
        await using var db = new AppDbContext(DatabaseOptions());
        var handler = new MockHandler { Status = (HttpStatusCode)status, Body = error };
        var service = Service(db, handler);
        var id = Guid.NewGuid();
        await service.RegisterTokenAsync(id, "browser-token");
        var result = await service.SendPushToUserAsync(id, "Title", "Body");
        Assert.Equal(1, result.TargetedDeviceCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Equal(remove ? 0 : 1, (await service.GetTokensForAccountAsync(id)).Count);
    }

    [Fact]
    public async Task RegistrationKeepsMostRecentTenDevices()
    {
        await using var db = new AppDbContext(DatabaseOptions());
        var service = Service(db);
        var id = Guid.NewGuid();
        for (var i = 0; i < 11; i++) await service.RegisterTokenAsync(id, $"token-{i}");
        var tokens = await service.GetTokensForAccountAsync(id);
        Assert.Equal(10, tokens.Count);
        Assert.DoesNotContain(tokens, t => t.Token == "token-0");
    }

    [Fact]
    public async Task MarkAllReadsOlderUnreadNotificationsAndPreservesOtherAccounts()
    {
        await using var db = new AppDbContext(DatabaseOptions());
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        for (var i = 0; i < 150; i++) db.Notifications.Add(new Notification { RecipientAccountId = id, Title = "Test", Content = "Body", SentAt = DateTimeOffset.UtcNow.AddMinutes(-i), IsRead = i < 100 });
        db.Notifications.Add(new Notification { RecipientAccountId = other, Title = "Other", Content = "Body" });
        await db.SaveChangesAsync();
        var service = Notifications(db);
        Assert.True((await service.MarkAllAsReadAsync(id)).IsSuccess);
        Assert.Equal(0, await service.GetUnreadCountAsync(id));
        Assert.Equal(1, await service.GetUnreadCountAsync(other));
        var foreign = await db.Notifications.SingleAsync(n => n.RecipientAccountId == other);
        Assert.False((await service.MarkAsReadAsync(foreign.Id, id)).IsSuccess);
    }

    [Theory]
    [InlineData(true, 200, 1)]
    [InlineData(true, 503, 0)]
    [InlineData(false, 200, 0)]
    public async Task CreatePersistsInAppAndReportsActualDispatch(bool configured, int status, int successes)
    {
        await using var db = new AppDbContext(DatabaseOptions());
        var id = Guid.NewGuid();
        var handler = new MockHandler { Status = (HttpStatusCode)status };
        var fcm = Service(db, handler, configured);
        await fcm.RegisterTokenAsync(id, "browser-token");
        var result = await Notifications(db, fcm).CreateAsync(new CreateNotificationDto { RecipientAccountId = id, Title = "Test", Content = "Body" });
        Assert.True(result.IsSuccess);
        Assert.Single(db.Notifications);
        Assert.NotNull(result.Data!.PushDelivery);
        Assert.Equal(successes, result.Data.PushDelivery.SuccessCount);
        Assert.Equal(configured, result.Data.PushDelivery.FirebaseConfigured);
        if (configured)
        {
            Assert.Contains("browser-token", handler.RequestBody);
            Assert.Contains("recipientAccountId", handler.RequestBody);
            Assert.Equal("Bearer", handler.Scheme);
        }
    }

    [PostgresFact]
    public async Task PostgresUpsertHasOneOwnerAndConcurrentRegistrationsStayUnique()
    {
        await using var database = await TestDatabase.CreateAsync();
        Guid a, b;
        await using (var seed = database.Context())
        {
            var first = new Account { FullName = "A", PhoneNumber = "0901111111" };
            var second = new Account { FullName = "B", PhoneNumber = "0902222222" };
            seed.Accounts.AddRange(first, second);
            await seed.SaveChangesAsync();
            a = first.Id; b = second.Id;
        }
        await using var dbA = database.Context();
        await using var dbB = database.Context();
        await Task.WhenAll(Service(dbA).RegisterTokenAsync(a, "shared-token"), Service(dbB).RegisterTokenAsync(b, "shared-token"));
        Assert.Equal(1, await dbA.FcmDeviceTokens.CountAsync());
        await Service(dbB).RegisterTokenAsync(b, "shared-token");
        await Service(dbA).UnregisterTokenAsync(a, "shared-token");
        Assert.Empty(await Service(dbA).GetTokensForAccountAsync(a));
        Assert.Single(await Service(dbB).GetTokensForAccountAsync(b));
    }

    [Theory]
    [InlineData("/", false)]
    [InlineData("/appointments", false)]
    [InlineData("https://example.com/appointments", true)]
    public async Task MergePreservesCustomPayloadLinksAndBothDispatchResponseNames(string link, bool hasFcmLink)
    {
        await using var db = new AppDbContext(DatabaseOptions());
        var id = Guid.NewGuid();
        var handler = new MockHandler();
        var fcm = Service(db, handler);
        await fcm.RegisterTokenAsync(id, "browser-token");
        var result = await Notifications(db, fcm).CreateAsync(new CreateNotificationDto
        {
            RecipientAccountId = id, Title = "Test", Content = "Body", Link = link,
            ExtraData = new Dictionary<string, string>
            {
                ["appointmentId"] = "appointment-123",
                ["recipientAccountId"] = "incorrect-recipient",
                ["notificationId"] = "incorrect-notification"
            }
        });
        Assert.True(result.IsSuccess);
        Assert.Same(result.Data!.PushDelivery, result.Data.PushResult);
        Assert.Equal(1, result.Data.PushResult!.SuccessCount);
        using var document = JsonDocument.Parse(handler.RequestBody!);
        var message = document.RootElement.GetProperty("message");
        var data = message.GetProperty("data");
        Assert.Equal(id.ToString(), data.GetProperty("recipientAccountId").GetString());
        Assert.Equal(result.Data.Id.ToString(), data.GetProperty("notificationId").GetString());
        Assert.Equal("appointment-123", data.GetProperty("appointmentId").GetString());
        Assert.Equal(link, data.GetProperty("url").GetString());
        Assert.Equal(link, data.GetProperty("link").GetString());
        var webPush = message.GetProperty("webpush");
        Assert.Equal("high", webPush.GetProperty("headers").GetProperty("Urgency").GetString());
        Assert.Equal(hasFcmLink, webPush.TryGetProperty("fcm_options", out var options));
        if (hasFcmLink) Assert.Equal(link, options.GetProperty("link").GetString());
        Assert.Equal("high", message.GetProperty("android").GetProperty("priority").GetString());
    }

    [Fact]
    public async Task TransportTimeoutDoesNotFailSavedNotificationOrRemoveDevice()
    {
        await using var db = new AppDbContext(DatabaseOptions());
        var id = Guid.NewGuid();
        var fcm = Service(db, new MockHandler { Timeout = true });
        await fcm.RegisterTokenAsync(id, "browser-token");
        var result = await Notifications(db, fcm).CreateAsync(new CreateNotificationDto
            { RecipientAccountId = id, Title = "Test", Content = "Body" });
        Assert.True(result.IsSuccess);
        Assert.Single(db.Notifications);
        Assert.Equal(1, result.Data!.PushDelivery!.FailureCount);
        Assert.Single(await fcm.GetTokensForAccountAsync(id));
    }

    private static NotificationService Notifications(AppDbContext db, FcmNotificationService? fcm = null) =>
        new(new UnitOfWork(db, new ProductRepository(db), new AuthRepository(db), new AppointmentRepository(db), new NotificationRepository(db), new CustomerProfileRepository(db)), fcm);

    private sealed class Factory(MockHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private sealed class MockHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string Body { get; init; } = "{}";
        public bool Timeout { get; init; }
        public string? RequestBody { get; private set; }
        public string? Scheme { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(ct);
            Scheme = request.Headers.Authorization?.Scheme;
            if (Timeout) throw new TaskCanceledException("Simulated HTTP timeout");
            return new(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }
}
