// Test D — the most important load test in this repo: 1,000 users, same
// train/coach/seat. Directly validates ADR-004 and the exactly-one-winner
// guarantee under real network + gateway conditions (SeatContentionTests
// proves the same property at the unit level, against Postgres directly).
//
// Requires a valid JWT and a real ScheduleId/CoachId/SeatId from a seeded
// database — set via environment variables.
import http from 'k6/http';
import { BASE_URL, randomIdempotencyKey } from './common.js';

const TOKEN = __ENV.TOKEN;
const SCHEDULE_ID = __ENV.SCHEDULE_ID;
const COACH_ID = __ENV.COACH_ID;
const SEAT_ID = __ENV.SEAT_ID;

export const options = { vus: 1000, iterations: 1000 };

export default function () {
  const payload = JSON.stringify({
    scheduleId: SCHEDULE_ID,
    coachId: COACH_ID,
    seatId: SEAT_ID,
    amount: 500,
    passengers: [{ fullName: 'Load Test', age: 30, gender: 'Other' }],
  });

  http.post(`${BASE_URL}/api/bookings`, payload, {
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${TOKEN}`,
      'Idempotency-Key': randomIdempotencyKey(),
    },
  });
  // Outcome is asserted out-of-band: after the run, query
  // SeatReservations for (SCHEDULE_ID, COACH_ID, SEAT_ID) and confirm
  // exactly one row has Status IN ('Locked','Confirmed').
}
