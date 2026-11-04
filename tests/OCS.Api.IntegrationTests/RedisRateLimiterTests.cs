using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OCS.Api.Configuration;
using OCS.Api.Extensions;
using OCS.Api.Interfaces.Services;
using OCS.Api.Services;
using StackExchange.Redis;

namespace OCS.Api.IntegrationTests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RedisFactAttribute : FactAttribute
{
    public RedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCS_TEST_REDIS")))
            Skip = "Set OCS_TEST_REDIS to an isolated Redis server for integration tests.";
    }
}

public sealed class RedisRateLimiterTests
{
    [RedisFact]
    public async Task ConfiguredServicesUseRedisForRateLimits()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Redis:ConnectionString"] = Environment.GetEnvironmentVariable("OCS_TEST_REDIS")
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddServices(config);
        services.AddCaching(config);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var limiter = scope.ServiceProvider.GetRequiredService<ILoginRateLimiter>();
        var connection = provider.GetRequiredService<IConnectionMultiplexer>();
        var ip = "integration-" + Guid.NewGuid().ToString("N");
        var key = (RedisKey)$"ocs:ratelimit:login:ip:{ip}";
        try
        {
            Assert.True(await limiter.AllowIpRequestAsync(ip));
            Assert.Equal(1, (long)await connection.GetDatabase().StringGetAsync(key));
        }
        finally
        {
            await connection.GetDatabase().KeyDeleteAsync(key);
        }
    }

    [RedisFact]
    public async Task ConcurrentLoginRequestsShareOneAtomicLimitAcrossInstances()
    {
        var connectionString = Environment.GetEnvironmentVariable("OCS_TEST_REDIS")!;
        await using var redis1 = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await using var redis2 = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var cache1 = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cache2 = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var options = Options.Create(new AuthOptions { MaxLoginAttemptsPerIpPerHour = 60 });
        var limiter1 = new LoginRateLimiter(cache1, options, redis1);
        var limiter2 = new LoginRateLimiter(cache2, options, redis2);
        var ip = "integration-" + Guid.NewGuid().ToString("N");
        var key = (RedisKey)$"ocs:ratelimit:login:ip:{ip}";
        try
        {
            var requests = Enumerable.Range(0, 120).Select(i =>
                (i % 2 == 0 ? limiter1 : limiter2).AllowIpRequestAsync(ip));
            var results = await Task.WhenAll(requests);
            Assert.Equal(60, results.Count(allowed => allowed));
            var db = redis1.GetDatabase();
            Assert.Equal(120, (long)await db.StringGetAsync(key));
            var ttl = await db.KeyTimeToLiveAsync(key);
            Assert.NotNull(ttl);
            Assert.InRange(ttl.Value.TotalSeconds, 3500, 3600);
            Assert.True(await limiter2.AllowIpRequestAsync(ip + "-other"));
        }
        finally
        {
            await redis1.GetDatabase().KeyDeleteAsync([key, (RedisKey)$"ocs:ratelimit:login:ip:{ip}-other"]);
        }
    }

    [RedisFact]
    public async Task ConcurrentOtpRequestsShareOneAtomicLimitAcrossInstances()
    {
        var connectionString = Environment.GetEnvironmentVariable("OCS_TEST_REDIS")!;
        await using var redis1 = await ConnectionMultiplexer.ConnectAsync(connectionString);
        await using var redis2 = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var cache1 = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var cache2 = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var options = Options.Create(new OtpOptions { MaxSendsPerIpPerHour = 20 });
        var limiter1 = new OtpRateLimiter(cache1, options, redis1);
        var limiter2 = new OtpRateLimiter(cache2, options, redis2);
        var ip = "integration-" + Guid.NewGuid().ToString("N");
        var key = (RedisKey)$"ocs:ratelimit:otp:ip:{ip}";
        try
        {
            var requests = Enumerable.Range(0, 80).Select(i =>
                (i % 2 == 0 ? limiter1 : limiter2).AllowIpRequestAsync(ip));
            var results = await Task.WhenAll(requests);
            Assert.Equal(20, results.Count(allowed => allowed));
            var db = redis1.GetDatabase();
            Assert.Equal(80, (long)await db.StringGetAsync(key));
            var ttl = await db.KeyTimeToLiveAsync(key);
            Assert.NotNull(ttl);
            Assert.InRange(ttl.Value.TotalSeconds, 3500, 3600);
        }
        finally
        {
            await redis1.GetDatabase().KeyDeleteAsync(key);
        }
    }
}
