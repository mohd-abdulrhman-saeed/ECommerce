using StackExchange.Redis;

namespace ECommerce.Infrastructure.Caching;

public class DistributedLockService
{
    private readonly IConnectionMultiplexer _redis;

    public DistributedLockService(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<T?> ExecuteWithLockAsync<T>(string key, TimeSpan expiry, Func<Task<T>> action)
    {
        var db = _redis.GetDatabase();
        var lockKey = $"lock:{key}";
        var lockValue = Environment.MachineName;

        bool acquired = await db.LockTakeAsync(lockKey, lockValue, expiry);

        if (!acquired)
        {
            await Task.Delay(50);
            return default;
        }

        try
        {
            return await action();
        }
        finally
        {
            await db.LockReleaseAsync(lockKey, lockValue);
        }
    }
}