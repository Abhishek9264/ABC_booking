# Load tests (k6)

Five scripts matching spec section 27 / `docs/benchmarks/README.md`:

| Script | Scenario |
|---|---|
| `test-a-normal-traffic.js` | 100 concurrent users, baseline |
| `test-b-high-traffic.js` | 1,000 concurrent users, ramping |
| `test-c-tatkal-spike.js` | 10,000+ arrival-rate spike |
| `test-d-same-seat-contention.js` | 1,000 users racing for one seat — validates ADR-004 |
| `test-e-invalid-resource-attack.js` | 100,000 requests against nonexistent booking IDs |

## Running

```bash
docker compose up -d
k6 run -e BASE_URL=http://localhost:8080 load-tests/test-a-normal-traffic.js

# Test D needs a real token + seeded schedule/coach/seat ids:
k6 run \
  -e BASE_URL=http://localhost:8080 \
  -e TOKEN=$(curl -s -X POST http://localhost:8080/api/users/login -d '{"email":"...","password":"..."}' -H 'Content-Type: application/json' | jq -r .token) \
  -e SCHEDULE_ID=... -e COACH_ID=... -e SEAT_ID=... \
  load-tests/test-d-same-seat-contention.js
```

No results are checked into this repo yet — `docs/benchmarks/README.md` is
populated from real runs in Phase 16/18, per Critical Engineering Rule #19
(never fabricate benchmark numbers).
