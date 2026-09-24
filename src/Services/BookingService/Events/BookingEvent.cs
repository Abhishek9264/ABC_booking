namespace BookingService.Events;

/// <summary>
/// Append-only event stream for a single booking's lifecycle (spec section
/// 18). The Bookings table row is a *projection* over this stream, not the
/// other way around, for the events that matter for audit/replay — see
/// docs/architecture/README.md for why event sourcing is scoped to just
/// the booking lifecycle rather than forced onto every service.
/// </summary>
public class BookingEvent
{
    public long SequenceNumber { get; set; } // identity, PK
    public Guid BookingId { get; set; }
    public string EventType { get; set; } = default!; // "BookingCreated", "SeatLocked", ...
    public string PayloadJson { get; set; } = default!;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
