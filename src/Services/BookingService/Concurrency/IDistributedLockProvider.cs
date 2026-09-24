namespace BookingService.Concurrency;

public interface IDistributedLockProvider
{
    /// <summary>Attempts to acquire a lock; returns a release token on success, or null if already held.</summary>
    Task<string?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);
    Task ReleaseAsync(string key, string releaseToken, CancellationToken ct = default);
}
