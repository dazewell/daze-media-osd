# Regenerates the icons in src/DazeMediaOsd.App/Assets from one mark: a flyout card with two level rows,
# output above input — what the OSD shows. app.ico (exe, windows) and app.png (About page) are the colour
# tile; tray-dark/light.ico are the monochrome mark, white for a dark taskbar, black for a light one.
# Geometry is on a 16-unit grid, snapped to whole pixels per size so small frames stay sharp.
param([string]$OutDir = "$PSScriptRoot/../src/DazeMediaOsd.App/Assets", [string]$PreviewDir)

Add-Type -AssemblyName System.Drawing
$sizes = 16, 20, 24, 28, 32, 40, 48, 64, 256
$top = [System.Drawing.Color]::FromArgb(255, 0x8B, 0x5C, 0xF6)    # violet
$bottom = [System.Drawing.Color]::FromArgb(255, 0x25, 0x63, 0xEB) # blue
$warm = [System.Drawing.Color]::FromArgb(255, 0xFC, 0xD3, 0x4D)   # amber: the input's level

function Snap([int]$size, [double]$units) { [Math]::Round($units * $size / 16) }

function New-RoundRect([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = [Math]::Min(2 * $r, [Math]::Min($w, $h))
    if ($d -le 0) { $path.AddRectangle([System.Drawing.RectangleF]::new($x, $y, $w, $h)); return , $path }
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    , $path
}

# A device glyph, filled, in grid units from (left, row centre): a speaker for output, a microphone for input.
function New-Glyph([string]$kind, [double]$left, [double]$cy, [double]$k) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    if ($kind -eq 'speaker') {
        $xy = 0, -0.75, 1.1, -0.75, 2.6, -1.7, 2.6, 1.7, 1.1, 0.75, 0, 0.75
        $points = for ($i = 0; $i -lt $xy.Count; $i += 2) { [System.Drawing.PointF]::new($left + $xy[$i] * $k, $cy + $xy[$i + 1] * $k) }
        $path.AddPolygon([System.Drawing.PointF[]]$points)
    } else {
        $capsule = New-RoundRect ($left + 0.6 * $k) ($cy - 1.7 * $k) (1.4 * $k) (2.3 * $k) (0.7 * $k)
        $path.AddPath($capsule, $false); $capsule.Dispose()
        $path.AddRectangle([System.Drawing.RectangleF]::new($left + 1.1 * $k, $cy + 0.6 * $k, 0.4 * $k, 1.1 * $k))
        $path.AddRectangle([System.Drawing.RectangleF]::new($left + 0.5 * $k, $cy + 1.3 * $k, 1.6 * $k, 0.4 * $k))
    }
    , $path
}

# One row: the device glyph and a level bar, its filled part solid and the rest of the track faint.
function Draw-Row($g, [int]$size, [string]$kind, [double]$cy, [double]$fill, [System.Drawing.Color]$ink,
    [System.Drawing.Color]$fillInk, [double]$left, [double]$right) {
    $k = $size / 16
    $h = [Math]::Max(2, (Snap $size 2))
    $y0 = (Snap $size $cy) - $h / 2
    $barX = [Math]::Round($left + 4 * $k)
    $barW = $right - $barX
    $fillW = [Math]::Max($h, [Math]::Round($barW * $fill))
    $solid = [System.Drawing.SolidBrush]::new($ink)
    $fillBrush = [System.Drawing.SolidBrush]::new($fillInk)
    $faint = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(90, $ink))
    $glyph = New-Glyph $kind $left ($y0 + $h / 2) $k
    $g.FillPath($solid, $glyph)
    $track = New-RoundRect $barX $y0 $barW $h ($h / 2)
    $g.FillPath($faint, $track)
    $bar = New-RoundRect $barX $y0 $fillW $h ($h / 2)
    $g.FillPath($fillBrush, $bar)
    $glyph.Dispose(); $track.Dispose(); $bar.Dispose(); $solid.Dispose(); $fillBrush.Dispose(); $faint.Dispose()
}

