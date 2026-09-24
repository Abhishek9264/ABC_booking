using BookingService;
using BookingService.Concurrency;
using BookingService.Consumers;
using BookingService.Expiration;
using BookingService.Hubs;
using BookingService.Idempotency;
using BookingService.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;
using StackExchange.Redis;
using Tatkal.Authentication;
using Tatkal.Messaging;
using Tatkal.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddTatkalObservability(builder.Configuration, "booking-service");

builder.Services.AddDbContext<BookingDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"] ?? "redis:6379"));

// ---- Seat concurrency strategy (ADR-004) ----
// Configurable via SeatLocking:Strategy = Optimistic | DbTransaction | RedisAssisted
// so the three approaches can be A/B'd against each other in load tests
// without a code change (see load-tests/ and docs/benchmarks).
builder.Services.AddScoped<OptimisticConcurrencySeatLockStrategy>();
builder.Services.AddScoped<DbTransactionSeatLockStrategy>();
builder.Services.AddSingleton<IDistributedLockProvider, RedisDistributedLockProvider>();
builder.Services.AddScoped<ISeatLockStrategy>(sp =>
{
    var strategyName = builder.Configuration["SeatLocking:Strategy"] ?? "RedisAssisted";
    return strategyName switch
    {
        "Optimistic" => sp.GetRequiredService<OptimisticConcurrencySeatLockStrategy>(),
        "DbTransaction" => sp.GetRequiredService<DbTransactionSeatLockStrategy>(),
        "RedisAssisted" => new RedisAssistedSeatLockStrategy(
            sp.GetRequiredService<IDistributedLockProvider>(),
            sp.GetRequiredService<OptimisticConcurrencySeatLockStrategy>(),
            sp.GetRequiredService<ILogger<RedisAssistedSeatLockStrategy>>()),
        _ => throw new InvalidOperationException($"Unknown SeatLocking:Strategy '{strategyName}'")
    };
});

builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
builder.Services.AddScoped<IBookingNotifier, BookingNotifier>();

builder.Services.AddTatkalMessaging(builder.Configuration, x =>
{
    x.AddConsumer<BookingCreatedConsumer>();
    x.AddConsumer<PaymentSucceededConsumer>();
    x.AddConsumer<PaymentFailedConsumer>();
});

builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddHostedService<BookingExpirationSweeperService>();

builder.Services.AddSignalR();
builder.Services.AddHttpClient();

builder.Services.AddTatkalJwtAuthentication(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres")!)
    .AddRedis(builder.Configuration["Redis:ConnectionString"] ?? "redis:6379")
    .AddRabbitMQ(rabbitConnectionString:
        $"amqp://{builder.Configuration["RabbitMq:Username"]}:{builder.Configuration["RabbitMq:Password"]}@{builder.Configuration["RabbitMq:Host"]}:5672");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseTatkalCorrelationId();
app.UseAuthentication();
app.UseAuthorization();

app.MapTatkalMetrics();
app.MapHealthChecks("/health");
app.MapControllers();
app.MapHub<BookingStatusHub>("/hubs/booking-status");

app.Run();
