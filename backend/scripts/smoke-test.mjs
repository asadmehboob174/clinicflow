// End-to-end check against a running API with real Keycloak, RabbitMQ, Redis and SQL Server.
// Usage: node scripts/smoke-test.mjs
const API = process.env.API_URL ?? 'http://localhost:5080';
const KEYCLOAK = process.env.KEYCLOAK_URL ?? 'http://localhost:8080';

let failed = 0;
const check = (name, ok, extra = '') => {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name} ${extra}`);
  if (!ok) failed++;
};

async function login(username) {
  const response = await fetch(`${KEYCLOAK}/realms/clinicflow/protocol/openid-connect/token`, {
    method: 'POST',
    body: new URLSearchParams({ grant_type: 'password', client_id: 'clinicflow-web', username, password: 'demo', scope: 'openid email' }),
  });
  if (!response.ok) throw new Error(`Login failed for ${username}: ${response.status} ${await response.text()}`);
  return (await response.json()).access_token;
}

const call = async (token, method, path, body) => {
  const response = await fetch(API + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...(token && { Authorization: `Bearer ${token}` }) },
    body: body && JSON.stringify(body),
  });
  const text = await response.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = text; }
  return { status: response.status, json };
};

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

const ready = await call(null, 'GET', '/health/ready');
check('API is ready', ready.status === 200);

const admin = await login('admin.demo');
const reception = await login('reception.demo');
const sara = await login('sara');

check('No token is refused', (await call(null, 'GET', '/api/patients')).status === 401);
check('Patient token cannot open staff data', (await call(sara, 'GET', '/api/patients')).status === 403);
check('Fake token is refused', (await call('dev:admin', 'GET', '/api/patients')).status === 401);

const doctors = (await call(null, 'GET', '/api/doctors')).json;
const treatments = (await call(null, 'GET', '/api/treatments')).json;
check('Sample doctors and treatments exist', doctors.length >= 3 && treatments.length >= 4);

const doctor = (await call(admin, 'POST', '/api/doctors', { fullName: `Dr. Smoke ${Date.now()}`, specialty: 'Test' })).json;
const days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const hours = days.map((_, day) => ({ day, start: '09:00:00', end: '17:00:00' }));
check('Admin sets working hours', (await call(admin, 'PUT', `/api/doctors/${doctor}/working-hours`, { hours })).status === 204);

const listed = (await call(null, 'GET', '/api/doctors')).json;
check('Redis cache was cleared after the write', listed.some(d => d.id === doctor));

const me = await call(sara, 'GET', '/api/patients/me');
check('Patient finds their profile from the token email', me.status === 200, me.json?.email);

const date = new Date(Date.now() + 8 * 864e5).toISOString().slice(0, 10);
const treatment = treatments.find(t => t.durationMinutes === 30);
const slotsPath = `/api/appointments/slots?doctorId=${doctor}&treatmentId=${treatment.id}&date=${date}`;
const slots = (await call(null, 'GET', slotsPath)).json;
check('Free slots are offered', slots.length > 10, `(${slots.length})`);

const booked = await call(sara, 'POST', '/api/appointments', { patientId: me.json.id, doctorId: doctor, treatmentId: treatment.id, startsAtUtc: slots[0] });
check('Patient books', booked.status === 201);

let notified = false;
for (let i = 0; i < 20 && !notified; i++) {
  await sleep(500);
  const list = (await call(reception, 'GET', '/api/notifications')).json;
  notified = list.some(n => n.appointmentId === booked.json);
}
check('Event went outbox > RabbitMQ > Notifications', notified);

const patients = [];
for (let i = 0; i < 12; i++) {
  const email = `race-${Date.now()}-${i}@example.com`;
  patients.push((await call(reception, 'POST', '/api/patients', { fullName: `Race ${i}`, email, phone: '+971 50 000 0000' })).json);
}
const results = await Promise.all(patients.map(patientId =>
  call(reception, 'POST', '/api/appointments', { patientId, doctorId: doctor, treatmentId: treatment.id, startsAtUtc: slots[4] })));
const created = results.filter(r => r.status === 201).length;
const conflict = results.filter(r => r.status === 409).length;
check('Race on SQL Server: exactly one booking wins', created === 1 && conflict === 11, `(201: ${created}, 409: ${conflict})`);

check('Admin deactivates the doctor', (await call(admin, 'POST', `/api/doctors/${doctor}/deactivate`)).status === 204);
let cancelled = false;
for (let i = 0; i < 20 && !cancelled; i++) {
  await sleep(500);
  const mine = (await call(sara, 'GET', '/api/appointments/mine')).json;
  cancelled = mine.find(a => a.id === booked.json)?.status === 'Cancelled';
}
check('Deactivation cancelled the visit through the queue', cancelled);

console.log(failed ? `\n${failed} check(s) failed` : '\nAll checks passed');
process.exit(failed ? 1 : 0);
