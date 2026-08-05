Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'

if (-not (Test-Path $dotnet)) {
    Write-Host 'No se encontro .NET local. Instala con:'
    Write-Host '  powershell -ExecutionPolicy Bypass -File .\.tools\dotnet-install.ps1 -Channel 10.0 -InstallDir .\.tools\dotnet -Architecture x64'
    exit 1
}

& $dotnet --list-sdks

$psql = Get-Command psql -ErrorAction SilentlyContinue
if ($null -eq $psql) {
    Write-Warning 'PostgreSQL/psql no esta disponible. La descarga portable del cluster de desarrollo queda pendiente para el siguiente incremento.'
} else {
    & psql --version
}

Write-Host 'Entorno base verificado.'
