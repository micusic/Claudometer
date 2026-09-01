# Builds Claudometer, registers it to start with Windows, and launches it.
# Undo with:  .\install.ps1 -Uninstall

param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe  = Join-Path $root 'bin\Claudometer.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startMenu = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Claudometer.lnk'
$legacyLnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Token Meter.lnk'

if ($Uninstall) {
    Get-Process Claudometer, TokenMeter -ErrorAction SilentlyContinue | Stop-Process -Force
    foreach ($n in 'Claudometer','TokenMeter') {
        if (Get-ItemProperty -Path $runKey -Name $n -ErrorAction SilentlyContinue) {
            Remove-ItemProperty -Path $runKey -Name $n; Write-Host "removed autostart ($n)"
        }
    }
    if (Test-Path $startMenu) { Remove-Item $startMenu; Write-Host "removed Start Menu shortcut" }
    if (Test-Path $legacyLnk) { Remove-Item $legacyLnk }
    Write-Host "Claudometer uninstalled. Data kept in $env:APPDATA\Claudometer (delete to reset)." -ForegroundColor Green
    return
}

# Stop first: a running instance holds the exe open and the build would fail on it.
Get-Process Claudometer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 400

& (Join-Path $root 'build.ps1')
if (-not (Test-Path $exe)) { throw "build produced no exe" }

if (Get-ItemProperty -Path $runKey -Name TokenMeter -ErrorAction SilentlyContinue) { Remove-ItemProperty -Path $runKey -Name TokenMeter }
Set-ItemProperty -Path $runKey -Name Claudometer -Value "`"$exe`""
Write-Host "autostart registered"

$sh = New-Object -ComObject WScript.Shell
$lnk = $sh.CreateShortcut($startMenu)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = Split-Path -Parent $exe
$lnk.Description = 'Claude usage limits at a glance'
$lnk.Save()
Write-Host "Start Menu shortcut created"

Start-Process $exe

# Windows 11 drops new tray icons into the hidden overflow flyout. An app whose whole job is to
# be glanceable is useless in there, so promote it onto the taskbar proper. The entry only
# exists once the icon has been registered, hence the short wait.
$promoted = $false
$base = 'HKCU:\Control Panel\NotifyIconSettings'
for ($i = 0; $i -lt 20 -and -not $promoted; $i++) {
    Start-Sleep -Milliseconds 500
    if (-not (Test-Path $base)) { continue }
    Get-ChildItem $base | ForEach-Object {
        if ((Get-ItemProperty $_.PSPath).ExecutablePath -eq $exe) {
            New-ItemProperty -Path $_.PSPath -Name IsPromoted -Value 1 -PropertyType DWord -Force | Out-Null
            $promoted = $true
        }
    }
}
if ($promoted) {
    Get-Process Claudometer -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 600
    Start-Process $exe
    Write-Host "tray icon pinned to the taskbar"
} else {
    Write-Host "note: could not pin the tray icon - drag it out of the '^' overflow manually" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Claudometer is running - look in the notification area (tray)." -ForegroundColor Green
Write-Host "  left click  = panel      right click = menu"
Write-Host "  first launch scans your transcripts once (~1s), then refreshes are instant"
