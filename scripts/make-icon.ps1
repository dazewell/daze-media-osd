# Regenerates the tray icons in src/DazeMediaOsd.App/Assets: a Segoe Fluent Icons speaker glyph, white for
# a dark taskbar and black for a light one (Win11 tray icons are monochrome). Multi-size .ico, PNG frames.
param([string]$OutDir = "$PSScriptRoot/../src/DazeMediaOsd.App/Assets")

Add-Type -AssemblyName System.Drawing
$glyph = [string][char]0xE995 # Volume3
$sizes = 16, 20, 24, 32, 40, 48, 64, 256

function New-Png([int]$size, [System.Drawing.Color]$color) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $font = [System.Drawing.Font]::new('Segoe Fluent Icons', [float]$size, [System.Drawing.GraphicsUnit]::Pixel)
    $format = [System.Drawing.StringFormat]::GenericTypographic
    # GenericTypographic's LineLimit drops the line: the font's line height exceeds the square.
    $format.FormatFlags = [System.Drawing.StringFormatFlags]::NoClip
    $format.Alignment = $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $brush = [System.Drawing.SolidBrush]::new($color)
    $g.DrawString($glyph, $font, $brush, [System.Drawing.RectangleF]::new(0, 0, $size, $size), $format)
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $brush.Dispose(); $font.Dispose(); $g.Dispose(); $bitmap.Dispose()
    , $stream.ToArray()
}

function Write-Ico([string]$path, [System.Drawing.Color]$color) {
    $images = foreach ($size in $sizes) { , (New-Png $size $color) }
    $out = [System.IO.BinaryWriter]::new([System.IO.File]::Create($path))
    $out.Write([uint16]0); $out.Write([uint16]1); $out.Write([uint16]$sizes.Count) # ICONDIR
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] } # 0 means 256
        $out.Write([byte]$dim); $out.Write([byte]$dim); $out.Write([byte]0); $out.Write([byte]0)
        $out.Write([uint16]1); $out.Write([uint16]32); $out.Write([uint32]$images[$i].Length); $out.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $out.Write($image) }
    $out.Dispose()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
Write-Ico (Join-Path $OutDir 'tray-dark.ico') ([System.Drawing.Color]::White)   # for a dark taskbar
Write-Ico (Join-Path $OutDir 'tray-light.ico') ([System.Drawing.Color]::Black)  # for a light taskbar
