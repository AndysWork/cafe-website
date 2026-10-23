'use strict';

const fs = require('fs');
const net = require('net');

function sendTcp({ host, port, timeoutMs }, data) {
  return new Promise((resolve, reject) => {
    let settled = false;
    const finish = error => {
      if (settled) return;
      settled = true;
      socket.destroy();
      if (error) reject(error);
      else resolve();
    };

    const socket = net.connect({ host, port });
    socket.setTimeout(timeoutMs, () => finish(new Error(`Printer ${host}:${port} timed out after ${timeoutMs} ms`)));
    socket.once('error', finish);
    socket.once('connect', () => {
      socket.end(data, () => finish());
    });
  });
}

function checkTcp({ host, port, timeoutMs }) {
  return new Promise(resolve => {
    const socket = net.connect({ host, port });
    const done = reachable => { socket.destroy(); resolve(reachable); };
    socket.setTimeout(Math.min(timeoutMs, 2000), () => done(false));
    socket.once('error', () => done(false));
    socket.once('connect', () => done(true));
  });
}

/** Creates the printer transport for the configured mode. */
function createPrinter(config) {
  const { mode, host, port, sharePath } = config.printer;
  if (mode === 'windows-share') {
    return {
      describe: () => `Windows shared printer ${sharePath}`,
      send: data => fs.promises.writeFile(sharePath, data),
      check: async () => 'unknown'
    };
  }
  const target = { host, port, timeoutMs: config.timeoutMs };
  return {
    describe: () => `network printer ${host}:${port}`,
    send: data => sendTcp(target, data),
    check: async () => (await checkTcp(target)) ? 'reachable' : 'unreachable'
  };
}

const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

/** Sends jobs one at a time so tickets never interleave, retrying transient failures. */
class PrintQueue {
  constructor(send, { retries = 1, retryDelayMs = 500 } = {}, logger = null) {
    this.send = send;
    this.retries = retries;
    this.retryDelayMs = retryDelayMs;
    this.logger = logger;
    this.tail = Promise.resolve();
    this.pending = 0;
  }

  enqueue(data, meta = {}) {
    this.pending++;
    const job = this.tail
      .then(() => this.sendWithRetry(data, meta))
      .finally(() => { this.pending--; });
    this.tail = job.catch(() => undefined);
    return job;
  }

  async sendWithRetry(data, meta) {
    for (let attempt = 0; ; attempt++) {
      try {
        await this.send(data);
        return;
      } catch (error) {
        if (attempt >= this.retries) throw error;
        this.logger?.warn('Print attempt failed; retrying', { ...meta, attempt: attempt + 1, error: error.message });
        await delay(this.retryDelayMs);
      }
    }
  }
}

module.exports = { createPrinter, sendTcp, checkTcp, PrintQueue };
