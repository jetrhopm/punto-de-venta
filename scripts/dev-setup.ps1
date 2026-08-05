[CmdletBinding()]
param(
    [string]$PostgreSqlArchiveUrl = 'https://get.enterprisedb.com/postgresql/postgresql-18.4-1-windows-x64-binaries.zip',
    [int]$Port = 55432
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-DevelopmentSecret {
    $bytes = New-Object byte[] 24
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($bytes) } finally { $generator.Dispose() }
    ([Convert]::ToBase64String($bytes) -replace '[^a-zA-Z0-9]', '').Substring(0, 28)
}

function Test-TcpPortInUse([int]$CandidatePort) {
    return (Get-NetTCPConnection -State Listen -LocalPort $CandidatePort -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0
}

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$tools = Join-Path $root '.tools'
$postgresTools = Join-Path $tools 'postgresql-18.4'
$archive = Join-Path $tools 'postgresql-18.4-windows-x64-binaries.zip'
$postgresRoot = Join-Path $root '.postgres'
$dataDirectory = Join-Path $postgresRoot 'data'
$logDirectory = Join-Path $postgresRoot 'logs'
$secretsDirectory = Join-Path $postgresRoot 'secrets'
$settingsPath = Join-Path $postgresRoot 'development-settings.json'
$passwordPath = Join-Path $secretsDirectory 'bootstrap-password.txt'
$binaryRoot = Join-Path $postgresTools 'pgsql'
$initdb = Join-Path $binaryRoot 'bin\initdb.exe'
$pgCtl = Join-Path $binaryRoot 'bin\pg_ctl.exe'
$psql = Join-Path $binaryRoot 'bin\psql.exe'

if ($Port -lt 1025 -or $Port -gt 65535) { throw 'El puerto debe estar entre 1025 y 65535.' }
New-Item -ItemType Directory -Force -Path $tools, $postgresRoot, $logDirectory, $secretsDirectory | Out-Null

if (-not (Test-Path -LiteralPath $initdb)) {
    if (-not (Test-Path -LiteralPath $archive)) {
        Write-Host 'Descargando binarios oficiales PostgreSQL 18.4 para desarrollo...'
        Invoke-WebRequest -Uri $PostgreSqlArchiveUrl -OutFile $archive -UseBasicParsing
    }
    Write-Host "SHA256 descargado: $((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash)"
    Write-Host 'Extrayendo binarios PostgreSQL aislados...'
    Expand-Archive -LiteralPath $archive -DestinationPath $postgresTools -Force
}

if (-not (Test-Path -LiteralPath $initdb)) { throw 'No se encontro initdb.exe despues de extraer el archivo oficial.' }

if (-not (Test-Path -LiteralPath $settingsPath)) {
    if (Test-TcpPortInUse $Port) { throw "El puerto $Port ya esta en uso. Ejecuta con -Port y un puerto libre." }
    $bootstrapPassword = New-DevelopmentSecret
    $applicationPassword = New-DevelopmentSecret
    Set-Content -LiteralPath $passwordPath -Value $bootstrapPassword -NoNewline -Encoding ascii
    try {
        Write-Host 'Inicializando cluster con checksums y SCRAM...'
        & $initdb --pgdata=$dataDirectory --username=pos_dev --encoding=UTF8 --auth-host=scram-sha-256 --data-checksums --pwfile=$passwordPath
        $startOptions = "-p $Port -c listen_addresses=127.0.0.1 -c fsync=on -c synchronous_commit=on -c full_page_writes=on"
        & $pgCtl -D $dataDirectory -l (Join-Path $logDirectory 'postgresql.log') -o $startOptions -w start
        $env:PGPASSWORD = $bootstrapPassword
        $arguments = @('-h', '127.0.0.1', '-p', $Port, '-U', 'pos_dev', '-d', 'postgres', '-v', 'ON_ERROR_STOP=1')
        & $psql @arguments -c "CREATE ROLE pos_app LOGIN PASSWORD '$applicationPassword';"
        & $psql @arguments -c 'CREATE DATABASE punto_venta_dev OWNER pos_app ENCODING ''UTF8'';'
        [ordered]@{ Port=$Port; Database='punto_venta_dev'; ApplicationUser='pos_app'; ApplicationPassword=$applicationPassword; ConnectionString="Host=127.0.0.1;Port=$Port;Database=punto_venta_dev;Username=pos_app;Password=$applicationPassword;Application Name=Pos.Development"; ArchiveSha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash } | ConvertTo-Json | Set-Content -LiteralPath $settingsPath -Encoding utf8
    } finally {
        Remove-Item -LiteralPath $passwordPath -Force -ErrorAction SilentlyContinue
        Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    }
}
else {
    & $pgCtl -D $dataDirectory status 2>$null
    if ($LASTEXITCODE -ne 0) {
        $startOptions = "-p $Port -c listen_addresses=127.0.0.1 -c fsync=on -c synchronous_commit=on -c full_page_writes=on"
        & $pgCtl -D $dataDirectory -l (Join-Path $logDirectory 'postgresql.log') -o $startOptions -w start
    }
}

Write-Host "PostgreSQL de desarrollo listo en 127.0.0.1:$Port."
