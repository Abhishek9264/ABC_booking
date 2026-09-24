namespace BookingService.Concurrency;

public record SeatLockRequest(Guid ScheduleId, Guid CoachId, Guid SeatId, Guid BookingId, Guid UserId, TimeSpan LockDuration);

public enum SeatLockOutcome { Locked, SeatUnavailable, TransientFailure }

public record SeatLockResult(SeatLockOutcome Outcome, Guid? SeatReservationId, DateTime? ExpiresAtUtc, string? Reason);

/// <summary>
/// The three approaches compared in ADR-004. Whichever implementation is
/// registered in DI, the DB unique index on
/// (ScheduleId, CoachId, SeatId, Status) is what actually prevents two
/// users from getting the same seat — these strategies only differ in how
/// much wasted work happens before that constraint is hit.
/// </summary>
public interface ISeatLockStrategy
{
    Task<SeatLockResult> TryLockSeatAsync(SeatLockRequest request, CancellationToken ct = default);
}
