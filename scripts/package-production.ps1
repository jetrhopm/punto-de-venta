[CmdletBinding()]
param([string]$Configuration = 'Release')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$wix = Join-Path $root '.tools\wix\wix.exe'
$output = Join-Path $root 'artifacts\production\win-x64'

if (-not (Test-Path $dotnet)) { throw 'No existe el SDK local.' }
if (-not (Test-Path $wix)) { throw 'No existe WiX local. Ejecuta: .tools\dotnet\dotnet.exe tool install wix --tool-path .tools\wix' }
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $output | Out-Null

& $dotnet publish (Join-Path $root 'src\Pos.Desktop\Pos.Desktop.csproj') -c $Configuration -r win-x64 --self-contained true -o (Join-Path $output 'client')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el cliente.' }
& $dotnet publish (Join-Path $root 'src\Pos.Api\Pos.Api.csproj') -c $Configuration -r win-x64 --self-contained true -o (Join-Path $output 'api')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar la API.' }
Set-Content (Join-Path $output 'VERSION.txt') $((Get-Date).ToUniversalTime().ToString('O')) -Encoding utf8
Write-Host "Publicacion de produccion preparada en $output"
Write-Host 'El Setup.exe final requiere WiX Bundle y prueba en maquinas limpias antes de liberarse.'
