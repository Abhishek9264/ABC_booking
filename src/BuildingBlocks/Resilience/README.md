# Resilience building block

Shared Polly policies: timeout, retry-with-jitter, circuit breaker, and
bulkhead isolation, applied consistently to every outbound HTTP/gRPC call
(e.g. Booking Service -> Payment Service).

Planned in Phase 4 / 23 / 24:
- `AddResilientHttpClient(...)` extension combining timeout + retry +
  circuit breaker via `Microsoft.Extensions.Http.Resilience`.
- Bulkhead policies isolating booking/payment/search workloads from
  each other under load.
