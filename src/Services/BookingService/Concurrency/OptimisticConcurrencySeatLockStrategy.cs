using BookingService.Domain;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Concurrency;

/// <summary>
/// Approach 1 (ADR-004): just try the insert and let the database's unique
/// index be the tiebreaker. No pre-check, no explicit locking — every one
/// of the 500 competing requests races straight to Postgres and 499 of
/// them get a unique-violation on INSERT.
///
/// Advantages: simplest possible code; zero extra infrastructure; correct
/// by construction because it relies on the same unique index every other
/// approach ultimately depends on anyway.
/// Disadvantages: under heavy contention on one seat, ALL competitors pay
/// a full DB round trip before finding out they lost — wasteful at
/// Tatkal-spike scale (that's exactly why Redis pre-filtering, Approach 3,
/// exists as a layer in front of this, not a replacement for it).
/// </summary>
public class OptimisticConcurrencySeatLockStrategy(BookingDbContext db) : ISeatLockStrategy
{
    public Task<SeatLockResult> TryLockSeatAsync(SeatLockRequest request, CancellationToken ct = default)
    {
        // The caller owns the transaction. Do not call SaveChanges here: the
        // seat reservation, booking state, inbox record and outbox messages
        // must commit atomically. The database unique index is the final
        // concurrency guarantee.
        var reservation = new SeatReservation
        {
            ScheduleId = request.ScheduleId,
            CoachId = request.CoachId,
            SeatId = request.SeatId,
            BookingId = request.BookingId,
            UserId = request.UserId,
            Status = SeatReservationStatus.Locked,
            ExpiresAtUtc = DateTime.UtcNow.Add(request.LockDuration)
        };

        db.SeatReservations.Add(reservation);
        return Task.FromResult(new SeatLockResult(
            SeatLockOutcome.Locked, reservation.Id, reservation.ExpiresAtUtc, null));
    }
}
