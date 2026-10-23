#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Installs the print bridge as a Windows scheduled task that starts at boot and restarts on failure.
.PARAMETER Credential
  Optional Windows account to run the bridge. Defaults to SYSTEM, which is fine for LAN/WiFi (tcp) printers.
  Use your own account if "windows-share" mode cannot write to the shared USB printer as SYSTEM.
#>
param(
    [string]$TaskName = 'MaaTaraPrintBridge',
    [string]$NodePath,
    [pscredential]$Credential
)

$ErrorActionPreference = 'Stop'
$bridgeDir = Split-Path -Parent $PSScriptRoot
$entryPoint = Join-Path $bridgeDir 'index.js'
$configPath = Join-Path $bridgeDir 'bridge.config.json'

if (-not $NodePath) { $NodePath = (Get-Command node -ErrorAction Stop).Source }
if (-not (Test-Path $configPath)) {
    throw "Missing $configPath. Copy bridge.config.example.json to bridge.config.json and edit it first."
}

$config = Get-Content $configPath -Raw | ConvertFrom-Json
$port = if ($config.listenPort) { $config.listenPort } else { 8080 }

$action = New-ScheduledTaskAction -Execute $NodePath -Argument "`"$entryPoint`"" -WorkingDirectory $bridgeDir
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
    -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
$description = 'Maa Tara Cafe KOT print bridge: forwards print jobs from the cafe API to the thermal printer.'

if ($Credential) {
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
        -Description $description -User $Credential.UserName `
        -Password $Credential.GetNetworkCredential().Password -RunLevel Highest -Force | Out-Null
} else {
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
        -Description $description -Principal $principal -Force | Out-Null
}

Start-ScheduledTask -TaskName $TaskName
Write-Host "Installed and started scheduled task '$TaskName'."

Start-Sleep -Seconds 3
try {
    $health = Invoke-RestMethod "http://127.0.0.1:$port/health" -TimeoutSec 5
    Write-Host "Bridge is running. Printer status: $($health.printer)"
} catch {
    Write-Warning "Bridge did not answer on port $port yet. Check the logs folder in $bridgeDir."
}
