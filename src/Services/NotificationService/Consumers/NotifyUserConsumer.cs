using MassTransit;
using Tatkal.Contracts;

namespace NotificationService.Consumers;

/// <summary>
/// Simulated notification delivery — logs what WOULD be sent (email/SMS/
/// push) rather than integrating a real provider, which is out of scope
/// for this portfolio project. The interesting part is that this consumer
/// exists at all: notifications are decoupled from the booking/payment
/// flow entirely via the queue, so a slow or failing notification provider
/// can never block a booking from confirming.
/// </summary>
public class NotifyUserConsumer(ILogger<NotifyUserConsumer> logger) : IConsumer<NotifyUser>
{
    public Task Consume(ConsumeContext<NotifyUser> context)
    {
        var msg = context.Message;
        logger.LogInformation("[SIMULATED {Channel} notification] template={Template} user={UserId} data={Data}",
            msg.Channel, msg.TemplateKey, msg.UserId, string.Join(",", msg.Data.Select(kv => $"{kv.Key}={kv.Value}")));
        return Task.CompletedTask;
    }
}
