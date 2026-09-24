using StackExchange.Redis;

namespace ApiGateway.RateLimiting;

public enum RateLimitStrategy { FixedWindow, SlidingWindow, TokenBucket }

public record RateLimitRule(string KeyPrefix, int Limit, TimeSpan Window, RateLimitStrategy Strategy);

public record RateLimitDecision(bool Allowed, int Remaining, TimeSpan RetryAfter);

/// <summary>
/// Spec section 9: distributed rate limiting that works correctly across
/// multiple API Gateway instances, because the counters live in Redis, not
/// in a per-instance dictionary. Implements all three strategies the spec
/// asks for so they can be compared directly:
///
/// - FixedWindow: cheapest (one INCR + one EXPIRE), but allows up to 2x
///   the limit right at a window boundary (burst at :59/:00).
/// - SlidingWindow: a Redis sorted set of request timestamps, trimmed to
///   the window on every call — accurate, but O(log N) per request and
///   more Redis memory per key.
/// - TokenBucket: allows short bursts up to the bucket size while still
///   enforcing a steady average rate — closest to how real Tatkal traffic
///   actually behaves (bursty, not uniform).
///
/// All three are implemented as single Lua scripts so the check-and-
/// increment is atomic even under concurrent requests hitting the same key.
/// </summary>
public class RedisRateLimiter(IConnectionMultiplexer redis)
{
    private const string FixedWindowScript = @"
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[2]) end
        local ttl = redis.call('PTTL', KEYS[1])
        if current > tonumber(ARGV[1]) then return {0, 0, ttl} end
        return {1, tonumber(ARGV[1]) - current, ttl}";

    private const string SlidingWindowScript = @"
        local now = tonumber(ARGV[3])
        local windowMs = tonumber(ARGV[2])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], 0, now - windowMs)
        local count = redis.call('ZCARD', KEYS[1])
        if count >= tonumber(ARGV[1]) then
            local oldest = redis.call('ZRANGE', KEYS[1], 0, 0, 'WITHSCORES')
            local retryMs = windowMs
            if #oldest > 0 then retryMs = windowMs - (now - tonumber(oldest[2])) end
            return {0, 0, retryMs}
        end
        redis.call('ZADD', KEYS[1], now, now .. '-' .. ARGV[4])
        redis.call('PEXPIRE', KEYS[1], windowMs)
        return {1, tonumber(ARGV[1]) - count - 1, windowMs}";

    private const string TokenBucketScript = @"
        local capacity = tonumber(ARGV[1])
        local refillPerMs = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])
        local bucket = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
        local tokens = tonumber(bucket[1])
        local ts = tonumber(bucket[2])
        if tokens == nil then tokens = capacity; ts = now end
        local elapsed = math.max(0, now - ts)
        tokens = math.min(capacity, tokens + elapsed * refillPerMs)
        if tokens < 1 then
            redis.call('HMSET', KEYS[1], 'tokens', tokens, 'ts', now)
            redis.call('PEXPIRE', KEYS[1], 60000)
            return {0, 0, math.ceil((1 - tokens) / refillPerMs)}
        end
        tokens = tokens - 1
        redis.call('HMSET', KEYS[1], 'tokens', tokens, 'ts', now)
        redis.call('PEXPIRE', KEYS[1], 60000)
        return {1, math.floor(tokens), 0}";

    public async Task<RateLimitDecision> CheckAsync(RateLimitRule rule, string identity)
    {
        var db = redis.GetDatabase();
        var key = $"ratelimit:{rule.KeyPrefix}:{identity}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        RedisResult result = rule.Strategy switch
        {
            RateLimitStrategy.FixedWindow => await db.ScriptEvaluateAsync(FixedWindowScript,
                new RedisKey[] { key }, new RedisValue[] { rule.Limit, (int)rule.Window.TotalMilliseconds }),

            RateLimitStrategy.SlidingWindow => await db.ScriptEvaluateAsync(SlidingWindowScript,
                new RedisKey[] { key }, new RedisValue[] { rule.Limit, (int)rule.Window.TotalMilliseconds, now, Guid.NewGuid().ToString("N")[..8] }),

            RateLimitStrategy.TokenBucket => await db.ScriptEvaluateAsync(TokenBucketScript,
                new RedisKey[] { key }, new RedisValue[] { rule.Limit, rule.Limit / (double)rule.Window.TotalMilliseconds, now }),

            _ => throw new ArgumentOutOfRangeException(nameof(rule))
        };

        var values = (RedisValue[])result!;
        return new RateLimitDecision(
            Allowed: (long)values[0] == 1,
            Remaining: (int)(long)values[1],
            RetryAfter: TimeSpan.FromMilliseconds((long)values[2]));
    }
}
