[CmdletBinding()]
param([string]$Configuration = 'Release')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$project = Join-Path $root 'tools\JetVenta.LicenseIssuer\JetVenta.LicenseIssuer.csproj'
$output = Join-Path $root 'artifacts\license-issuer'

if (-not (Test-Path $dotnet)) { throw 'No existe el SDK local de .NET.' }
if (-not (Test-Path $project)) { throw 'No existe el proyecto del emisor de licencias.' }
if (Test-Path $output) { Remove-Item -LiteralPath $output -Recurse -Force }
& $dotnet publish $project -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o $output
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el emisor de licencias.' }
Write-Host "Emisor listo: $(Join-Path $output 'JetVenta.LicenseIssuer.exe')"
