# ADR-006: Why the transactional outbox?

**Status:** Accepted

## Context
Confirming a booking means two things must both happen: the `Bookings`
row's status updates AND a `BookingConfirmed` event reaches RabbitMQ (so
Notification Service can act on it). If those are two separate
operations — write to Postgres, then call `IPublishEndpoint.Publish` — a
crash or network blip between them silently loses the event while the DB
says CONFIRMED (failure scenario 2/3, spec section 22).

## Decision
`OutboxWriter.Enqueue` writes the event as a row in `OutboxMessages` in
the SAME `SaveChangesAsync` call as the business update. A separate
`OutboxPublisherService` (a `BackgroundService`) polls for unpublished
rows and publishes them to RabbitMQ, marking them `Published` only after
a successful publish. The DB transaction is now the only thing that has
to succeed atomically; publishing becomes an at-least-once background
concern with its own bounded retry.

## Alternatives considered
- **Publish directly, no outbox**: simplest code, but reintroduces the
  exact dual-write problem above. Rejected for Booking Service, the
  service where losing an event actually matters (a ticket someone paid
  for). Payment Service and Notification Service still publish directly in this
  repo as a scope trade-off. Payment Service should receive its own transactional
  outbox before the system is described as fully production-grade.
- **CDC (Debezium reading the Postgres WAL)**: more robust and removes the
  polling latency entirely, but a much heavier piece of infrastructure for
  a portfolio project than a 500ms poll loop justifies.

## Trade-offs
- Adds latency between "DB committed" and "event actually published"
  (bounded by `PollInterval`, currently 500ms).
- The outbox table needs its own cleanup/retention policy in a real
  system (not implemented here — noted as a future improvement).

## Consequences
Every state-changing operation in Booking Service that needs to notify
another service goes through `OutboxWriter`, never a direct
`IPublishEndpoint.Publish` call, to preserve the atomicity guarantee.
