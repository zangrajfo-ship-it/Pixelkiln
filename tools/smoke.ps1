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
# settings, autosave and the test project live in a temp folder: the user's real settings are never touched
$work = Join-Path $env:TEMP ("pf-smoke-" + [guid]::NewGuid().ToString('N'))
$env:PIXELFORGE_HOME = Join-Path $work 'home'
$projectDir = Join-Path $work 'game'
New-Item -ItemType Directory -Force $projectDir | Out-Null
Copy-Item "$PSScriptRoot\..\tests\PixelForge.Core.Tests\Fixtures\*" $projectDir
$expectedAssets = (Get-ChildItem $projectDir).Count
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
$p = Start-Process $Exe -ArgumentList "`"$projectDir`"" -PassThru
$failures = @()
try {
    for ($i = 0; $i -lt 50 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 200; $p.Refresh() }
    Start-Sleep 2
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    $A = [System.Windows.Automation.AutomationElement]
    $list = $root.FindFirst('Descendants', (New-Object System.Windows.Automation.PropertyCondition $A::NameProperty, 'Assets'))
    $count = if ($list) { (Find-All $list ([System.Windows.Automation.ControlType]::ListItem)).Count } else { 0 }
    Write-Host "assets in browser: $count (expected $expectedAssets)"
    if ($count -ne $expectedAssets) { $failures += "asset browser shows $count assets, expected $expectedAssets" }
    if (-not (Test-Path (Join-Path $projectDir 'game.pfproj'))) { $failures += 'project file was not created from the folder' }
    $tabs = Find-All $root ([System.Windows.Automation.ControlType]::TabItem)
    if ($tabs.Count -lt 5) { $failures += "expected at least 5 tabs, found $($tabs.Count)" }
    foreach ($t in $tabs) {
        $name = $t.Current.Name
        $t.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 700
        $menu = (Find-All $root ([System.Windows.Automation.ControlType]::Menu))[0]
        $items = $menu.FindAll('Children', [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name -replace '_', '' }
        foreach ($required in 'File', 'Project', 'Options', 'Help') {
            if ($items -notcontains $required) { $failures += "${name}: menu '$required' missing" }
        }
        Write-Host ("{0,-8} menus: {1}" -f $name, ($items -join ', '))
        if ($ShotDir) { New-Item -ItemType Directory -Force $ShotDir | Out-Null; Save-Shot $p.MainWindowHandle (Join-Path $ShotDir "$name.png") }
    }
    # game preview opens from the Scene tab, renders and closes
    $sceneTab = $tabs | Where-Object { $_.Current.Name -eq 'Scene' } | Select-Object -First 1
    if ($sceneTab) {
        $sceneTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 500
        $btn = (Find-All $root ([System.Windows.Automation.ControlType]::Button)) | Where-Object { $_.Current.Name -like '*Game preview' } | Select-Object -First 1
        if (-not $btn) { $failures += 'Game preview button missing' }
        else {
            $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep 2
            $cond = New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $p.Id
            # an owned window shows up under the desktop or under its owner, depending on Windows version
            $wins = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll('Children', $cond)) + @(Find-All $root ([System.Windows.Automation.ControlType]::Window))
            $preview = $wins | Where-Object { $_.Current.Name -like 'Game preview*' } | Select-Object -First 1
            if (-not $preview) { Write-Host ("windows: " + (($wins | ForEach-Object { $_.Current.Name }) -join ' | ')) }
            if (-not $preview) { $failures += 'game preview window did not open' }
            else {
                Write-Host "game preview: $($preview.Current.Name)"
                $preview.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
                Start-Sleep -Milliseconds 500
            }
        }
    }
    # export: Project > Export, run the default Godot profile on the fixture project
    $menu = (Find-All $root ([System.Windows.Automation.ControlType]::Menu))[0]
    $projMenu = $menu.FindAll('Children', [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { ($_.Current.Name -replace '_', '') -eq 'Project' } | Select-Object -First 1
    $projMenu.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Start-Sleep -Milliseconds 500
    $exportItem = (Find-All $root ([System.Windows.Automation.ControlType]::MenuItem)) | Where-Object { ($_.Current.Name -replace '_', '') -like 'Export*' } | Select-Object -First 1
    if (-not $exportItem) { $failures += 'Project > Export missing' }
    else {
        $exportItem.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep 2
        $cond = New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $p.Id
        $wins = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll('Children', $cond)) + @(Find-All $root ([System.Windows.Automation.ControlType]::Window))
        $exp = $wins | Where-Object { $_.Current.Name -like 'Export*' } | Select-Object -First 1
        if (-not $exp) { $failures += 'export window did not open' }
        else {
            $go = (Find-All $exp ([System.Windows.Automation.ControlType]::Button)) | Where-Object { $_.Current.Name -eq 'Export' } | Select-Object -First 1
            $go.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep 3
            $rep = (Find-All $exp ([System.Windows.Automation.ControlType]::Edit)) | Where-Object { $_.Current.Name -eq 'Export report' } | Select-Object -First 1
            $text = $rep.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
            $first = ($text -split "`n")[0]
            Write-Host "export: $first"
            if ($text -notlike 'Exported*') { $failures += "export failed: $text" }
            if (-not (Test-Path (Join-Path $projectDir 'export\godot\project.json'))) { $failures += 'export folder missing project.json' }
            $exp.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
            Start-Sleep -Milliseconds 500
        }
    }
    $p.CloseMainWindow() | Out-Null
    if (-not $p.WaitForExit(5000)) { $failures += 'app did not exit within 5 s after closing' }
}
finally {
    if (-not $p.HasExited) { Stop-Process $p -Force }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
if ($failures) { $failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }; exit 1 }
Write-Host 'SMOKE OK' -ForegroundColor Green
