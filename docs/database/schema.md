# Database design

Phase 2 is implemented. The repository currently uses hand-authored SQL
schemas under each service's `Migrations/001_init.sql`; EF Core migrations
history is not committed.

## Tables

| Table | Purpose |
|---|---|
| `Users` | Account + auth identity |
| `Stations` | Railway stations (code, name, city) |
| `Trains` | Train master data |
| `TrainRoutes` | Ordered stations a train stops at |
| `Coaches` | Coach types/classes per train |
| `Seats` | Physical seat inventory per coach |
| `Schedules` | Train run instances on a specific date |
| `Bookings` | Booking header + current `BookingStatus` |
| `BookingPassengers` | Passenger details per booking |
| `SeatReservations` | The seat-lock/ownership record — unique constraint lives here |
| `Payments` | Payment attempts and their state |
| `OutboxMessages` | Transactional outbox (see ADR-006, once written) |
| `InboxMessages` | Consumer-side dedup for at-least-once delivery |
| `BookingEvents` | Append-only event stream for the booking lifecycle |
| `IdempotencyRecords` | Idempotency-Key -> booking mapping, with TTL |

## Key constraints

- `SeatReservations`: **unique index** on `(ScheduleId, CoachId, SeatId)`
  filtered to active/confirmed reservations — this is the actual
  correctness guarantee referenced in ADR-004, independent of whichever
  locking strategy sits in front of it.
- `Bookings.RowVersion` / `SeatReservations.RowVersion`: EF Core concurrency tokens mapped to PostgreSQL `xmin`; no custom `RowVersion` database column is required.
- `IdempotencyRecords.IdempotencyKey`: unique index, with `RequestHash` to
  detect the same key reused with a different body (spec section 7).
- `OutboxMessages`: index on `(ProcessedAt)` for the background publisher
  to efficiently pick up unpublished events.

## Indexing notes

- `Schedules(TrainId, DepartureDate)` — the hot path for search.
- `SeatReservations(ScheduleId)` — availability lookups.
- `BookingEvents(BookingId, SequenceNumber)` — event stream replay order.

The SQL schemas and EF Core model configuration are both present in the source tree. Runtime schema verification still requires Docker/PostgreSQL.
