using ECommerce.API.Services;
using ECommerce.Core.Interfaces;
using ECommerce.Infrastructure.Caching;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "ECommerce API", Version = "v1" });
});

// Redis Multiplexer
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379"));

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
});

//builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
//    ConnectionMultiplexer.Connect("127.0.0.1:6379,abortConnect=false,connectTimeout=10000,syncTimeout=10000"));

//builder.Services.AddStackExchangeRedisCache(options =>
//{
//    options.Configuration = "127.0.0.1:6379,abortConnect=false,connectTimeout=10000,syncTimeout=10000";
//});
// Polly Pipeline
builder.Services.AddSingleton<ResiliencePipeline<byte[]>>(sp =>
{
    return new ResiliencePipelineBuilder<byte[]>()
        .AddTimeout(TimeSpan.FromMilliseconds(100))
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions<byte[]>()
        {
            FailureRatio = 0.8,
            SamplingDuration = TimeSpan.FromSeconds(10),
            MinimumThroughput = 3,
            BreakDuration = TimeSpan.FromSeconds(5),
            ShouldHandle = new PredicateBuilder<byte[]>().Handle<Exception>()
        })
        .Build();
});


// Services & Distributed Locks & Cache Warming
builder.Services.AddSingleton<DistributedLockService>();
builder.Services.AddScoped<ICachingService, RedisCachingService>();
builder.Services.AddHostedService<CacheWarmingService>();

// OpenTelemetry Metrics
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddMeter("ECommerce.Caching")
        .AddPrometheusExporter());

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ECommerce API v1");
    c.RoutePrefix = string.Empty;
});

// تفعيل نقطة نهاية Prometheus للـ Metrics
app.MapPrometheusScrapingEndpoint();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();