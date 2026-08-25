[CmdletBinding()]
param(
    [string]$InputFile = 'C:\Users\jethr\.codex\attachments\6291c3fe-302d-46fe-88ee-7e0e5c3a2822\pasted-text.txt',
    [string]$OutputHtml = 'docs\JetVenta-Plan-de-Pruebas-Usuario-Final-2.1.7.html'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Html([string]$Value) {
    return [System.Net.WebUtility]::HtmlEncode($Value)
}

function Expected([string]$Text) {
    $value = $Text.ToLowerInvariant()
    if ($value -match 'restaur|respaldo|base de datos') { return 'La operación termina correctamente, conserva el respaldo original y deja la información disponible.' }
    if ($value -match 'no instalar .net|no instalar postgresql') { return 'El instalador incluye el componente y no solicita una instalación manual.' }
    if ($value -match 'sin alertas|no se cierre|no quede bloqueado|no duplique|no se altere|no queden') { return 'No ocurre el problema indicado y JetVenta permanece utilizable.' }
    if ($value -match 'confirmar|revisar|verificar|revisar') { return 'La condición indicada se muestra correctamente y coincide con la operación realizada.' }
    if ($value -match 'crear|agregar|registrar|configurar|seleccionar|activar|editar|desactivar|cambiar|abrir|cerrar|buscar|consultar|probar|introducir|escanear|escribir|eliminar|cancelar|devolver|cobrar|exportar|instalar|reiniciar|detener|filtrar|ordenar') { return 'La acción se completa sin error y guarda el resultado de forma consistente.' }
    return 'La prueba se completa sin errores y el resultado queda disponible para continuar.'
}

$lines = Get-Content -LiteralPath $InputFile -Encoding UTF8
$sections = [System.Collections.Generic.List[object]]::new()
$current = $null
$reportItems = [System.Collections.Generic.List[string]]::new()
$inReport = $false
foreach ($line in $lines) {
    $trimmed = $line.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }
    if ($trimmed -match '^\d+\.\s+(.+)$') {
        $current = [ordered]@{ Title = $Matches[1]; Items = [System.Collections.Generic.List[string]]::new() }
        $sections.Add($current)
        $inReport = $false
        continue
    }
    if ($trimmed -match '^Cómo reportar cada problema$') {
        $inReport = $true
        continue
    }
    if ($trimmed -match '^-\s+(.+)$') {
        if ($inReport) { $reportItems.Add($Matches[1]) }
        elseif ($null -ne $current) { $current.Items.Add($Matches[1]) }
    }
}

$outputPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')) $OutputHtml
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null

$html = [System.Text.StringBuilder]::new()
[void]$html.AppendLine('<!doctype html><html><head><meta charset="utf-8"><title>JetVenta - Plan de pruebas de usuario final 2.1.7</title>')
[void]$html.AppendLine('<style>')
[void]$html.AppendLine('@page { size: Letter landscape; margin: 0.55in; } body { font-family: Calibri, Arial, sans-serif; color: #102a43; font-size: 9pt; line-height: 1.2; margin: 0; } h1 { color: #0b4f71; font-size: 24pt; margin: 0 0 4pt; } h2 { color: #0b6e99; font-size: 15pt; margin: 16pt 0 6pt; border-bottom: 2px solid #8ecae6; padding-bottom: 3pt; } h3 { color: #135d7a; font-size: 11pt; margin: 8pt 0 3pt; } p { margin: 4pt 0 7pt; } .subtitle { color: #59758a; font-size: 11pt; margin-bottom: 16pt; } table.callout { width: 100%; border-collapse: collapse; margin: 9pt 0 12pt; table-layout: fixed; } table.callout td { padding: 9pt 11pt; } table.callout.blue td { background: #eaf5fa; border: 4px solid #0b78a8; } table.callout.yellow td { background: #fff6df; border: 4px solid #d49a00; } .meta { width: 100%; border-collapse: collapse; margin: 8pt 0 12pt; } .meta td { border: 1px solid #c8d6df; padding: 6pt; height: 20pt; } .meta .label { width: 18%; background: #eaf0f4; font-weight: bold; } table.checklist { width: 100%; border-collapse: collapse; table-layout: fixed; margin: 4pt 0 13pt; page-break-inside: auto; font-size: 9pt; } table.checklist col.status-col { width: 0.8in; } table.checklist col.test-col { width: 2.9in; } table.checklist col.expected-col { width: 3.0in; } table.checklist col.notes-col { width: 3.2in; } table.checklist thead { display: table-header-group; } table.checklist tr { page-break-inside: avoid; } table.checklist th { background: #dcecf4; color: #0b4f71; font-weight: bold; text-align: left; padding: 6pt; border: 1px solid #aac3d0; } table.checklist td { vertical-align: top; padding: 6pt; border: 1px solid #c8d6df; overflow-wrap: anywhere; } table.checklist th:nth-child(1), table.checklist td:nth-child(1) { text-align: center; } .status { color: #526b7a; font-weight: bold; white-space: nowrap; } .notes { color: #526b7a; min-height: 30pt; } .section-note { color: #526b7a; font-style: italic; margin: 3pt 0 5pt; } .page-break { page-break-before: always; } .footer { color: #718096; font-size: 8pt; margin-top: 16pt; border-top: 1px solid #c8d6df; padding-top: 5pt; }')
[void]$html.AppendLine('</style></head><body>')
[void]$html.AppendLine('<h1>JetVenta</h1>')
[void]$html.AppendLine('<div class="subtitle">Plan de pruebas como usuario final - versión 2.1.7</div>')
[void]$html.AppendLine('<table class="callout blue"><tr><td><strong>Objetivo.</strong> Validar la instalación, operación, recuperación de datos y experiencia de uso de JetVenta como lo haría un usuario final, sin ejecutar comandos técnicos salvo que una prueba lo indique expresamente.</td></tr></table>')
[void]$html.AppendLine('<h2>Cómo usar este documento</h2>')
[void]$html.AppendLine('<p>En cada fila marca una sola opción de estado: <strong>F</strong> = Funciona, <strong>D</strong> = Funciona con detalles, <strong>P</strong> = Hay un problema. Escribe en “Resultado obtenido y notas” todo lo necesario: pasos realizados, mensaje visible, hora, captura, datos usados y qué debería corregirse. Puedes ampliar cada fila en Word sin límite práctico de caracteres.</p>')
[void]$html.AppendLine('<table class="callout yellow"><tr><td><strong>Correcciones relevantes de la versión 2.1.7.</strong><br>1. La restauración carga primero la copia en una base temporal y solo la intercambia cuando la API responde correctamente.<br>2. Los avisos normales de PostgreSQL, como “la base no existe, omitiendo”, ya no detienen la restauración. Solo un código de salida real se considera error.<br>3. La restauración desde pruebas hacia producción conserva el respaldo original y crea una copia preventiva de la base actual.<br>4. Si la API no inicia con la base restaurada, JetVenta intenta volver a la base anterior.<br>5. Los respaldos locales ya no tienen un límite automático de cinco copias; el administrador puede eliminarlos manualmente.</td></tr></table>')
[void]$html.AppendLine('<h2>Datos de la prueba</h2><table class="meta"><tr><td class="label">Versión probada</td><td>2.1.7</td><td class="label">Fecha</td><td></td></tr><tr><td class="label">Probador</td><td></td><td class="label">Equipo / VM</td><td></td></tr><tr><td class="label">Windows</td><td></td><td class="label">Resolución / escala</td><td></td></tr><tr><td class="label">Tipo de instalación</td><td>Limpia / actualización / restauración</td><td class="label">Tienda</td><td></td></tr></table>')

