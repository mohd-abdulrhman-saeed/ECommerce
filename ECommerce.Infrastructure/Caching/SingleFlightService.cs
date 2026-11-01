using System.Collections.Concurrent;

namespace ECommerce.Infrastructure.Caching;

public class SingleFlightService
{
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _locks = new();

    public async Task<T?> ExecuteAsync<T>(string key, Func<Task<T>> action)
    {
        var lazyTask = _locks.GetOrAdd(key, k => new Lazy<Task<object?>>(async () =>
        {
            try
            {
                return await action();
            }
            finally
            {
                _locks.TryRemove(k, out _);
            }
        }));

        var result = await lazyTask.Value;
        return (T?)result;
    }
}