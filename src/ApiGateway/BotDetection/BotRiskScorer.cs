using StackExchange.Redis;

namespace ApiGateway.BotDetection;

public enum RiskTier { Normal, Suspicious, HighRisk }
public record RiskAssessment(int Score, RiskTier Tier);

/// <summary>
/// Spec sections 10-11: a SIMULATED risk score, explicitly not a
/// production fraud-detection system. Signals used, each contributing
/// points to a 0-100 score:
///   - request burst: many requests from the same fingerprint in a short window
///   - repeated failures: prior 4xx responses from this fingerprint
///   - missing client metadata: no User-Agent / no fingerprint header at all
/// 0-30 Normal (allow), 31-70 Suspicious (would show a CAPTCHA challenge —
/// see CaptchaController), 71-100 High risk (temporarily blocked).
/// </summary>
public class BotRiskScorer(IConnectionMultiplexer redis)
{
    public async Task<RiskAssessment> AssessAsync(HttpContext context)
    {
        var fingerprint = context.Request.Headers["X-Device-Fingerprint"].FirstOrDefault();
        var userAgent = context.Request.Headers.UserAgent.FirstOrDefault();
        var db = redis.GetDatabase();

        int score = 0;

        if (string.IsNullOrWhiteSpace(userAgent)) score += 15;
        if (string.IsNullOrWhiteSpace(fingerprint)) score += 15;

        var key = fingerprint is not null ? $"botrisk:burst:{fingerprint}" : $"botrisk:burst:ip:{context.Connection.RemoteIpAddress}";
        var burstCount = await db.StringIncrementAsync(key);
        if (burstCount == 1) await db.KeyExpireAsync(key, TimeSpan.FromSeconds(10));
        if (burstCount > 20) score += 40;      // >20 req/10s from one fingerprint is unusual for a human
        else if (burstCount > 10) score += 20;

        var failureKey = fingerprint is not null ? $"botrisk:failures:{fingerprint}" : $"botrisk:failures:ip:{context.Connection.RemoteIpAddress}";
        var recentFailures = await db.StringGetAsync(failureKey);
        if (recentFailures.HasValue && int.TryParse(recentFailures, out var failures) && failures > 5) score += 30;

        score = Math.Min(score, 100);
        var tier = score <= 30 ? RiskTier.Normal : score <= 70 ? RiskTier.Suspicious : RiskTier.HighRisk;
        return new RiskAssessment(score, tier);
    }

    public async Task RecordFailureAsync(HttpContext context)
    {
        var fingerprint = context.Request.Headers["X-Device-Fingerprint"].FirstOrDefault();
        var key = fingerprint is not null ? $"botrisk:failures:{fingerprint}" : $"botrisk:failures:ip:{context.Connection.RemoteIpAddress}";
        var db = redis.GetDatabase();
        await db.StringIncrementAsync(key);
        await db.KeyExpireAsync(key, TimeSpan.FromMinutes(10));
    }
}
