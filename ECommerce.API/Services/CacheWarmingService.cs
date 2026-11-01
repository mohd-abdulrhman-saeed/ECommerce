using ECommerce.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ECommerce.API.Services;

public class CacheWarmingService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CacheWarmingService> _logger;

    public CacheWarmingService(IServiceScopeFactory scopeFactory, ILogger<CacheWarmingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Warming up cache for popular products...");

        // فتح Scope مؤقت لأن الـ HostedService يعتبر Singleton
        using var scope = _scopeFactory.CreateScope();
        var cachingService = scope.ServiceProvider.GetRequiredService<ICachingService>();

        var popularIds = new[] { 1, 2, 3, 5 };
        foreach (var id in popularIds)
        {
            string key = $"product-{id}";
            await cachingService.GetOrSetAsync(key, async (token) =>
            {
                await Task.Delay(50, token);
                return new { Id = id, Name = $"Preloaded Product {id}", Price = 199.99m };
            }, TimeSpan.FromMinutes(30), cancellationToken);
        }

        _logger.LogInformation("Cache warming finished.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}