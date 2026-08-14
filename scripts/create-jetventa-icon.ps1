param(
    [Parameter(Mandatory = $true)][string]$InputImage,
    [string]$Root = (Split-Path $PSScriptRoot -Parent)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$desktopAssets = Join-Path $Root 'src\Pos.Desktop\Assets\Icons'
$installerAssets = Join-Path $Root 'installer\Pos.Setup\Assets'
New-Item -ItemType Directory -Force -Path $desktopAssets, $installerAssets | Out-Null

$source = [System.Drawing.Bitmap]::new($InputImage)
$bitmap = [System.Drawing.Bitmap]::new($source.Width, $source.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.DrawImage($source, 0, 0, $source.Width, $source.Height)
$graphics.Dispose(); $source.Dispose()

# Remove only near-white pixels connected to the outside border.
$queue = [System.Collections.Generic.Queue[System.Drawing.Point]]::new()
$transparent = [bool[,]]::new($bitmap.Width, $bitmap.Height)
for ($x = 0; $x -lt $bitmap.Width; $x++) { $queue.Enqueue([System.Drawing.Point]::new($x, 0)); $queue.Enqueue([System.Drawing.Point]::new($x, $bitmap.Height - 1)) }
for ($y = 1; $y -lt ($bitmap.Height - 1); $y++) { $queue.Enqueue([System.Drawing.Point]::new(0, $y)); $queue.Enqueue([System.Drawing.Point]::new($bitmap.Width - 1, $y)) }
while ($queue.Count -gt 0) {
    $point = $queue.Dequeue()
    if ($point.X -lt 0 -or $point.Y -lt 0 -or $point.X -ge $bitmap.Width -or $point.Y -ge $bitmap.Height -or $transparent[$point.X, $point.Y]) { continue }
    $pixel = $bitmap.GetPixel($point.X, $point.Y)
    if ($pixel.R -lt 242 -or $pixel.G -lt 242 -or $pixel.B -lt 242) { continue }
    $transparent[$point.X, $point.Y] = $true
    $bitmap.SetPixel($point.X, $point.Y, [System.Drawing.Color]::Transparent)
    $queue.Enqueue([System.Drawing.Point]::new($point.X + 1, $point.Y)); $queue.Enqueue([System.Drawing.Point]::new($point.X - 1, $point.Y))
    $queue.Enqueue([System.Drawing.Point]::new($point.X, $point.Y + 1)); $queue.Enqueue([System.Drawing.Point]::new($point.X, $point.Y - 1))
}

$masterPath = Join-Path $desktopAssets 'app-icon-1024.png'
$bitmap.Save($masterPath, [System.Drawing.Imaging.ImageFormat]::Png)
$namedDesktopPath = Join-Path $Root 'src\Pos.Desktop\Assets\jetventa-icon.png'
$namedInstallerPath = Join-Path $installerAssets 'jetventa-icon.png'
$bitmap.Save($namedDesktopPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Save($namedInstallerPath, [System.Drawing.Imaging.ImageFormat]::Png)

function Save-PngSize([System.Drawing.Bitmap]$sourceBitmap, [int]$size, [string]$path) {
    $target = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($target)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($sourceBitmap, 0, 0, $size, $size)
    $g.Dispose(); $target.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $target.Dispose()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256, 512)
foreach ($size in $sizes) { Save-PngSize $bitmap $size (Join-Path $desktopAssets "app-icon-$size.png") }
foreach ($size in $sizes) { Copy-Item (Join-Path $desktopAssets "app-icon-$size.png") (Join-Path $installerAssets "app-icon-$size.png") -Force }

# Build a PNG-compressed ICO with the Windows shell sizes.
$iconSizes = @(16, 24, 32, 48, 64, 128, 256)
$pngData = @{}
foreach ($size in $iconSizes) {
    $temp = [System.IO.MemoryStream]::new(); $target = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($target); $g.Clear([System.Drawing.Color]::Transparent); $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic; $g.DrawImage($bitmap, 0, 0, $size, $size); $g.Dispose()
    $target.Save($temp, [System.Drawing.Imaging.ImageFormat]::Png); $target.Dispose(); $pngData[$size] = $temp.ToArray(); $temp.Dispose()
}
$icon = [System.IO.MemoryStream]::new(); $writer = [System.IO.BinaryWriter]::new($icon)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$iconSizes.Count)
$offset = 6 + (16 * $iconSizes.Count)
foreach ($size in $iconSizes) { $data = $pngData[$size]; $writer.Write([byte]($(if ($size -eq 256) { 0 } else { $size }))); $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size })); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$data.Length); $writer.Write([uint32]$offset); $offset += $data.Length }
foreach ($size in $iconSizes) { $writer.Write($pngData[$size]) }
$writer.Flush(); [System.IO.File]::WriteAllBytes((Join-Path $desktopAssets 'app.ico'), $icon.ToArray()); [System.IO.File]::WriteAllBytes((Join-Path $installerAssets 'app.ico'), $icon.ToArray()); $writer.Dispose(); $icon.Dispose(); $bitmap.Dispose()
Write-Host "Icono JetVenta creado en $desktopAssets y $installerAssets"
