using ECommerce.Core.Interfaces;
using ECommerce.Infrastructure.Caching;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

var builder = WebApplication.CreateBuilder(args);

// تأكد من ضبط الـ SwaggerGen مع تحديد الإصدار صراحة لتجنب مشكلة الـ Version field
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "ECommerce.API",
        Version = "v1"
    });
});

// 1. إضافة StackExchange Redis Distributed Cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
});

// 2. إعداد Polly Pipeline (Timeout: 100ms + Circuit Breaker)
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


// 3. حقن التبعيات
builder.Services.AddSingleton<SingleFlightService>();
builder.Services.AddScoped<ICachingService, RedisCachingService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

//app.UseSwagger();
//app.UseSwaggerUI();
//app.UseSwagger();
//app.UseSwaggerUI(c =>
//{
//    c.SwaggerEndpoint("/swagger/v1/swagger.json", "ECommerce.API v1");
//});
// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ECommerce.API v1");
        //c.RoutePrefix = string.Empty; // يجعل الـ Swagger يفتح مباشرة عند تشغيل المشروع على الـ Root URL
    });
}

app.MapControllers();

app.Run();