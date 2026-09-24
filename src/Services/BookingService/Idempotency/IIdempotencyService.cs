namespace BookingService.Idempotency;

public enum IdempotencyCheckResult { NewRequest, DuplicateSameBody, ConflictDifferentBody }

public record IdempotencyCheck(IdempotencyCheckResult Result, Guid? ExistingBookingId);

/// <summary>
/// Spec section 7: the same Idempotency-Key replayed with the same body
/// returns the original booking; replayed with a DIFFERENT body is a
/// client bug and gets rejected rather than silently processed.
/// </summary>
public interface IIdempotencyService
{
    Task<IdempotencyCheck> CheckAsync(string idempotencyKey, Guid userId, string requestBodyJson, CancellationToken ct = default);
    Task CompleteAsync(string idempotencyKey, Guid bookingId, CancellationToken ct = default);
}
