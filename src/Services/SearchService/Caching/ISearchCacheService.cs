namespace SearchService.Caching;

/// <summary>
/// Cache-aside with two tiers (spec section 20): a per-instance in-memory
/// L1 for the hottest keys, and a shared Redis L2 behind it so a miss on
/// one gateway-routed instance doesn't always fall all the way through to
/// Postgres. Search Service must NOT hit the booking database for every
/// search request (spec section 3) — this is the mechanism that enforces
/// that rule.
/// </summary>
public interface ISearchCacheService
{
    Task<T?> GetOrCreateAsync<T>(string key, TimeSpan l1Ttl, TimeSpan l2Ttl, Func<Task<T>> factory, CancellationToken ct = default) where T : class;
    Task InvalidateAsync(string key, CancellationToken ct = default);
}
