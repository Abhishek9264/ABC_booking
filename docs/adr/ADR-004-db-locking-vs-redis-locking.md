# ADR-004: Database locking vs. Redis distributed locking

**Status:** Accepted — measured benchmark comparison still pending

## Context
Spec section 5 requires that when 500 users attempt to book the same seat
simultaneously, exactly one succeeds. Three candidate approaches exist:
optimistic concurrency (row version column), pessimistic DB
transactions/locking, and a Redis-based distributed lock (e.g. Redlock).

## Decision
The implementation exposes three strategies behind `ISeatLockStrategy`. The configured/default strategy is **RedisAssisted** as an admission-control optimization, while PostgreSQL remains the final correctness authority. **Optimistic** remains available as the baseline comparison strategy, and `DbTransaction` uses a PostgreSQL transaction advisory lock. PostgreSQL `xmin` is used as the EF Core concurrency token for entity updates that require optimistic concurrency.

The Redis lock is never the sole source of truth for seat ownership. The partial
unique index on active reservations remains the final database invariant.

## Alternatives considered
- **DB transaction advisory lock**: serializes attempts for the same logical
  seat without requiring a physical seat row to exist first. The advisory
  lock is held only for the caller's transaction; the partial unique index
  remains the final correctness guarantee.
- **Pure Redis distributed lock (Redlock)**: fastest under contention, but
  the database must still re-check and enforce the constraint (a unique
  index on `(seat_id, schedule_id)` for confirmed reservations), because a
  lock can be lost to a crash/network partition — Redis is a
  performance/admission optimization, never the correctness guarantee.
- **Optimistic concurrency only, no Redis**: correct and simple, but under
  a real Tatkal-style spike, most of the 500 competing requests still pay
  a full DB round trip only to lose the optimistic-concurrency race.

## Trade-offs
| Approach | Correctness source | Throughput under contention | Failure mode |
|---|---|---|---|
| Optimistic concurrency | DB unique constraint / row version | 499 requests still hit DB and fail late | Safe — DB always the tiebreaker |
| DB pessimistic lock | PostgreSQL transaction advisory lock + unique constraint | Waiters serialize per seat | Safe; bounded by DB transaction lifetime |
| Redis lock (admission only) | DB unique constraint (Redis is pre-filter) | Best — most losers rejected before DB | Safe as long as DB still enforces uniqueness |

## Consequences
The database retains a unique constraint on confirmed seat reservations
regardless of which locking strategy sits in front of it. Numbers in the
table above are to be replaced with actual benchmark results from
`load-tests/` once Test D (same-seat contention) has been run — see
Critical Engineering Rule #19: do not fabricate benchmark results.
