$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src\RealKeyboardTyper.cs"
$dist = Join-Path $root "dist"
$out = Join-Path $dist "RealKeyboardTyper.exe"
$icon = Join-Path $root "assets\app.ico"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (!(Test-Path -LiteralPath $csc)) {
    throw "Cannot find csc.exe at $csc"
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null

if (!(Test-Path -LiteralPath $icon)) {
    & (Join-Path $root "tools\Generate-AppIcon.ps1")
}

& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /out:$out /win32icon:$icon `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $src

Write-Host "Built $out"
