/**
 * Kyntus capacity load test — cible ≥ 300 utilisateurs simultanés.
 *
 * Usage:
 *   k6 run -e BASE_URL=http://localhost:8500 -e KYNTUS_LOAD_TOKEN=<jwt> tests/load/kyntus-capacity.js
 */
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://localhost:8500';
const TOKEN = __ENV.KYNTUS_LOAD_TOKEN || '';

const errorRate = new Rate('kyntus_errors');
const readLatency = new Trend('kyntus_read_ms', true);

export const options = {
  stages: [
    { duration: '2m', target: 50 },
    { duration: '3m', target: 150 },
    { duration: '5m', target: 300 },
    { duration: '5m', target: 300 },
    { duration: '2m', target: 0 },
  ],
  thresholds: {
    http_req_failed: ['rate<0.01'],
    kyntus_errors: ['rate<0.01'],
    kyntus_read_ms: ['p(95)<1000'],
    http_req_duration: ['p(95)<3000'],
  },
};

function authHeaders() {
  const h = { 'Content-Type': 'application/json', Accept: 'application/json' };
  if (TOKEN) h.Authorization = `Bearer ${TOKEN}`;
  return h;
}

export default function () {
  const headers = authHeaders();

  // Health / readiness (toujours)
  let res = http.get(`${BASE}/`, { headers, tags: { name: 'gateway_root' } });
  const okRoot = check(res, { 'gateway reachable': (r) => r.status < 500 });
  errorRate.add(!okRoot);

  // Auth health (via gateway)
  res = http.get(`${BASE}/api/auth/health`, { headers, tags: { name: 'auth_health' } });
  check(res, { 'auth health': (r) => r.status === 200 || r.status === 404 || r.status === 401 });

  // Directory org overview (hot path — must be cached)
  const t0 = Date.now();
  res = http.get(`${BASE}/api/directory/org/overview`, {
    headers,
    tags: { name: 'directory_org_overview' },
  });
  readLatency.add(Date.now() - t0);
  const okDir = check(res, {
    'directory overview ok or auth': (r) =>
      r.status === 200 || r.status === 401 || r.status === 403,
  });
  errorRate.add(res.status >= 500);

  // Planning health
  res = http.get(`${BASE}/api/planning/health`, {
    headers,
    tags: { name: 'planning_health' },
  });
  check(res, { 'planning health': (r) => r.status < 500 });
  errorRate.add(res.status >= 500);

  // Prime health
  res = http.get(`${BASE}/api/prime/health`, {
    headers,
    tags: { name: 'prime_health' },
  });
  check(res, { 'prime health': (r) => r.status < 500 });
  errorRate.add(res.status >= 500);

  // Think time 3–8 s (utilisateurs réels)
  sleep(3 + Math.random() * 5);
}
