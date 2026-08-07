[CmdletBinding()]
param([string]$Version = '1.0.0')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$wix = Join-Path $root '.tools\wix\wix.exe'
if (-not (Test-Path $wix)) { throw 'Instala WiX localmente antes de compilar.' }
& $wix extension list | Out-Host
& (Join-Path $PSScriptRoot 'package-production.ps1')
$source = Join-Path $root 'artifacts\production\win-x64'
$out = Join-Path $root 'artifacts\installer'
New-Item -ItemType Directory -Force $out | Out-Null
$msi = Join-Path $out 'PuntoDeVenta.msi'
$setup = Join-Path $out 'Setup.exe'
& $wix build (Join-Path $root 'installer\Product.wxs') -d SourceDir=$source -d ProductVersion=$Version -o $msi
if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el MSI.' }
& $wix build (Join-Path $root 'installer\Bundle.wxs') -ext WixToolset.Bal.wixext -d MsiPath=$msi -d ProductVersion=$Version -o $setup
if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear Setup.exe.' }
Write-Host "Instalador creado: $setup"
