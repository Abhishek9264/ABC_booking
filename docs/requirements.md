# Requirements and implementation status

This document is the current requirements checklist for the Tatkal Booking
System. It separates implemented source functionality from runtime verification
and remaining production-hardening work.

## 1. Core functional requirements

- [x] User registration and JWT authentication.
- [x] Train/station/schedule search.
- [x] Two-tier search caching (L1 memory + L2 Redis).
- [x] Booking creation with `202 Accepted` and asynchronous processing.
- [x] Explicit booking state machine.
- [x] Passenger validation.
- [x] Seat reservation and release.
- [x] Payment simulation with success/failure/timeout outcomes.
- [x] Booking confirmation after successful payment.
- [x] Compensation after payment failure.
- [x] Booking expiration with a persisted sweep rather than an in-memory timer.
- [x] User/admin authorization for booking reads and cancellation.
- [x] Real-time booking status through SignalR.

## 2. High-concurrency requirements

### Seat allocation invariant

For a given `(ScheduleId, CoachId, SeatId)` there must be at most one active
reservation in `Locked` or `Confirmed` state.

- [x] PostgreSQL partial unique index enforces the invariant.
- [x] Optimistic strategy implemented.
- [x] PostgreSQL advisory-lock strategy implemented.
- [x] Redis-assisted strategy implemented.
- [x] Concurrency test project exists.
- [ ] Run the concurrency test against a real PostgreSQL/Testcontainers environment.
- [ ] Record actual contention benchmark results.

### Idempotency

- [x] `Idempotency-Key` support.
- [x] Request-hash conflict detection.
- [x] User ownership validation for idempotency records.
- [x] Deterministic fallback message IDs for at-least-once redelivery.
- [ ] Automated retention/cleanup of expired idempotency records.

## 3. Messaging requirements

- [x] RabbitMQ + MassTransit.
- [x] Booking Service transactional outbox.
- [x] Booking Service inbox/deduplication.
- [x] Retry and dead-letter conventions.
- [x] Outbox claim/lease handling for multiple Booking Service instances.
- [x] Deterministic handling of duplicate integration messages.
- [ ] Payment Service transactional outbox.
- [ ] Notification Service transactional outbox if notification delivery must be durable.
- [ ] Automated cleanup/retention for outbox and inbox records.

### Current reliability boundary

Booking Service protects business writes and its downstream events with its
outbox. Payment Service currently commits its payment state and publishes its
result as separate operations. Therefore, a Payment Service crash between
those two operations remains a known dual-write failure window.

## 4. Distributed-systems requirements

- [x] Queue-based booking processing.
- [x] Distributed rate limiting through Redis.
- [x] Redis-assisted seat contention filtering.
- [x] PostgreSQL remains the seat-ownership source of truth.
- [x] Bounded retry with resilience building blocks.
- [x] Booking expiration survives service restart.
- [x] Consumer redelivery protection through inbox checks.
- [x] Optimistic concurrency handling for competing booking updates.
- [ ] Validate all failure scenarios with actual infrastructure fault injection.

## 5. Security and abuse controls

- [x] JWT authentication.
- [x] Role-based authorization.
- [x] Per-IP/per-route/per-user rate limiting.
- [x] Bot risk scoring simulation.
- [x] CAPTCHA simulation.
- [x] Input validation.
- [x] Correlation IDs and structured logging.
- [ ] Replace the CAPTCHA simulation with a real provider only if the project is later used as a real application.

## 6. Observability requirements

- [x] OpenTelemetry instrumentation.
- [x] Distributed tracing configuration.
- [x] Prometheus metrics.
- [x] Grafana configuration.
- [x] Jaeger configuration.
- [x] Booking/user/correlation identifiers propagated through relevant flows.
- [ ] Verify traces and metrics end-to-end in Docker.
- [ ] Add alerting rules for queue depth, booking failures, payment failures,
      outbox age, and seat-lock conflicts.

## 7. Load-testing requirements

The repository contains k6 scenarios for:

- [x] Normal traffic.
- [x] High traffic.
- [x] Tatkal spike.
- [x] Same-seat contention.
- [x] Invalid/nonexistent-resource traffic.

Required measurements:

- [ ] Requests/sec.
- [ ] P50 latency.
- [ ] P95 latency.
- [ ] P99 latency.
- [ ] Error rate.
- [ ] Queue depth.
- [ ] Redis hit ratio.
- [ ] Database CPU and latency.
- [ ] Memory/CPU utilization.
- [ ] Number of successful seat allocations.
- [ ] Number of seat-allocation conflicts.

**No benchmark numbers should be committed until they come from actual runs.**

## 8. Production-hardening requirements

- [x] Docker Compose topology.
- [x] Service health checks.
- [x] Environment-based configuration.
- [x] `.dockerignore`.
- [ ] Run a clean `docker compose up --build` from an empty environment.
- [ ] Verify all health checks.
- [ ] Verify all database initialization scripts.
- [ ] Verify all integration tests.
- [ ] Verify all concurrency tests.
- [ ] Add Payment Service outbox.
- [ ] Add SignalR Redis backplane before horizontal Booking Service scaling.
- [ ] Add database/index migration/versioning strategy beyond hand-authored bootstrap SQL.
- [ ] Add operational cleanup jobs for outbox/inbox/idempotency data.
- [ ] Add alerting and dashboards based on measured thresholds.

## 9. Documentation requirements

- [x] Architecture overview.
- [x] End-to-end project flow.
- [x] User-facing functional flow.
- [x] Failure scenarios.
- [x] ADRs for major architectural decisions.
- [x] Database schema documentation.
- [x] Interview/system-design questions.
- [x] Load-test scenarios.
- [x] This implementation-status document.
- [ ] Update benchmark documentation after actual runs.
- [ ] Update architecture diagrams if runtime architecture changes.

## 10. Definition of production-grade for this portfolio project

The project should not be described as fully production-grade until all of
these are true:

1. `dotnet build` succeeds.
2. `dotnet test` succeeds.
3. Docker Compose starts successfully from a clean volume.
4. Same-seat contention proves exactly one active reservation wins.
5. Booking Service outbox recovery is verified during RabbitMQ failure.
6. Payment Service has an equivalent transactional outbox, or its direct
   publication model is explicitly accepted as a deliberate limitation.
7. Expiration and recovery are verified after service restart.
8. SignalR behavior is verified for the intended deployment topology.
9. k6/NBomber benchmark results are recorded from actual executions.
10. Failure scenarios are tested with controlled service/Redis/RabbitMQ/database
    interruptions.
