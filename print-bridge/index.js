'use strict';

const { loadConfig } = require('./src/config');
const { createLogger } = require('./src/logger');
const { createPrinter, PrintQueue } = require('./src/printer');
const { createServer } = require('./src/server');

let config;
try {
  config = loadConfig();
} catch (error) {
  console.error(error.message);
  console.error('Copy bridge.config.example.json to bridge.config.json and edit it. See README.md.');
  process.exit(1);
}

const logger = createLogger(config);
logger.cleanup();
setInterval(() => logger.cleanup(), 24 * 60 * 60 * 1000).unref();

const printer = createPrinter(config);
const queue = new PrintQueue(printer.send, config, logger);
const server = createServer({ config, queue, printer, logger });

server.listen(config.listenPort, config.listenHost, async () => {
  logger.info('Print bridge started', {
    listen: `http://${config.listenHost}:${config.listenPort}`,
    printer: printer.describe(),
    outletId: config.outletId || 'any'
  });
  logger.info('Printer status', { printer: await printer.check() });
});

server.on('error', error => {
  logger.error('Server error', { error: error.message });
  process.exit(1);
});

const shutdown = signal => {
  logger.info('Shutting down', { signal });
  server.close(() => process.exit(0));
  setTimeout(() => process.exit(0), 5000).unref();
};
process.on('SIGINT', () => shutdown('SIGINT'));
process.on('SIGTERM', () => shutdown('SIGTERM'));
