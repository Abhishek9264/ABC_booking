// Test E: 100,000 requests against nonexistent booking IDs — measures the
// impact of negative caching (or its absence) and confirms a flood of
// guaranteed-404s doesn't itself become a denial-of-service vector against
// the database.
import http from 'k6/http';
import { BASE_URL } from './common.js';

export const options = {
  scenarios: {
    invalid_resource_flood: {
      executor: 'constant-arrival-rate',
      rate: 2000,
      timeUnit: '1s',
      duration: '50s',
      preAllocatedVUs: 500,
      maxVUs: 2000,
    },
  },
};

export default function () {
  const fakeId = '00000000-0000-0000-0000-' + Math.floor(Math.random() * 1e12).toString().padStart(12, '0');
  http.get(`${BASE_URL}/api/bookings/${fakeId}`);
}
