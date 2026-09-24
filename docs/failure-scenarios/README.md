# Failure scenarios this system must handle

Each of these (spec section 22) maps to a specific mechanism, implemented
in the phase noted:

1. **Two users, same seat** → unique constraint + optimistic concurrency /
   Redis pre-filter (ADR-004). Phase 5.
2. **Payment succeeds, confirmation call fails** → saga + outbox ensure the
   confirmation event isn't lost even if the synchronous response is.
   Phase 8/9.
3. **Payment succeeds, service crashes** → Booking Service outbox/event history can recover pending downstream publication; a Payment Service crash after committing payment but before publishing its result remains a known gap until the Payment Service outbox is added. Phase 9/13 + remaining Payment Outbox requirement.
4. **RabbitMQ temporarily unavailable** → outbox holds events until the
   publisher can retry; producers don't block on broker availability.
   Phase 9.
5. **Redis unavailable** → cache-aside falls through to Postgres; rate
   limiting degrades (documented, not silently broken); seat correctness
   is unaffected since Redis is never the source of truth (ADR-003/004).
   Phase 6.
6. **Booking worker crashes mid-job** → RabbitMQ redelivers to another
   consumer; consumer idempotency (inbox pattern) prevents double
   processing. Phase 7.
7. **Duplicate payment callback** → idempotent payment handler keyed on
   provider transaction id; only one `Payments` row transitions to
   `SUCCESS`. Phase 8.
8. **Duplicate booking request** → `Idempotency-Key` + `RequestHash`
   check in `IdempotencyRecords`. Phase 10.
9. **Seat lock expires** → persisted `ExpiresAt` + a recovery sweep on
   startup (not `Task.Delay`), so the seat becomes available again even
   after a restart. Phase 12.
10. **Database temporarily unavailable** → timeout + retry (bounded,
    exponential backoff + jitter) + circuit breaker via the Resilience
    building block. Phase 15.

Load test **Test D** (`docs/benchmarks/`) is the concrete, measured proof
for scenario 1; the others are proven by the concurrency/failure test
suites in `tests/ConcurrencyTests` and `tests/IntegrationTests`.
