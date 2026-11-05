# Cold-start baseline (BASE-1)
#
# Why the dotnet host: app.manifest is requireAdministrator, so running the .exe pops UAC and
# cannot be timed unattended. Launching the same app through `dotnet <dll>` skips the apphost
# manifest, so the number measured is runtime + WPF + DI + main-window creation.
# It does NOT include the UAC consent wait.
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\bench-coldstart.ps1
#   powershell ... -File scripts\bench-coldstart.ps1 -Runs 7 -Dll bin\Release\net9.0-windows\CleanMaster.dll
#
# Output: per-run ms + median on stdout, and docs\perf\coldstart.json.
# Kept ASCII-only on purpose: Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI, so any
# non-ASCII text here would arrive garbled.

param(
    [string]$Dll = "bin\Release\net9.0-windows\CleanMaster.dll",
    [int]$Runs = 5,
    [int]$TimeoutMs = 30000
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dllPath = Join-Path $root $Dll
if (-not (Test-Path $dllPath)) { throw "Not found: $dllPath (run dotnet build -c Release first)" }

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W {
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
}
"@

$samples = @()
for ($i = 1; $i -le $Runs; $i++) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath "dotnet" -ArgumentList "`"$dllPath`"" -PassThru -WindowStyle Normal

    $seen = $false
    while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
        Start-Sleep -Milliseconds 20
        $proc = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
        if (-not $proc) { break }
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne [IntPtr]::Zero -and [W]::IsWindowVisible($proc.MainWindowHandle)) {
            $seen = $true
            break
        }
    }

    $ms = $sw.ElapsedMilliseconds
    $sw.Stop()
    try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
    Start-Sleep -Milliseconds 600

    if ($seen) {
        $samples += $ms
        Write-Host ("run {0}: {1} ms" -f $i, $ms)
    } else {
        Write-Host ("run {0}: window not seen within {1} ms" -f $i, $TimeoutMs)
    }
}

if ($samples.Count -eq 0) { throw "No samples collected" }

$sorted = $samples | Sort-Object
$median = $sorted[[int][math]::Floor($sorted.Count / 2)]
$out = [ordered]@{
    measured_at = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    dll         = $Dll
    mode        = "dotnet host (no UAC, no apphost manifest)"
    runs        = $samples.Count
    samples_ms  = $samples
    median_ms   = $median
    min_ms      = $sorted[0]
    max_ms      = $sorted[-1]
    os          = (Get-CimInstance Win32_OperatingSystem).Caption
    cpu         = (Get-CimInstance Win32_Processor).Name
    ram_gb      = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
}

$perfDir = Join-Path $root "docs\perf"
New-Item -ItemType Directory -Force -Path $perfDir | Out-Null
$jsonPath = Join-Path $perfDir "coldstart.json"
$out | ConvertTo-Json -Depth 4 | Set-Content -Path $jsonPath -Encoding UTF8

Write-Host ""
Write-Host ("median: {0} ms  (min {1} / max {2}, n={3})" -f $median, $sorted[0], $sorted[-1], $samples.Count)
Write-Host ("written: {0}" -f $jsonPath)
