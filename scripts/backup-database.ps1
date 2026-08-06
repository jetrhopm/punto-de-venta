param(
    [string]$OutputDirectory = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')) 'artifacts\backups')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$settings = Get-Content (Join-Path $root '.postgres\development-settings.json') | ConvertFrom-Json
$pgDump = Join-Path $root '.tools\postgresql-18.4\pgsql\bin\pg_dump.exe'
if (-not (Test-Path $pgDump)) { throw 'No se encontro pg_dump.exe. Ejecuta scripts/dev-setup.ps1.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = Join-Path $OutputDirectory "punto-venta-$stamp.dump"
$manifest = Join-Path $OutputDirectory "punto-venta-$stamp.json"
$env:PGPASSWORD = $settings.ApplicationPassword
try {
    & $pgDump '--host=127.0.0.1' ("--port={0}" -f $settings.Port) ("--username={0}" -f $settings.ApplicationUser) ("--dbname={0}" -f $settings.Database) '--format=custom' ("--file={0}" -f $backup) '--no-password'
    if ($LASTEXITCODE -ne 0) { throw 'pg_dump no pudo crear el respaldo.' }
    $hash = (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash
    [ordered]@{ Database = $settings.Database; CreatedAtUtc = [DateTimeOffset]::UtcNow; File = [IO.Path]::GetFileName($backup); Sha256 = $hash; SizeBytes = (Get-Item $backup).Length } | ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding utf8
    Write-Host "Respaldo creado: $backup"
    Write-Host "SHA-256: $hash"
}
finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
