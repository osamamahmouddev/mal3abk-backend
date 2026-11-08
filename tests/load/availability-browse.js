import http from 'k6/http';
import { check, sleep } from 'k6';

// TASK-071: High-concurrency read simulation (500 VUs querying venue availability)
export const options = {
  stages: [
    { duration: '30s', target: 100 },  // Ramp up to 100 users
    { duration: '1m', target: 500 },   // Surge to 500 concurrent virtual users
    { duration: '2m', target: 500 },   // Sustained peak load
    { duration: '30s', target: 0 },    // Ramp down
  ],
  thresholds: {
    // 95% of requests must complete below 200ms SLA
    http_req_duration: ['p(95)<200'],
    http_req_failed: ['rate<0.01'],    // Under 1% failure rate
  },
};

const BASE_URL = __ENV.API_BASE_URL || 'https://api.malabak.com';
const SAMPLE_VENUE_ID = __ENV.VENUE_ID || '11111111-1111-1111-1111-111111111111';

export default function () {
  const headers = {
    'Accept': 'application/json',
    'User-Agent': 'k6-load-agent/1.0',
  };

  // 1. Search venues list (Explore flow)
  const listRes = http.get(`${BASE_URL}/api/v1/venues?governorate=Cairo&page=1&pageSize=10`, { headers });
  check(listRes, {
    'explore status is 200': (r) => r.status === 200,
    'venues returned': (r) => r.json('items') !== undefined,
  });

  // 2. Query pitch availability grid
  const today = new Date().toISOString().split('T')[0];
  const availRes = http.get(`${BASE_URL}/api/v1/venues/${SAMPLE_VENUE_ID}/pitches?date=${today}`, { headers });
  check(availRes, {
    'availability status is 200 or 404': (r) => r.status === 200 || r.status === 404,
  });

  sleep(1);
}
