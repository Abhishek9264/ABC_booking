# Project flow — how the pieces fit together end to end

This is the "what talks to what, in what order, and why" document —
complementary to `README.md` (what it is) and `docs/architecture/README.md`
(the diagrams). Read this to understand the SYSTEM; read
`functional-flow.md` for the USER-facing journey.

## 1. Request entry
Every client request enters through **API Gateway (YARP)**. Before
routing anywhere, three pieces of gateway middleware run in order:
1. **Correlation-id middleware** — reads or generates `X-Correlation-Id`,
   pushes it into the Serilog log context so every downstream log line
   (once each service also adopts `UseTatkalCorrelationId`) can be
   correlated back to this one request.
2. **BotDetectionMiddleware** — computes a 0–100 risk score from request
   burst rate + missing metadata (`BotRiskScorer`); blocks High-risk
   outright, tags Suspicious for a (simulated) CAPTCHA challenge.
3. **RateLimitingMiddleware** — checks per-IP, per-route, and (if
   authenticated) per-user limits against Redis-backed counters; a 429
   short-circuits before anything reaches a backend service.

Only requests that pass all three get proxied to a backend service by
YARP's routing table (`appsettings.json` → `ReverseProxy:Routes`).

## 2. Identity
**User Service** owns accounts and issues JWTs (`AuthController` →
`IJwtTokenService`). Every other service only *validates* tokens
(`AddTatkalJwtAuthentication`, same signing key, same issuer/audience) —
there's exactly one place a token can be minted, and everywhere else just
trusts a correctly-signed one. Role claims (`User`/`Admin`/`System`)
travel inside the JWT itself.

## 3. Search (read path)
**Search Service** answers `GET /api/trains/search` from its own
Postgres database, behind a two-tier cache (`ISearchCacheService`: L1
in-memory, L2 Redis). This is a pure read path — it never touches
Booking Service's database, by design (spec section 3), so search load
can never contend with booking writes.

## 4. Booking (write path) — the interesting part
1. `POST /api/bookings` on **Booking Service**: checks
   `Idempotency-Key` against `IdempotencyRecords`
   (`IIdempotencyService`), writes a `Booking` row as `Initiated` →
   `Queued`, appends a `BookingCreated` event to `OutboxMessages` in the
   SAME transaction, and returns `202 Accepted` immediately. The seat is
   **not yet locked** at this point.
2. `OutboxPublisherService` (background) picks up the pending
   `BookingCreated` row and publishes it to RabbitMQ.
3. `BookingCreatedConsumer` (the "Booking Worker") consumes it,
   transitions the booking to `Processing`, and calls whichever
   `ISeatLockStrategy` is configured (`RedisAssisted` by default) to
   actually reserve the seat. Win → `SeatLocked` → `PaymentPending`,
   and a `PaymentRequested` event goes out via the outbox. Lose →
   `Cancelled`.
4. **Payment Service**'s `PaymentRequestedConsumer` picks up
   `PaymentRequested`, creates a `Payment` row, simulates gateway
   latency + a randomized outcome, and publishes `PaymentSucceeded` or
   `PaymentFailed` directly. Payment Service does not yet have a
   transactional outbox; this is a remaining reliability requirement.
5. Back in Booking Service, `PaymentSucceededConsumer` confirms the
   booking and marks the seat reservation `Confirmed`; or
   `PaymentFailedConsumer` releases the seat and cancels the booking
   (the saga's compensating action — ADR-005).
6. At every status change, `IBookingNotifier` pushes an update over
   SignalR to any client that joined `booking:{id}`.
7. In parallel, `NotifyUser` events (queued whenever a booking confirms)
   are picked up by **Notification Service**'s `NotifyUserConsumer`,
   which logs a simulated email/SMS/push.

## 5. Recovery paths (run continuously, independent of any single request)
- **`BookingExpirationSweeperService`** (Booking Service): every 15s,
  finds `SeatReservations` past their `ExpiresAtUtc` still `Locked`,
  releases them, and expires the associated booking — this is what
  recovers a seat if a user abandons checkout, AND what recovers state
  correctly after a service restart (nothing here depends on an
  in-memory timer surviving).
- **`OutboxPublisherService`** (Booking Service): every 500ms, publishes
  anything still `Pending` — this is what recovers a lost publish after a
  RabbitMQ outage.
- **Consumer redelivery** (RabbitMQ + MassTransit): if any consumer
  crashes mid-message, RabbitMQ redelivers it to another consumer
  instance once the visibility/ack timeout elapses; `InboxGuard` makes
  that redelivery safe.

## 6. Observability threading through all of the above
Every service calls `AddTatkalObservability(...)` with its own service
name, exporting traces to Jaeger over OTLP and metrics to Prometheus's
`/metrics` scrape endpoint. Because the correlation id is pushed onto the
current `Activity`'s tags, a single booking request can be traced from
the gateway through the queue hop through to the payment simulation and
back, even though it crosses three separate services and one asynchronous
hand-off.
