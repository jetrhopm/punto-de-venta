Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$settingsPath = Join-Path $root '.postgres\development-settings.json'
if (-not (Test-Path $settingsPath)) { Write-Host 'FALLO: no existe configuracion de PostgreSQL de desarrollo.'; exit 1 }
$settings = Get-Content $settingsPath | ConvertFrom-Json
$pgIsReady = Join-Path $root '.tools\postgresql-18.4\pgsql\bin\pg_isready.exe'
$psql = Join-Path $root '.tools\postgresql-18.4\pgsql\bin\psql.exe'
$env:PGPASSWORD = $settings.ApplicationPassword
try {
    & $pgIsReady '--host=127.0.0.1' ("--port={0}" -f $settings.Port) ("--dbname={0}" -f $settings.Database)
    if ($LASTEXITCODE -ne 0) { Write-Host 'FALLO: PostgreSQL no responde.' } else { Write-Host 'OK: PostgreSQL responde.' }
    $checks = & $psql '--host=127.0.0.1' ("--port={0}" -f $settings.Port) ("--username={0}" -f $settings.ApplicationUser) ("--dbname={0}" -f $settings.Database) '--tuples-only' "--command=SELECT current_setting('fsync') || '|' || current_setting('synchronous_commit') || '|' || current_setting('full_page_writes');"
    Write-Host "Persistencia PostgreSQL (fsync|synchronous_commit|full_page_writes): $($checks.Trim())"
    $migration = & $psql '--host=127.0.0.1' ("--port={0}" -f $settings.Port) ("--username={0}" -f $settings.ApplicationUser) ("--dbname={0}" -f $settings.Database) '--tuples-only' '--command=SELECT count(*) FROM information_schema.tables WHERE table_schema = ''public'' AND table_name = ''__EFMigrationsHistory'';'
    Write-Host "Historial de migraciones disponible: $($migration.Trim() -eq '1')"
    $free = [IO.DriveInfo]::new($root.Path.Substring(0, 1)).AvailableFreeSpace
    Write-Host "Espacio libre del disco: $([math]::Round($free / 1GB, 2)) GB"
}
finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
