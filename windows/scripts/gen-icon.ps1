# Generates windows/src/FluidVoice.App/Assets/fluidvoice.ico (multi-size, PNG-compressed entries).
# Design: rounded square with cyan->blue gradient + white voice-waveform bars.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot "..\src\FluidVoice.App\Assets"
New-Item -ItemType Directory -Force $outDir | Out-Null

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = "AntiAlias"
    $g.Clear([System.Drawing.Color]::Transparent)

    $g.InterpolationMode = "HighQualityBicubic"
    $g.PixelOffsetMode = "HighQuality"
    $inset = [double]($size * 0.06)  # small breathing room so the squircle isn't edge-to-edge
    $sz = $size - 2 * $inset

    # squircle background (large-radius rounded rect) with a diagonal teal gradient
    $r = [double]($sz * 0.30)
    $rect = New-Object System.Drawing.RectangleF($inset, $inset, $sz, $sz)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($inset, $inset, $d, $d, 180, 90)
    $path.AddArc($inset + $sz - $d, $inset, $d, $d, 270, 90)
    $path.AddArc($inset + $sz - $d, $inset + $sz - $d, $d, $d, 0, 90)
    $path.AddArc($inset, $inset + $sz - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $c1 = [System.Drawing.Color]::FromArgb(255, 74, 214, 196)   # bright teal (top-left)
    $c2 = [System.Drawing.Color]::FromArgb(255, 22, 120, 122)   # deep teal (bottom-right)
    $grect = New-Object System.Drawing.RectangleF(($inset - 1), ($inset - 1), ($sz + 2), ($sz + 2))
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($grect, $c1, $c2, 55.0)
    $g.FillPath($brush, $path)

    # soft top highlight for a subtle glassy sheen
    $g.SetClip($path)
    $hlRect = New-Object System.Drawing.RectangleF($inset, $inset, $sz, ($sz * 0.55))
    $hl1 = [System.Drawing.Color]::FromArgb(46, 255, 255, 255)
    $hl2 = [System.Drawing.Color]::FromArgb(0, 255, 255, 255)
    $hlBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($hlRect, $hl1, $hl2, 90.0)
    $g.FillRectangle($hlBrush, $hlRect)
    $g.ResetClip()

    # centered waveform — 7 bars with a smooth bell envelope, rounded caps
    $env = @(0.30, 0.52, 0.74, 0.92, 0.74, 0.52, 0.30)
    $barW = [double]($sz * 0.072)
    $gap = [double]($sz * 0.058)
    $maxBar = [double]($sz * 0.62)
    $totalW = $env.Count * $barW + ($env.Count - 1) * $gap
    $x = ($size - $totalW) / 2.0
    $cy = $size / 2.0
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(245, 255, 255, 255))
    foreach ($e in $env) {
        $bh = [Math]::Max($barW, $maxBar * $e)
        $y = $cy - $bh / 2.0
        $barRect = New-Object System.Drawing.RectangleF($x, $y, $barW, $bh)
        $bp = New-Object System.Drawing.Drawing2D.GraphicsPath
        $br = $barW / 2.0
        $bp.AddArc($barRect.X, $barRect.Y, $br * 2, $br * 2, 180, 180)
        $bp.AddArc($barRect.X, $barRect.Bottom - $br * 2, $br * 2, $br * 2, 0, 180)
        $bp.CloseFigure()
        $g.FillPath($white, $bp)
        $x += $barW + $gap
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ,([byte[]]$ms.ToArray())  # comma prevents PowerShell pipeline unrolling
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @{}
foreach ($s in $sizes) { $pngs[$s] = [byte[]](New-IconPng $s); Write-Host "  $s px -> $($pngs[$s].Length) bytes" }

# assemble ICO container (PNG entries)
$icoPath = Join-Path $outDir "fluidvoice.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    [byte[]]$data = $pngs[$s]
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # width (0 = 256)
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))  # height
    $bw.Write([Byte]0); $bw.Write([Byte]0)                   # palette, reserved
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)              # planes, bpp
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { [byte[]]$blob = $pngs[$s]; $bw.Write($blob, 0, $blob.Length) }
$bw.Close()

# also export a 256 png for docs
[System.IO.File]::WriteAllBytes((Join-Path $outDir "fluidvoice-256.png"), $pngs[256])
Write-Host "wrote $icoPath ($([math]::Round((Get-Item $icoPath).Length/1KB,1)) KB)"
