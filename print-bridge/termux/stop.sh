#!/data/data/com.termux/files/usr/bin/bash
set -euo pipefail

INSTALL_DIR="$HOME/maatara-print-bridge"
PID_FILE="$INSTALL_DIR/supervisor.pid"

if [ ! -f "$PID_FILE" ]; then
  printf 'Print bridge is not running (no supervisor PID file).\n'
  exit 0
fi

PID="$(cat "$PID_FILE")"
case "$PID" in
  ''|*[!0-9]*) printf 'Invalid supervisor PID file. Remove it only after checking the bridge is stopped: %s\n' "$PID_FILE" >&2; exit 1 ;;
esac

if [ -r "/proc/$PID/cmdline" ] &&
  tr '\0' ' ' < "/proc/$PID/cmdline" | grep -Fq "$INSTALL_DIR/supervise.sh"; then
  kill -TERM "$PID"
  printf 'Stopping print bridge supervisor (pid %s)...\n' "$PID"
else
  rm -f "$PID_FILE"
  printf 'Removed stale supervisor PID file.\n'
fi
