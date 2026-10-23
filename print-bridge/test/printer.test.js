'use strict';

const test = require('node:test');
const assert = require('node:assert');
const net = require('net');
const { sendTcp, PrintQueue } = require('../src/printer');

test('sendTcp delivers the full job to a raw TCP printer', async () => {
  const received = [];
  const server = net.createServer(socket => socket.on('data', chunk => received.push(chunk)));
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const closed = new Promise(resolve => server.on('connection', socket => socket.on('close', resolve)));

  await sendTcp({ host: '127.0.0.1', port: server.address().port, timeoutMs: 2000 }, Buffer.from('KOT'));
  await closed;
  server.close();

  assert.strictEqual(Buffer.concat(received).toString(), 'KOT');
});

test('sendTcp rejects when the printer is offline', async () => {
  const server = net.createServer();
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const port = server.address().port;
  await new Promise(resolve => server.close(resolve));

  await assert.rejects(sendTcp({ host: '127.0.0.1', port, timeoutMs: 1000 }, Buffer.from('x')));
});

test('PrintQueue prints jobs one at a time in order', async () => {
  const order = [];
  let active = 0;
  const queue = new PrintQueue(async data => {
    active++;
    assert.strictEqual(active, 1, 'jobs must not overlap');
    await new Promise(resolve => setTimeout(resolve, 10));
    order.push(data.toString());
    active--;
  });

  await Promise.all(['1', '2', '3'].map(id => queue.enqueue(Buffer.from(id))));
  assert.deepStrictEqual(order, ['1', '2', '3']);
});

test('PrintQueue retries a failed send and keeps processing after a failure', async () => {
  let attempts = 0;
  const queue = new PrintQueue(async data => {
    attempts++;
    if (data.toString() === 'flaky' && attempts === 1) throw new Error('busy');
    if (data.toString() === 'broken') throw new Error('offline');
  }, { retries: 1, retryDelayMs: 1 });

  await queue.enqueue(Buffer.from('flaky'));
  assert.strictEqual(attempts, 2);
  await assert.rejects(queue.enqueue(Buffer.from('broken')), /offline/);
  await queue.enqueue(Buffer.from('ok'));
  assert.strictEqual(queue.pending, 0);
});
