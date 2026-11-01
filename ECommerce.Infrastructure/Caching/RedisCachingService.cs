using System.Text.Json;
using ECommerce.Core.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Polly;

namespace ECommerce.Infrastructure.Caching;

public class RedisCachingService : ICachingService
{
    private readonly IDistributedCache _distributedCache;
    private readonly SingleFlightService _singleFlight;
    private readonly ResiliencePipeline<byte[]> _resiliencePipeline;
    private readonly ILogger<RedisCachingService> _logger;

    public RedisCachingService(
        IDistributedCache distributedCache,
        SingleFlightService singleFlight,
        ResiliencePipeline<byte[]> resiliencePipeline,
        ILogger<RedisCachingService> logger)
    {
        _distributedCache = distributedCache;
        _singleFlight = singleFlight;
        _resiliencePipeline = resiliencePipeline;
        _logger = logger;
    }

    public async Task<T?> GetOrSetAsync<T>(
        string cacheKey,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        //byte[]? cachedBytes = null;
        //try
        //{
        //    // 1. قراءة من الكاش مع Timeout + Circuit Breaker (Polly)
        //    cachedBytes = await _resiliencePipeline.ExecuteAsync(
        //        async token => await _distributedCache.GetAsync(cacheKey, token),
        //        cancellationToken);
        //}
        //catch (Exception ex)
        //{
        //    _logger.LogWarning(ex, "Redis cache is unavailable or timed out. Falling back to Database.");
        //}
        byte[]? cachedBytes = null;
        try
        {
            // إخبار الـ Pipeline أن النتيجة قد تكون byte[]? من خلال تحديد النوع صراحةً
            cachedBytes = await _resiliencePipeline.ExecuteAsync<byte[]?>(
                async token => await _distributedCache.GetAsync(cacheKey, token),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis cache is unavailable or timed out. Falling back to Database.");
        }


        if (cachedBytes is not null)
        {
            return JsonSerializer.Deserialize<T>(cachedBytes);
        }

        // 2. استخدام Single Flight لمنع الـ Stampede Problem
        return await _singleFlight.ExecuteAsync(cacheKey, async () =>
        {
            var dbData = await factory(cancellationToken);

            if (dbData is not null)
            {
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromMinutes(5)
                };

                var serializedData = JsonSerializer.SerializeToUtf8Bytes(dbData);

                // حفظ غير متزامن لا يعطل الطلب لو حصلت مشكلة طارئة في Redis
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _distributedCache.SetAsync(cacheKey, serializedData, options);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to write back to cache for key: {Key}", cacheKey);
                    }
                });
            }

            return dbData;
        });
    }

    public async Task RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        // Command-Based Invalidation
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