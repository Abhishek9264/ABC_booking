# ADR-003: Why Redis?

**Status:** Accepted

## Context
Several workloads need sub-millisecond, high-throughput reads/writes that
don't belong on the system of record: cached seat-availability lookups,
distributed rate-limit counters, idempotency-key lookups, and (candidate,
see ADR-004) short-lived seat locks.

## Decision
Use Redis for caching (cache-aside, L2 behind an in-process L1), for
distributed rate limiting counters, and for idempotency-key storage with
TTLs. Redis is explicitly NOT the source of truth for seat ownership
(Critical Engineering Rule #2) — PostgreSQL is.

## Alternatives considered
- **In-memory only (per-instance) caching**: doesn't work once services
  scale horizontally — rate limits and idempotency checks would be
  bypassable by hitting a different instance.
- **Using Postgres for rate limiting**: works but adds write load to the
  database precisely when it's most contended (Tatkal spikes).

## Trade-offs
- Another moving part that can fail (see failure scenario 5) — the system
  must degrade gracefully, not fall over, if Redis is unavailable.
- Cache invalidation and stampede protection have to be designed
  deliberately (see the caching strategy section once Phase 6 lands).

## Consequences
Redis outage should degrade search-latency and rate-limiting precision,
never seat-uniqueness guarantees, since those are enforced in Postgres.
