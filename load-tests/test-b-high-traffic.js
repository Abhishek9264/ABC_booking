// Test B: 1,000 concurrent users, high traffic — ramping so we can see
// where latency/error-rate starts degrading rather than just the
// steady-state number.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE_URL } from './common.js';

export const options = {
  stages: [
    { duration: '1m', target: 1000 },
    { duration: '3m', target: 1000 },
    { duration: '1m', target: 0 },
  ],
  thresholds: {
    http_req_duration: ['p(95)<1000'],
    http_req_failed: ['rate<0.05'],
  },
};

export default function () {
  const res = http.get(`${BASE_URL}/api/trains/search?from=NDLS&to=BCT&date=2026-10-01`);
  check(res, { 'status is 200': (r) => r.status === 200 });
  sleep(0.5);
}
