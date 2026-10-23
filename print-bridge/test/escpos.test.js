'use strict';

const test = require('node:test');
const assert = require('node:assert');
const { sanitizeText, buildPrintJob } = require('../src/escpos');

test('sanitizeText converts rupee, accents and typography to printable ASCII', () => {
  assert.strictEqual(sanitizeText('Total \u20b9120 \u2013 Caf\u00e9 \u201cspecial\u201d'), 'Total Rs.120 - Cafe "special"');
});

test('sanitizeText strips control characters so names cannot inject printer commands', () => {
  assert.strictEqual(sanitizeText('Asha\u001b@\u001dV\u0000\r\nTea'), 'Asha@V\nTea');
});

test('sanitizeText replaces unsupported scripts with a single placeholder', () => {
  assert.strictEqual(sanitizeText('Name: \u0985\u09b6\u09be'), 'Name: ?');
});

test('buildPrintJob initialises, feeds and cuts each copy', () => {
  const job = buildPrintJob('Line 1\nLine 2', { copies: 2, feedLines: 3, cut: true });
  const ticket = Buffer.concat([
    Buffer.from([0x1b, 0x40, 0x1b, 0x74, 0x00]),
    Buffer.from('Line 1\nLine 2\n', 'ascii'),
    Buffer.from([0x1b, 0x64, 0x03, 0x1d, 0x56, 0x42, 0x00])
  ]);
  assert.deepStrictEqual(job, Buffer.concat([ticket, ticket]));
});

test('buildPrintJob prints the takeaway banner and token bold and double height', () => {
  const job = buildPrintJob('*** TAKEAWAY - PACK AS PARCEL ***\nTOKEN: K-012\nTea', { cut: false });
  const emphasised = Buffer.from([0x1b, 0x45, 0x01, 0x1d, 0x21, 0x01]);
  assert.strictEqual(job.indexOf(emphasised), 5);
  assert.ok(job.indexOf(emphasised, 6) > 0, 'token line is emphasised');
  assert.strictEqual(job.indexOf(Buffer.from([0x1d, 0x56])), -1, 'no cut when disabled');
});
