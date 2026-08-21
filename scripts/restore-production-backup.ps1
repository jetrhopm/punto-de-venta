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

function Wait-ServiceStopped([string]$Name) {
    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $service) { return }
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(30))
}

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

$developmentSettingsPath = Join-Path $repositoryRoot '.postgres\development-settings.json'
$isDevelopment = -not (Test-Path -LiteralPath $connectionPath) -and (Test-Path -LiteralPath $developmentSettingsPath)
if ($isDevelopment) {
    $developmentSettings = Get-Content -LiteralPath $developmentSettingsPath -Raw | ConvertFrom-Json
    $port = [int]$developmentSettings.Port
    $database = [string]$developmentSettings.Database
    $applicationUser = [string]$developmentSettings.ApplicationUser
    if ($database -ne 'punto_venta_dev' -or $applicationUser -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,62}$') { throw 'La configuración de pruebas no es válida.' }
    $databasePassword = [string]$developmentSettings.ApplicationPassword
    Write-RestoreLog 'Modo de pruebas detectado. Se restaurará la base local de desarrollo.'
} else {
    $connection = Get-ProtectedText $connectionPath
    $adminPassword = Get-ProtectedText $adminSecretPath
    $port = Read-ConnectionValue $connection 'Port'
    $database = Read-ConnectionValue $connection 'Database'
    $applicationUser = Read-ConnectionValue $connection 'Username'
    $applicationPassword = Read-ConnectionValue $connection 'Password'
    if ($database -ne 'punto_venta' -or $applicationUser -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,62}$') { throw 'La instalación local tiene una configuración de base de datos no admitida.' }
    $databasePassword = $adminPassword
}

$env:PGPASSWORD = $databasePassword
try {
    $backupDirectory = Join-Path $dataRoot 'backups'
    New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
    $safetyBackup = Join-Path $backupDirectory ("antes-de-restaurar-{0:yyyyMMdd-HHmmss}.dump" -f (Get-Date))
    Write-RestoreLog 'Creando copia preventiva de la base local.'
    $maintenanceUser = if ($isDevelopment) { $applicationUser } else { 'pos_admin' }
    Invoke-Native $pgDump @('--host=127.0.0.1', "--port=$port", "--username=$maintenanceUser", "--dbname=$database", '--format=custom', "--file=$safetyBackup", '--no-password')
    $safetyHash = (Get-FileHash -LiteralPath $safetyBackup -Algorithm SHA256).Hash
    Set-Content -LiteralPath "$safetyBackup.sha256" -Value $safetyHash -Encoding ASCII

    if ($isDevelopment) {
        Write-RestoreLog 'Preparando la base de pruebas para la restauración.'
        Invoke-Native $psql @('--host=127.0.0.1', "--port=$port", "--username=$applicationUser", '--dbname=postgres', '-v', 'ON_ERROR_STOP=1', '-c', "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$database' AND pid <> pg_backend_pid();")
        Write-RestoreLog 'Restaurando estructura y datos de JetVenta en modo de pruebas.'
        Invoke-Native $pgRestore @('--host=127.0.0.1', "--port=$port", "--username=$applicationUser", "--dbname=$database", '--clean', '--if-exists', '--no-owner', '--exit-on-error', $backup)
        Write-RestoreLog 'Restauración de pruebas terminada.'
    } else {
        Write-RestoreLog 'Deteniendo temporalmente la API de JetVenta.'
        Stop-Service -Name $apiService -Force -ErrorAction SilentlyContinue
        Wait-ServiceStopped $apiService
        Invoke-Native $psql @('--host=127.0.0.1', "--port=$port", '--username=pos_admin', '--dbname=postgres', '-v', 'ON_ERROR_STOP=1', '-c', "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$database' AND pid <> pg_backend_pid();")
        Invoke-Native $psql @('--host=127.0.0.1', "--port=$port", '--username=pos_admin', '--dbname=postgres', '-v', 'ON_ERROR_STOP=1', '-c', "DROP DATABASE $database;")
        Invoke-Native $psql @('--host=127.0.0.1', "--port=$port", '--username=pos_admin', '--dbname=postgres', '-v', 'ON_ERROR_STOP=1', '-c', "CREATE DATABASE $database OWNER $applicationUser;")
        Write-RestoreLog 'Restaurando estructura y datos de JetVenta.'
        Invoke-Native $pgRestore @('--host=127.0.0.1', "--port=$port", '--username=pos_admin', "--dbname=$database", '--clean', '--if-exists', '--no-owner', '--exit-on-error', $backup)
        Write-RestoreLog 'Reparando propietarios y permisos de la base restaurada.'
        $permissionsSql = @'
CREATE SCHEMA IF NOT EXISTS pos AUTHORIZATION pos_app;
ALTER SCHEMA pos OWNER TO pos_app;
GRANT USAGE, CREATE ON SCHEMA pos TO pos_app;
GRANT USAGE, CREATE ON SCHEMA public TO pos_app;
GRANT SELECT, INSERT, UPDATE, DELETE, REFERENCES, TRIGGER ON ALL TABLES IN SCHEMA pos TO pos_app;
GRANT SELECT, INSERT, UPDATE, DELETE, REFERENCES, TRIGGER ON ALL TABLES IN SCHEMA public TO pos_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA pos TO pos_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO pos_app;
DO $$
DECLARE item record;
BEGIN
    FOR item IN SELECT schemaname, tablename FROM pg_catalog.pg_tables WHERE schemaname IN ('pos', 'public') LOOP
        EXECUTE format('ALTER TABLE %I.%I OWNER TO pos_app', item.schemaname, item.tablename);
    END LOOP;
    FOR item IN SELECT sequence_schema, sequence_name FROM information_schema.sequences WHERE sequence_schema IN ('pos', 'public') LOOP
        EXECUTE format('ALTER SEQUENCE %I.%I OWNER TO pos_app', item.sequence_schema, item.sequence_name);
    END LOOP;
END $$;
'@
        Invoke-Native $psql @('--host=127.0.0.1', "--port=$port", '--username=pos_admin', "--dbname=$database", '-v', 'ON_ERROR_STOP=1', '-c', $permissionsSql)
        $historyCheckSql = @'
DO $$
DECLARE history_table regclass;
BEGIN
    history_table := COALESCE(
        to_regclass('pos."__EFMigrationsHistory"'),
        to_regclass('public."__EFMigrationsHistory"'));
    IF history_table IS NOT NULL THEN
        EXECUTE format('SELECT 1 FROM %s LIMIT 1', history_table);
    END IF;
END $$;
'@
        $env:PGPASSWORD = $applicationPassword
        try {
            Invoke-Native $psql @('--host=127.0.0.1', "--port=$port", '--username=pos_app', "--dbname=$database", '-v', 'ON_ERROR_STOP=1', '-c', $historyCheckSql)
        } finally {
            $env:PGPASSWORD = $databasePassword
        }
        Write-RestoreLog 'Permisos de la base restaurada comprobados con la cuenta de JetVenta.'
        Start-Service -Name $apiService
        Write-RestoreLog 'Restauración terminada. JetVenta aplicará migraciones pendientes al iniciar la API.'
    }
} finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    if (-not $isDevelopment) {
        $api = Get-Service -Name $apiService -ErrorAction SilentlyContinue
        if ($null -ne $api -and $api.Status -ne 'Running') { Start-Service -Name $apiService -ErrorAction SilentlyContinue }
    }
}
