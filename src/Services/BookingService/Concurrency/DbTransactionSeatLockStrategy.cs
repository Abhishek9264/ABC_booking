using BookingService.Domain;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Concurrency;

/// <summary>
/// Database-pessimistic strategy. The caller owns the transaction. A
/// PostgreSQL transaction advisory lock serializes attempts for the same
/// logical seat; the partial unique index remains the final invariant.
/// </summary>
public class DbTransactionSeatLockStrategy(BookingDbContext db) : ISeatLockStrategy
{
    public async Task<SeatLockResult> TryLockSeatAsync(SeatLockRequest request, CancellationToken ct = default)
    {
        var lockKey = $"{request.ScheduleId:N}:{request.CoachId:N}:{request.SeatId:N}";

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0));", ct);

        var alreadyTaken = await db.SeatReservations.AnyAsync(r =>
            r.ScheduleId == request.ScheduleId &&
            r.CoachId == request.CoachId &&
            r.SeatId == request.SeatId &&
            (r.Status == SeatReservationStatus.Locked || r.Status == SeatReservationStatus.Confirmed), ct);

        if (alreadyTaken)
            return new SeatLockResult(SeatLockOutcome.SeatUnavailable, null, null, "Seat already reserved.");

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
        return new SeatLockResult(SeatLockOutcome.Locked, reservation.Id, reservation.ExpiresAtUtc, null);
    }
}
