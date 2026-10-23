#!/data/data/com.termux/files/usr/bin/bash
set -euo pipefail

SOURCE_DIR="$(cd "$(dirname "$0")/.." && pwd)"
INSTALL_DIR="$HOME/maatara-print-bridge"
CONFIG_DIR="$HOME/.config/maatara-print-bridge"
BOOT_DIR="$HOME/.termux/boot"

fail() {
  printf 'ERROR: %s\n' "$1" >&2
  exit 1
}

command -v node >/dev/null 2>&1 || fail "Node.js is missing. Install it with: pkg install nodejs"
command -v cloudflared >/dev/null 2>&1 || fail "cloudflared is missing. Install a Termux-compatible cloudflared build, then rerun setup."
command -v termux-wake-lock >/dev/null 2>&1 || printf 'NOTICE: termux-wake-lock is unavailable. Install Termux:API and run pkg install termux-api.\n'

mkdir -p "$INSTALL_DIR/app" "$INSTALL_DIR/logs" "$CONFIG_DIR" "$BOOT_DIR"
cp "$SOURCE_DIR/index.js" "$INSTALL_DIR/app/"
cp -R "$SOURCE_DIR/src" "$INSTALL_DIR/app/"
cp "$SOURCE_DIR/package.json" "$INSTALL_DIR/app/"
cp "$SOURCE_DIR/termux/supervise.sh" "$INSTALL_DIR/supervise.sh"
cp "$SOURCE_DIR/termux/start.sh" "$INSTALL_DIR/start.sh"
cp "$SOURCE_DIR/termux/stop.sh" "$INSTALL_DIR/stop.sh"
cp "$SOURCE_DIR/termux/status.sh" "$INSTALL_DIR/status.sh"
chmod 700 "$INSTALL_DIR" "$INSTALL_DIR/logs" "$CONFIG_DIR"
chmod 700 "$INSTALL_DIR/"*.sh

if [ ! -f "$CONFIG_DIR/bridge.config.json" ]; then
  printf 'Printer fixed WiFi IP address (example 192.168.1.250): '
  read -r PRINTER_HOST
  [ -n "$PRINTER_HOST" ] || fail "A printer IP is required."

  printf 'Outlet ID (optional; press Enter to accept jobs for all outlets): '
  read -r OUTLET_ID

  API_KEY="$(node -e "process.stdout.write(require('crypto').randomBytes(32).toString('base64url'))")"
  umask 077
  PRINTER_HOST="$PRINTER_HOST" OUTLET_ID="$OUTLET_ID" API_KEY="$API_KEY" CONFIG_PATH="$CONFIG_DIR/bridge.config.json" node <<'NODE'
const fs = require('fs');
const config = {
  listenHost: '127.0.0.1',
  listenPort: 8080,
  apiKey: process.env.API_KEY,
  outletId: process.env.OUTLET_ID,
  printer: { mode: 'tcp', host: process.env.PRINTER_HOST, port: 9100, sharePath: '' },
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
fs.writeFileSync(process.env.CONFIG_PATH, `${JSON.stringify(config, null, 2)}\n`, { mode: 0o600 });
NODE
  chmod 600 "$CONFIG_DIR/bridge.config.json"
  printf '\nGenerated API key (copy it now to Azure ThermalPrinter__ApiKey):\n%s\n' "$API_KEY"
else
  printf 'Keeping existing bridge configuration: %s\n' "$CONFIG_DIR/bridge.config.json"
fi

if [ ! -f "$CONFIG_DIR/cloudflared-token" ]; then
  printf '\nPaste the Cloudflare Tunnel token (input hidden): '
  read -rs CLOUDFLARED_TOKEN
  printf '\n'
  [ -n "$CLOUDFLARED_TOKEN" ] || fail "A Cloudflare Tunnel token is required."
  umask 077
  printf '%s' "$CLOUDFLARED_TOKEN" > "$CONFIG_DIR/cloudflared-token"
  chmod 600 "$CONFIG_DIR/cloudflared-token"
fi

BOOT_SCRIPT="$BOOT_DIR/maatara-print-bridge"
cat > "$BOOT_SCRIPT" <<BOOT
#!/data/data/com.termux/files/usr/bin/bash
if command -v termux-wake-lock >/dev/null 2>&1; then
  termux-wake-lock
fi
"$INSTALL_DIR/start.sh"
BOOT
chmod 700 "$BOOT_SCRIPT"

printf '\nSetup files installed. Starting the bridge and tunnel...\n'
"$INSTALL_DIR/start.sh"
sleep 5

if node -e "fetch('http://127.0.0.1:8080/health').then(r => { if (!r.ok) process.exit(1); return r.text(); }).then(console.log).catch(() => process.exit(1))"; then
  printf '\nBridge health check passed.\n'
else
  printf '\nBridge did not answer yet. Check %s/logs/bridge.log and %s/logs/supervisor.log\n' "$INSTALL_DIR" "$INSTALL_DIR"
  exit 1
fi

printf '\nInstalled. Use %s/status.sh for status and %s/stop.sh to stop it.\n' "$INSTALL_DIR" "$INSTALL_DIR"
printf 'Configure the Cloudflare public hostname origin as http://127.0.0.1:8080.\n'
printf 'Set Azure ThermalPrinter__KotEndpoint to https://<your-hostname>/print.\n'
