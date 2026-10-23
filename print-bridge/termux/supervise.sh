#!/data/data/com.termux/files/usr/bin/bash
set -euo pipefail

INSTALL_DIR="$HOME/maatara-print-bridge"
APP_DIR="$INSTALL_DIR/app"
LOG_DIR="$INSTALL_DIR/logs"
CONFIG_DIR="$HOME/.config/maatara-print-bridge"
PID_FILE="$INSTALL_DIR/supervisor.pid"

mkdir -p "$LOG_DIR"
cd "$APP_DIR"

BRIDGE_PID=''
TUNNEL_PID=''

stop_children() {
  trap - TERM INT
  [ -z "$BRIDGE_PID" ] || kill -TERM "$BRIDGE_PID" 2>/dev/null || true
  [ -z "$TUNNEL_PID" ] || kill -TERM "$TUNNEL_PID" 2>/dev/null || true
  [ -z "$BRIDGE_PID" ] || wait "$BRIDGE_PID" 2>/dev/null || true
  [ -z "$TUNNEL_PID" ] || wait "$TUNNEL_PID" 2>/dev/null || true
  rm -f "$PID_FILE"
  exit 0
}
trap stop_children TERM INT

start_bridge() {
  PRINT_BRIDGE_CONFIG="$CONFIG_DIR/bridge.config.json" node index.js >> "$LOG_DIR/bridge.log" 2>&1 &
  BRIDGE_PID=$!
  printf 'Bridge process started (pid %s)\n' "$BRIDGE_PID" >> "$LOG_DIR/supervisor.log"
}

start_tunnel() {
  CLOUDFLARED_TOKEN="$(cat "$CONFIG_DIR/cloudflared-token")"
  cloudflared tunnel --no-autoupdate run --token "$CLOUDFLARED_TOKEN" >> "$LOG_DIR/cloudflared.log" 2>&1 &
  TUNNEL_PID=$!
  unset CLOUDFLARED_TOKEN
  printf 'Cloudflare tunnel process started (pid %s)\n' "$TUNNEL_PID" >> "$LOG_DIR/supervisor.log"
}

if [ -f "$PID_FILE" ]; then
  EXISTING_PID="$(cat "$PID_FILE")"
  if [[ "$EXISTING_PID" =~ ^[0-9]+$ ]] &&
    kill -0 "$EXISTING_PID" 2>/dev/null &&
    [ -r "/proc/$EXISTING_PID/cmdline" ] &&
    tr '\0' ' ' < "/proc/$EXISTING_PID/cmdline" | grep -Fq "$INSTALL_DIR/supervise.sh"; then
    printf 'A print bridge supervisor is already running (pid %s).\n' "$EXISTING_PID"
    exit 0
  fi
  rm -f "$PID_FILE"
fi

printf '%s\n' "$$" > "$PID_FILE"
chmod 600 "$PID_FILE"
start_bridge
start_tunnel

while :; do
  sleep 10
  if ! kill -0 "$BRIDGE_PID" 2>/dev/null; then
    wait "$BRIDGE_PID" 2>/dev/null || true
    printf 'Bridge exited; restarting.\n' >> "$LOG_DIR/supervisor.log"
    start_bridge
  fi
  if ! kill -0 "$TUNNEL_PID" 2>/dev/null; then
    wait "$TUNNEL_PID" 2>/dev/null || true
    printf 'Cloudflare tunnel exited; restarting.\n' >> "$LOG_DIR/supervisor.log"
    start_tunnel
  fi
done
