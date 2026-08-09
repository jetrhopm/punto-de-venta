[CmdletBinding()]
param(
    [string]$InstallRoot = "$env:ProgramFiles\Punto de Venta",
    [string]$DataRoot = "$env:ProgramData\PuntoDeVenta",
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Este script debe ejecutarse como administrador.'
    }
}

function Invoke-Native([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Fallo el comando $([IO.Path]::GetFileName($File)) con codigo $LASTEXITCODE." }
}

Assert-Administrator
$apiService = 'PuntoDeVentaApi'
$postgresService = 'PuntoDeVentaPostgreSQL'
$postgresBin = Join-Path $InstallRoot 'postgresql\pgsql\bin'
$pgData = Join-Path $DataRoot 'postgresql\data'
$secretPath = Join-Path $DataRoot 'config\connection.bin'
$adminSecretPath = Join-Path $DataRoot 'config\postgres-admin.bin'
$logPath = Join-Path $DataRoot 'logs\postgresql.log'
$pgCtl = Join-Path $postgresBin 'pg_ctl.exe'
$initDb = Join-Path $postgresBin 'initdb.exe'
$psql = Join-Path $postgresBin 'psql.exe'
$api = Join-Path $InstallRoot 'api\Pos.Api.exe'

if ($Uninstall) {
    Stop-Service $apiService -ErrorAction SilentlyContinue
    Stop-Service $postgresService -ErrorAction SilentlyContinue
    sc.exe delete $apiService | Out-Null
    sc.exe delete $postgresService | Out-Null
    [Environment]::SetEnvironmentVariable('POS_CONNECTION_FILE', $null, 'Machine')
    exit 0
}

foreach ($directory in @($DataRoot, (Join-Path $DataRoot 'config'), (Join-Path $DataRoot 'logs'), $pgData)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
if (-not (Test-Path $pgCtl)) { throw "No se encontraron los binarios PostgreSQL en $postgresBin." }

$passwordBytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($passwordBytes) } finally { $rng.Dispose() }
$password = [Convert]::ToBase64String($passwordBytes).Replace('+','A').Replace('/','B').Replace('=','C')
$adminPassword = $password
$port = 5432

if (-not (Test-Path (Join-Path $pgData 'PG_VERSION'))) {
    $adminBytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($adminBytes) } finally { $rng.Dispose() }
    $adminPassword = [Convert]::ToBase64String($adminBytes).Replace('+','A').Replace('/','B').Replace('=','C')
    $bootstrap = Join-Path $DataRoot 'config\bootstrap-password.txt'
    Set-Content -LiteralPath $bootstrap -Value $adminPassword -NoNewline -Encoding ascii
    try {
        Invoke-Native $initDb @('--pgdata', $pgData, '--username', 'pos_admin', '--encoding', 'UTF8', '--auth-host', 'scram-sha-256', '--data-checksums', '--pwfile', $bootstrap)
    } finally { Remove-Item $bootstrap -Force -ErrorAction SilentlyContinue }
    [IO.File]::WriteAllBytes($adminSecretPath, [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($adminPassword), $null, [Security.Cryptography.DataProtectionScope]::LocalMachine))
} elseif (Test-Path $adminSecretPath) {
    $adminPassword = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($adminSecretPath), $null, [Security.Cryptography.DataProtectionScope]::LocalMachine))
}

if (-not (Get-Service $postgresService -ErrorAction SilentlyContinue)) {
    Invoke-Native $pgCtl @('register', '-N', $postgresService, '-D', $pgData, '-S', 'auto')
}
Set-Service -Name $postgresService -StartupType Automatic
Start-Service $postgresService
Start-Sleep -Seconds 2

$env:PGPASSWORD = $adminPassword
try {
    $sql = "DO `$`$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'pos_app') THEN CREATE ROLE pos_app LOGIN PASSWORD '$password'; ELSE ALTER ROLE pos_app PASSWORD '$password'; END IF; END `$`$;"
    Invoke-Native $psql @('-h','127.0.0.1','-p',$port,'-U','pos_admin','-d','postgres','-v','ON_ERROR_STOP=1','-c',$sql)
    $exists = & $psql -h 127.0.0.1 -p $port -U pos_admin -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='punto_venta'"
    if ($exists.Trim() -ne '1') { Invoke-Native $psql @('-h','127.0.0.1','-p',$port,'-U','pos_admin','-d','postgres','-v','ON_ERROR_STOP=1','-c','CREATE DATABASE punto_venta OWNER pos_app;') }
} finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }

$connection = "Host=127.0.0.1;Port=$port;Database=punto_venta;Username=pos_app;Password=$password;Application Name=Pos.Production"
$encrypted = [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($connection), $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
[IO.File]::WriteAllBytes($secretPath, $encrypted)
$acl = Get-Acl $secretPath
$acl.SetAccessRuleProtection($true, $false)
$acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new('SYSTEM','FullControl','Allow'))
$acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new('BUILTIN\Administrators','Read','Allow'))
Set-Acl $secretPath $acl
[Environment]::SetEnvironmentVariable('POS_CONNECTION_FILE', $secretPath, 'Machine')

if (-not (Get-Service $apiService -ErrorAction SilentlyContinue)) {
    New-Service -Name $apiService -BinaryPathName "`"$api`"" -DisplayName 'Punto de Venta - API' -Description 'API local del sistema Punto de Venta' -StartupType Automatic
} else { Restart-Service $apiService -Force }
Start-Service $apiService -ErrorAction SilentlyContinue
Write-Host 'PostgreSQL y la API quedaron registrados como servicios de Windows.'
