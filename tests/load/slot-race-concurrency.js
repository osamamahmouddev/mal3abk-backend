import http from 'k6/http';
import { check, group } from 'k6';
import { Counter } from 'k6/metrics';

// TASK-071: Slot reservation race condition simulation
// 50 concurrent users attempt to hold the exact same pitch slot at the exact same instant.
// Verification: Exactly 1 succeeds, 49 rejected (409 Conflict / 400 Bad Request), zero double bookings.

const successfulHolds = new Counter('successful_holds');
const rejectedConflicts = new Counter('rejected_conflicts');

export const options = {
  scenarios: {
    race_condition_burst: {
      executor: 'per-vu-iterations',
      vus: 50,
      iterations: 1,
      maxDuration: '15s',
    },
  },
  thresholds: {
    // Exactly one winner per target slot race
    'successful_holds': ['count==1'],
    // 49 rejected requests
    'rejected_conflicts': ['count==49'],
    // Response time under high contention remains within SLA
    http_req_duration: ['p(95)<300'],
  },
};

const BASE_URL = __ENV.API_BASE_URL || 'https://api.malabak.com';
const PITCH_ID = __ENV.PITCH_ID || '22222222-2222-2222-2222-222222222222';
const VENUE_ID = __ENV.VENUE_ID || '11111111-1111-1111-1111-111111111111';

export default function () {
  const targetDate = new Date();
  targetDate.setDate(targetDate.getDate() + 3); // 3 days in advance
  targetDate.setHours(20, 0, 0, 0); // 8:00 PM slot
  const startUtc = targetDate.toISOString();

  targetDate.setHours(21, 0, 0, 0); // 9:00 PM slot
  const endUtc = targetDate.toISOString();

  const payload = JSON.stringify({
    venueId: VENUE_ID,
    pitchId: PITCH_ID,
    startUtc: startUtc,
    endUtc: endUtc,
    quoteToken: 'MOCK_QUOTE_TOKEN_FOR_BENCHMARK',
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${__ENV.USER_JWT || 'MOCK_PLAYER_JWT'}`,
      'Idempotency-Key': `race-${__VU}-${Date.now()}`,
    },
  };

  const res = http.post(`${BASE_URL}/api/v1/bookings`, payload, params);

  if (res.status === 201 || res.status === 200) {
    successfulHolds.add(1);
  } else if (res.status === 409 || res.status === 400) {
    rejectedConflicts.add(1);
  }

  check(res, {
    'status is either success or handled conflict': (r) =>
      r.status === 201 || r.status === 200 || r.status === 409 || r.status === 400,
    'no database 500 deadlock': (r) => r.status !== 500,
  });
}
