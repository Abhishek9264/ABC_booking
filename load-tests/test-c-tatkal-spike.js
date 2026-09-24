// Test C: 10,000+ concurrent requests arriving in a near-instant spike —
// simulates the actual Tatkal-opening moment (spec section 8). The point
// isn't 0% errors; it's that the SYSTEM degrades in a controlled way
// (429s from the rate limiter, queue depth rising, but Postgres never
// falling over) rather than crashing outright.
import http from 'k6/http';
import { BASE_URL } from './common.js';

export const options = {
  scenarios: {
    tatkal_spike: {
      executor: 'ramping-arrival-rate',
      startRate: 100,
      timeUnit: '1s',
      preAllocatedVUs: 2000,
      maxVUs: 12000,
      stages: [
        { target: 10000, duration: '10s' }, // the spike
        { target: 10000, duration: '30s' },
        { target: 0, duration: '20s' },
      ],
    },
  },
};

export default function () {
  http.get(`${BASE_URL}/api/trains/search?from=NDLS&to=BCT&date=2026-10-01`);
}
