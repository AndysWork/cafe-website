'use strict';

const fs = require('fs');
const path = require('path');

const DAY_MS = 24 * 60 * 60 * 1000;

function createLogger({ logDir, logRetentionDays = 14, console: output = console } = {}) {
  if (logDir) fs.mkdirSync(logDir, { recursive: true });

  const write = (level, message, meta) => {
    const timestamp = new Date().toISOString();
    const details = meta && Object.keys(meta).length ? ` ${JSON.stringify(meta)}` : '';
    const line = `${timestamp} ${level.padEnd(5)} ${message}${details}`;
    (level === 'ERROR' ? output.error : output.log)(line);
    if (logDir) {
      try {
        fs.appendFileSync(path.join(logDir, `bridge-${timestamp.slice(0, 10)}.log`), `${line}\n`);
      } catch {
        // Logging must never stop printing.
      }
    }
  };

  const cleanup = () => {
    if (!logDir || !logRetentionDays) return;
    const cutoff = Date.now() - logRetentionDays * DAY_MS;
    for (const file of fs.readdirSync(logDir)) {
      const filePath = path.join(logDir, file);
      if (/^bridge-\d{4}-\d{2}-\d{2}\.log$/.test(file) && fs.statSync(filePath).mtimeMs < cutoff) {
        fs.rmSync(filePath, { force: true });
      }
    }
  };

  return {
    info: (message, meta) => write('INFO', message, meta),
    warn: (message, meta) => write('WARN', message, meta),
    error: (message, meta) => write('ERROR', message, meta),
    cleanup
  };
}

module.exports = { createLogger };
