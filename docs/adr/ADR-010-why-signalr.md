# ADR-010: Why SignalR for real-time booking status?

**Status:** Accepted

## Context
Spec section 19: the client should see QUEUED -> PROCESSING -> SEAT_LOCKED
-> PAYMENT_PENDING -> CONFIRMED without polling continuously, since the
whole point of the queue-based architecture is that confirmation isn't
immediate.

## Decision
SignalR hub (`BookingStatusHub`) with one group per booking id
(`booking:{id}`); every consumer that changes a booking's status calls
`IBookingNotifier.NotifyStatusChanged` after committing the change.
Clients join the group right after `POST /api/bookings` returns its
`bookingId`.

## Alternatives considered
- **Client polling `GET /api/bookings/{id}`**: simplest, and still works
  as a fallback (e.g. for clients that can't hold a WebSocket open), but
  at Tatkal-spike scale, thousands of clients polling every second is
  exactly the kind of load the queue architecture is trying to keep off
  the API — SignalR turns "N clients asking repeatedly" into "1 push when
  something actually changes."
- **Server-Sent Events (SSE)**: simpler protocol than SignalR/WebSockets
  and would also work; SignalR was chosen for its native ASP.NET Core
  integration and built-in group/connection management, which a raw SSE
  implementation would have to hand-roll.

## Trade-offs
- Requires sticky-session awareness or a backplane (e.g. a SignalR Redis
  backplane) once Booking Service scales to multiple instances, since a
  client's WebSocket connection lands on one specific instance — NOT yet
  configured in this repo (see Known Limitations); `AddSignalR()` alone
  only works correctly with a single Booking Service replica.

## Consequences
Scaling Booking Service horizontally (Phase 18/20) requires adding
`AddStackExchangeRedis` to the SignalR configuration so status pushes
reach a client regardless of which instance's consumer produced them.
