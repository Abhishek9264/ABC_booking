# ADR-007: Why event sourcing (scoped to the booking lifecycle only)?

**Status:** Accepted

## Context
Spec section 18 asks for event sourcing on the booking lifecycle
specifically, plus a note not to force it onto every service (section 18:
"Do NOT force event sourcing into every service").

## Decision
`BookingEvents` is an append-only table (`BookingCreated`, `SeatLocked`,
`BookingConfirmed`, ...). `Bookings.Status` remains a regular mutable
column — effectively a materialized projection maintained inline by the
same code that appends events — rather than being rebuilt by replaying
the stream on every read. `BookingEventsController` exposes the raw
stream for audit/debugging.

## Alternatives considered
- **Full event sourcing** (Bookings table dropped entirely, state derived
  by replaying BookingEvents on every read): gives a "true" audit log and
  free time-travel debugging, but adds real complexity (snapshotting for
  performance, versioned event schemas) that isn't justified for a
  service whose current state is queried far more often than its history.
- **No event log at all, just the status column**: simplest, but loses
  the audit trail entirely — can't answer "how did this booking end up
  CANCELLED" without log-diving across three services.

## Trade-offs
- Two representations of "what happened" (the events table and the
  status column) that must be kept in sync by convention, not by
  construction — a bug could append an event without updating status or
  vice versa. Code review discipline substitutes for the guarantee a
  fully event-sourced system would give for free.

## Consequences
User Service, Search Service, and Payment Service do NOT get their own
event streams — their history isn't valuable enough to justify the
pattern, matching the spec's explicit guidance not to force it everywhere.
