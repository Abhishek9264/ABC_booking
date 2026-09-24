# ADR-001: Why microservices?

**Status:** Accepted

## Context
The project exists to demonstrate distributed-systems engineering, not just
CRUD functionality. A monolith can be scaled and made resilient too, but it
hides the specific coordination problems (seat locking across instances,
queue-based load shedding, saga compensation across service boundaries)
that this project is meant to showcase.

## Decision
Split along clear bounded contexts: User, Search, Booking, Payment,
Notification, fronted by a YARP API Gateway. Booking Service is the core —
everything else exists to support it.

## Alternatives considered
- **Modular monolith**: simpler to run and debug, but doesn't force real
  network boundaries, so it can't demonstrate retries, circuit breakers,
  or partial-failure recovery honestly.
- **Fine-grained microservices per entity** (e.g. separate Seat service,
  separate Coach service): rejected — rule #1 in the spec is "do not
  introduce unnecessary microservices." Seats/coaches are owned by Search
  and Booking respectively; they don't need their own network boundary.

## Trade-offs
- More operational surface area (5 services + gateway vs. 1 app).
- Cross-service transactions become sagas instead of DB transactions.
- Local dev requires Docker Compose rather than "just run one project."

## Consequences
Every cross-service write (booking -> payment -> confirmation) must be
designed as a saga with compensation, not a single ACID transaction. This
is deliberate — see ADR-005.
