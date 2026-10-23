#!/data/data/com.termux/files/usr/bin/bash
set -euo pipefail

INSTALL_DIR="$HOME/maatara-print-bridge"
PID_FILE="$INSTALL_DIR/supervisor.pid"

[ -f "$INSTALL_DIR/app/index.js" ] || { printf 'Bridge is not installed. Run termux/setup.sh first.\n' >&2; exit 1; }

is_supervisor() {
  local pid="$1"
  [ -r "/proc/$pid/cmdline" ] &&
    tr '\0' ' ' < "/proc/$pid/cmdline" | grep -Fq "$INSTALL_DIR/supervise.sh"
}

if [ -f "$PID_FILE" ]; then
  PID="$(cat "$PID_FILE")"
  if [[ "$PID" =~ ^[0-9]+$ ]] && kill -0 "$PID" 2>/dev/null && is_supervisor "$PID"; then
    printf 'Print bridge supervisor is already running (pid %s).\n' "$PID"
    exit 0
  fi
  rm -f "$PID_FILE"
fi

nohup "$INSTALL_DIR/supervise.sh" >> "$INSTALL_DIR/logs/supervisor.log" 2>&1 </dev/null &
printf 'Started print bridge supervisor (pid %s).\n' "$!"
