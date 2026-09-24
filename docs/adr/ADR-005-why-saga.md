# ADR-005: Why the Saga pattern (and why choreography, not a MassTransit state-machine)?

**Status:** Accepted

## Context
Booking a Tatkal ticket spans three services with no shared database:
Booking (reserve seat) -> Payment (charge) -> Booking (confirm or
compensate). A single ACID transaction across services isn't possible, so
consistency has to be modeled explicitly as a sequence of local
transactions with compensating actions for failure.

## Decision
Implement the saga as **event-choreographed consumers** rather than a
formal `MassTransitStateMachine<TState>`: `BookingCreatedConsumer` locks
the seat and publishes `PaymentRequested`; `PaymentSucceededConsumer` /
`PaymentFailedConsumer` in Booking Service react to the payment outcome
and either confirm the booking or compensate (release the seat, cancel
the booking). Each booking's `BookingStatus` plus the `BookingEvents`
stream (ADR/spec section 18) doubles as the saga's persisted state,
instead of a separate saga-instance table.

## Alternatives considered
- **MassTransit state-machine saga** (`Automatonymous`/`MassTransitStateMachine`):
  more explicit about legal state transitions and comes with built-in
  saga persistence, but adds a second state representation on top of the
  `BookingStatus` state machine that already exists — two sources of
  truth for the same thing. Worth revisiting if a third service needs to
  join the saga (today it's a 2-hop handoff: Booking -> Payment -> Booking).
- **Synchronous orchestration** (Booking Service calls Payment Service's
  HTTP API directly and waits): simpler to trace, but couples the two
  services' availability together and makes the Booking Worker's
  throughput dependent on Payment Service's response time — exactly the
  coupling the queue-based architecture (ADR-002) exists to avoid.

## Trade-offs
- Choreography is harder to visualize than a single state-machine
  definition — the saga's logic is spread across 3 consumer classes
  instead of one file. Mitigated by the sequence/state diagrams in
  `docs/architecture/README.md`.
- No single "saga timeout" primitive — timeouts are handled separately by
  `BookingExpirationSweeperService` (ADR/spec section 17), not by the saga
  machinery itself.

## Consequences
Every new saga step (e.g. a future "issue e-ticket" step) is another
consumer reacting to an event and calling `Booking.MoveTo(...)`, keeping
the state machine in `BookingStateMachine.cs` as the single legal-
transition authority regardless of how many steps the saga grows to.
