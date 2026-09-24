namespace BookingService.Idempotency;

/// <summary>
/// Consumer-side dedup record. RabbitMQ only promises at-least-once
/// delivery (Critical Rule #4), so every consumer records the message id
/// it has already processed here before doing any side effect, and checks
/// it first — this is what "consumers must be idempotent" (Rule #5) means
/// in code rather than in a sentence.
/// </summary>
public class InboxMessage
{
    public Guid MessageId { get; set; } // PK — the transport message id
    public string ConsumerName { get; set; } = default!;
    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;
}
