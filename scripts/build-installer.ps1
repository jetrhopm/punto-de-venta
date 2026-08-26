[CmdletBinding()]
param([string]$Version = '2.2.1')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$source = Join-Path $root 'artifacts\production\win-x64'
$setupProject = Join-Path $root 'installer\Pos.Setup\Pos.Setup.csproj'
$setupProjectRoot = Join-Path $root 'installer\Pos.Setup'
$payload = Join-Path $setupProjectRoot 'Payload.zip'
$output = Join-Path $root 'artifacts\installer'
$published = Join-Path $root 'artifacts\production\setup'
$assemblyVersion = if ($Version.Split('.').Count -eq 3) { "$Version.0" } else { $Version }

if (-not (Test-Path $dotnet)) { throw 'No existe el SDK local.' }
& (Join-Path $PSScriptRoot 'package-production.ps1')
if (-not (Test-Path (Join-Path $source 'vc_redist.x64.exe'))) { throw 'No se encontro Visual C++ Redistributable dentro del paquete.' }

Write-Host 'Comprimiendo el paquete interno del instalador...'
if (Test-Path $payload) { Remove-Item $payload -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($source, $payload, [System.IO.Compression.CompressionLevel]::Fastest, $false)

if (Test-Path $published) { Remove-Item $published -Recurse -Force }
New-Item -ItemType Directory -Force -Path $published, $output | Out-Null
& $dotnet publish $setupProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:Version=$Version -p:AssemblyVersion=$assemblyVersion -p:FileVersion=$assemblyVersion -p:InformationalVersion=$Version -o $published
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el instalador autocontenido.' }

$setup = Join-Path $output 'Setup.exe'
Copy-Item (Join-Path $published 'Pos.Setup.exe') $setup -Force
Remove-Item $payload -Force
Write-Host "Instalador autocontenido creado: $setup"
Write-Host "Version: $Version"
