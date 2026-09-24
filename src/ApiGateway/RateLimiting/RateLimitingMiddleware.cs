namespace ApiGateway.RateLimiting;

/// <summary>
/// Applies per-IP, per-user (when authenticated), and per-API-route limits
/// (spec section 9). Per-device/fingerprint limiting is handled by
/// BotRiskMiddleware instead, since a fingerprint is a risk *signal* more
/// than a hard quota. Checked in order from cheapest/broadest to
/// narrowest so an obviously-abusive IP is rejected before doing any
/// per-route bookkeeping.
/// </summary>
public class RateLimitingMiddleware(RequestDelegate next, RedisRateLimiter limiter, ILogger<RateLimitingMiddleware> logger)
{
    private static readonly RateLimitRule PerIpRule = new("ip", Limit: 100, Window: TimeSpan.FromMinutes(1), RateLimitStrategy.SlidingWindow);
    private static readonly RateLimitRule PerUserRule = new("user", Limit: 60, Window: TimeSpan.FromMinutes(1), RateLimitStrategy.TokenBucket);
    private static readonly RateLimitRule PerRouteRule = new("route", Limit: 2000, Window: TimeSpan.FromSeconds(10), RateLimitStrategy.FixedWindow);

    public async Task InvokeAsync(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var route = context.Request.Path.Value ?? "/";
        var userId = context.User?.FindFirst("sub")?.Value;

        foreach (var (rule, identity) in new[]
        {
            (PerIpRule, ip),
            (PerRouteRule, route),
        })
        {
            var decision = await limiter.CheckAsync(rule, identity);
            if (!decision.Allowed)
            {
                await Reject(context, decision, rule.KeyPrefix);
                return;
            }
        }

        if (userId is not null)
        {
            var decision = await limiter.CheckAsync(PerUserRule, userId);
            if (!decision.Allowed)
            {
                await Reject(context, decision, PerUserRule.KeyPrefix);
                return;
            }
        }

        await next(context);
    }

    private async Task Reject(HttpContext context, RateLimitDecision decision, string scope)
    {
        logger.LogInformation("Rate limit exceeded ({Scope}) for {Path} from {Ip}", scope, context.Request.Path, context.Connection.RemoteIpAddress);
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = ((int)Math.Ceiling(decision.RetryAfter.TotalSeconds)).ToString();
        await context.Response.WriteAsJsonAsync(new { error = "rate_limited", scope, retryAfterSeconds = decision.RetryAfter.TotalSeconds });
    }
}
