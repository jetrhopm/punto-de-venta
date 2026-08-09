[CmdletBinding()]
param([string]$Version = '1.0.0')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$wix = Join-Path $root '.tools\wix6\wix.exe'
$extension = Join-Path $root '.tools\wix6-extension\wixext6\WixToolset.BootstrapperApplications.wixext.dll'
$uiExtension = Join-Path $root '.tools\wix6-extension\wixext6\WixToolset.UI.wixext.dll'
$vcRedist = Join-Path $root '.tools\vc_redist.x64.exe'
$bootstrap = Join-Path $root 'artifacts\production\bootstrap\Pos.ProductionBootstrap.exe'
$icon = Join-Path $root 'src\Pos.Desktop\Assets\Icons\app.ico'
$logo = Join-Path $root 'src\Pos.Desktop\Assets\Icons\sales-icon-256.png'
if (-not (Test-Path $wix)) { & (Join-Path $root '.tools\dotnet\dotnet.exe') tool install wix --tool-path (Join-Path $root '.tools\wix6') --version 6.0.2 }
if (-not (Test-Path $extension)) {
    $zip = Join-Path $env:TEMP 'WixToolset.Bal.wixext.6.0.2.zip'
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/wixtoolset.bal.wixext/6.0.2/wixtoolset.bal.wixext.6.0.2.nupkg' -OutFile $zip
    if (Test-Path (Join-Path $root '.tools\wix6-extension')) { Remove-Item (Join-Path $root '.tools\wix6-extension') -Recurse -Force }
    Expand-Archive $zip (Join-Path $root '.tools\wix6-extension')
}
if (-not (Test-Path $uiExtension)) {
    $uiZip = Join-Path $env:TEMP 'WixToolset.UI.wixext.6.0.2.zip'
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/wixtoolset.ui.wixext/6.0.2/wixtoolset.ui.wixext.6.0.2.nupkg' -OutFile $uiZip
    Expand-Archive $uiZip (Join-Path $root '.tools\wix6-extension') -Force
}
if (-not (Test-Path $vcRedist)) {
    Write-Host 'Descargando Visual C++ Redistributable x64 oficial de Microsoft...'
    Invoke-WebRequest -Uri 'https://aka.ms/vc14/vc_redist.x64.exe' -OutFile $vcRedist -UseBasicParsing
}
& (Join-Path $PSScriptRoot 'package-production.ps1')
$bootstrapOutput = Join-Path $root 'artifacts\production\bootstrap'
New-Item -ItemType Directory -Force -Path $bootstrapOutput | Out-Null
& (Join-Path $root '.tools\dotnet\dotnet.exe') publish (Join-Path $root 'installer\Pos.ProductionBootstrap\Pos.ProductionBootstrap.csproj') -c Release -r win-x64 --self-contained true -o $bootstrapOutput
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el bootstrap de produccion.' }
Copy-Item (Join-Path $PSScriptRoot 'install-production.ps1') $bootstrapOutput -Force
$source = Join-Path $root 'artifacts\production\win-x64'
$out = Join-Path $root 'artifacts\installer'
New-Item -ItemType Directory -Force $out | Out-Null
$msi = Join-Path $out 'PuntoDeVenta.msi'
$setup = Join-Path $out 'Setup.exe'
& $wix build (Join-Path $root 'installer\Product.wxs') -ext $uiExtension -d SourceDir=$source -d ProductVersion=$Version -o $msi
if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el MSI.' }
& $wix build (Join-Path $root 'installer\Bundle.wxs') -ext $extension -d MsiPath=$msi -d VcRedistPath=$vcRedist -d BootstrapPath=$bootstrap -d IconPath=$icon -d LogoPath=$logo -d ProductVersion=$Version -o (Join-Path $out 'PuntoDeVenta-Setup.exe')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear Setup.exe.' }
Copy-Item (Join-Path $out 'PuntoDeVenta-Setup.exe') $setup -Force
Write-Host "Instalador creado: $setup"
