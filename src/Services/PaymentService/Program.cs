using Microsoft.EntityFrameworkCore;
using PaymentService;
using PaymentService.Consumers;
using Serilog;
using Tatkal.Authentication;
using Tatkal.Messaging;
using Tatkal.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddTatkalObservability(builder.Configuration, "payment-service");

builder.Services.AddDbContext<PaymentDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddTatkalMessaging(builder.Configuration, x =>
{
    x.AddConsumer<PaymentRequestedConsumer>();
});

builder.Services.AddTatkalJwtAuthentication(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Postgres")!)
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

app.Run();
