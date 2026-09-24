namespace ApiGateway.BotDetection;

public class BotDetectionMiddleware(RequestDelegate next, BotRiskScorer scorer, ILogger<BotDetectionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var assessment = await scorer.AssessAsync(context);
        context.Items["RiskScore"] = assessment.Score;
        context.Items["RiskTier"] = assessment.Tier;

        switch (assessment.Tier)
        {
            case RiskTier.HighRisk:
                logger.LogWarning("High-risk request blocked (score {Score}) for {Path}", assessment.Score, context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "high_risk_traffic_blocked", score = assessment.Score });
                return;

            case RiskTier.Suspicious:
                // In a full implementation this would redirect the client to
                // POST /captcha/challenge and require a solved token before
                // continuing. Here we just tag the response header so the
                // simulation is visible without blocking the demo flow.
                context.Response.Headers["X-Risk-Tier"] = "Suspicious";
                break;
        }

        await next(context);
    }
}
