param(
    [switch]$SkipIntegrationTests
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $root 'Directory.Build.props'
$changelogPath = Join-Path $root 'CHANGELOG.md'
$solutionPath = Join-Path $root 'PuntoDeVenta.slnx'

[xml]$props = Get-Content -Raw -LiteralPath $propsPath
$version = [string]$props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Directory.Build.props debe contener una version con formato X.Y.Z.'
}

$changelog = Get-Content -Raw -LiteralPath $changelogPath
if ($changelog -notmatch [regex]::Escape($version)) {
    throw "CHANGELOG.md no menciona la version $version. Registra el cambio antes de generar una compilacion."
}

Write-Host "Validando JetVenta $version" -ForegroundColor Cyan
dotnet build $solutionPath -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'La compilacion Release fallo.' }

if (-not $SkipIntegrationTests) {
    dotnet test (Join-Path $root 'tests\Pos.IntegrationTests\Pos.IntegrationTests.csproj') -c Release --no-build --filter 'FullyQualifiedName~SaleDraftIntegrationTests' --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Las pruebas criticas de tickets fallaron.' }
}

$status = git -C $root status --short
if ($status) {
    Write-Warning 'Hay cambios sin confirmar. No generes instalador hasta subir un commit a GitHub.'
}

$commit = git -C $root rev-parse --short HEAD
Write-Host "Validacion completada. Commit base: $commit" -ForegroundColor Green