$sectionNumber = 0
foreach ($section in $sections) {
    $sectionNumber++
    [void]$html.AppendLine("<h2>$sectionNumber. $(Html $section.Title)</h2>")
    if ($section.Title -eq 'Respaldos y restauración') {
        [void]$html.AppendLine('<p class="section-note">Para la restauración de otra computadora: usa el archivo <strong>.dump</strong> junto a su <strong>.dump.sha256</strong>. Cierra JetVenta antes de iniciar el proceso y confirma que la ventana de progreso llegue a su etapa final.</p>')
    } elseif ($section.Title -eq 'Actualización') {
        [void]$html.AppendLine('<p class="section-note">La actualización debe reconocer la instalación existente, conservar datos y no crear un segundo PostgreSQL. No desinstales antes de esta prueba.</p>')
    } elseif ($section.Title -eq 'Servicios y recuperación') {
        [void]$html.AppendLine('<p class="section-note">Si la API se detiene durante la operación, verifica que JetVenta muestre una explicación y dirija al usuario a Configuración &gt; Diagnóstico &gt; Levantar API, sin dejar la ventana bloqueada.</p>')
    }
    [void]$html.AppendLine('<table class="checklist"><colgroup><col class="status-col"><col class="test-col"><col class="expected-col"><col class="notes-col"></colgroup><thead><tr><th>Estado</th><th>Prueba</th><th>Resultado esperado</th><th>Resultado obtenido y notas</th></tr></thead><tbody>')
    $itemNumber = 0
    foreach ($item in $section.Items) {
        $itemNumber++
        [void]$html.AppendLine("<tr><td class=""status"">[ ] F<br>[ ] D<br>[ ] P</td><td><strong>$sectionNumber.$itemNumber</strong><br>$(Html $item)</td><td>$(Html (Expected $item))</td><td class=""notes""><br><br><br></td></tr>")
    }
    [void]$html.AppendLine('</tbody></table>')
}

[void]$html.AppendLine('<h2>Cómo reportar cada problema</h2>')
[void]$html.AppendLine('<p>Completa estos datos para que el problema pueda reproducirse y corregirse:</p><table class="checklist"><colgroup><col class="status-col"><col class="test-col"><col class="expected-col"><col class="notes-col"></colgroup><thead><tr><th>Estado</th><th>Dato</th><th>Qué registrar</th><th>Notas</th></tr></thead><tbody>')
foreach ($item in $reportItems) {
    [void]$html.AppendLine("<tr><td class=""status"">[ ]</td><td>$(Html $item)</td><td>Información concreta de la prueba, sin contraseñas ni tokens.</td><td class=""notes""><br><br><br></td></tr>")
}
[void]$html.AppendLine('</tbody></table><div class="footer">JetVenta - Plan de pruebas de usuario final - versión 2.1.7</div></body></html>')
Set-Content -LiteralPath $outputPath -Value $html.ToString() -Encoding UTF8
Write-Host "Documento HTML generado: $outputPath"
