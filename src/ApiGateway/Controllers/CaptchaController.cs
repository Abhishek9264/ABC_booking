using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace ApiGateway.Controllers;

public record CaptchaChallengeResponse(string ChallengeId, string Question);
public record CaptchaVerifyRequest(string ChallengeId, string Answer);

/// <summary>
/// Mock CAPTCHA (spec section 11): demonstrates WHERE a real CAPTCHA
/// (reCAPTCHA/hCaptcha/Turnstile) would plug into the architecture for
/// Suspicious-tier traffic, without integrating a third-party service.
/// The "challenge" is a trivial arithmetic question stored in Redis with a
/// short TTL; verifying it correctly issues a one-time pass token that
/// downstream logic could check for the next request from that identity.
/// </summary>
[ApiController]
[Route("captcha")]
public class CaptchaController(IConnectionMultiplexer redis) : ControllerBase
{
    [HttpPost("challenge")]
    public async Task<ActionResult<CaptchaChallengeResponse>> Challenge()
    {
        var a = Random.Shared.Next(1, 10);
        var b = Random.Shared.Next(1, 10);
        var challengeId = Guid.NewGuid().ToString("N");

        var db = redis.GetDatabase();
        await db.StringSetAsync($"captcha:{challengeId}", (a + b).ToString(), TimeSpan.FromMinutes(2));

        return Ok(new CaptchaChallengeResponse(challengeId, $"What is {a} + {b}?"));
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] CaptchaVerifyRequest request)
    {
        var db = redis.GetDatabase();
        var key = $"captcha:{request.ChallengeId}";
        var expected = await db.StringGetAsync(key);

        if (!expected.HasValue) return BadRequest(new { error = "challenge_expired_or_unknown" });

        var correct = expected == request.Answer.Trim();
        await db.KeyDeleteAsync(key); // one-time use

        return correct ? Ok(new { verified = true }) : Ok(new { verified = false });
    }
}
