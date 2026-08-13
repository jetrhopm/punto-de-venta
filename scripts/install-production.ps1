[CmdletBinding()]
param(
    [string]$InstallRoot = "$env:ProgramFiles\Punto de Venta",
    [string]$DataRoot = "$env:ProgramData\PuntoDeVenta",
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 does not load DPAPI's assembly by default.
Add-Type -AssemblyName System.Security

$installLogPath = Join-Path $DataRoot 'logs\instalacion.log'

function Write-InstallLog([string]$Message) {
    $timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff')
    $line = "[$timestamp] $Message"
    Write-Host $line
    $directory = Split-Path -Parent $installLogPath
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    Add-Content -LiteralPath $installLogPath -Value $line -Encoding UTF8
}

trap {
    Write-InstallLog "ERROR: $($_.Exception.Message)"
    Write-InstallLog 'La instalacion se detuvo. Revisa este archivo y el log de PostgreSQL para diagnostico.'
    throw
}

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Este script debe ejecutarse como administrador.'
    }
}

function Invoke-Native([string]$File, [string[]]$Arguments) {
    $safeArguments = ($Arguments -join ' ') -replace "PASSWORD '[^']*'", "PASSWORD '<oculta>'"
    Write-InstallLog "Ejecutando $([IO.Path]::GetFileName($File)): $safeArguments"
    $previousErrorActionPreference = $ErrorActionPreference
    try {
        # Algunos binarios nativos escriben advertencias en stderr aunque terminen correctamente.
        $ErrorActionPreference = 'Continue'
        & $File @Arguments 2>&1 | ForEach-Object {
            if ($_ -ne $null) { Write-InstallLog "  $_" }
        }
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($exitCode -ne 0) { throw "Fallo el comando $([IO.Path]::GetFileName($File)) con codigo $exitCode." }
    Write-InstallLog "Finalizo $([IO.Path]::GetFileName($File)) correctamente."
}

function Start-PostgresForRecovery {
    $status = & $pgCtl status -D $pgData 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-InstallLog 'PostgreSQL ya esta activo; se usara para recuperar la credencial administrativa.'
        return
    }

    Write-InstallLog 'Iniciando PostgreSQL directamente con pg_ctl y esperando confirmacion de disponibilidad.'
    try {
        Invoke-Native $pgCtl @('start', '-D', $pgData, '-l', $logPath, '-o', "-p $port", '-w')
    } catch {
        Write-InstallLog 'PostgreSQL no pudo iniciar. Ultimas lineas de postgresql.log:'
        if (Test-Path $logPath) {
            Get-Content -LiteralPath $logPath -Tail 60 | ForEach-Object { Write-InstallLog "  $_" }
        } else {
            Write-InstallLog "No existe el log esperado: $logPath"
        }
        throw
    }
}

Assert-Administrator
$installLogPath = Join-Path $DataRoot 'logs\instalacion.log'
Write-InstallLog "Inicio del instalador. Carpeta de aplicacion: $InstallRoot"
Write-InstallLog "Carpeta de datos: $DataRoot"
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
    Write-InstallLog 'Modo desinstalacion: deteniendo y eliminando servicios. Los datos se conservan.'
    Stop-Service $apiService -ErrorAction SilentlyContinue
    Stop-Service $postgresService -ErrorAction SilentlyContinue
    sc.exe delete $apiService | Out-Null
    sc.exe delete $postgresService | Out-Null
    [Environment]::SetEnvironmentVariable('POS_CONNECTION_FILE', $null, 'Machine')
    Write-InstallLog 'Desinstalacion de servicios finalizada.'
    exit 0
}

Write-InstallLog 'Etapa 1/8: creando carpetas protegidas de datos, configuracion y registros.'
foreach ($directory in @($DataRoot, (Join-Path $DataRoot 'config'), (Join-Path $DataRoot 'logs'), $pgData)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    Write-InstallLog "Carpeta lista: $directory"
}
if (-not (Test-Path $pgCtl)) { throw "No se encontraron los binarios PostgreSQL en $postgresBin." }
Write-InstallLog "Binarios PostgreSQL encontrados en $postgresBin"

$passwordBytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($passwordBytes) } finally { $rng.Dispose() }
$password = [Convert]::ToBase64String($passwordBytes).Replace('+','A').Replace('/','B').Replace('=','C')
$adminPassword = $password
$port = 5432

