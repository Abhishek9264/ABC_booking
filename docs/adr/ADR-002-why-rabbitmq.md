# ADR-002: Why RabbitMQ?

**Status:** Accepted

## Context
During a Tatkal window, demand can arrive far faster than the database can
safely absorb it (spec section 8: "1 million requests arrive"). Requests
need to be admitted, queued, and drained at a rate the downstream seat
allocation logic can actually handle.

## Decision
Use RabbitMQ (via MassTransit) as the buffer between "request accepted by
the gateway" and "request actually processed by a booking worker," with
consumer prefetch limits controlling drain rate, and a dead-letter queue
for messages that repeatedly fail.

## Alternatives considered
- **Kafka**: better fit for high-throughput event streams and long-term
  replay, but heavier to operate for a single bounded queue-plus-DLQ use
  case. MassTransit's RabbitMQ integration (retry, DLQ, saga persistence)
  is a more direct fit for this project's needs.
- **No queue, direct DB writes with heavy connection pooling**: rejected —
  this is exactly the design that collapses under Tatkal-style spikes;
  the whole point of section 8 is to demonstrate why that fails.

## Trade-offs
- Added operational component and another thing that can be unavailable
  (see failure scenario 4 in `docs/failure-scenarios/`).
- Message ordering guarantees are weaker than a single DB transaction —
  consumers must be idempotent (Critical Engineering Rule #5).

## Consequences
Every consumer (booking worker, payment worker, notification worker) must
assume at-least-once delivery and handle duplicate messages safely.
