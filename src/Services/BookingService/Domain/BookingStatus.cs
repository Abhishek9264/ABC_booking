namespace BookingService.Domain;

/// <summary>
/// Every state a booking can be in. Transitions are enforced centrally
/// by <see cref="BookingStateMachine"/> — nothing else should mutate
/// this value directly.
/// </summary>
public enum BookingStatus
{
    Initiated,
    Queued,
    Processing,
    SeatLocked,
    PaymentPending,
    Confirmed,
    PaymentFailed,
    Expired,
    Cancelled,
    Failed
}
