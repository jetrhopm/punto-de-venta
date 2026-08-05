Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$settings = Join-Path $root '.postgres\development-settings.json'

if (-not (Test-Path $dotnet)) {
    throw 'No se encontro .NET local. Ejecuta scripts/dev-setup.ps1 despues de instalar el SDK local.'
}

if (-not (Test-Path -LiteralPath $settings)) { & (Join-Path $PSScriptRoot 'dev-setup.ps1') }

Write-Host 'Iniciando API y cliente WPF en modo desarrollo...'
Start-Process -FilePath $dotnet -ArgumentList @('run', '--project', (Join-Path $root 'src\Pos.Api\Pos.Api.csproj'), '--no-launch-profile') -WorkingDirectory $root
Start-Process -FilePath $dotnet -ArgumentList @('run', '--project', (Join-Path $root 'src\Pos.Desktop\Pos.Desktop.csproj')) -WorkingDirectory $root
Write-Host 'Procesos de desarrollo iniciados con el SDK local.'
