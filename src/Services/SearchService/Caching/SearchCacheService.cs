using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace SearchService.Caching;

public class SearchCacheService(IMemoryCache l1, IDistributedCache l2, ILogger<SearchCacheService> logger) : ISearchCacheService
{
    public async Task<T?> GetOrCreateAsync<T>(string key, TimeSpan l1Ttl, TimeSpan l2Ttl, Func<Task<T>> factory, CancellationToken ct = default) where T : class
    {
        if (l1.TryGetValue(key, out T? cached))
            return cached;

        try
        {
            var fromL2 = await l2.GetStringAsync(key, ct);
            if (fromL2 is not null)
            {
                var value = JsonSerializer.Deserialize<T>(fromL2);
                l1.Set(key, value, l1Ttl);
                return value;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis L2 cache read failed for key {Key}; falling through to source", key);
        }

        var fresh = await factory();

        l1.Set(key, fresh, l1Ttl);
        try
        {
            await l2.SetStringAsync(key, JsonSerializer.Serialize(fresh),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = l2Ttl }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis L2 cache write failed for key {Key}", key);
        }

        return fresh;
    }

    public async Task InvalidateAsync(string key, CancellationToken ct = default)
    {
        l1.Remove(key);
        try { await l2.RemoveAsync(key, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "Redis L2 cache invalidation failed for key {Key}", key); }
    }
}
