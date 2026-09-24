# System design interview questions this project answers

Use this as prep material: for each question, a one-line answer plus
exactly where in the codebase/docs to point to for the full version.

## Concurrency & correctness

**Q: 500 people click "book" on the same seat at the same moment. Walk me
through what happens.**
All 500 requests are accepted and queued (`POST /api/bookings` just
writes `Initiated`→`Queued` and returns 202). `BookingCreatedConsumer`
instances pick them up and call `ISeatLockStrategy.TryLockSeatAsync`. With
the default `RedisAssisted` strategy, a Redis lock admits roughly one
writer at a time to Postgres; whichever one gets there first succeeds on
the partial unique index on `SeatReservations(ScheduleId, CoachId,
SeatId)` filtered to `Locked`/`Confirmed`; everyone else gets a
unique-violation and returns `SeatUnavailable`. → `Concurrency/`, ADR-004.

**Q: Why not just use a Redis lock and skip the database check entirely?**
Because Redis is never the source of truth for ticket ownership (Critical
Engineering Rule #2) — a lock can be lost to a Redis failover or expire
early under clock drift. If that happened with no DB-level guarantee,
two people really could get the same seat. The DB unique index is what
makes the guarantee actually hold; Redis only makes the common case fast.

**Q: Optimistic vs. pessimistic concurrency — how do you choose?**
Optimistic (try the write, handle the conflict) wins when conflicts are
rare-ish and you want to avoid holding locks; pessimistic (check inside a
transaction) wins when you want to fail fast before doing other work in
the same transaction. Here, both ultimately rely on the same unique index
as the real guarantee — the difference is how much wasted work a loser
does before finding out. See the comparison table in ADR-004.

## Distributed systems fundamentals

**Q: How do you guarantee exactly-once processing when your message
broker only guarantees at-least-once?**
You don't guarantee exactly-once delivery — you make redelivery a safe
no-op instead. Every consumer checks an `InboxMessages` row for the
transport message id before doing anything (`InboxGuard`), and every
externally-visible side effect (booking status, payment status) is
itself idempotent by construction (state-machine transitions from a
specific `from` state; duplicate `ProviderTransactionId` hits a unique
index). → `Consumers/InboxGuard.cs`, failure scenarios 6-8.

**Q: What happens if RabbitMQ is down when you try to confirm a booking?**
The DB write (booking confirmed) already succeeded and committed. The
`BookingConfirmed` event sits in `OutboxMessages` with `Status=Pending`.
`OutboxPublisherService` keeps retrying on its poll loop; once RabbitMQ
comes back, the event publishes with no data loss — just added latency.
→ ADR-006, failure scenario 4.

**Q: What happens if Redis is down?**
Search Service's cache-aside falls through to Postgres directly (higher
latency, correct results). The gateway's Redis-backed rate limiter and
bot scorer would need a decision here too — this repo does NOT currently
implement a "fail open" path for those (see Known Limitations); in
production you'd choose explicitly whether "Redis down" means
"stop rate limiting" (fail open, risk abuse) or "reject everything"
(fail closed, risk false-positive outage). Seat-locking's Redis layer
fails safe automatically, because it's an admission filter in front of a
DB guarantee, not the guarantee itself.

**Q: Why a saga instead of a two-phase commit across services?**
2PC needs a coordinator all participants trust and stay available to, and
it holds locks across the whole transaction — exactly the kind of
cross-service coupling and availability risk a Tatkal-scale system can't
afford. A saga trades strict atomicity for eventual consistency with
explicit compensation (release the seat if payment fails), which is
slower to reason about but doesn't require Payment Service to be up for
Booking Service to accept a request. → ADR-005.

## Scalability

**Q: How would this scale horizontally?**
Every service is stateless except its DB connection — add replicas behind
the gateway/queue. The two things that need extra thought: (1) SignalR
needs a Redis backplane once Booking Service has >1 replica (ADR-010,
noted as a known limitation — not yet wired), and (2) Postgres itself
becomes the eventual bottleneck for `SeatReservations` writes on the
hottest schedules — see the bottleneck doc for what's next after that.

**Q: How do you protect the database during a Tatkal-opening spike?**
Multiple layers, deliberately redundant: gateway rate limiting (per-IP,
per-user, per-route, Redis-backed so it works across gateway replicas) →
RabbitMQ queue with bounded consumer prefetch (workers pull at a rate
they can sustain, not at request-arrival rate) → Redis admission lock
(rejects most same-seat losers before they reach Postgres). Removing any
one layer degrades gracefully; the database is still protected by the
ones below it. → docs/architecture "Tatkal queue" diagram.

## Data & consistency

**Q: Why does each service have its own database instead of one shared
schema?**
Database-per-service is what actually enforces the "microservice"
boundary — without it, a supposedly independent service can be coupled
to another's schema and neither can evolve independently. The cost is
exactly what the saga/outbox/event-driven machinery in this repo exists
to pay for: no cross-service transactions, no cross-service joins.

**Q: Walk me through what happens if the Booking Service crashes right
after payment succeeds but before it commits the confirmation.**
Payment Service already committed `Payment.Status = Success` and
published `PaymentSucceeded` — that event sits durably in RabbitMQ
regardless of Booking Service's state. When Booking Service restarts,
its consumer (`PaymentSucceededConsumer`) is still subscribed to that
queue; RabbitMQ redelivers the unacknowledged message, `InboxGuard` lets
it through (first time this message id has been seen by *this* service),
and the booking gets confirmed on restart. → failure scenario 3.
