<#
.SYNOPSIS
  Builds the release package: Pixelkiln for 64-bit Windows with .NET included, in one zip.
  The target machine needs nothing installed. The version comes from Directory.Build.props.
.EXAMPLE
  powershell -File tools/publish.ps1
  powershell -File tools/publish.ps1 -Out D:\packages
#>
param(
    [string]$Out = "$PSScriptRoot\..\artifacts"
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..").Path
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = "$($props.Project.PropertyGroup.Version)".Trim()
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }

$name = "Pixelkiln-$version-win-x64"
New-Item -ItemType Directory -Force $Out | Out-Null
$dir = Join-Path (Resolve-Path $Out).Path $name
$zip = "$dir.zip"
if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
if (Test-Path $zip) { Remove-Item -Force $zip }

# self-contained: .NET travels with the app; ReadyToRun: compiled ahead, so the first start is faster
dotnet publish (Join-Path $root 'src\Pixelkiln.App') -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false --nologo -o $dir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

Copy-Item (Join-Path $root 'LICENSE') (Join-Path $dir 'LICENSE.txt')
@"
Pixelkiln $version

Start:      Pixelkiln.App.exe
Settings:   %AppData%\Pixelkiln (autosave, recent files, crash.log)
Remove:     delete this folder; delete %AppData%\Pixelkiln to remove the settings too
Help:       F1 in the app, Help > Getting started
Source:     https://github.com/zangrajfo-ship-it/Pixelkiln

The app is not signed, so Windows may show "Windows protected your PC" the first time.
Choose "More info", then "Run anyway".
"@ | Set-Content (Join-Path $dir 'README.txt') -Encoding ascii

Compress-Archive -Path $dir -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$hash  $name.zip" | Set-Content "$zip.sha256" -Encoding ascii

"{0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)
"SHA-256  $hash"
