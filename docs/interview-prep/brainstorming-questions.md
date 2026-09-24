# Brainstorming / design-extension questions

Open-ended prompts for extending this project further — useful both as
self-study ("could I actually design an answer to this?") and as the kind
of follow-up a senior interviewer asks after the standard questions run out.

## Product & scope
1. Tatkal tickets are famously resold above face value by bots the moment
   they're booked. Beyond the bot-risk score already in this repo, what
   product-level (not just technical) levers exist to reduce scalping —
   and which of them are actually technical problems in disguise (e.g.
   identity verification at booking time)?
2. Waitlisted bookings (RAC / waitlist in real Indian Railways) aren't
   modeled here at all. How would you extend `BookingStatus` and the seat
   allocation logic to support "no seat available now, but queue me for
   one if it frees up"? Does this change the concurrency strategy?
3. How would you support a "hold for group booking" — 4 people who all
   need seats in the same coach, confirmed atomically or not at all?
   (Hint: this is a distributed transaction across N seats instead of 1 —
   does the same unique-index trick still work, or do you need something
   like a two-phase seat hold?)

## Architecture extensions
4. This repo has one Redis instance and one RabbitMQ instance — both
   single points of failure in the current docker-compose. How would you
   introduce Redis Cluster / Sentinel and RabbitMQ quorum queues, and what
   changes for `RedisDistributedLockProvider` (single-node SET NX) once
   Redis is a cluster? Is single-node Redlock still "safe enough" the way
   ADR-004 argues, or does clustering change that argument?
5. `OutboxPublisherService` is a single background service per Booking
   Service instance — if you run 5 replicas, do they race on the same
   outbox rows? Design the claim/lock mechanism that would make that safe
   (e.g. `SELECT ... FOR UPDATE SKIP LOCKED`).
6. The saga in this repo is a 2-hop choreography (Booking -> Payment ->
   Booking). Design what changes if a third service joins — say, a
   "Seat Upgrade" service that can bump a confirmed booking to a better
   seat for a fee. Would you still choreograph, or is this the point
   where a formal state-machine saga earns its complexity (ADR-005)?
7. How would you add multi-region support (e.g. one region per country/
   zone) where most trains only need to be visible in one region, but a
   user in region A might book a train whose seat inventory lives in
   region B?

## Data & correctness
8. `SeatReservations` has a partial unique index scoped to one Postgres
   instance. If you eventually needed to shard Postgres by, say, train
   number, does the uniqueness guarantee still hold across shards? What
   would you need to add (or give up) to keep it?
9. The outbox pattern here guarantees "the event was eventually
   published." It does NOT guarantee "the event was published in the
   same order the DB rows were written," if the publisher processes a
   batch out of strict order under retry. Does booking status ever
   actually depend on event ORDER, or only on which events eventually
   arrive? Where would ordering start to matter as the system grows?
10. `IdempotencyRecords` currently never gets cleaned up in code (only an
    `ExpiresAtUtc` column exists). Design the cleanup job — how often, in
    what batch size, and how do you avoid it competing with live booking
    traffic for the same table?

## Failure & chaos
11. Pick any two of the ten failure scenarios in
    `docs/failure-scenarios/README.md` and combine them — e.g. "Redis is
    down AND a booking worker crashes mid-message." Walk through what
    actually happens step by step. Does the system still end up correct,
    just slower — or does the combination expose a real gap?
12. Design a chaos-testing plan for this repo specifically (which
    containers to kill, in what order, during which load test) that would
    give you real confidence in the failure-scenario claims instead of
    just the unit-test-level proof `SeatContentionTests` provides.

## Observability & operations
13. Today, "seat allocation conflicts" (a specific metric named in spec
    section 25) isn't actually emitted as a Prometheus metric anywhere in
    this repo — `SeatLockOutcome.SeatUnavailable` is only logged. Design
    the metric (name, labels, cardinality concerns — should it be labeled
    per-seat? Per-schedule? Neither, probably) and where in the code it'd
    be incremented.
14. If you were paged at 2am because Grafana shows RabbitMQ queue depth
    climbing steadily for 20 minutes, what's your triage order — which
    dashboard panel do you check first, second, third, and what does each
    possible finding tell you to do next?

## Cost & trade-offs
15. Everything in this repo optimizes for correctness and observability.
    If you were told to cut infrastructure cost by 60% for a
    lower-stakes version of this system (say, a college-club event
    ticketing clone), which pieces would you cut first, and which would
    you refuse to cut no matter the budget? Justify the refusal.
