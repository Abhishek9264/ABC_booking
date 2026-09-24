namespace BookingService.Outbox;

/// <summary>
/// Written in the SAME database transaction as the business row it
/// describes (e.g. the Booking insert/update). A background publisher
/// (see OutboxPublisherService) polls unprocessed rows and pushes them to
/// RabbitMQ, so "DB commit succeeded, broker publish failed" can never
/// silently lose an event (spec section 14 / ADR-006).
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = default!;      // e.g. "BookingConfirmed"
    public string Payload { get; set; } = default!;   // JSON-serialized event
    public Guid CorrelationId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
    public int RetryCount { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }
    public Guid? ClaimedBy { get; set; }
    public string Status { get; set; } = "Pending";   // Pending | Processing | Published | Failed
}
