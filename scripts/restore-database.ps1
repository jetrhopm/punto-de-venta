param(
    [Parameter(Mandatory = $true)][string]$BackupFile,
    [string]$TargetDatabase = 'punto_venta_restore_test',
    [switch]$Confirm
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$settings = Get-Content (Join-Path $root '.postgres\development-settings.json') | ConvertFrom-Json
$pgRestore = Join-Path $root '.tools\postgresql-18.4\pgsql\bin\pg_restore.exe'
$psql = Join-Path $root '.tools\postgresql-18.4\pgsql\bin\psql.exe'
$createdb = Join-Path $root '.tools\postgresql-18.4\pgsql\bin\createdb.exe'
if (-not (Test-Path -LiteralPath $BackupFile)) { throw "No existe el respaldo: $BackupFile" }
if (-not $Confirm) { throw 'La restauracion requiere -Confirm y debe apuntar a una base temporal o de recuperacion.' }
$env:PGPASSWORD = $settings.ApplicationPassword
try {
    & $psql '--host=127.0.0.1' ("--port={0}" -f $settings.Port) ("--username={0}" -f $settings.ApplicationUser) '--dbname=postgres' ("--command=SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$TargetDatabase' AND pid <> pg_backend_pid();") | Out-Null
    & $psql '--host=127.0.0.1' ("--port={0}" -f $settings.Port) ("--username={0}" -f $settings.ApplicationUser) '--dbname=postgres' ("--command=DROP DATABASE IF EXISTS `"$TargetDatabase`";") | Out-Null
    & $psql '--host=127.0.0.1' ("--port={0}" -f $settings.Port) ("--username={0}" -f $settings.ApplicationUser) '--dbname=postgres' ("--command=CREATE DATABASE `"$TargetDatabase`" OWNER `"$($settings.ApplicationUser)`";") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo preparar la base temporal de restauracion.' }
    & $pgRestore '--host=127.0.0.1' ("--port={0}" -f $settings.Port) ("--username={0}" -f $settings.ApplicationUser) ("--dbname={0}" -f $TargetDatabase) '--no-owner' '--exit-on-error' $BackupFile
    if ($LASTEXITCODE -ne 0) { throw 'pg_restore no pudo restaurar el respaldo.' }
    $count = & $psql '--host=127.0.0.1' ("--port={0}" -f $settings.Port) ("--username={0}" -f $settings.ApplicationUser) ("--dbname={0}" -f $TargetDatabase) '--tuples-only' '--command=SELECT count(*) FROM pos.product;'
    Write-Host "Restauracion verificada en $TargetDatabase. Productos: $($count.Trim())"
}
finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
