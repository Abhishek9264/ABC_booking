using System.Text.Json;

namespace BookingService.Outbox;

/// <summary>
/// Call this INSIDE the same DbContext SaveChanges call that writes the
/// business row (e.g. Booking.MoveTo(Confirmed)) — both go in one
/// transaction, so a crash between "booking confirmed in DB" and "event
/// published to RabbitMQ" can never happen (spec section 14 / ADR-006).
/// The actual publish happens later, out-of-band, in OutboxPublisherService.
/// </summary>
public static class OutboxWriter
{
    public static OutboxMessage Enqueue<T>(BookingDbContext db, T @event, Guid correlationId) where T : notnull
    {
        var message = new OutboxMessage
        {
            Type = typeof(T).Name,
            Payload = JsonSerializer.Serialize(@event),
            CorrelationId = correlationId
        };
        db.OutboxMessages.Add(message);
        return message;
    }
}
