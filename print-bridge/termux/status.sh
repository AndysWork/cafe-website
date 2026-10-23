#!/data/data/com.termux/files/usr/bin/bash
set -euo pipefail

INSTALL_DIR="$HOME/maatara-print-bridge"
PID_FILE="$INSTALL_DIR/supervisor.pid"

PID=''
if [ -f "$PID_FILE" ]; then
  PID="$(cat "$PID_FILE")"
fi
if [[ "$PID" =~ ^[0-9]+$ ]] && [ -r "/proc/$PID/cmdline" ] &&
  tr '\0' ' ' < "/proc/$PID/cmdline" | grep -Fq "$INSTALL_DIR/supervise.sh"; then
  printf 'Supervisor: running (pid %s)\n' "$PID"
else
  printf 'Supervisor: stopped\n'
fi

node -e "fetch('http://127.0.0.1:8080/health').then(async r => { const text = await r.text(); console.log('Bridge health (' + r.status + '): ' + text); if (!r.ok) process.exitCode = 1; }).catch(e => { console.error('Bridge health check failed: ' + e.message); process.exitCode = 1; })"

printf '\nRecent bridge log:\n'
tail -n 20 "$INSTALL_DIR/logs/bridge.log" 2>/dev/null || true
printf '\nRecent tunnel log:\n'
tail -n 20 "$INSTALL_DIR/logs/cloudflared.log" 2>/dev/null || true
