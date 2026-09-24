namespace BookingService.Idempotency;

/// <summary>
/// Maps an `Idempotency-Key` header to the booking it produced, plus a hash
/// of the original request body so a key reused with a *different* payload
/// can be rejected instead of silently returning the wrong booking.
/// See spec section 7 and Critical Engineering Rule #8 (duplicate booking).
/// </summary>
public class IdempotencyRecord
{
    public string IdempotencyKey { get; set; } = default!; // PK
    public Guid UserId { get; set; }
    public string RequestHash { get; set; } = default!;
    public Guid? BookingId { get; set; }
    public string Status { get; set; } = "Pending"; // Pending | Completed | Failed
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(24);
}
