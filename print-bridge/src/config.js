'use strict';

const fs = require('fs');
const path = require('path');

const DEFAULTS = {
  listenHost: '127.0.0.1',
  listenPort: 8080,
  apiKey: '',
  outletId: '',
  printer: { mode: 'tcp', host: '', port: 9100, sharePath: '' },
  feedLines: 4,
  cut: true,
  beep: false,
  maxCopies: 3,
  timeoutMs: 3000,
  retries: 1,
  retryDelayMs: 500,
  duplicateWindowSeconds: 30,
  logDir: 'logs',
  logRetentionDays: 14
};

const ENV_OVERRIDES = {
  PRINT_BRIDGE_HOST: ['listenHost', String],
  PRINT_BRIDGE_PORT: ['listenPort', Number],
  PRINT_BRIDGE_API_KEY: ['apiKey', String],
  PRINT_BRIDGE_OUTLET_ID: ['outletId', String],
  PRINTER_MODE: ['printer.mode', String],
  PRINTER_HOST: ['printer.host', String],
  PRINTER_PORT: ['printer.port', Number],
  PRINTER_SHARE_PATH: ['printer.sharePath', String]
};

function setPath(target, dottedPath, value) {
  const keys = dottedPath.split('.');
  let node = target;
  for (const key of keys.slice(0, -1)) node = node[key];
  node[keys[keys.length - 1]] = value;
}

function validate(config) {
  const errors = [];
  if (!config.apiKey || config.apiKey.length < 16 || config.apiKey.startsWith('REPLACE_WITH')) {
    errors.push('apiKey must be set to a secret of at least 16 characters (same value as ThermalPrinter__ApiKey).');
  }
  if (!Number.isInteger(config.listenPort) || config.listenPort < 1 || config.listenPort > 65535) {
    errors.push('listenPort must be between 1 and 65535.');
  }
  const mode = config.printer.mode;
  if (mode === 'tcp') {
    if (!config.printer.host) errors.push('printer.host is required when printer.mode is "tcp".');
    if (!Number.isInteger(config.printer.port) || config.printer.port < 1 || config.printer.port > 65535) {
      errors.push('printer.port must be between 1 and 65535.');
    }
  } else if (mode === 'windows-share') {
    if (!config.printer.sharePath) errors.push('printer.sharePath is required when printer.mode is "windows-share".');
  } else {
    errors.push('printer.mode must be "tcp" or "windows-share".');
  }
  for (const key of ['feedLines', 'maxCopies', 'timeoutMs', 'retries', 'retryDelayMs', 'duplicateWindowSeconds', 'logRetentionDays']) {
    if (!Number.isInteger(config[key]) || config[key] < 0) errors.push(`${key} must be a non-negative integer.`);
  }
  if (config.maxCopies < 1) errors.push('maxCopies must be at least 1.');
  if (config.timeoutMs < 500) errors.push('timeoutMs must be at least 500.');
  if (errors.length) throw new Error(`Invalid print bridge configuration:\n- ${errors.join('\n- ')}`);
  return config;
}

function loadConfig({ baseDir = path.join(__dirname, '..'), env = process.env } = {}) {
  const configPath = env.PRINT_BRIDGE_CONFIG
    ? path.resolve(env.PRINT_BRIDGE_CONFIG)
    : path.join(baseDir, 'bridge.config.json');

  let fileConfig = {};
  if (fs.existsSync(configPath)) {
    fileConfig = JSON.parse(fs.readFileSync(configPath, 'utf8'));
  }

  const config = {
    ...DEFAULTS,
    ...fileConfig,
    printer: { ...DEFAULTS.printer, ...(fileConfig.printer || {}) }
  };

  for (const [envName, [target, convert]] of Object.entries(ENV_OVERRIDES)) {
    if (env[envName] !== undefined && env[envName] !== '') setPath(config, target, convert(env[envName]));
  }

  config.outletId = (config.outletId || '').trim();
  config.logDir = path.resolve(baseDir, config.logDir);
  config.configPath = configPath;
  return validate(config);
}

module.exports = { loadConfig, validate, DEFAULTS };
