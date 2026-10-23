'use strict';

const crypto = require('crypto');
const http = require('http');
const { buildPrintJob } = require('./escpos');

const MAX_BODY_BYTES = 256 * 1024;
const MAX_TEXT_LENGTH = 64 * 1024;

class HttpError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

const sha256 = value => crypto.createHash('sha256').update(String(value)).digest();

function isAuthorized(req, apiKey) {
  const supplied = req.headers['x-api-key'];
  return typeof supplied === 'string' && crypto.timingSafeEqual(sha256(supplied), sha256(apiKey));
}

function readJson(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on('data', chunk => {
      size += chunk.length;
      if (size > MAX_BODY_BYTES) {
        reject(new HttpError(413, 'Request body is too large'));
        req.destroy();
        return;
      }
      chunks.push(chunk);
    });
    req.on('end', () => {
      try {
        resolve(JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}'));
      } catch {
        reject(new HttpError(400, 'Request body must be valid JSON'));
      }
    });
    req.on('error', reject);
  });
}

function sendJson(res, status, body) {
  res.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
  res.end(JSON.stringify(body));
}

function sampleTicket(outletId) {
  const now = new Date().toLocaleString('en-IN', { timeZone: 'Asia/Kolkata' });
  return [
    '              MAA TARA CAFE',
    '           PRINT BRIDGE TEST',
    '------------------------------------------------',
    `Time   : ${now}`,
    `Outlet : ${outletId || 'any'}`,
    '------------------------------------------------',
    '*** TAKEAWAY - PACK AS PARCEL ***',
    'TOKEN: K-000',
    '2 x Masala Tea',
    '1 x Veg Sandwich',
    '------------------------------------------------',
    'If you can read this, KOT printing works.'
  ].join('\n');
}

/**
 * Creates the bridge HTTP server.
 * Routes: GET /health, POST /print (called by the cafe API), POST /test-print.
 */
function createServer({ config, queue, printer, logger }) {
  const recentJobs = new Map();
  let jobCounter = 0;

  const isRecentDuplicate = fingerprint => {
    const windowMs = config.duplicateWindowSeconds * 1000;
    if (!windowMs) return false;
    const now = Date.now();
    for (const [key, printedAt] of recentJobs) {
      if (now - printedAt > windowMs) recentJobs.delete(key);
    }
    return recentJobs.has(fingerprint);
  };

  const print = async (text, copies, meta) => {
    const jobId = `${Date.now().toString(36)}-${++jobCounter}`;
    const data = buildPrintJob(text, { copies, feedLines: config.feedLines, cut: config.cut, beep: config.beep });
    const started = Date.now();
    try {
      await queue.enqueue(data, { jobId, ...meta });
      logger.info('Printed', { jobId, ...meta, copies, bytes: data.length, ms: Date.now() - started });
      return jobId;
    } catch (error) {
      logger.error('Print failed', { jobId, ...meta, error: error.message });
      throw new HttpError(502, 'Printer is unavailable; the job was not printed');
    }
  };

  const handlePrint = async (req, res) => {
    const body = await readJson(req);
    const text = typeof body.kotText === 'string' ? body.kotText : body.invoiceText;
    if (typeof text !== 'string' || !text.trim()) throw new HttpError(400, 'kotText or invoiceText is required');
    if (text.length > MAX_TEXT_LENGTH) throw new HttpError(413, 'Ticket text is too long');

    const outletId = typeof body.outletId === 'string' ? body.outletId.trim() : '';
    if (config.outletId && outletId && outletId !== config.outletId) {
      logger.warn('Rejected job for another outlet', { orderId: body.orderId, outletId });
      throw new HttpError(409, 'This printer is configured for a different outlet');
    }

    const requestedCopies = Number.parseInt(body.copies, 10);
    const copies = Math.min(Math.max(Number.isFinite(requestedCopies) ? requestedCopies : 1, 1), config.maxCopies);
    const documentType = body.documentType === 'invoice' ? 'invoice' : 'kot';
    const meta = { orderId: body.orderId, trigger: body.trigger, documentType };

    // The API retries jobs it believes failed (for example after a timeout), so identical
    // tickets inside the duplicate window are acknowledged without printing again.
    const fingerprint = crypto.createHash('sha256')
      .update(JSON.stringify([body.orderId, body.trigger, documentType, text]))
      .digest('hex');
    if (isRecentDuplicate(fingerprint)) {
      logger.info('Skipped duplicate job', meta);
      sendJson(res, 200, { status: 'duplicate-skipped' });
      return;
    }

    recentJobs.set(fingerprint, Date.now());
    let jobId;
    try {
      jobId = await print(text, copies, meta);
    } catch (error) {
      recentJobs.delete(fingerprint);
      throw error;
    }
    recentJobs.set(fingerprint, Date.now());
    sendJson(res, 200, { status: 'printed', jobId });
  };

  const handler = async (req, res) => {
    const url = new URL(req.url, 'http://bridge.local');
    try {
      if (req.method === 'GET' && url.pathname === '/health') {
        sendJson(res, 200, { status: 'ok', printer: await printer.check(), queued: queue.pending });
        return;
      }
      if (req.method === 'POST' && (url.pathname === '/print' || url.pathname === '/test-print')) {
        if (!isAuthorized(req, config.apiKey)) {
          logger.warn('Rejected unauthorised request', { path: url.pathname, remote: req.socket.remoteAddress });
          throw new HttpError(401, 'Invalid or missing X-Api-Key');
        }
        if (url.pathname === '/print') {
          await handlePrint(req, res);
        } else {
          const jobId = await print(sampleTicket(config.outletId), 1, { trigger: 'BridgeTest' });
          sendJson(res, 200, { status: 'printed', jobId });
        }
        return;
      }
      throw new HttpError(404, 'Not found');
    } catch (error) {
      const status = error instanceof HttpError ? error.status : 500;
      if (status === 500) logger.error('Unexpected error', { error: error.message });
      if (!res.headersSent) sendJson(res, status, { error: status === 500 ? 'Internal error' : error.message });
    }
  };

  const server = http.createServer(handler);
  server.requestTimeout = 30000;
  return server;
}

module.exports = { createServer };
