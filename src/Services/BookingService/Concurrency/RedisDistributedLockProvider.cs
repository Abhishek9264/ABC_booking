using StackExchange.Redis;

namespace BookingService.Concurrency;

/// <summary>
/// Single-node Redis lock (SET key token NX PX ttl) with a Lua-scripted
/// compare-and-delete release so a slow caller can never release a lock
/// someone else has since acquired. This is intentionally the *simple*
/// single-instance version, not full multi-node Redlock — sufficient here
/// because (per ADR-004 and Critical Rule #2) Redis is only ever used as
/// an admission-control optimization in front of the real correctness
/// guarantee in Postgres. If this lock is lost to a Redis failover, the
/// worst case is two requests both proceed to the DB — which is exactly
/// the scenario Approaches 1/2 already handle safely on their own.
/// </summary>
public class RedisDistributedLockProvider(IConnectionMultiplexer redis) : IDistributedLockProvider
{
    private const string ReleaseScript = @"
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end";

    public async Task<string?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");
        var acquired = await db.StringSetAsync(key, token, ttl, When.NotExists);
        return acquired ? token : null;
    }

    public async Task ReleaseAsync(string key, string releaseToken, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await db.ScriptEvaluateAsync(ReleaseScript, new RedisKey[] { key }, new RedisValue[] { releaseToken });
    }
}
