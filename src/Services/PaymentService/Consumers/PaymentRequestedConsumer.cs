using MassTransit;
using Microsoft.EntityFrameworkCore;
using PaymentService.Domain;
using Tatkal.Contracts;

namespace PaymentService.Consumers;

/// <summary>
/// Simulated payment gateway integration (spec section 12). Randomly
/// produces Success / Failed / Timeout / "network error" outcomes so the
/// rest of the system (compensation in BookingService, retry policies)
/// has something realistic to react to. NOT a real payment integration —
/// clearly a simulation, as required by spec section 10's sibling rule for
/// bot detection ("do not make [it] claim to be production-grade").
/// </summary>
public class PaymentRequestedConsumer(PaymentDbContext db, IPublishEndpoint publishEndpoint, ILogger<PaymentRequestedConsumer> logger) : IConsumer<PaymentRequested>
{
    private static readonly Random Rng = new();

    public async Task Consume(ConsumeContext<PaymentRequested> context)
    {
        var msg = context.Message;

        // Idempotent-consumer guard: if this booking already has a payment
        // attempt in flight or completed, don't create a second one
        // (handles RabbitMQ at-least-once redelivery of PaymentRequested).
        var existing = await db.Payments.FirstOrDefaultAsync(p => p.BookingId == msg.BookingId, context.CancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Payment already exists for booking {BookingId}, status {Status} — skipping duplicate PaymentRequested", msg.BookingId, existing.Status);
            return;
        }

        var payment = new Payment { BookingId = msg.BookingId, UserId = msg.UserId, Amount = msg.Amount, Status = PaymentStatus.Processing };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(context.CancellationToken);

        // Simulated gateway latency + outcome distribution: 80% success,
        // 12% failed, 5% timeout, 3% "network error" (treated as failed
        // with a distinct reason for observability purposes).
        await Task.Delay(TimeSpan.FromMilliseconds(Rng.Next(50, 300)), context.CancellationToken);
        var roll = Rng.NextDouble();

        var correlationId = context.CorrelationId ?? Guid.NewGuid();

        if (roll < 0.80)
        {
            payment.Status = PaymentStatus.Success;
            payment.ProviderTransactionId = $"SIM-{Guid.NewGuid():N}";
            payment.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(context.CancellationToken);

            await publishEndpoint.Publish(new PaymentSucceeded(msg.BookingId, payment.Id, payment.ProviderTransactionId, correlationId, DateTime.UtcNow), context.CancellationToken);
        }
        else
        {
            var reason = roll < 0.92 ? "Card declined" : roll < 0.97 ? "Gateway timeout" : "Simulated network error";
            payment.Status = roll < 0.97 && roll >= 0.92 ? PaymentStatus.Timeout : PaymentStatus.Failed;
            payment.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(context.CancellationToken);

            await publishEndpoint.Publish(new PaymentFailed(msg.BookingId, payment.Id, reason, correlationId, DateTime.UtcNow), context.CancellationToken);
        }
    }
}
