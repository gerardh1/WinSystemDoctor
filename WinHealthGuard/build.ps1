# Builds WinHealthGuard.exe with the .NET Framework compiler that ships with Windows.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$outDir = Join-Path $root "bin"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$exe = Join-Path $outDir "WinHealthGuard.exe"
$manifest = Join-Path $root "app.manifest"
$files = @(Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName })
$args = @(
  "/nologo",
  "/optimize+",
  "/target:winexe",
  "/platform:anycpu",
  "/codepage:65001",
  "/utf8output",
  "/debug:pdbonly",
  "/win32manifest:$manifest",
  "/out:$exe",
  "/r:System.dll",
  "/r:System.Core.dll",
  "/r:System.Drawing.dll",
  "/r:System.Windows.Forms.dll",
  "/r:System.Management.dll",
  "/r:System.Web.Extensions.dll"
) + $files
& $csc $args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$config = Join-Path $root "WinHealthGuard.exe.config"
if (Test-Path $config) { Copy-Item $config (Join-Path $outDir "WinHealthGuard.exe.config") -Force }
Write-Host "Built $exe"
