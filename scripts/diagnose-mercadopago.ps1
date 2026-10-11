# Muestra el estado de Mercado Pago Point en la base de desarrollo, sin tokens ni secretos.
# Uso: powershell -ExecutionPolicy Bypass -File scripts\diagnose-mercadopago.ps1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$settings = Get-Content (Join-Path $root '.postgres\development-settings.json') | ConvertFrom-Json
$psql = Join-Path $root '.tools\postgresql-18.4\pgsql\bin\psql.exe'
$env:PGPASSWORD = $settings.ApplicationPassword
$env:PGCLIENTENCODING = 'UTF8'
function Run([string]$title, [string]$sql) {
    Write-Host ""
    Write-Host "== $title ==" -ForegroundColor Cyan
    & $psql '--host=127.0.0.1' ("--port={0}" -f $settings.Port) ("--username={0}" -f $settings.ApplicationUser) ("--dbname={0}" -f $settings.Database) '--pset=pager=off' "--command=$sql"
}
try {
    Run 'Cuenta conectada' 'SELECT "MercadoPagoEnvironment" AS ambiente, "MercadoPagoUserId" AS usuario_mp, "MercadoPagoTokenExpiresAtUtc" AS token_vence, length("MercadoPagoAccessTokenProtected") > 0 AS tiene_token FROM pos.store;'
    Run 'Cajas y terminal' 'SELECT "Name" AS caja, "MercadoPagoEnabled" AS point_activo, "MercadoPagoTerminalId" AS terminal FROM pos."register" WHERE "IsActive";'
    Run 'Ultimos cobros Point' 'SELECT "CreatedAtUtc" AS creado, "Amount" AS importe, "Status" AS estado, left("StatusDetail", 90) AS detalle, "ProviderOrderId" AS orden_mp, "UpdatedAtUtc" AS actualizado FROM pos.mercado_pago_order ORDER BY "CreatedAtUtc" DESC LIMIT 5;'
}
finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }
