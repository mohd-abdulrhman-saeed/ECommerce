using ECommerce.Core.Interfaces;
using MessagePack;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Polly;

namespace ECommerce.Infrastructure.Caching;

public class RedisCachingService : ICachingService
{
    private readonly IDistributedCache _distributedCache;
    private readonly DistributedLockService _distributedLock;
    private readonly ResiliencePipeline<byte[]> _resiliencePipeline;
    private readonly ILogger<RedisCachingService> _logger;

    public RedisCachingService(
        IDistributedCache distributedCache,
        DistributedLockService distributedLock,
        ResiliencePipeline<byte[]> resiliencePipeline,
        ILogger<RedisCachingService> logger)
    {
        _distributedCache = distributedCache;
        _distributedLock = distributedLock;
        _resiliencePipeline = resiliencePipeline;
        _logger = logger;
    }

    public async Task<T?> GetOrSetAsync<T>(
        string cacheKey,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        byte[]? cachedBytes = null;
        try
        {
            cachedBytes = await _resiliencePipeline.ExecuteAsync<byte[]?>(
                async token => await _distributedCache.GetAsync(cacheKey, token),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache unavailable. Falling back to DB.");
        }

        if (cachedBytes is not null)
        {
            // فك الـ Serialization السريع باستخدام MessagePack
            return MessagePackSerializer.Deserialize<T>(cachedBytes, cancellationToken: cancellationToken);
        }

        // استخدام Distributed Lock لمنع الـ Stampede عبر عدة سيرفرات
        return await _distributedLock.ExecuteWithLockAsync(cacheKey, TimeSpan.FromSeconds(10), async () =>
        {
            var dbData = await factory(cancellationToken);

            if (dbData is not null)
            {
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromMinutes(5)
                };

                // تحويل البيانات لـ bytes باستخدام MessagePack
                var serializedData = MessagePackSerializer.Serialize(dbData, cancellationToken: cancellationToken);

                _ = Task.Run(async () =>
                {
                    try { await _distributedCache.SetAsync(cacheKey, serializedData, options); }
                    catch { /* Log */ }
                });
            }

            return dbData;
        });
    }

    public async Task RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _distributedCache.RemoveAsync(cacheKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate cache key: {Key}", cacheKey);
        }
    }
}