// Shared helpers for the k6 scripts in this directory.
// Usage: k6 run -e BASE_URL=http://localhost:8080 load-tests/test-a-normal.js

export const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';

export function authHeaders(token) {
  return { headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` } };
}

export function randomIdempotencyKey() {
  return `k6-${Date.now()}-${Math.random().toString(36).slice(2)}`;
}
