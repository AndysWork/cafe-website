# Maa Tara Cafe — KOT Print Bridge

A small, dependency-free Node.js service that runs on a PC or an Android cashier tablet **inside the cafe** and prints KOTs and bills on an 80 mm ESC/POS thermal printer (tested target: **KP307-UEWB**, USB + LAN + WiFi + BT).

The cafe API runs in Azure and cannot reach a printer on the cafe network directly. The API's outbox sends each ticket over HTTPS to this bridge; the bridge sends it to the printer and returns success or failure so that failed prints are retried.

```
Cafe API (Azure) --HTTPS POST /print + X-Api-Key--> Tunnel --> Print bridge (cafe PC/tablet :8080) --TCP 9100 / USB share--> KP307
```

What the bridge does:
- Checks the shared API key (`X-Api-Key`) with a constant-time comparison.
- Converts text to printer-safe ASCII (`₹` → `Rs.`, accents removed, unsupported scripts → `?`) and strips control characters, so customer names cannot inject printer commands.
- Prints the takeaway banner (`*** … ***`) and `TOKEN:` line bold and double height, then feeds and auto-cuts each copy.
- Prints one job at a time (no interleaved tickets) and retries once on a transient failure.
- Skips an identical job repeated within 30 s, so an API retry after a timeout does not print a second kitchen ticket.
- Optionally rejects jobs for any outlet other than the configured one.
- Writes daily logs to `logs/` (kept 14 days). Ticket text and the API key are never logged.

## 1. Requirements

