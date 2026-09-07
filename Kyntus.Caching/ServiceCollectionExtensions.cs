using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kyntus.Caching;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Enregistre L1 (IMemoryCache) + L2 Redis si <c>KYNTUS_REDIS_CONNECTION</c> ou
    /// <c>ConnectionStrings:Redis</c> / <c>Caching:Redis</c> est défini ; sinon L1 seul.
    /// </summary>
    public static IServiceCollection AddKyntusCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemoryCache();

        var redis =
            configuration["KYNTUS_REDIS_CONNECTION"]
            ?? configuration.GetConnectionString("Redis")
            ?? configuration["Caching:Redis"]
            ?? configuration["Redis:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = redis.Trim();
                o.InstanceName = configuration["Caching:InstanceName"] ?? "kyntus:";
            });
        }
        else
        {
            // Placeholder so HybridCacheService can inject IDistributedCache optionally via factory
            services.TryAddSingleton<IDistributedCache, NullDistributedCache>();
        }

        services.AddSingleton<ICacheService>(sp =>
        {
            var memory = sp.GetRequiredService<IMemoryCache>();
            var log = sp.GetRequiredService<ILogger<HybridCacheService>>();
            IDistributedCache? distributed = null;
            if (!string.IsNullOrWhiteSpace(redis))
            {
                try
                {
                    distributed = sp.GetService<IDistributedCache>();
                    if (distributed is NullDistributedCache)
                        distributed = null;
                }
                catch
                {
                    distributed = null;
                }
            }

            return new HybridCacheService(memory, distributed, log);
        });

        return services;
    }
}

/// <summary>No-op distributed cache when Redis is not configured.</summary>
internal sealed class NullDistributedCache : IDistributedCache
{
    public byte[]? Get(string key) => null;
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
    public void Refresh(string key) { }
    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    public void Remove(string key) { }
    public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        Task.CompletedTask;
}
