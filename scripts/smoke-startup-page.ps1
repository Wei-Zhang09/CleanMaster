# UI smoke: click the Startup nav item with a real mouse click, then dump rendered texts.
# XAML DataTemplates are instantiated lazily, so a broken badge template only fails when the
# page is actually shown -- this drives the real UI to prove it renders.
# ASCII-only source: Chinese literals are built from UTF-8 byte arrays to stay ANSI-safe.

param([string]$Dll = "bin\Release\net9.0-windows\CleanMaster.dll")

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    public const uint LEFTDOWN = 0x0002;
    public const uint LEFTUP = 0x0004;
}
"@

function U8([int[]]$bytes) { [System.Text.Encoding]::UTF8.GetString([byte[]]$bytes) }
$signedText   = U8 @(0xE6,0x9C,0x89,0xE7,0xAD,0xBE,0xE5,0x90,0x8D)
$unsignedText = U8 @(0xE6,0x97,0xA0,0xE7,0xAD,0xBE,0xE5,0x90,0x8D)

$root = Split-Path -Parent $PSScriptRoot
$outFile = Join-Path $env:TEMP "cm-startup-page-dump.txt"
$lines = New-Object System.Collections.Generic.List[string]

$p = Start-Process -FilePath "dotnet" -ArgumentList "`"$root\$Dll`"" -PassThru
Start-Sleep -Seconds 6

try {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
    $win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children, $cond)
    if (-not $win) { $lines.Add("window not found"); return }

    [Win]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 600

    $radioCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::RadioButton)
    $radios = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $radioCond)
    $lines.Add("radio buttons: " + $radios.Count)
    $wr = $win.Current.BoundingRectangle
    $lines.Add("window rect: " + [int]$wr.X + "," + [int]$wr.Y + " " + [int]$wr.Width + "x" + [int]$wr.Height)
    $ri = 0
    foreach ($r in $radios) { $rr = $r.Current.BoundingRectangle; $lines.Add("  radio[$ri] " + [int]$rr.X + "," + [int]$rr.Y + " " + [int]$rr.Width + "x" + [int]$rr.Height); $ri++ }

    if ($radios.Count -ge 4) {
        $nav = $radios.Item(3)
        $rect = $nav.Current.BoundingRectangle
        $cx = [int]($rect.X + $rect.Width / 2)
        $cy = [int]($rect.Y + $rect.Height / 2)
        $lines.Add("clicking startup nav at $cx,$cy")
        [Win]::SetCursorPos($cx, $cy) | Out-Null
        Start-Sleep -Milliseconds 150
        [Win]::mouse_event([Win]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
        [Win]::mouse_event([Win]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
        Start-Sleep -Seconds 3
    }

    $textCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $texts = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)
    $hit = 0
    foreach ($t in $texts) {
        $n = $t.Current.Name
        if ($n -and ($n -eq $signedText -or $n -eq $unsignedText)) {
            $lines.Add("  badge: " + $n)
            $hit++
        }
    }
    $lines.Add("signature badges found: " + $hit)
    $shown = 0
    foreach ($t in $texts) { if ($shown -ge 60) { break }; $n = $t.Current.Name; if ($n) { $lines.Add("  txt: " + $n); $shown++ } }
    $lines.Add("total text nodes: " + $texts.Count)
}
finally {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    [System.IO.File]::WriteAllLines($outFile, $lines, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("dump written: " + $outFile)
}