- An always-on Windows PC/mini-PC, or Android tablet on the same network as a LAN/WiFi printer.
- [Node.js 18 or newer](https://nodejs.org/) (LTS recommended).
- No `npm install` is needed — the bridge uses only built-in Node modules.
- Android tablet setup is described in [§5.1 Termux on Android](#51-run-on-an-android-tablet-with-termux).

## 2. Connect the KP307 printer

### Option A — LAN cable (recommended)
1. Load 80 mm paper and connect the printer to the router with a LAN cable.
2. Print the self-test page (usually: hold **FEED** while switching the printer on) and note its **IP address**.
3. Give the printer a fixed IP, for example `192.168.1.250`, using the vendor's network setting tool or a DHCP reservation in the router. Otherwise the address can change after a power cut.
4. From the bridge PC, check the raw print port: `Test-NetConnection 192.168.1.250 -Port 9100` → `TcpTestSucceeded : True`.
5. Use `"mode": "tcp"` in the configuration.

### Option B — WiFi
Join the printer to the cafe WiFi with the vendor's WiFi configuration tool, give it a fixed IP, and use `"mode": "tcp"` exactly as for LAN. WiFi works, but LAN is more reliable in a kitchen.

### Option C — USB to a Windows bridge PC
1. Install the KP307 Windows driver from the vendor and print a Windows test page.
2. Printer properties → **Sharing** → share it as `KP307`.
3. Use `"mode": "windows-share"` and `"sharePath": "\\\\localhost\\KP307"` in the configuration.
4. If tickets print as garbage or not at all, change the printer's driver to **Generic / Text Only**, which passes ESC/POS bytes through unchanged.

The bridge sends KOTs to a printer over LAN/WiFi (TCP port 9100) or a Windows USB share. Bluetooth is not used for KOT printing: keep the kitchen printer on the cafe WiFi/LAN and reserve its IP address in the router.

## 3. Configure the bridge

1. Copy `bridge.config.example.json` to `bridge.config.json` (this file is git-ignored; never commit it).
2. Generate a strong shared secret in PowerShell:
   ```powershell
   $b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
   ```
3. Edit `bridge.config.json`:

| Setting | Default | Meaning |
|---|---|---|
| `listenHost` | `127.0.0.1` | Keep this so only the local tunnel can reach the bridge. |
| `listenPort` | `8080` | Local HTTP port. |
| `apiKey` | — | **Required.** The secret from step 2; must equal the API's `ThermalPrinter__ApiKey`. |
| `outletId` | empty | Optional. Your outlet's ID; jobs for other outlets get HTTP 409. Leave empty to accept all. |
| `printer.mode` | `tcp` | `tcp` (LAN/WiFi) or `windows-share` (USB). |
| `printer.host` / `printer.port` | — / `9100` | Printer IP and raw port for `tcp`. |
| `printer.sharePath` | — | Shared printer path for `windows-share`, e.g. `\\localhost\KP307`. |
| `feedLines` | `4` | Blank lines fed before the cut. |
| `cut` | `true` | Auto-cut after each copy. |
| `beep` | `false` | Sound the buzzer after a job (only if your model has one). |
| `maxCopies` | `3` | Upper limit for copies requested by the API. |
| `timeoutMs`, `retries`, `retryDelayMs` | `3000`, `1`, `500` | Printer timeouts. Worst case stays below the API's 8-second timeout; do not raise these much. |
| `duplicateWindowSeconds` | `30` | Identical repeat jobs within this window are not printed again. `0` disables. |
| `logDir`, `logRetentionDays` | `logs`, `14` | Log location and retention. |

Environment variables override the file: `PRINT_BRIDGE_API_KEY`, `PRINT_BRIDGE_PORT`, `PRINT_BRIDGE_HOST`, `PRINT_BRIDGE_OUTLET_ID`, `PRINTER_MODE`, `PRINTER_HOST`, `PRINTER_PORT`, `PRINTER_SHARE_PATH`, and `PRINT_BRIDGE_CONFIG` (path to another config file).

## 4. Run and test locally

```powershell
cd print-bridge
npm start
```

In a second window:

```powershell
Invoke-RestMethod http://127.0.0.1:8080/health          # printer: reachable
Invoke-RestMethod http://127.0.0.1:8080/test-print -Method Post -Headers @{ 'X-Api-Key' = '<your apiKey>' }
```

A sample ticket with a takeaway banner and token should print and cut.

## 5. Start automatically

### 5.1 Run on an Android tablet with Termux

This is for a cashier tablet on the cafe WiFi and a kitchen KOT printer on the same network. The tablet runs the bridge and Cloudflare Tunnel; KOT jobs then travel from the Azure API through the tunnel to the tablet, then over WiFi to the kitchen printer. The printer does **not** pair to the tablet over Bluetooth.

#### Prepare Termux

1. Install **Termux**, **Termux:Boot**, and **Termux:API** from F-Droid (do not mix F-Droid and Play Store builds).
2. Open Termux and install the required packages:
   ```sh
   pkg update
   pkg install nodejs cloudflared termux-api
   termux-setup-storage
   ```
3. Open Termux:API once after installing it, then open Termux again.
4. Copy the `print-bridge` folder into the tablet's `Download` folder (or clone the repository in Termux). In Termux:
   ```sh
   cd ~/storage/shared/Download/print-bridge
   bash termux/setup.sh
   ```
5. The setup asks for the printer's fixed IP, optional outlet ID, and Cloudflare Tunnel token. It generates an API key and displays it **once**; copy it into Azure `ThermalPrinter__ApiKey`. The token and bridge API key are saved under `$HOME/.config/maatara-print-bridge/` with owner-only file permissions. Do not share the generated key or commit either secret.
6. In the Cloudflare Zero Trust dashboard, create a remotely managed tunnel and a public hostname (for example `kot.yourdomain.in`) with service `http://127.0.0.1:8080`. Use the token for that tunnel when the setup script prompts. Check that `https://kot.yourdomain.in/health` responds.
7. In Azure, set `ThermalPrinter__KotEndpoint` to `https://kot.yourdomain.in/print` and `ThermalPrinter__ApiKey` to the generated key. Keep `ThermalPrinter__Enabled=true`.
8. Check the tablet's local bridge with:
   ```sh
   ~/maatara-print-bridge/status.sh
   ```
   Then, as admin, call `POST /api/kitchen/kot/test-print` and verify the ticket comes out in the kitchen.

The setup installs a Termux:Boot entry and supervises both Node and `cloudflared`, restarting either if it exits. Useful commands:

```sh
~/maatara-print-bridge/start.sh
~/maatara-print-bridge/status.sh
~/maatara-print-bridge/stop.sh
```

For reliable operation, keep the tablet plugged in, disable battery optimization for Termux and Termux:Boot, enable the device's vendor-specific background/auto-start permission, and allow Termux notifications. Termux:Boot runs after the tablet restarts; Android settings may still restrict background processes. `termux-wake-lock` helps keep the CPU awake but cannot prevent every vendor's process-killing behavior. A tablet is less reliable than an always-on mini-PC; use a PC if missing a KOT is unacceptable.

The tablet must stay on the cafe WiFi, and the printer needs a fixed/reserved IP. USB `windows-share` mode is not supported on Android. Keep the bridge listener at `127.0.0.1`; do not expose its port directly to the internet.

### 5.2 Start automatically with Windows

From an **Administrator** PowerShell:

```powershell
cd print-bridge
.\scripts\install-service.ps1
```

This registers the scheduled task `MaaTaraPrintBridge`, which starts at boot (before anyone logs in), restarts every minute if it stops, and checks `/health`. For USB share mode, if printing fails while running as SYSTEM, reinstall with your account: `.\scripts\install-service.ps1 -Credential (Get-Credential)`.

Remove it with `.\scripts\uninstall-service.ps1`.

## 6. Expose the bridge to the API securely

Do **not** forward a router port to the printer or bridge. Use an outbound tunnel that gives an HTTPS URL:

- **Cloudflare Tunnel** (recommended when your domain is on Cloudflare): Zero Trust dashboard → Networks → Tunnels → Create a remotely managed tunnel → add a public hostname such as `kot.yourdomain.in` pointing to `http://localhost:8080`. On a Windows PC, install the connector using the shown `cloudflared.exe service install <token>` command. For the Android Termux option, follow §5.1; its supervisor runs `cloudflared tunnel run --token ...`.
- **ngrok** with a reserved static domain: `ngrok http 8080 --url=<your-static-domain>` (run it as a service so it survives reboots).

Check from any network: `https://kot.yourdomain.in/health`.

## 7. Configure the cafe API

Set these Function App application settings (Azure Portal → Function App → Environment variables, or `api/local.settings.json` for local development):

| Setting | Value |
|---|---|
| `ThermalPrinter__KotEndpoint` | `https://kot.yourdomain.in/print` |
| `ThermalPrinter__ApiKey` | The same secret as the bridge `apiKey` |
| `ThermalPrinter__Enabled` | `true` (set `false` to pause printing) |
| `ThermalPrinter__KotCopies` | `1` (use `2` for a kitchen copy plus a parcel/runner copy) |
| `ThermalPrinter__InvoiceEndpoint` | Optional. Empty = bills print on the same printer. |
| `ThermalPrinter__InvoiceCopies` | Optional. Defaults to `KotCopies`. |

Then, signed in as admin, call `POST /api/kitchen/kot/test-print` (or place a waiter or takeaway order) and confirm the KOT prints.

## Troubleshooting

| Symptom | Check |
|---|---|
| `/health` shows `printer: unreachable` | Printer power and paper, cable/WiFi, fixed IP, `Test-NetConnection <ip> -Port 9100`. |
| API test print returns "Thermal printer is not configured" | `ThermalPrinter__KotEndpoint` is missing in the Function App settings. |
| API test print returns "dispatch failed" | Tunnel down, wrong URL, or API key mismatch (bridge log shows `Rejected unauthorised request`). |
| Bridge log shows `Rejected job for another outlet` | `outletId` in `bridge.config.json` does not match the outlet placing orders. |
| Garbled characters | USB mode: switch the driver to Generic / Text Only. Non-English names print as `?` by design. |
| Paper not cut | Set `"cut": true`, and check the cutter is enabled in the printer's settings. |
| KOT printed twice | Increase `duplicateWindowSeconds`; check the API isn't timing out (`ms` in the bridge log should be well under 8000). |
| Termux setup says `cloudflared` is missing | Run `pkg update && pkg install cloudflared` in Termux, then rerun setup. |
| Termux says `set: pipefail: invalid option name` or `$'\r': command not found` | The scripts were copied with Windows (CRLF) line endings. Copy a fresh `print-bridge` folder from the repository, or in Termux run `sed -i 's/\r$//' termux/*.sh`, then rerun `bash termux/setup.sh`. |
| Termux says `CANNOT LINK EXECUTABLE "node": library "lib….so" not found` or `cannot locate symbol "…" referenced by …/bin/node` | Termux packages are only partly upgraded (Node is newer or older than its libraries). Run `pkg update && pkg upgrade -y`, then `pkg reinstall nodejs` (or `pkg uninstall nodejs && pkg install nodejs-lts`), and confirm `node -v` works before rerunning setup. Use Termux from F-Droid, not the outdated Play Store build. |
| Tablet bridge works but Azure cannot print | Check `~/maatara-print-bridge/status.sh`, the Cloudflare public hostname origin (`http://127.0.0.1:8080`), the Azure endpoint/API key, and that Termux is allowed to run in the background. |
| Tablet bridge stops after a reboot or screen-off | Open Termux once, confirm Termux:Boot is installed from the same source, disable battery optimization and enable the device's background/auto-start permission; inspect `~/maatara-print-bridge/logs/supervisor.log`. |

Logs: `print-bridge/logs/bridge-YYYY-MM-DD.log`.

## Development

```powershell
npm test
```

Unit tests cover ESC/POS output, text sanitising, TCP delivery, queueing/retries, authentication, outlet filtering, copy limits, and duplicate suppression.
