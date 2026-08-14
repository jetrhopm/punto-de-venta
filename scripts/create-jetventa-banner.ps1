param(
    [Parameter(Mandatory = $true)][string]$InputImage,
    [string]$Root = (Split-Path $PSScriptRoot -Parent)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$desktopPath = Join-Path $Root 'src\Pos.Desktop\Assets\jetventa-banner.png'
$installerPath = Join-Path $Root 'installer\Pos.Setup\Assets\jetventa-banner.png'
New-Item -ItemType Directory -Force -Path (Split-Path $desktopPath), (Split-Path $installerPath) | Out-Null

$source = [System.Drawing.Bitmap]::new($InputImage)
$bitmap = [System.Drawing.Bitmap]::new($source.Width, $source.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bitmap); $g.DrawImage($source, 0, 0, $source.Width, $source.Height); $g.Dispose(); $source.Dispose()

# La imagen JPG conserva un tablero de transparencia; quitamos solo los pixeles neutros claros conectados al borde.
$queue = [System.Collections.Generic.Queue[System.Drawing.Point]]::new()
$visited = [bool[,]]::new($bitmap.Width, $bitmap.Height)
for ($x = 0; $x -lt $bitmap.Width; $x++) { $queue.Enqueue([System.Drawing.Point]::new($x, 0)); $queue.Enqueue([System.Drawing.Point]::new($x, $bitmap.Height - 1)) }
for ($y = 1; $y -lt ($bitmap.Height - 1); $y++) { $queue.Enqueue([System.Drawing.Point]::new(0, $y)); $queue.Enqueue([System.Drawing.Point]::new($bitmap.Width - 1, $y)) }
while ($queue.Count -gt 0) {
    $point = $queue.Dequeue()
    if ($point.X -lt 0 -or $point.Y -lt 0 -or $point.X -ge $bitmap.Width -or $point.Y -ge $bitmap.Height -or $visited[$point.X, $point.Y]) { continue }
    $visited[$point.X, $point.Y] = $true
    $pixel = $bitmap.GetPixel($point.X, $point.Y)
    $neutral = [Math]::Max($pixel.R, [Math]::Max($pixel.G, $pixel.B)) - [Math]::Min($pixel.R, [Math]::Min($pixel.G, $pixel.B)) -lt 14
    $light = $pixel.R -gt 175 -and $pixel.G -gt 175 -and $pixel.B -gt 175
    if (-not ($neutral -and $light)) { continue }
    $bitmap.SetPixel($point.X, $point.Y, [System.Drawing.Color]::Transparent)
    $queue.Enqueue([System.Drawing.Point]::new($point.X + 1, $point.Y)); $queue.Enqueue([System.Drawing.Point]::new($point.X - 1, $point.Y))
    $queue.Enqueue([System.Drawing.Point]::new($point.X, $point.Y + 1)); $queue.Enqueue([System.Drawing.Point]::new($point.X, $point.Y - 1))
}
$bitmap.Save($desktopPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Save($installerPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Host "Banner JetVenta creado: $desktopPath"
