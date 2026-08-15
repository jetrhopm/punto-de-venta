[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BackupFile,
    [switch]$Approve
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$installRoot = $PSScriptRoot
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$dataRoot = Join-Path $env:ProgramData 'PuntoDeVenta'
$logPath = Join-Path $dataRoot 'logs\restauracion.log'
$connectionPath = Join-Path $dataRoot 'config\connection.bin'
$adminSecretPath = Join-Path $dataRoot 'config\postgres-admin.bin'
$postgresBinCandidates = @(
    (Join-Path $installRoot 'postgresql\pgsql\bin'),
    (Join-Path $repositoryRoot '.tools\postgresql-18.4\pgsql\bin')
)
$postgresBin = $postgresBinCandidates | Where-Object { Test-Path (Join-Path $_ 'psql.exe') } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($postgresBin)) { throw 'No se encontraron los componentes de PostgreSQL de JetVenta.' }
$psql = Join-Path $postgresBin 'psql.exe'
$pgRestore = Join-Path $postgresBin 'pg_restore.exe'
$pgDump = Join-Path $postgresBin 'pg_dump.exe'
$apiService = 'PuntoDeVentaApi'

function Write-RestoreLog([string]$Message) {
    $line = "[$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff'))] $Message"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    Write-Host $line
}

function Assert-Administrator {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Ejecuta PowerShell como administrador para restaurar una copia.' }
}

function Get-ProtectedText([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "No existe el archivo protegido requerido: $Path" }
    return [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($Path), $null, [Security.Cryptography.DataProtectionScope]::LocalMachine))
}

function Invoke-Native([string]$File, [string[]]$Arguments) {
    & $File @Arguments 2>&1 | ForEach-Object { Write-RestoreLog "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "Falló $([IO.Path]::GetFileName($File)) con código $LASTEXITCODE." }
}

function Read-ConnectionValue([string]$Connection, [string]$Name) {
    $match = [Text.RegularExpressions.Regex]::Match($Connection, "(?i)(?:^|;)\s*$Name\s*=\s*([^;]+)")
    if (-not $match.Success) { throw "La conexión protegida no contiene $Name." }
    return $match.Groups[1].Value.Trim()
}

trap { Write-RestoreLog "ERROR: $($_.Exception.Message)"; throw }

Assert-Administrator
if (-not $Approve) { throw 'La restauración sustituye la base de datos local. Vuelve a ejecutar con -Approve después de verificar el archivo.' }
$backup = (Resolve-Path -LiteralPath $BackupFile).Path
if (-not $backup.EndsWith('.dump', [StringComparison]::OrdinalIgnoreCase)) { throw 'Selecciona un respaldo .dump de JetVenta.' }
foreach ($file in @($psql, $pgRestore, $pgDump)) { if (-not (Test-Path $file)) { throw "No existe el binario requerido: $file" } }

$checksumPath = "$backup.sha256"
if (-not (Test-Path $checksumPath)) { throw "Falta el comprobante SHA-256 junto al respaldo: $checksumPath" }
$expectedHash = (Get-Content -LiteralPath $checksumPath -Raw).Trim().ToUpperInvariant()
$actualHash = (Get-FileHash -LiteralPath $backup -Algorithm SHA256).Hash.ToUpperInvariant()
if ($expectedHash -ne $actualHash) { throw 'El SHA-256 no coincide. No se restauró ningún dato.' }
Write-RestoreLog "Respaldo verificado: $([IO.Path]::GetFileName($backup))."

$connection = Get-ProtectedText $connectionPath
$adminPassword = Get-ProtectedText $adminSecretPath
$port = Read-ConnectionValue $connection 'Port'
$database = Read-ConnectionValue $connection 'Database'
$applicationUser = Read-ConnectionValue $connection 'Username'
if ($database -ne 'punto_venta' -or $applicationUser -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,62}$') { throw 'La instalación local tiene una configuración de base de datos no admitida.' }

$env:PGPASSWORD = $adminPassword
try {
    $backupDirectory = Join-Path $dataRoot 'backups'
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    $safetyBackup = Join-Path $backupDirectory ("antes-de-restaurar-{0:yyyyMMdd-HHmmss}.dump" -f (Get-Date))
    Write-RestoreLog 'Creando copia preventiva de la base local.'
    Invoke-Native $pgDump @('--host=127.0.0.1', "--port=$port", '--username=pos_admin', "--dbname=$database", '--format=custom', "--file=$safetyBackup", '--no-password')
    $safetyHash = (Get-FileHash -LiteralPath $safetyBackup -Algorithm SHA256).Hash
    Set-Content -LiteralPath "$safetyBackup.sha256" -Value $safetyHash -Encoding ASCII

    Write-RestoreLog 'Deteniendo temporalmente la API de JetVenta.'
    Stop-Service -Name $apiService -Force -ErrorAction SilentlyContinue
    Invoke-Native $psql @('--host=127.0.0.1', "--port=$port", '--username=pos_admin', '--dbname=postgres', '-v', 'ON_ERROR_STOP=1', '-c', "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$database' AND pid <> pg_backend_pid();")
    Invoke-Native $psql @('--host=127.0.0.1', "--port=$port", '--username=pos_admin', '--dbname=postgres', '-v', 'ON_ERROR_STOP=1', '-c', "DROP DATABASE $database;")
    Invoke-Native $psql @('--host=127.0.0.1', "--port=$port", '--username=pos_admin', '--dbname=postgres', '-v', 'ON_ERROR_STOP=1', '-c', "CREATE DATABASE $database OWNER $applicationUser;")
    Write-RestoreLog 'Restaurando estructura y datos de JetVenta.'
    Invoke-Native $pgRestore @('--host=127.0.0.1', "--port=$port", '--username=pos_admin', "--dbname=$database", '--no-owner', '--exit-on-error', $backup)
    Start-Service -Name $apiService
    Write-RestoreLog 'Restauración terminada. JetVenta aplicará migraciones pendientes al iniciar la API.'
} finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    $api = Get-Service -Name $apiService -ErrorAction SilentlyContinue
    if ($null -ne $api -and $api.Status -ne 'Running') { Start-Service -Name $apiService -ErrorAction SilentlyContinue }
}
