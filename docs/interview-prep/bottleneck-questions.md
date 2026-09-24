# Bottleneck & failure-mode questions an interviewer can ask

Organized as: the question, the honest answer (including what's NOT yet
solved in this repo), and where to look.

## 1. "What's the actual bottleneck in this system, and where does it move
to once you fix it?"
Current chain, in order of what saturates first under a real Tatkal spike:
1. **Gateway rate limiter (Redis)** — cheap, scales with Redis, unlikely
   to be first bottleneck in practice.
2. **RabbitMQ queue depth** — absorbs the burst; if consumers can't drain
   fast enough, queue depth grows and booking latency grows with it (this
   is a FEATURE, not a bug — it's deliberately better than the DB falling
   over — but it does mean "how fast does a user find out their booking
   failed" degrades under load).
3. **Postgres writes to `SeatReservations`** for whichever handful of
   trains/seats are the most contended — this is the real ceiling. The
   Redis admission lock (ADR-004 Approach 3) reduces load here but
   doesn't eliminate it; every WINNING request still writes to Postgres.
4. Once (3) is addressed (see "What's next" below), the next bottleneck
   would likely be **Payment Service's simulated gateway latency**
   (50-300ms sleep) multiplied across however many bookings are
   in-flight simultaneously — a real payment gateway would have its own,
   probably worse, latency and rate limits.

**What's next after (3):** partition `SeatReservations` by schedule (or
even by train+date), so contention on one hot train doesn't serialize
writes for unrelated trains on the same Postgres instance; consider a
read replica for `SearchService` availability queries so search load
never competes with booking writes on the primary.

## 2. "Your Redis admission lock has a 10-second TTL. What happens if the
DB write that's supposed to release it takes longer than that?"
A second request can acquire the lock and also proceed to Postgres. This
is safe — not "ideal" — because the unique index is still there: the
second writer just gets a unique-violation and returns `SeatUnavailable`
correctly, at the cost of one extra wasted DB round trip. This is exactly
why ADR-004 insists Redis is an optimization layer, never the guarantee:
a too-short TTL degrades throughput, never correctness.

## 3. "What happens if the same booking worker instance processes the
same message twice?"
`InboxGuard` checks `InboxMessages` for the message id before any side
effect. But look closely at `BookingCreatedConsumer`: it also checks
`booking.Status != BookingStatus.Queued` and no-ops if the booking has
already moved past that state — a second layer of defense in case the
inbox check and the business check ever disagree (e.g. inbox row written
but crash before the rest of the transaction commits — they're in the
SAME `SaveChangesAsync` call, so this specific race shouldn't happen, but
it's worth being able to explain why the second check exists anyway).

## 4. "SignalR is wired up but you flagged it as broken at scale. What
breaks, exactly, and how would you fix it?"
A WebSocket connection is pinned to whichever Booking Service instance
accepted it. If `PaymentSucceededConsumer` runs on instance B but the
client's SignalR connection is on instance A, `IHubContext.Clients.Group`
on instance B only reaches clients connected to instance B — the update
is silently lost for that client. Fix: `AddSignalR().AddStackExchangeRedis(...)`,
which uses Redis pub/sub as a backplane so a `Clients.Group(...).Send(...)`
call on any instance reaches connections on every instance. Not yet added
— see Known Limitations in the root README.

## 5. "Your outbox publisher polls every 500ms. Why not push instantly?"
Could use a Postgres `LISTEN/NOTIFY` trigger for near-instant wakeup
instead of polling — noted as a future improvement. Trade-off: NOTIFY
requires the same connection to stay subscribed and doesn't survive a
publisher restart on its own the way "poll for Pending rows" trivially
does; the 500ms poll is simpler to reason about for a portfolio project,
at the cost of up to 500ms added latency on every event.

## 6. "You have 5 booking workers running. A Tatkal spike hits. What
actually limits how fast they drain the queue?"
`MessagingOptions.PrefetchCount` (16 per instance by default) bounds how
many messages one worker pulls concurrently; multiply by instance count
for total in-flight capacity. Past that, it's whatever `ISeatLockStrategy`
+ Postgres can sustain — this is the parameter Load Test C (Tatkal spike)
exists to tune empirically rather than guess at.

## 7. "What's a scenario in this design that's UNSAFE, not just slow?"
Good answer to have ready: none that we're aware of in the core
seat-booking path (that's the one thing every layer is built to protect),
but the admin dead-letter "retry" endpoint (`DeadLetterAdminController`)
re-publishes a raw message body without going through
`IIdempotencyService` or reconstructing the original MassTransit envelope
— if used carelessly it could replay a message that's since become stale
relative to the booking's current state. It's explicitly flagged as a
simplification in the code for this reason; a production version would
deserialize and re-publish through `IPublishEndpoint` properly.

## 8. "How would you find out these bottlenecks are real, not
theoretical?"
Run Test D (`load-tests/test-d-same-seat-contention.js`) against a seeded
schedule/seat and check Grafana's Postgres-CPU and RabbitMQ-queue-depth
panels during the run; run Test C (Tatkal spike) and watch p95/p99
latency and 429 rate climb together. Per Critical Engineering Rule #19,
this repo does not claim numbers it hasn't actually measured —
`docs/benchmarks/README.md` stays empty until those runs happen.
