<#
.SYNOPSIS
  UI smoke test for PixelForge: starts the built app, visits every tab, checks the menu bar,
  optionally screenshots ONLY the app window (PrintWindow, never the screen), then closes it.
.EXAMPLE
  powershell -File tools/smoke.ps1
  powershell -File tools/smoke.ps1 -ShotDir $env:TEMP\pf-shots
#>
param(
    [string]$Exe = "$PSScriptRoot\..\src\PixelForge.App\bin\Debug\net10.0-windows\PixelForge.App.exe",
    [string]$ShotDir = ""
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class PfWin {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  public struct R { public int L, T, Ri, B; } }
'@

function Find-All($root, $type) {
    $A = [System.Windows.Automation.AutomationElement]
    $root.FindAll('Descendants', (New-Object System.Windows.Automation.PropertyCondition $A::ControlTypeProperty, $type))
}

function Save-Shot($h, $file) {
    $r = New-Object PfWin+R; [PfWin]::GetWindowRect($h, [ref]$r) | Out-Null
    $bmp = New-Object System.Drawing.Bitmap ($r.Ri - $r.L), ($r.B - $r.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [PfWin]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc); $bmp.Save($file); $bmp.Dispose()
}

if (-not (Test-Path $Exe)) { throw "Build first: $Exe not found." }
$p = Start-Process $Exe -PassThru
$failures = @()
try {
    for ($i = 0; $i -lt 50 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 200; $p.Refresh() }
    Start-Sleep 2
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    $tabs = Find-All $root ([System.Windows.Automation.ControlType]::TabItem)
    if ($tabs.Count -ne 4) { $failures += "expected 4 tabs, found $($tabs.Count)" }
    foreach ($t in $tabs) {
        $name = $t.Current.Name
        $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 700
        $menu = (Find-All $root ([System.Windows.Automation.ControlType]::Menu))[0]
        $items = $menu.FindAll('Children', [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name -replace '_', '' }
        foreach ($required in 'File', 'Options', 'Help') {
            if ($items -notcontains $required) { $failures += "${name}: menu '$required' missing" }
        }
        Write-Host ("{0,-8} menus: {1}" -f $name, ($items -join ', '))
        if ($ShotDir) { New-Item -ItemType Directory -Force $ShotDir | Out-Null; Save-Shot $p.MainWindowHandle (Join-Path $ShotDir "$name.png") }
    }
    $p.CloseMainWindow() | Out-Null
    if (-not $p.WaitForExit(5000)) { $failures += 'app did not exit within 5 s after closing' }
}
finally {
    if (-not $p.HasExited) { Stop-Process $p -Force }
}
if ($failures) { $failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }; exit 1 }
Write-Host 'SMOKE OK' -ForegroundColor Green
