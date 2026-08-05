Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$pgCtl = Join-Path $root '.tools\postgresql-18.4\pgsql\bin\pg_ctl.exe'
$dataDirectory = Join-Path $root '.postgres\data'
if (-not (Test-Path -LiteralPath $dataDirectory)) { Write-Host 'No existe un cluster PostgreSQL de desarrollo.'; exit 0 }
if (-not (Test-Path -LiteralPath $pgCtl)) { throw 'No se encontro pg_ctl.exe.' }
& $pgCtl -D $dataDirectory status 2>$null
if ($LASTEXITCODE -eq 0) { & $pgCtl -D $dataDirectory -m fast -w stop | Write-Host; Write-Host 'PostgreSQL de desarrollo detenido.' }
else { Write-Host 'PostgreSQL de desarrollo ya estaba detenido.' }