if (-not (Test-Path (Join-Path $pgData 'PG_VERSION'))) {
    Write-InstallLog 'Etapa 2/8: inicializando el cluster PostgreSQL con checksums y autenticacion SCRAM.'
    $adminBytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($adminBytes) } finally { $rng.Dispose() }
    $adminPassword = [Convert]::ToBase64String($adminBytes).Replace('+','A').Replace('/','B').Replace('=','C')
    $bootstrap = Join-Path $DataRoot 'config\bootstrap-password.txt'
    Set-Content -LiteralPath $bootstrap -Value $adminPassword -NoNewline -Encoding ascii
    Write-InstallLog "Archivo temporal de inicializacion creado: $bootstrap"
    [IO.File]::WriteAllBytes($adminSecretPath, [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($adminPassword), $null, [Security.Cryptography.DataProtectionScope]::LocalMachine))
    Write-InstallLog "Credencial administrativa protegida antes de inicializar: $adminSecretPath"
    try {
        Invoke-Native $initDb @('--pgdata', $pgData, '--username', 'pos_admin', '--encoding', 'UTF8', '--auth-host', 'scram-sha-256', '--data-checksums', '--pwfile', $bootstrap)
    } finally {
        Remove-Item $bootstrap -Force -ErrorAction SilentlyContinue
        Write-InstallLog 'Archivo temporal de inicializacion eliminado.'
    }
} elseif (Test-Path $adminSecretPath) {
    Write-InstallLog 'Etapa 2/8: cluster PostgreSQL existente y configurado detectado; se conserva y se recuperan sus credenciales protegidas.'
    $adminPassword = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($adminSecretPath), $null, [Security.Cryptography.DataProtectionScope]::LocalMachine))
} else {
    Write-InstallLog 'Etapa 2/8: cluster existente sin credencial protegida; intentando recuperar pos_admin mediante la conexion local de confianza.'
    Start-PostgresForRecovery
    $adminBytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($adminBytes) } finally { $rng.Dispose() }
    $adminPassword = [Convert]::ToBase64String($adminBytes).Replace('+','A').Replace('/','B').Replace('=','C')
    $localSql = "ALTER ROLE pos_admin PASSWORD '$adminPassword';"
    $hbaPath = Join-Path $pgData 'pg_hba.conf'
    $hbaBackup = Join-Path $DataRoot 'config\pg_hba.conf.before-recovery'
    Copy-Item $hbaPath $hbaBackup -Force
    try {
        $hbaText = [IO.File]::ReadAllText($hbaPath)
        $hbaText = [Text.RegularExpressions.Regex]::Replace($hbaText, '(?m)^(\s*host\s+all\s+all\s+127\.0\.0\.1/32\s+)\S+', '${1}trust')
        $hbaText = [Text.RegularExpressions.Regex]::Replace($hbaText, '(?m)^(\s*host\s+all\s+all\s+::1/128\s+)\S+', '${1}trust')
        [IO.File]::WriteAllText($hbaPath, $hbaText, [Text.UTF8Encoding]::new($false))
        Invoke-Native $pgCtl @('reload', '-D', $pgData)
        Invoke-Native $psql @('-h','127.0.0.1','-p',$port,'-U','pos_admin','-d','postgres','-v','ON_ERROR_STOP=1','-c',$localSql)
    } finally {
        Copy-Item $hbaBackup $hbaPath -Force
        Invoke-Native $pgCtl @('reload', '-D', $pgData)
        $status = & $pgCtl status -D $pgData 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-InstallLog 'Deteniendo el servidor temporal iniciado por pg_ctl antes de registrar el servicio.'
            Invoke-Native $pgCtl @('stop', '-D', $pgData, '-w')
        }
        Remove-Item $hbaBackup -Force -ErrorAction SilentlyContinue
        Write-InstallLog 'Configuracion temporal de pg_hba.conf restaurada.'
    }
    [IO.File]::WriteAllBytes($adminSecretPath, [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($adminPassword), $null, [Security.Cryptography.DataProtectionScope]::LocalMachine))
    Write-InstallLog "Credencial administrativa recuperada y protegida: $adminSecretPath"
}

if (-not (Get-Service $postgresService -ErrorAction SilentlyContinue)) {
    Write-InstallLog 'Etapa 3/8: registrando el servicio dedicado de PostgreSQL.'
    Invoke-Native $pgCtl @('register', '-N', $postgresService, '-D', $pgData, '-S', 'auto')
} else {
    Write-InstallLog 'Etapa 3/8: servicio PostgreSQL existente detectado; se conserva su registro.'
}
Set-Service -Name $postgresService -StartupType Automatic
Write-InstallLog 'Iniciando PostgreSQL y esperando el servicio.'
$postgresStatus = (Get-Service $postgresService).Status
if ($postgresStatus -ne 'Running') {
    Start-Service $postgresService
    Start-Sleep -Seconds 2
    Write-InstallLog 'Servicio PostgreSQL iniciado.'
} else {
    Write-InstallLog 'Servicio PostgreSQL ya estaba activo; se conserva.'
}

