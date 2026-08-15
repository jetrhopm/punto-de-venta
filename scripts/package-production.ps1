[CmdletBinding()]
param([string]$Configuration = 'Release')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$dotnet = Join-Path $root '.tools\dotnet\dotnet.exe'
$wix = Join-Path $root '.tools\wix\wix.exe'
$output = Join-Path $root 'artifacts\production\win-x64'
$postgresSource = Join-Path $root '.tools\postgresql-18.4\pgsql'

if (-not (Test-Path $dotnet)) { throw 'No existe el SDK local.' }
if (-not (Test-Path (Join-Path $postgresSource 'bin\pg_ctl.exe'))) { throw 'No existe PostgreSQL portable. Ejecuta scripts/dev-setup.ps1 para preparar los binarios oficiales.' }
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $output | Out-Null

& $dotnet publish (Join-Path $root 'src\Pos.Desktop\Pos.Desktop.csproj') -c $Configuration -r win-x64 --self-contained true -o (Join-Path $output 'client')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el cliente.' }
& $dotnet publish (Join-Path $root 'src\Pos.Api\Pos.Api.csproj') -c $Configuration -r win-x64 --self-contained true -o (Join-Path $output 'api')
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar la API.' }
New-Item -ItemType Directory -Force -Path (Join-Path $output 'postgresql\pgsql') | Out-Null
foreach ($directory in @('bin', 'lib', 'share')) {
    Copy-Item (Join-Path $postgresSource $directory) (Join-Path $output "postgresql\pgsql\$directory") -Recurse -Force
}
Set-Content (Join-Path $output 'VERSION.txt') $((Get-Date).ToUniversalTime().ToString('O')) -Encoding utf8
$productionScript = Join-Path $output 'install-production.ps1'
Copy-Item (Join-Path $PSScriptRoot 'install-production.ps1') $productionScript -Force
# Windows PowerShell 5.1 necesita BOM para interpretar UTF-8 de forma confiable.
$productionScriptText = [IO.File]::ReadAllText($productionScript, [Text.Encoding]::UTF8)
[IO.File]::WriteAllText($productionScript, $productionScriptText, [Text.UTF8Encoding]::new($true))
$restoreScript = Join-Path $output 'restore-production-backup.ps1'
Copy-Item (Join-Path $PSScriptRoot 'restore-production-backup.ps1') $restoreScript -Force
$restoreScriptText = [IO.File]::ReadAllText($restoreScript, [Text.Encoding]::UTF8)
[IO.File]::WriteAllText($restoreScript, $restoreScriptText, [Text.UTF8Encoding]::new($true))
Copy-Item (Join-Path $root '.tools\vc_redist.x64.exe') (Join-Path $output 'vc_redist.x64.exe') -Force
Copy-Item (Join-Path $root 'src\Pos.Desktop\Assets\Icons\app.ico') (Join-Path $output 'client\app.ico') -Force
Write-Host "Publicacion de produccion preparada en $output"
Write-Host 'Paquete de produccion preparado para el instalador autocontenido.'
