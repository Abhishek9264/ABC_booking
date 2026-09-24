using ApiGateway.BotDetection;
using ApiGateway.RateLimiting;
using Serilog;
using StackExchange.Redis;
using Tatkal.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddTatkalObservability(builder.Configuration, "api-gateway");

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"] ?? "redis:6379"));
builder.Services.AddSingleton<RedisRateLimiter>();
builder.Services.AddSingleton<BotRiskScorer>();

builder.Services.AddControllers(); // for /captcha/*

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddHealthChecks();

var app = builder.Build();

// Correlation-id first, so every log line and downstream trace shares one id.
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString();
    context.Request.Headers["X-Correlation-Id"] = correlationId;
    context.Response.Headers["X-Correlation-Id"] = correlationId;
    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});

app.UseMiddleware<BotDetectionMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();

app.MapTatkalMetrics();
app.MapHealthChecks("/health");
app.MapControllers();
app.MapReverseProxy();

app.Run();