$env:PGPASSWORD = $adminPassword
try {
    Write-InstallLog 'Etapa 4/8: creando o actualizando el usuario de aplicacion.'
    $sql = "DO `$`$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'pos_app') THEN CREATE ROLE pos_app LOGIN PASSWORD '$password'; ELSE ALTER ROLE pos_app PASSWORD '$password'; END IF; END `$`$;"
    Invoke-Native $psql @('-h','127.0.0.1','-p',$port,'-U','pos_admin','-d','postgres','-v','ON_ERROR_STOP=1','-c',$sql)
    $existsOutput = @(& $psql -h 127.0.0.1 -p $port -U pos_admin -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='punto_venta'")
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo comprobar si existe la base de datos punto_venta.' }
    $exists = ([string]($existsOutput -join '')).Trim()
    if ($exists -ne '1') {
        Write-InstallLog 'Base punto_venta no existe; creando base de datos.'
        Invoke-Native $psql @('-h','127.0.0.1','-p',$port,'-U','pos_admin','-d','postgres','-v','ON_ERROR_STOP=1','-c','CREATE DATABASE punto_venta OWNER pos_app;')
    } else { Write-InstallLog 'Base punto_venta existente detectada; se conserva sin reinicializar.' }
} finally { Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue }

$connection = "Host=127.0.0.1;Port=$port;Database=punto_venta;Username=pos_app;Password=$password;Application Name=Pos.Production"
$encrypted = [Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($connection), $null, [Security.Cryptography.DataProtectionScope]::LocalMachine)
[IO.File]::WriteAllBytes($secretPath, $encrypted)
Write-InstallLog "Cadena de conexion protegida: $secretPath"
$acl = Get-Acl $secretPath
$acl.SetAccessRuleProtection($true, $false)
$systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
$administratorsSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
$acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new($systemSid, [Security.AccessControl.FileSystemRights]::FullControl, [Security.AccessControl.AccessControlType]::Allow))
$acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new($administratorsSid, [Security.AccessControl.FileSystemRights]::Read, [Security.AccessControl.AccessControlType]::Allow))
Set-Acl $secretPath $acl
[Environment]::SetEnvironmentVariable('POS_CONNECTION_FILE', $secretPath, 'Machine')
[Environment]::SetEnvironmentVariable('POS_API_URLS', 'http://0.0.0.0:5000', 'Machine')
if (-not (Get-NetFirewallRule -DisplayName 'Punto de Venta API LAN' -ErrorAction SilentlyContinue)) {
    Write-InstallLog 'Etapa 5/8: creando regla de Firewall solo para perfil privado, puerto 5000.'
    New-NetFirewallRule -DisplayName 'Punto de Venta API LAN' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5000 -Profile Private | Out-Null
} else {
    Write-InstallLog 'Etapa 5/8: regla de Firewall existente detectada; se conserva.'
}

if (-not (Get-Service $apiService -ErrorAction SilentlyContinue)) {
    Write-InstallLog 'Etapa 6/8: registrando el servicio de Windows de la API.'
    New-Service -Name $apiService -BinaryPathName "`"$api`"" -DisplayName 'Punto de Venta - API' -Description 'API local del sistema Punto de Venta' -StartupType Automatic
} else { Write-InstallLog 'Etapa 6/8: servicio de API existente detectado; se conserva su registro.' }
Write-InstallLog 'Etapa 7/8: iniciando la API y comprobando el servicio.'
Start-Service $apiService -ErrorAction SilentlyContinue
$apiReady = $false
for ($attempt = 1; $attempt -le 30; $attempt++) {
    try {
        $health = Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:5000/health' -TimeoutSec 2
        if ($health.StatusCode -eq 200) {
            $apiReady = $true
            Write-InstallLog "API disponible despues de $attempt intento(s)."
            break
        }
    } catch {
        Write-InstallLog "Esperando que la API termine de iniciar ($attempt/30)."
        Start-Sleep -Seconds 1
    }
}
if (-not $apiReady) { throw 'La API no respondió a la comprobación de inicio. Revisa el servicio PuntoDeVentaApi.' }
Write-InstallLog 'Etapa 8/8: instalacion terminada. PostgreSQL y la API quedaron registrados como servicios de Windows.'
