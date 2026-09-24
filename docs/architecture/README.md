# Architecture Overview

## System diagram

```mermaid
flowchart TB
    Clients["Clients (Web / Mobile / Test)"] --> Gateway["API Gateway (YARP)"]
    Gateway --> User["User Service"]
    Gateway --> Search["Search Service"]
    Gateway --> Booking["Booking Service"]
    Booking --> Queue["RabbitMQ"]
    Queue --> BookingWorker["Booking Worker"]
    Queue --> PaymentWorker["Payment Worker"]
    Queue --> NotificationWorker["Notification Worker"]
    BookingWorker --> Redis[(Redis)]
    BookingWorker --> Postgres[(PostgreSQL)]
    Search --> Redis
    Search --> Postgres
```

## Booking sequence (happy path)

```mermaid
sequenceDiagram
    participant U as User
    participant G as API Gateway
    participant B as Booking Service
    participant R as Redis
    participant Q as RabbitMQ
    participant P as Payment Service
    participant DB as PostgreSQL

    U->>G: POST /api/bookings (Idempotency-Key)
    G->>B: Booking Request
    B->>R: Check idempotency key
    B->>Q: Enqueue booking job
    Q->>B: Booking worker picks up job
    B->>DB: Lock seat (PostgreSQL advisory lock / unique constraint)
    B->>Q: Publish PaymentRequested
    Q->>P: Payment Service consumes event
    P->>Q: PaymentSucceeded / PaymentFailed
    Q->>B: Payment result
    B->>DB: Confirm or compensate booking
    B-->>U: 200 CONFIRMED (via SignalR / polling)
```

## Seat locking lifecycle

```mermaid
stateDiagram-v2
    [*] --> AVAILABLE
    AVAILABLE --> LOCKED: booking started
    LOCKED --> CONFIRMED: payment succeeded
    LOCKED --> EXPIRED: lock timer elapses
    EXPIRED --> AVAILABLE: released
    LOCKED --> AVAILABLE: payment failed, released
```

## Booking state machine

```mermaid
stateDiagram-v2
    [*] --> INITIATED
    INITIATED --> QUEUED
    QUEUED --> PROCESSING
    PROCESSING --> SEAT_LOCKED
    SEAT_LOCKED --> PAYMENT_PENDING
    PAYMENT_PENDING --> CONFIRMED
    SEAT_LOCKED --> PAYMENT_FAILED
    PAYMENT_PENDING --> PAYMENT_FAILED
    SEAT_LOCKED --> EXPIRED
    PAYMENT_PENDING --> EXPIRED
    INITIATED --> CANCELLED
    QUEUED --> CANCELLED
    PROCESSING --> CANCELLED
    PAYMENT_FAILED --> CANCELLED
```

Implemented as an explicit lookup table in
[`BookingStateMachine`](../../src/Services/BookingService/StateMachine/BookingStateMachine.cs) —
see the tests in `tests/UnitTests/BookingStateMachineTests.cs`.

## Tatkal queue under load

```mermaid
flowchart LR
    Users -->|1M requests| Gateway["API Gateway"]
    Gateway --> RateLimiter["Rate Limiter (Redis)"]
    RateLimiter -->|admitted| MQ["RabbitMQ Queue"]
    RateLimiter -->|rejected: 429| Users
    MQ --> Workers["Booking Workers (scaled)"]
    Workers --> Seat["Seat Allocation"]
```

The queue exists specifically so that a Tatkal-opening spike does not send
every booking request directly into seat-allocation database work. Booking
creation is asynchronous; actual seat allocation occurs in the Booking Service
consumer. See ADR-002 and ADR-006.

## Payment saga

```mermaid
stateDiagram-v2
    [*] --> BookingCreated
    BookingCreated --> SeatReserved
    SeatReserved --> PaymentProcessing
    PaymentProcessing --> BookingConfirmed: payment succeeded
    PaymentProcessing --> SeatReleased: payment failed
    SeatReleased --> BookingCancelled
```

## Outbox pattern

```mermaid
flowchart LR
    subgraph "Single DB transaction"
        BookingRow["Bookings row"]
        OutboxRow["OutboxMessages row"]
    end
    BookingRow -.same commit.- OutboxRow
    OutboxRow --> Publisher["Background publisher"]
    Publisher --> MQ["RabbitMQ"]
```

Guarantees the booking write and the "I intend to publish this event"
write commit atomically — see ADR-006.

## Deployment topology

```mermaid
flowchart TB
    subgraph "docker compose"
        GW[api-gateway]
        US[user-service]
        SS[search-service]
        BS[booking-service]
        PS[payment-service]
        NS[notification-service]
        PG[(postgres)]
        RD[(redis)]
        MQ[(rabbitmq)]
        Prom[prometheus]
        Graf[grafana]
        Jae[jaeger]
    end
    GW --> US & SS & BS & PS
    BS & PS & NS --> MQ
    US & SS & BS & PS --> PG
    BS & SS --> RD
```

## Phase plan

The source implementation now covers all 20 planned phases. The checkboxes
below describe implementation status, not successful runtime verification.
Runtime verification requires the .NET SDK and Docker.

- [x] **Phase 1** — architecture & repository structure
- [x] **Phase 2** — database entities, DbContexts, hand-authored SQL schemas
- [x] **Phase 3** — User & Search services
- [x] **Phase 4** — Booking Service
- [x] **Phase 5** — seat concurrency control
- [x] **Phase 6** — Redis integration
- [x] **Phase 7** — RabbitMQ / MassTransit
- [x] **Phase 8** — payment saga
- [x] **Phase 9** — Booking Service transactional outbox
- [x] **Phase 10** — idempotency
- [x] **Phase 11** — rate limiting & bot simulation
- [x] **Phase 12** — booking expiration
- [x] **Phase 13** — event sourcing
- [x] **Phase 14** — SignalR real-time status
- [x] **Phase 15** — observability (OTel/Prometheus/Grafana/Jaeger wiring)
- [x] **Phase 16** — load-test scripts and DLQ admin API
- [x] **Phase 17** — concurrency/integration test projects
- [x] **Phase 18** — optimization notes and bottleneck analysis
- [x] **Phase 19** — documentation
- [x] **Phase 20** — Docker Compose and health checks

### Remaining runtime/production requirements

- [ ] Run `dotnet restore/build/test` successfully in a real .NET 8 environment.
- [ ] Run Docker Compose end-to-end and verify all health checks.
- [ ] Run the same-seat concurrency test against PostgreSQL/Testcontainers.
- [ ] Add a Payment Service transactional outbox.
- [ ] Add an outbox/inbox retention and cleanup strategy.
- [ ] Add a Redis SignalR backplane before horizontally scaling Booking Service.
- [ ] Run k6/NBomber benchmarks and replace placeholder performance claims with measured results.
- [ ] Validate failure/recovery scenarios under actual infrastructure faults.

See individual ADRs in `docs/adr/` for the reasoning behind key choices,
and `docs/failure-scenarios/` for the failure modes and mechanisms.
