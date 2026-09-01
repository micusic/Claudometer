# Builds Claudometer.exe with the .NET Framework compiler that ships with Windows.
# No SDK, no NuGet, no network.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw "csc.exe not found - .NET Framework 4.x is required." }

$bin = Join-Path $root 'bin'
if (-not (Test-Path $bin)) { New-Item -ItemType Directory -Path $bin | Out-Null }
$out = Join-Path $bin 'Claudometer.exe'

$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }

# /target:winexe keeps the console window from appearing behind the tray icon.
$refs = @(
    '/r:System.dll'
    '/r:System.Core.dll'
    '/r:System.Drawing.dll'
    '/r:System.Windows.Forms.dll'
    '/r:System.Security.dll'   # ProtectedData (DPAPI) for token storage
)

$args = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu',
          "/out:$out", '/warnaserror-') + $refs + $sources

Write-Host "csc -> $out"
& $csc $args
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }

$size = [math]::Round((Get-Item $out).Length / 1KB, 1)
Write-Host "Built $out ($size KB)" -ForegroundColor Green
