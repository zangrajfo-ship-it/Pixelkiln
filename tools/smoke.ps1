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
Get-ChildItem -File (Join-Path $PSScriptRoot "../tests/PixelForge.Core.Tests/Fixtures") | Copy-Item -Destination $projectDir   # top level only: Fixtures/current refers to asset ids of no project
$expectedAssets = (Get-ChildItem $projectDir).Count
# the sample data plugin, installed into the temp home
New-Item -ItemType Directory -Force (Join-Path $env:PIXELFORGE_HOME 'plugins') | Out-Null
Copy-Item -Recurse (Join-Path $PSScriptRoot '../samples/plugins/retro-pack') (Join-Path $env:PIXELFORGE_HOME 'plugins')
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class PfWin {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
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

# opens a top-level menu and returns the item whose name matches (retries: menus open asynchronously)
# menu steps need the app in the foreground; Windows refuses that while someone else is using the PC
$script:background = $false
$script:skipped = @()
function Missing($what) { if ($script:background) { $script:skipped += $what } else { $script:failures += $what } }

function Find-MenuItem($root, $top, $pattern) {
    for ($try = 0; $try -lt 5; $try++) {
        [void][PfWin]::SetForegroundWindow([IntPtr]$root.Current.NativeWindowHandle)   # menus only open in the foreground window
        try { $root.SetFocus() } catch { }   # after another window closes, the main window may not be active yet
        Start-Sleep -Milliseconds (200 * $try)
        $bar = (Find-All $root ([System.Windows.Automation.ControlType]::Menu))[0]
        $m = $bar.FindAll('Children', [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { ($_.Current.Name -replace '_', '') -eq $top } | Select-Object -First 1
        $ec = $m.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        if ($ec.Current.ExpandCollapseState -eq 'Expanded') { $ec.Collapse(); Start-Sleep -Milliseconds 200 }
        $ec.Expand()
        Start-Sleep -Milliseconds 500
        $item = (Find-All $root ([System.Windows.Automation.ControlType]::MenuItem)) | Where-Object { ($_.Current.Name -replace '_', '') -like $pattern } | Select-Object -First 1
        if ($item) { return $item }
    }
    $bar = (Find-All $root ([System.Windows.Automation.ControlType]::Menu))
    if ([PfWin]::GetForegroundWindow() -ne [IntPtr]$root.Current.NativeWindowHandle) { $script:background = $true }
    return $null
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
    # the editors are mode tabs (radio buttons) in the top bar
    $tabs = @(Find-All $root ([System.Windows.Automation.ControlType]::RadioButton) | Where-Object { $_.Current.Name -in 'Sprite', 'Character', 'Tilemap' })
    if ($tabs.Count -ne 3) { $failures += "expected 3 tabs (Sprite, Character, Tilemap), found $($tabs.Count)" }
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
    # simplified app (21. 10. 2026): switched off with the parts it tests; set to $true to run it again
    if ($false) {
    # modes: the Sprite tab has a Paint mode with its own menus
    $spriteTab = $tabs | Where-Object { $_.Current.Name -eq 'Sprite' } | Select-Object -First 1
    $spriteTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 500
    $paintMode = (Find-All $root ([System.Windows.Automation.ControlType]::RadioButton)) | Where-Object { $_.Current.Name -eq 'Paint mode' } | Select-Object -First 1
    if (-not $paintMode) { $failures += 'Paint mode button missing' }
    else {
        $paintMode.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 1200
        $menu = (Find-All $root ([System.Windows.Automation.ControlType]::Menu))[0]
        $items = $menu.FindAll('Children', [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name -replace '_', '' }
        Write-Host ("Paint    menus: {0} | title: {1}" -f ($items -join ', '), $root.Current.Name)
        if ($items -notcontains 'Layer' -or $root.Current.Name -notlike '*untitled painting*') { $failures += 'Paint mode did not take over menus and title' }
        if ($ShotDir) { Save-Shot $p.MainWindowHandle (Join-Path $ShotDir 'Paint.png') }
        $vecMode = (Find-All $root ([System.Windows.Automation.ControlType]::RadioButton)) | Where-Object { $_.Current.Name -eq 'Vector mode' } | Select-Object -First 1
        if (-not $vecMode) { $failures += 'Vector mode button missing' }
        else {
            $vecMode.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Milliseconds 1200
            $menu = (Find-All $root ([System.Windows.Automation.ControlType]::Menu))[0]
            $items = $menu.FindAll('Children', [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name -replace '_', '' }
            Write-Host ("Vector   menus: {0} | title: {1}" -f ($items -join ', '), $root.Current.Name)
            if ($items -notcontains 'Object' -or $root.Current.Name -notlike '*untitled drawing*') { $failures += 'Vector mode did not take over menus and title' }
            if ($ShotDir) { Save-Shot $p.MainWindowHandle (Join-Path $ShotDir 'Vector.png') }
        }
        $texMode = (Find-All $root ([System.Windows.Automation.ControlType]::RadioButton)) | Where-Object { $_.Current.Name -eq 'Texture mode' } | Select-Object -First 1
        if (-not $texMode) { $failures += 'Texture mode button missing' }
        else {
            $texMode.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Milliseconds 2000
            $status = (Find-All $root ([System.Windows.Automation.ControlType]::Text)) | Where-Object { $_.Current.Name -like '*px*tile*' -or $_.Current.Name -like '*seam*' } | Select-Object -First 1
            Write-Host ("Texture  title: {0} | status: {1}" -f $root.Current.Name, $status.Current.Name)
            if ($root.Current.Name -notlike '*untitled texture*' -or -not $status) { $failures += 'Texture mode did not build its maps' }
            if ($ShotDir) { Save-Shot $p.MainWindowHandle (Join-Path $ShotDir 'Texture.png') }
        }
        $uiMode = (Find-All $root ([System.Windows.Automation.ControlType]::RadioButton)) | Where-Object { $_.Current.Name -eq 'UI mode' } | Select-Object -First 1
        if (-not $uiMode) { $failures += 'UI mode button missing' }
        else {
            $uiMode.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Start-Sleep -Milliseconds 1500
            $screens = (Find-All $root ([System.Windows.Automation.ControlType]::ListItem)) | Where-Object { $_.Current.Name -like '*320*180*' }
            Write-Host ("UI       title: {0} | screens listed: {1}" -f $root.Current.Name, $screens.Count)
            if ($root.Current.Name -notlike '*untitled UI kit*' -or $screens.Count -lt 2) { $failures += 'UI mode did not show its screens' }
            if ($ShotDir) { Save-Shot $p.MainWindowHandle (Join-Path $ShotDir 'UI.png') }
            # device preview of the current screen
            $dev = Find-MenuItem $root 'Options' 'Device preview*'
            if (-not $dev) { Missing 'Options > Device preview' }
            else {
                $dev.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                Start-Sleep 2
                $cond = New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $p.Id
                $wins = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll('Children', $cond)) + @(Find-All $root ([System.Windows.Automation.ControlType]::Window))
                $devWin = $wins | Where-Object { $_.Current.Name -like 'Device preview*' } | Select-Object -First 1
                if (-not $devWin) { $failures += 'device preview did not open' }
                else {
                    Write-Host "device preview: $($devWin.Current.Name)"
                    $devWin.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
                    Start-Sleep -Milliseconds 500
                }
            }
        }
        $pixelMode = (Find-All $root ([System.Windows.Automation.ControlType]::RadioButton)) | Where-Object { $_.Current.Name -eq 'Pixel mode' } | Select-Object -First 1
        $pixelMode.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 500
    }
    }
    # simplified app (21. 10. 2026): switched off with the parts it tests; set to $true to run it again
    if ($false) {
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
    # modes of phases 17 and 20: each takes over the menus (and the title where it owns a file)
    $SIP = [System.Windows.Automation.SelectionItemPattern]::Pattern
    foreach ($m in @(@('Scene', 'Motion mode', '*untitled animations*', 'Play', 'Layout mode'), @('Scene', 'Logic mode', '*untitled logic*', 'Edit', 'Layout mode'), @('Character', 'Rig mode', '*', 'Rig', 'States mode'))) {
        $tab = $tabs | Where-Object { $_.Current.Name -eq $m[0] } | Select-Object -First 1
        if (-not $tab) { continue }
        $tab.GetCurrentPattern($SIP).Select()
        Start-Sleep -Milliseconds 500
        $modeBtn = (Find-All $root ([System.Windows.Automation.ControlType]::RadioButton)) | Where-Object { $_.Current.Name -eq $m[1] } | Select-Object -First 1
        if (-not $modeBtn) { $failures += "$($m[1]) button missing"; continue }
        $modeBtn.GetCurrentPattern($SIP).Select()
        Start-Sleep -Milliseconds 800
        $menu = (Find-All $root ([System.Windows.Automation.ControlType]::Menu))[0]
        $items = $menu.FindAll('Children', [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name -replace '_', '' }
        Write-Host ("{0,-12} title: {1} | menus: {2}" -f $m[1], $root.Current.Name, ($items -join ', '))
        if ($items -notcontains $m[3] -or $root.Current.Name -notlike $m[2]) { $failures += "$($m[1]) did not take over menus and title" }
        if ($ShotDir) { Save-Shot $p.MainWindowHandle (Join-Path $ShotDir (($m[1] -replace ' mode', '') + '.png')) }
        $back = (Find-All $root ([System.Windows.Automation.ControlType]::RadioButton)) | Where-Object { $_.Current.Name -eq $m[4] } | Select-Object -First 1
        if ($back) { $back.GetCurrentPattern($SIP).Select(); Start-Sleep -Milliseconds 400 }
    }
    }
    # project windows open and close (Optimize assets and Style guide are switched off in the simplified app)
    foreach ($mw in @(, @('Help', 'Getting started'))) {
        $w = $mw[1]
        $item = Find-MenuItem $root $mw[0] "$w*"
        if (-not $item) { Missing "$($mw[0]) > $w"; continue }
        $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep 3
        $cond = New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $p.Id
        $wins = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll('Children', $cond)) + @(Find-All $root ([System.Windows.Automation.ControlType]::Window))
        $win = $wins | Where-Object { $_.Current.Name -like "$w*" } | Select-Object -First 1
        if (-not $win) { $failures += "$w window did not open" }
        else {
            $report = (Find-All $win ([System.Windows.Automation.ControlType]::Edit)) | Select-Object -Last 1
            $first = if ($report) { ($report.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -split "`n")[0] } else { 'opened' }
            Write-Host ("{0}: {1}" -f $w, $first)
            $win.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
            Start-Sleep -Milliseconds 500
        }
    }
    # command palette: commands of every tab and of the sample plugin are listed
    $palItem = Find-MenuItem $root 'Options' 'Command palette*'
    if (-not $palItem) { Missing 'Options > Command palette' }
    else {
        $palItem.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep 2
        $cond = New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $p.Id
        $wins = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll('Children', $cond)) + @(Find-All $root ([System.Windows.Automation.ControlType]::Window))
        $pal = $wins | Where-Object { $_.Current.Name -eq 'Command palette' } | Select-Object -First 1
        if (-not $pal) { $failures += 'command palette did not open' }
        else {
            $box = (Find-All $pal ([System.Windows.Automation.ControlType]::Edit))[0]
            foreach ($q in 'Retro pack Macro', 'Flip horizontally', 'Generate tileset from seed') {
                $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($q)
                Start-Sleep -Milliseconds 400
                $names = (Find-All $pal ([System.Windows.Automation.ControlType]::ListItem)) | ForEach-Object { $_.Current.Name }
                Write-Host ("palette '{0}': {1}" -f $q, (($names | Select-Object -First 2) -join ' | '))
                if (-not $names) { $failures += "command palette found nothing for '$q'" }
            }
            $pal.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
            Start-Sleep -Milliseconds 500
        }
    }
    # simplified app (21. 10. 2026): switched off with the parts it tests; set to $true to run it again
    if ($false) {
    # workspace: Options > Workspace > Levels selects the Scene tab
    $ws = Find-MenuItem $root 'Options' 'Workspace*'
    if (-not $ws) { Missing 'Options > Workspace' }
    else {
        $ws.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Start-Sleep -Milliseconds 500
        # submenus open in their own popup window: look through every top-level window of the app
        $cond = New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $p.Id
        $levels = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll('Children', $cond)) + @($root) |
            ForEach-Object { Find-All $_ ([System.Windows.Automation.ControlType]::MenuItem) } | Where-Object { $_.Current.Name -eq 'Levels' } | Select-Object -First 1
        if ($levels) { $levels.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } else { $failures += 'Workspace > Levels not found' }
        Start-Sleep -Milliseconds 700
        $sel = @(Find-All $root ([System.Windows.Automation.ControlType]::RadioButton) | Where-Object { $_.Current.Name -in 'Sprite', 'Character', 'Tilemap' }) | Where-Object { $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } | Select-Object -First 1
        Write-Host "workspace Levels -> tab $($sel.Current.Name)"
        if ($sel.Current.Name -ne 'Scene') { $failures += 'workspace Levels did not select the Scene tab' }
    }
    }
    # templates: Project > New from template, create the first template (starting point), new assets appear
    $tplItem = Find-MenuItem $root 'Project' 'New from template*'
    if (-not $tplItem) { Missing 'Project > New from template' }
    else {
        $before = (Find-All $list ([System.Windows.Automation.ControlType]::ListItem)).Count
        $tplItem.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep 3
        $cond = New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $p.Id
        $wins = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll('Children', $cond)) + @(Find-All $root ([System.Windows.Automation.ControlType]::Window))
        $studio = $wins | Where-Object { $_.Current.Name -eq 'Template Studio' } | Select-Object -First 1
        if (-not $studio) { $failures += 'Template Studio did not open' }
        else {
            $tpls = (Find-All $studio ([System.Windows.Automation.ControlType]::ListItem)) | Where-Object { $_.Current.Name -like '*BuiltIn' }
            Write-Host "templates listed: $($tpls.Count)"
            if ($tpls.Count -lt 5) { $failures += "Template Studio lists only $($tpls.Count) built-in templates" }   # virtualized: only visible rows are counted
            $go = (Find-All $studio ([System.Windows.Automation.ControlType]::Button)) | Where-Object { $_.Current.Name -eq 'Create' } | Select-Object -First 1
            if ($go) { $go.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } else { $failures += 'Template Studio has no Create button' }
            Start-Sleep 4
            $studio.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
            Start-Sleep -Milliseconds 800
            $after = (Find-All $list ([System.Windows.Automation.ControlType]::ListItem)).Count
            Write-Host "assets after template: $after (before $before)"
            if ($after -le $before) { $failures += 'creating from a template added no assets' }
        }
    }
    # history: select the first asset, Project > History of selected asset
    $first = (Find-All $list ([System.Windows.Automation.ControlType]::ListItem))[0]
    $first.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 300
    $histItem = Find-MenuItem $root 'Project' 'History*'
    if (-not $histItem) { Missing 'Project > History' }
    else {
        $histItem.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep 2
        $cond = New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $p.Id
        $wins = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll('Children', $cond)) + @(Find-All $root ([System.Windows.Automation.ControlType]::Window))
        $hist = $wins | Where-Object { $_.Current.Name -like 'History*' } | Select-Object -First 1
        if (-not $hist) { $failures += 'history window did not open' }
        else {
            $versions = (Find-All $hist ([System.Windows.Automation.ControlType]::ListItem)).Count
            Write-Host "history: $versions version(s)"
            if ($versions -lt 1) { $failures += 'history shows no versions' }
            if (-not (Test-Path (Join-Path $projectDir '.history\index.json'))) { $failures += '.history/index.json missing' }
            $hist.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
            Start-Sleep -Milliseconds 500
        }
    }
    # export: Project > Export, run the default Godot profile on the fixture project
    $exportItem = Find-MenuItem $root 'Project' 'Export*'
    if (-not $exportItem) { Missing 'Project > Export' }
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
    if (-not $p.WaitForExit(5000))
    {
        $cond = New-Object System.Windows.Automation.PropertyCondition $A::ProcessIdProperty, $p.Id
        $open = ([System.Windows.Automation.AutomationElement]::RootElement.FindAll('Children', $cond) | ForEach-Object { $_.Current.Name }) -join ' | '
        $failures += "app did not exit within 5 s after closing (open: $open)"
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process $p -Force }
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
if ($failures) { $failures | ForEach-Object { Write-Host "FAIL: $_" -ForegroundColor Red }; exit 1 }
if ($script:skipped) {
    Write-Host ("SMOKE INCOMPLETE: the app window could not get the foreground (someone is using the PC), so these menu steps did not run: " + ($script:skipped -join ', ')) -ForegroundColor Yellow
    exit 2
}
Write-Host 'SMOKE OK' -ForegroundColor Green
