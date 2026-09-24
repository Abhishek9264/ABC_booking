using BookingService.Domain;

namespace BookingService.StateMachine;

/// <summary>
/// Central authority on which <see cref="BookingStatus"/> transitions are
/// legal. Every state change in the Booking Service — whether triggered by
/// an API call, a queue consumer, a payment callback, or the expiration
/// sweeper — must go through <see cref="CanTransition"/> / <see cref="Transition"/>
/// rather than assigning the status directly. This is what makes
/// "do not allow invalid state transitions" (spec section 4) enforceable
/// instead of just documented.
/// </summary>
public static class BookingStateMachine
{
    // Happy path:
    //   Initiated -> Queued -> Processing -> SeatLocked -> PaymentPending -> Confirmed
    // Failure branches:
    //   SeatLocked      -> PaymentFailed  (payment rejected)
    //   PaymentPending  -> PaymentFailed
    //   SeatLocked      -> Expired        (lock timer elapses before payment)
    //   PaymentPending  -> Expired
    //   Initiated/Queued/Processing -> Cancelled (user- or system-initiated)
    //   * -> Failed (unrecoverable system error; terminal)
    private static readonly Dictionary<BookingStatus, BookingStatus[]> AllowedTransitions = new()
    {
        [BookingStatus.Initiated] = new[] { BookingStatus.Queued, BookingStatus.Cancelled, BookingStatus.Failed },
        [BookingStatus.Queued] = new[] { BookingStatus.Processing, BookingStatus.Cancelled, BookingStatus.Failed },
        [BookingStatus.Processing] = new[] { BookingStatus.SeatLocked, BookingStatus.Cancelled, BookingStatus.Failed },
        [BookingStatus.SeatLocked] = new[] { BookingStatus.PaymentPending, BookingStatus.PaymentFailed, BookingStatus.Expired, BookingStatus.Failed },
        [BookingStatus.PaymentPending] = new[] { BookingStatus.Confirmed, BookingStatus.PaymentFailed, BookingStatus.Expired, BookingStatus.Failed },
        [BookingStatus.PaymentFailed] = new[] { BookingStatus.Cancelled },
        [BookingStatus.Expired] = Array.Empty<BookingStatus>(),
        [BookingStatus.Confirmed] = Array.Empty<BookingStatus>(),
        [BookingStatus.Cancelled] = Array.Empty<BookingStatus>(),
        [BookingStatus.Failed] = Array.Empty<BookingStatus>(),
    };

    public static bool CanTransition(BookingStatus from, BookingStatus to) =>
        AllowedTransitions.TryGetValue(from, out var next) && next.Contains(to);

    /// <summary>
    /// Returns the new status, or throws <see cref="InvalidOperationException"/>
    /// if the transition is not legal. Callers should treat that exception as a
    /// bug (a caller attempting a transition it shouldn't), not an expected
    /// business outcome.
    /// </summary>
    public static BookingStatus Transition(BookingStatus from, BookingStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException(
                $"Illegal booking state transition: {from} -> {to}");
        }

        return to;
    }
}
