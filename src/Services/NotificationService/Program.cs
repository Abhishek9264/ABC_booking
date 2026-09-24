using NotificationService.Consumers;
using Serilog;
using Tatkal.Messaging;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext());

builder.Services.AddTatkalMessaging(builder.Configuration, x =>
{
    x.AddConsumer<NotifyUserConsumer>();
});

builder.Services.AddControllers();
builder.Services.AddHealthChecks()
    .AddRabbitMQ(rabbitConnectionString:
        $"amqp://{builder.Configuration["RabbitMq:Username"]}:{builder.Configuration["RabbitMq:Password"]}@{builder.Configuration["RabbitMq:Host"]}:5672");

var app = builder.Build();
app.MapHealthChecks("/health");
app.MapControllers();
app.Run();
