using BookingService.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Consumers;

/// <summary>
/// Shared consumer-idempotency check (Critical Rule #5). Every consumer
/// calls this FIRST, before any side effect, and skips processing if the
/// transport message id has already been recorded — RabbitMQ only
/// guarantees at-least-once delivery, so redelivery after a crash or a
/// visibility timeout must be a safe no-op.
/// </summary>
public static class InboxGuard
{
    public static async Task<bool> AlreadyProcessedAsync(BookingDbContext db, Guid messageId, string consumerName, CancellationToken ct)
    {
        var seen = await db.InboxMessages.FindAsync([messageId, consumerName], ct);
        if (seen is not null) return true;

        db.InboxMessages.Add(new InboxMessage { MessageId = messageId, ConsumerName = consumerName });
        return false;
    }
}
