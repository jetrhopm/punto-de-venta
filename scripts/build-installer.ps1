[CmdletBinding()]
param([string]$Version = '1.0.0')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$wix = Join-Path $root '.tools\wix6\wix.exe'
$extension = Join-Path $root '.tools\wix6-extension\wixext6\WixToolset.BootstrapperApplications.wixext.dll'
if (-not (Test-Path $wix)) { & (Join-Path $root '.tools\dotnet\dotnet.exe') tool install wix --tool-path (Join-Path $root '.tools\wix6') --version 6.0.2 }
if (-not (Test-Path $extension)) {
    $zip = Join-Path $env:TEMP 'WixToolset.Bal.wixext.6.0.2.zip'
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/wixtoolset.bal.wixext/6.0.2/wixtoolset.bal.wixext.6.0.2.nupkg' -OutFile $zip
    if (Test-Path (Join-Path $root '.tools\wix6-extension')) { Remove-Item (Join-Path $root '.tools\wix6-extension') -Recurse -Force }
    Expand-Archive $zip (Join-Path $root '.tools\wix6-extension')
}
& (Join-Path $PSScriptRoot 'package-production.ps1')
$source = Join-Path $root 'artifacts\production\win-x64'
$out = Join-Path $root 'artifacts\installer'
New-Item -ItemType Directory -Force $out | Out-Null
$msi = Join-Path $out 'PuntoDeVenta.msi'
$setup = Join-Path $out 'Setup.exe'
& $wix build (Join-Path $root 'installer\Product.wxs') -d SourceDir=$source -d ProductVersion=$Version -o $msi
if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el MSI.' }
& $wix build (Join-Path $root 'installer\Bundle.wxs') -ext $extension -d MsiPath=$msi -d ProductVersion=$Version -o (Join-Path $out 'PuntoDeVenta-Setup.exe')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear Setup.exe.' }
Copy-Item (Join-Path $out 'PuntoDeVenta-Setup.exe') $setup -Force
Write-Host "Instalador creado: $setup"
