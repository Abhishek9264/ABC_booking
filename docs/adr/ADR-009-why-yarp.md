# ADR-009: Why YARP for the API Gateway?

**Status:** Accepted

## Context
Need a single entry point for routing, rate limiting, and bot-risk
scoring (spec section 3 / 9-11) in front of five backend services.

## Decision
YARP (Yarp.ReverseProxy) — a first-class .NET reverse-proxy library that
runs as a normal ASP.NET Core app, so gateway-level middleware
(`RateLimitingMiddleware`, `BotDetectionMiddleware`, correlation-id) is
just ordinary ASP.NET Core middleware, sharing the same
Serilog/OpenTelemetry setup as every other service in the solution.

## Alternatives considered
- **Nginx / Envoy / Kong**: more battle-tested at extreme scale and would
  work fine, but configuration and custom logic (Redis-backed rate
  limiting, the bot-risk scorer) would live in a different language/config
  system than the rest of the codebase — a real cost for a project meant
  to be read end-to-end as one coherent C# codebase.
- **Ocelot**: another .NET API gateway option; YARP is Microsoft's own,
  more actively maintained, and has a lower-level extensibility model that
  fits custom middleware better.

## Trade-offs
- Less operationally battle-tested than Nginx/Envoy at internet scale.
- Being in-process with the rest of .NET means gateway restarts are a
  .NET app restart, not a config reload on a dedicated proxy.

## Consequences
Gateway-specific logic (rate limiting, bot detection, CAPTCHA simulation)
is implemented once, in C#, instead of being split between proxy config
and application code.
