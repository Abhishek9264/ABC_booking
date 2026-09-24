namespace BookingService.Concurrency;

/// <summary>
/// Approach 3 (ADR-004): a short-lived Redis lock as an ADMISSION FILTER in
/// front of the real strategy. Most of the 499 losing requests in a
/// same-seat pile-up get rejected here — in memory, no DB round trip — and
/// only the request that wins the Redis race proceeds to actually write to
/// Postgres via the wrapped strategy. Postgres still enforces the final
/// guarantee (Critical Rule #2: Redis is never the source of truth for
/// ticket ownership), so if the Redis lock is ever wrong (failover,
/// expired early under clock drift), the wrapped strategy's unique index
/// still catches it — this class can only make correctness worse never,
/// only throughput better.
///
/// Advantages: best throughput under heavy same-seat contention — the
/// database only ever sees ~1-2 writers instead of hundreds.
/// Disadvantages: another moving part; a lock TTL that's too short can let
/// a slow winner get raced by a second Redis-lock holder before it
/// finishes (still safe, just means the DB does two round trips instead of
/// one).
/// </summary>
public class RedisAssistedSeatLockStrategy(
    IDistributedLockProvider lockProvider,
    ISeatLockStrategy inner, // typically OptimisticConcurrencySeatLockStrategy
    ILogger<RedisAssistedSeatLockStrategy> logger) : ISeatLockStrategy
{
    private static readonly TimeSpan LockAcquireTtl = TimeSpan.FromSeconds(10);

    public async Task<SeatLockResult> TryLockSeatAsync(SeatLockRequest request, CancellationToken ct = default)
    {
        var lockKey = $"seatlock:{request.ScheduleId}:{request.CoachId}:{request.SeatId}";
        var token = await lockProvider.TryAcquireAsync(lockKey, LockAcquireTtl, ct);

        if (token is null)
        {
            logger.LogInformation("Redis admission filter rejected booking {BookingId} for seat {SeatId} (already contended)",
                request.BookingId, request.SeatId);
            return new SeatLockResult(SeatLockOutcome.SeatUnavailable, null, null, "Seat currently being booked by another user.");
        }

        try
        {
            return await inner.TryLockSeatAsync(request, ct);
        }
        finally
        {
            await lockProvider.ReleaseAsync(lockKey, token, ct);
        }
    }
}
