using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tatkal.Messaging;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires MassTransit + RabbitMQ with the retry/prefetch/DLQ conventions
    /// used across every service (spec sections 8, 15, 16):
    /// - Exponential backoff + jitter, bounded retry count (Critical Rule #7: no infinite retries).
    /// - Failed-after-retries messages land in RabbitMQ's built-in
    ///   `<queue>_error` queue automatically (MassTransit's default fault
    ///   pipeline), which is what the admin dead-letter API in Phase 16
    ///   inspects.
    /// - Bounded prefetch so one worker instance can't be overwhelmed —
    ///   this is the actual mechanism, alongside the queue itself, that
    ///   protects Postgres from a Tatkal-opening spike.
    /// `configureConsumers` lets each service register only the consumers
    /// it owns.
    /// </summary>
    public static IServiceCollection AddTatkalMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureConsumers = null)
    {
        var section = configuration.GetSection("RabbitMq");
        services.Configure<MessagingOptions>(section);
        var options = section.Get<MessagingOptions>() ?? new MessagingOptions();

        services.AddMassTransit(x =>
        {
            configureConsumers?.Invoke(x);

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(options.Host, "/", h =>
                {
                    h.Username(options.Username);
                    h.Password(options.Password);
                });

                cfg.PrefetchCount = options.PrefetchCount;

                cfg.UseMessageRetry(r => r.Exponential(
                    retryLimit: options.MaxRetryCount,
                    minInterval: TimeSpan.FromMilliseconds(100),
                    maxInterval: TimeSpan.FromSeconds(5),
                    intervalDelta: TimeSpan.FromMilliseconds(200)));
                // Note: MassTransit's Exponential retry already jitters
                // internally via the interval delta above (spec section 15).

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
