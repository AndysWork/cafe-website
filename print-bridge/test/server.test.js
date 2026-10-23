'use strict';

const test = require('node:test');
const assert = require('node:assert');
const { createServer } = require('../src/server');
const { validate, DEFAULTS } = require('../src/config');

const API_KEY = 'test-secret-key-1234567890';
const silentLogger = { info() {}, warn() {}, error() {} };

async function startBridge({ outletId = '', failPrint = false } = {}) {
  const jobs = [];
  const config = validate({
    ...DEFAULTS,
    apiKey: API_KEY,
    outletId,
    printer: { mode: 'tcp', host: '127.0.0.1', port: 9100 }
  });
  const queue = {
    pending: 0,
    enqueue: async data => {
      if (failPrint) throw new Error('offline');
      jobs.push(data);
    }
  };
  const printer = { check: async () => 'reachable' };
  const server = createServer({ config, queue, printer, logger: silentLogger });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;
  return { jobs, base, close: () => new Promise(resolve => server.close(resolve)) };
}

const post = (base, path, body, key = API_KEY) => fetch(`${base}${path}`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json', ...(key ? { 'X-Api-Key': key } : {}) },
  body: JSON.stringify(body)
});

const kot = (overrides = {}) => ({
  orderId: 'order-1', outletId: 'outlet-1', copies: 1, trigger: 'WaiterTakeawayOrder',
  kotText: 'KOT\nTOKEN: K-001\n1 x Tea', ...overrides
});

test('prints an authorised KOT', async () => {
  const bridge = await startBridge();
  try {
    const response = await post(bridge.base, '/print', kot());
    assert.strictEqual(response.status, 200);
    assert.strictEqual((await response.json()).status, 'printed');
    assert.strictEqual(bridge.jobs.length, 1);
    assert.ok(bridge.jobs[0].includes(Buffer.from('1 x Tea')));
  } finally {
    await bridge.close();
  }
});

test('rejects requests without the correct API key', async () => {
  const bridge = await startBridge();
  try {
    assert.strictEqual((await post(bridge.base, '/print', kot(), null)).status, 401);
    assert.strictEqual((await post(bridge.base, '/print', kot(), 'wrong-key')).status, 401);
    assert.strictEqual((await post(bridge.base, '/test-print', {}, 'wrong-key')).status, 401);
    assert.strictEqual(bridge.jobs.length, 0);
  } finally {
    await bridge.close();
  }
});

test('validates the ticket text and clamps copies to maxCopies', async () => {
  const bridge = await startBridge();
  try {
    assert.strictEqual((await post(bridge.base, '/print', kot({ kotText: ' ' }))).status, 400);
    assert.strictEqual((await post(bridge.base, '/print', kot({ copies: 50 }))).status, 200);
    const ticketStart = Buffer.from([0x1b, 0x40]);
    let count = 0;
    for (let i = bridge.jobs[0].indexOf(ticketStart); i >= 0; i = bridge.jobs[0].indexOf(ticketStart, i + 1)) count++;
    assert.strictEqual(count, DEFAULTS.maxCopies);
  } finally {
    await bridge.close();
  }
});

test('prints invoices sent with invoiceText', async () => {
  const bridge = await startBridge();
  try {
    const response = await post(bridge.base, '/print', {
      orderId: 'order-2', documentType: 'invoice', invoiceText: 'INVOICE\nTotal Rs.50'
    });
    assert.strictEqual(response.status, 200);
    assert.ok(bridge.jobs[0].includes(Buffer.from('INVOICE')));
  } finally {
    await bridge.close();
  }
});

test('rejects jobs for a different outlet when an outlet is configured', async () => {
  const bridge = await startBridge({ outletId: 'outlet-1' });
  try {
    assert.strictEqual((await post(bridge.base, '/print', kot({ outletId: 'outlet-2' }))).status, 409);
    assert.strictEqual((await post(bridge.base, '/print', kot())).status, 200);
    assert.strictEqual(bridge.jobs.length, 1);
  } finally {
    await bridge.close();
  }
});

test('skips an identical retry inside the duplicate window', async () => {
  const bridge = await startBridge();
  try {
    await post(bridge.base, '/print', kot());
    const retry = await post(bridge.base, '/print', kot());
    assert.strictEqual((await retry.json()).status, 'duplicate-skipped');
    await post(bridge.base, '/print', kot({ trigger: 'DuplicateKot', kotText: 'DUPLICATE\n1 x Tea' }));
    assert.strictEqual(bridge.jobs.length, 2);
  } finally {
    await bridge.close();
  }
});

test('returns 502 when the printer is unavailable so the API retries', async () => {
  const bridge = await startBridge({ failPrint: true });
  try {
    const response = await post(bridge.base, '/print', kot());
    assert.strictEqual(response.status, 502);
  } finally {
    await bridge.close();
  }
});

test('health reports printer status without authentication', async () => {
  const bridge = await startBridge();
  try {
    const response = await fetch(`${bridge.base}/health`);
    assert.deepStrictEqual(await response.json(), { status: 'ok', printer: 'reachable', queued: 0 });
  } finally {
    await bridge.close();
  }
});

test('config validation requires a real API key and printer target', () => {
  assert.throws(() => validate({ ...DEFAULTS, printer: { ...DEFAULTS.printer, host: '10.0.0.5' } }), /apiKey/);
  assert.throws(() => validate({ ...DEFAULTS, apiKey: API_KEY }), /printer.host/);
  assert.throws(() => validate({ ...DEFAULTS, apiKey: API_KEY, printer: { mode: 'windows-share' } }), /sharePath/);
});
