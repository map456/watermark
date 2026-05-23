<#
.SYNOPSIS
    Build the RDP Watermark MSI installer.

.DESCRIPTION
    1. Publishes the WPF project as a self-contained single-file exe (win-x64).
    2. Packages the exe + settings.json into Watermark.msi using WiX v5.

.NOTES
    Requires:
      - .NET 8 SDK   (dotnet --list-sdks)
      - WiX v5       (dotnet tool install --global wix --version 5.0.2)
      - Util ext.    (wix extension add -g WixToolset.Util.wixext/5.0.2)

    Output:
      - publish\Watermark.exe   (self-contained single-file)
      - dist\Watermark.msi      (installer)
#>

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$proj = Join-Path $root 'src\Watermark\Watermark.csproj'
$pub  = Join-Path $root 'publish'
$dist = Join-Path $root 'dist'
$wxs  = Join-Path $root 'installer\Package.wxs'
$msi  = Join-Path $dist 'Watermark.msi'

# Make sure dotnet tool path & runtime root are available for WiX
$env:Path += ";$env:USERPROFILE\.dotnet\tools"
if (-not $env:DOTNET_ROOT) { $env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet" }

New-Item -ItemType Directory -Force -Path $pub  | Out-Null
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host '==> Publishing self-contained Watermark.exe ...' -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained true -o $pub -nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Write-Host '==> Building Watermark.msi with WiX v5 ...' -ForegroundColor Cyan
wix build $wxs -arch x64 -define "PublishDir=$pub" -ext WixToolset.Util.wixext -o $msi
if ($LASTEXITCODE -ne 0) { throw 'wix build failed' }

Write-Host ''
Write-Host "Done. Installer: $msi" -ForegroundColor Green
Get-Item $msi | Format-List Name, Length, LastWriteTime
