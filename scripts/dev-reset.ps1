Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$target = Resolve-Path (Join-Path $PSScriptRoot '..\.local') -ErrorAction SilentlyContinue
if ($null -eq $target) {
    Write-Host 'No hay datos locales para reiniciar.'
    exit 0
}

Write-Host "Destino a reiniciar: $target"
$confirmation = Read-Host 'Escribe REINICIAR para borrar datos locales de desarrollo'
if ($confirmation -ne 'REINICIAR') {
    Write-Host 'Operacion cancelada.'
    exit 0
}

Remove-Item -LiteralPath $target -Recurse -Force
Write-Host 'Datos locales de desarrollo reiniciados.'
