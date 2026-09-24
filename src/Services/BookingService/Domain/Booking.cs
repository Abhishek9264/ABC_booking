namespace BookingService.Domain;

public class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ScheduleId { get; set; }
    public Guid CoachId { get; set; }
    public Guid SeatId { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Initiated;
    public decimal Amount { get; set; }
    public Guid? SeatReservationId { get; set; }
    public Guid? PaymentId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>Optimistic-concurrency token — see ADR-004 / Approach 1.</summary>
    public uint RowVersion { get; set; }

    public ICollection<BookingPassenger> Passengers { get; set; } = new List<BookingPassenger>();

    /// <summary>
    /// The only way a booking's status should ever change. Delegates to
    /// BookingStateMachine so illegal transitions throw instead of silently
    /// corrupting state.
    /// </summary>
    public void MoveTo(BookingStatus next)
    {
        Status = StateMachine.BookingStateMachine.Transition(Status, next);
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public class BookingPassenger
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public string FullName { get; set; } = default!;
    public int Age { get; set; }
    public string Gender { get; set; } = default!;
}

/// <summary>
/// The record that actually owns a seat for a schedule. The unique index on
/// (ScheduleId, CoachId, SeatId) filtered to Active rows is the real
/// correctness guarantee (see ADR-004) — everything else (Redis lock,
/// optimistic concurrency) is an optimization layered on top of this.
/// </summary>
public class SeatReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScheduleId { get; set; }
    public Guid CoachId { get; set; }
    public Guid SeatId { get; set; }
    public Guid BookingId { get; set; }
    public Guid UserId { get; set; }
    public SeatReservationStatus Status { get; set; } = SeatReservationStatus.Locked;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public uint RowVersion { get; set; }
}

public enum SeatReservationStatus { Locked, Confirmed, Released, Expired }
