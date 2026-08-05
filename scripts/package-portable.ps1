[CmdletBinding()]
param([string]$Configuration = 'Release')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$output = Join-Path $root 'artifacts\portable\win-x64'
$zip = Join-Path $root 'artifacts\PuntoDeVenta-portable-win-x64.zip'

if (-not (Test-Path -LiteralPath $dotnet)) { throw 'No existe el SDK local. Ejecuta scripts/dev-setup.ps1.' }
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $output | Out-Null

& $dotnet restore (Join-Path $root 'PuntoDeVenta.slnx') -r win-x64
if ($LASTEXITCODE -ne 0) { throw 'No se pudo restaurar la solucion para win-x64.' }
& $dotnet publish (Join-Path $root 'src\Pos.Desktop\Pos.Desktop.csproj') -c $Configuration -r win-x64 --self-contained true --no-restore -o (Join-Path $output 'app')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el cliente WPF win-x64.' }
& $dotnet publish (Join-Path $root 'src\Pos.Api\Pos.Api.csproj') -c $Configuration -r win-x64 --self-contained true --no-restore -o (Join-Path $output 'api')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar la API win-x64.' }
Set-Content -LiteralPath (Join-Path $output 'MODO DE PRUEBA.txt') -Value 'Este paquete es solo para revision. No usar en produccion ni conectar a datos reales.' -Encoding utf8
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $output
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -LiteralPath $output -DestinationPath $zip
Write-Host "Paquete portable creado: $zip"
