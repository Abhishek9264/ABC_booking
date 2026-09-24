# ADR-008: Why PostgreSQL (over SQL Server or a NoSQL store)?

**Status:** Accepted

## Context
The seat-uniqueness guarantee this entire project is built around (ADR-004)
needs a database that can enforce a partial unique index (only over
`Status IN ('Locked','Confirmed')`) cheaply and correctly under
concurrent writers.

## Decision
PostgreSQL, as the spec's stated preference (section 1). Partial/filtered
unique indexes, `SERIALIZABLE`/`REPEATABLE READ` isolation, `jsonb` for
the outbox/event payloads, and first-class Docker images made it the
straightforward choice.

## Alternatives considered
- **SQL Server**: also supports filtered unique indexes and would work
  fine; Postgres was preferred per spec + broader familiarity with its
  concurrency/locking behavior for this kind of write-heavy contention
  scenario.
- **A NoSQL store (e.g. DynamoDB/Mongo) for SeatReservations only**: some
  of these support conditional writes that could enforce uniqueness too,
  but would mean two different persistence technologies for one bounded
  context, with no corresponding benefit here (the data is fully
  relational).

## Trade-offs
- Vertical write throughput on the `SeatReservations` table is ultimately
  bounded by single-primary Postgres — mitigated, not eliminated, by the
  Redis admission-control layer (ADR-004 Approach 3).

## Consequences
Every service gets its own Postgres database (`tatkal_<service>`) rather
than a shared schema — enforced at the connection-string level in
docker-compose — so no service can accidentally query another's tables
directly.
