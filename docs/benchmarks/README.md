# Benchmarks

No numbers yet — this file is populated in Phase 16/18/17 from actual k6/NBomber
runs against `load-tests/`. Per Critical Engineering Rule #19, no benchmark
figures are fabricated; until real runs exist, this file stays empty of
numbers by design.

Planned tests (spec section 27):

| Test | Scenario |
|---|---|
| A | 100 concurrent users, normal traffic |
| B | 1,000 concurrent users, high traffic |
| C | 10,000+ concurrent requests, Tatkal spike |
| D | 1,000 users contending for the same seat |
| E | 100,000 requests against nonexistent booking IDs (negative-cache impact) |

Metrics to record per run: requests/sec, p50/p95/p99 latency, error rate,
DB/Redis CPU, RabbitMQ queue depth, cache hit ratio, successful vs. failed
bookings, seat allocation conflicts.