function New-Bitmap([int]$size, [bool]$tile, [System.Drawing.Color]$ink) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $cardY = Snap $size 2; $cardH = (Snap $size 14) - $cardY
    $radius = [Math]::Max(2, $size * 3 / 16)
    if ($tile) {
        $card = New-RoundRect 0 $cardY $size $cardH $radius
        $rect = [System.Drawing.RectangleF]::new(0, $cardY, $size, $cardH)
        $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new($rect, $top, $bottom, 60)
        $g.FillPath($brush, $card)
        $brush.Dispose(); $card.Dispose()
        $rowInk = [System.Drawing.Color]::White
        $left = Snap $size 2; $right = $size - $left
    } else {
        $stroke = [Math]::Max(1, (Snap $size 1))
        $inset = $stroke / 2
        $card = New-RoundRect $inset ($cardY + $inset) ($size - $stroke) ($cardH - $stroke) $radius
        $pen = [System.Drawing.Pen]::new($ink, $stroke)
        $g.DrawPath($pen, $card)
        $pen.Dispose(); $card.Dispose()
        $rowInk = $ink
        $left = Snap $size 2.5; $right = $size - $left
    }
    Draw-Row $g $size 'speaker' 5.75 0.78 $rowInk $rowInk $left $right
    Draw-Row $g $size 'mic' 10.25 0.45 $rowInk $(if ($tile) { $warm } else { $rowInk }) $left $right
    $g.Dispose()
    $bitmap
}

function New-Png([int]$size, [bool]$tile, [System.Drawing.Color]$ink) {
    $bitmap = New-Bitmap $size $tile $ink
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}

function Write-Ico([string]$path, [bool]$tile, [System.Drawing.Color]$ink) {
    $images = foreach ($size in $sizes) { , (New-Png $size $tile $ink) }
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

$white = [System.Drawing.Color]::White
$black = [System.Drawing.Color]::Black
New-Item -ItemType Directory -Force $OutDir | Out-Null
Write-Ico (Join-Path $OutDir 'app.ico') $true $white
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'app.png'), (New-Png 256 $true $white))
Write-Ico (Join-Path $OutDir 'tray-dark.ico') $false $white  # for a dark taskbar
Write-Ico (Join-Path $OutDir 'tray-light.ico') $false $black # for a light taskbar

# -PreviewDir: every frame of every icon at 4x (256 at 1x), nearest-neighbour, on dark and light grounds.
if ($PreviewDir) {
    $scale = 4
    $drawn = foreach ($size in $sizes) { if ($size -eq 256) { 256 } else { $size * $scale } }
    [int]$width = ($drawn | Measure-Object -Sum).Sum + 16 * ($sizes.Count + 1)
    [int]$rowH = 256 + 16
    $rows = @(
        @{ Ground = [System.Drawing.Color]::FromArgb(32, 32, 32); Tile = $true; Ink = $white },
        @{ Ground = [System.Drawing.Color]::FromArgb(32, 32, 32); Tile = $false; Ink = $white },
        @{ Ground = [System.Drawing.Color]::FromArgb(243, 243, 243); Tile = $false; Ink = $black },
        @{ Ground = [System.Drawing.Color]::FromArgb(243, 243, 243); Tile = $true; Ink = $white })
    $sheet = [System.Drawing.Bitmap]::new($width, [int]($rows.Count * $rowH))
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    for ($r = 0; $r -lt $rows.Count; $r++) {
        $ground = [System.Drawing.SolidBrush]::new($rows[$r].Ground)
        $g.FillRectangle($ground, 0, $r * $rowH, $width, $rowH)
        $ground.Dispose()
        $x = 16
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $bitmap = New-Bitmap $sizes[$i] $rows[$r].Tile $rows[$r].Ink
            $g.DrawImage($bitmap, [System.Drawing.Rectangle]::new($x, $r * $rowH + 8, $drawn[$i], $drawn[$i]))
            $bitmap.Dispose()
            $x += $drawn[$i] + 16
        }
    }
    $g.Dispose()
    New-Item -ItemType Directory -Force $PreviewDir | Out-Null
    $sheet.Save((Join-Path $PreviewDir 'icons.png'), [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
}
