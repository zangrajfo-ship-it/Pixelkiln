<#
.SYNOPSIS
  Draws the app icon (the logo: accent square with four pixels) and writes src/Pixelkiln.App/app.ico
  with 16, 24, 32, 48, 64 and 256 px images. Run again only when the logo changes.
#>
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\Pixelkiln.App\app.ico'
$sizes = 16, 24, 32, 48, 64, 256

function New-LogoPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $u = $size / 32.0   # the logo is designed in a 32-unit box
    # rounded accent square
    $r = 8 * $u; $d = 2 * $r
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90); $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x6A, 0xA1, 0xFF))), $path)
    # four pixels: two solid, two at 55 %
    $g.SmoothingMode = 'None'
    foreach ($p in @(@(9, 9, 255), @(17, 9, 140), @(9, 17, 140), @(17, 17, 255))) {
        $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb($p[2], 0x0F, 0x11, 0x15))
        $g.FillRectangle($brush, [float]($p[0] * $u), [float]($p[1] * $u), [float](6 * $u), [float](6 * $u))
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

# ICO container with PNG images: header, one 16-byte entry per image, then the PNG data
$pngs = $sizes | ForEach-Object { , (New-LogoPng $_) }
$file = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $file
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256))   # 256 is written as 0
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
[System.IO.File]::WriteAllBytes($out, $file.ToArray())
"wrote $out ($($file.Length) bytes)"
