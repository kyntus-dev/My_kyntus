using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Kyntus.Caching;

public sealed class HybridCacheService(
    IMemoryCache memory,
    IDistributedCache? distributed,
    ILogger<HybridCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ConcurrentDictionary<string, byte> _l1Keys = new(StringComparer.Ordinal);

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        if (memory.TryGetValue(key, out T? memHit) && memHit is not null)
            return memHit;

        if (distributed is not null)
        {
            try
            {
                var bytes = await distributed.GetAsync(key, ct);
                if (bytes is { Length: > 0 })
                {
                    var fromRedis = JsonSerializer.Deserialize<T>(bytes, JsonOpts);
                    if (fromRedis is not null)
                    {
                        memory.Set(key, fromRedis, TimeSpan.FromMinutes(1));
                        _l1Keys[key] = 0;
                        return fromRedis;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Redis GET failed for {Key} — factory fallback", key);
            }
        }

        var value = await factory(ct);
        await SetAsync(key, value, ttl, ct);
        return value;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        if (memory.TryGetValue(key, out T? mem) && mem is not null)
            return mem;

        if (distributed is null)
            return default;

        try
        {
            var bytes = await distributed.GetAsync(key, ct);
            if (bytes is null || bytes.Length == 0)
                return default;

            var value = JsonSerializer.Deserialize<T>(bytes, JsonOpts);
            if (value is not null)
            {
                // Promote to L1 (shorter TTL than L2)
                var l1Ttl = TimeSpan.FromMinutes(1);
                memory.Set(key, value, l1Ttl);
                _l1Keys[key] = 0;
            }
            return value;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Redis GET failed for {Key} — L1/DB fallback", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        var l1Ttl = ttl < TimeSpan.FromMinutes(1) ? ttl : TimeSpan.FromMinutes(Math.Min(ttl.TotalMinutes, 2));
        memory.Set(key, value!, l1Ttl);
        _l1Keys[key] = 0;

        if (distributed is null)
            return;

        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOpts);
            await distributed.SetAsync(key, bytes, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Redis SET failed for {Key} — L1 only", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        memory.Remove(key);
        _l1Keys.TryRemove(key, out _);

        if (distributed is null)
            return;

        try
        {
            await distributed.RemoveAsync(key, ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Redis REMOVE failed for {Key}", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var matching = _l1Keys.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        foreach (var key in matching)
        {
            // RemoveAsync purge L1 + Redis pour chaque clé connue de cette instance.
            await RemoveAsync(key, ct);
        }

        // Exact known keys for common invalidation patterns
        if (prefix.StartsWith("directory:", StringComparison.Ordinal))
        {
            await RemoveAsync(KyntusCacheKeys.DirectoryOrgOverview(), ct);
        }
    }
}
