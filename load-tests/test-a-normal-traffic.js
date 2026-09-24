// Test A (spec section 27): 100 concurrent users, normal traffic.
// Baseline — establishes p50/p95/p99 latency and error rate before
// looking at anything more aggressive.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { BASE_URL } from './common.js';

export const options = {
  vus: 100,
  duration: '2m',
  thresholds: {
    http_req_duration: ['p(95)<500'],
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  const res = http.get(`${BASE_URL}/api/trains/search?from=NDLS&to=BCT&date=2026-10-01`);
  check(res, { 'status is 200': (r) => r.status === 200 });
  sleep(1);
}
