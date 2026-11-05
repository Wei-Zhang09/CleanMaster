# UI smoke: click the nav item whose text is "关于" (About/Settings page), then screenshot the window.
# Verifies the official-account name + QR actually render.
param([string]$Dll = "bin\Release\net9.0-windows\win-x64\CleanMaster.dll")

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class W3 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
function U8([int[]]$b) { [System.Text.Encoding]::UTF8.GetString([byte[]]$b) }
$aboutText = U8 @(0xE5,0x85,0xB3,0xE4,0xBA,0x8E)   # guan yu

$root = Split-Path -Parent $PSScriptRoot
$png = Join-Path $env:TEMP "cm-settings-page.png"

$p = Start-Process -FilePath "dotnet" -ArgumentList "`"$root\$Dll`"" -PassThru
Start-Sleep -Seconds 6
try {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
    $win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children, $cond)
    if (-not $win) { Write-Host "window not found"; return }
    [W3]::SetForegroundWindow([IntPtr]$win.Current.NativeWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 500

    $textCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $texts = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)
    $target = $null
    foreach ($t in $texts) { if ($t.Current.Name -eq $aboutText) { $target = $t; break } }
    if ($target) {
        $r = $target.Current.BoundingRectangle
        [W3]::SetCursorPos([int]($r.X + $r.Width/2), [int]($r.Y + $r.Height/2)) | Out-Null
        Start-Sleep -Milliseconds 150
        [W3]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
        [W3]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
        Write-Host "clicked about nav"
        Start-Sleep -Seconds 4
    } else { Write-Host "about nav not found" }

    $rect = New-Object W3+RECT
    [W3]::GetWindowRect([IntPtr]$win.Current.NativeWindowHandle, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left; $h = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
    $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host ("screenshot: " + $png)
}
finally { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
