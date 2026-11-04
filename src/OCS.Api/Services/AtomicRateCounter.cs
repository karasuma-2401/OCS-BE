using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace OCS.Api.Services;

/// <summary>Redis uses one atomic operation across instances; memory is for local/test only.</summary>
internal static class AtomicRateCounter
{
    private sealed class Window { public DateTimeOffset ExpiresAt; public int Count; }
    private sealed class Store { public readonly Dictionary<string, Window> Windows = []; }
    private static readonly ConditionalWeakTable<IDistributedCache, Store> LocalStores = new();

    public static async Task<bool> AcquireAsync(IDistributedCache cache, IConnectionMultiplexer? redis, string key, int maximum)
    {
        if (redis is not null)
        {
            const string script = "local n = redis.call('INCR', KEYS[1]); if n == 1 then redis.call('EXPIRE', KEYS[1], 3600) end; return n";
            var result = await redis.GetDatabase().ScriptEvaluateAsync(script, [new RedisKey(key)]);
            return (long)result <= maximum;
        }

        var store = LocalStores.GetValue(cache, _ => new Store());
        lock (store.Windows)
        {
            var now = DateTimeOffset.UtcNow;
            if (!store.Windows.TryGetValue(key, out var window) || window.ExpiresAt <= now)
            {
                window = new Window { ExpiresAt = now.AddHours(1) };
                store.Windows[key] = window;
            }
            window.Count++;
            return window.Count <= maximum;
        }
    }
}
