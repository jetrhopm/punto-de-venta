Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'

if (-not (Test-Path $dotnet)) {
    throw 'No se encontro .NET local. Ejecuta scripts/dev-setup.ps1 despues de instalar el SDK local.'
}

Write-Host 'Iniciando API en modo desarrollo...'
& $dotnet run --project (Join-Path $root 'src\Pos.Api\Pos.Api.csproj')
