[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$settingsPath = Join-Path $root '.postgres\development-settings.json'
$postgresRoot = Join-Path $root '.tools\postgresql-18.4\pgsql\bin'
$psql = Join-Path $postgresRoot 'psql.exe'
$pgDump = Join-Path $postgresRoot 'pg_dump.exe'
$sqlPath = Join-Path $PSScriptRoot 'seed-realistic-data.sql'

if (-not (Test-Path -LiteralPath $settingsPath)) { throw 'No existe la configuracion de PostgreSQL de desarrollo.' }
if (-not (Test-Path -LiteralPath $psql)) { throw "No se encontro psql.exe en $postgresRoot." }
if (-not (Test-Path -LiteralPath $pgDump)) { throw "No se encontro pg_dump.exe en $postgresRoot." }

$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if ($settings.Database -ne 'punto_venta_dev') { throw "Este script solo puede modificar punto_venta_dev; destino detectado: $($settings.Database)." }

$backupDirectory = Join-Path $root '.postgres\backups'
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
$backupPath = Join-Path $backupDirectory ("pre-datos-prueba-{0:yyyyMMdd-HHmmss}.dump" -f (Get-Date))

try {
    $env:PGPASSWORD = $settings.ApplicationPassword
    Write-Host 'Creando respaldo previo de la base de desarrollo...'
    & $pgDump -h 127.0.0.1 -p $settings.Port -U $settings.ApplicationUser -d $settings.Database -F c -f $backupPath
    if ($LASTEXITCODE -ne 0) { throw "pg_dump termino con codigo $LASTEXITCODE." }
    (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash | Set-Content -LiteralPath ($backupPath + '.sha256') -Encoding ascii

    Write-Host 'Cargando un ano de ventas, cortes, movimientos, clientes y compras de prueba...'
    & $psql -h 127.0.0.1 -p $settings.Port -U $settings.ApplicationUser -d $settings.Database -v ON_ERROR_STOP=1 -f $sqlPath
    if ($LASTEXITCODE -ne 0) { throw "psql termino con codigo $LASTEXITCODE." }

    Write-Host "Datos de prueba listos. Respaldo previo: $backupPath"
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}
