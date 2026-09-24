using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tatkal.Contracts;

namespace BookingService.Outbox;

/// <summary>
/// Publishes transactional outbox rows with a short claim lease. Multiple
/// Booking Service instances can run this worker concurrently: a row is
/// claimed with a conditional UPDATE before publishing, and stale claims
/// are returned to Pending after a crash.
/// </summary>
public class OutboxPublisherService(
    IServiceScopeFactory scopeFactory,
    ILogger<OutboxPublisherService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);
    private static readonly Guid InstanceId = Guid.NewGuid();

    private static readonly Dictionary<string, Type> EventTypeMap = new()
    {
        [nameof(BookingCreated)] = typeof(BookingCreated),
        [nameof(BookingQueued)] = typeof(BookingQueued),
        [nameof(SeatLocked)] = typeof(SeatLocked),
        [nameof(SeatLockFailed)] = typeof(SeatLockFailed),
        [nameof(PaymentRequested)] = typeof(PaymentRequested),
        [nameof(SeatReleaseRequested)] = typeof(SeatReleaseRequested),
        [nameof(SeatReleased)] = typeof(SeatReleased),
        [nameof(BookingConfirmed)] = typeof(BookingConfirmed),
        [nameof(BookingExpired)] = typeof(BookingExpired),
        [nameof(BookingCancelled)] = typeof(BookingCancelled),
        [nameof(NotifyUser)] = typeof(NotifyUser),
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox publish loop failed; will retry next tick");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task PublishPendingBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var staleBefore = DateTime.UtcNow.Subtract(ClaimLease);

        // Recover messages claimed by an instance that died before publishing.
        await db.OutboxMessages
            .Where(m => m.Status == "Processing" &&
                        m.ClaimedAtUtc != null &&
                        m.ClaimedAtUtc < staleBefore)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(m => m.Status, "Pending")
                .SetProperty(m => m.ClaimedAtUtc, (DateTime?)null)
                .SetProperty(m => m.ClaimedBy, (Guid?)null), ct);

        var candidates = await db.OutboxMessages
            .Where(m => m.Status == "Pending")
            .OrderBy(m => m.CreatedAtUtc)
            .Take(50)
            .ToListAsync(ct);

        foreach (var candidate in candidates)
        {
            // Only one publisher instance can win this conditional update.
            var claimed = await db.OutboxMessages
                .Where(m => m.Id == candidate.Id && m.Status == "Pending")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(m => m.Status, "Processing")
                    .SetProperty(m => m.ClaimedAtUtc, DateTime.UtcNow)
                    .SetProperty(m => m.ClaimedBy, InstanceId), ct);

            if (claimed != 1)
                continue;

            try
            {
                if (!EventTypeMap.TryGetValue(candidate.Type, out var clrType))
                {
                    logger.LogWarning(
                        "Unknown outbox message type {Type}; marking Failed",
                        candidate.Type);

                    await db.OutboxMessages
                        .Where(m => m.Id == candidate.Id && m.ClaimedBy == InstanceId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(m => m.Status, "Failed")
                            .SetProperty(m => m.ClaimedAtUtc, (DateTime?)null)
                            .SetProperty(m => m.ClaimedBy, (Guid?)null), ct);

                    continue;
                }

                var @event = JsonSerializer.Deserialize(candidate.Payload, clrType)
                    ?? throw new JsonException($"Could not deserialize outbox message {candidate.Id}.");

                await publishEndpoint.Publish(@event, clrType, ct);

                await db.OutboxMessages
                    .Where(m => m.Id == candidate.Id && m.ClaimedBy == InstanceId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(m => m.Status, "Published")
                        .SetProperty(m => m.ProcessedAtUtc, DateTime.UtcNow)
                        .SetProperty(m => m.ClaimedAtUtc, (DateTime?)null)
                        .SetProperty(m => m.ClaimedBy, (Guid?)null), ct);
            }
            catch (Exception ex)
            {
                var nextRetry = candidate.RetryCount + 1;
                logger.LogWarning(
                    ex,
                    "Failed to publish outbox message {Id} (attempt {Attempt})",
                    candidate.Id,
                    nextRetry);

                await db.OutboxMessages
                    .Where(m => m.Id == candidate.Id && m.ClaimedBy == InstanceId)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(m => m.RetryCount, nextRetry)
                        .SetProperty(m => m.Status, nextRetry >= 10 ? "Failed" : "Pending")
                        .SetProperty(m => m.ClaimedAtUtc, (DateTime?)null)
                        .SetProperty(m => m.ClaimedBy, (Guid?)null), ct);
            }
        }
    }
}
