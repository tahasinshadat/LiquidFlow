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

    # rounded-rect background with vertical gradient
    $r = [int]($size * 0.22)
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $c1 = [System.Drawing.Color]::FromArgb(255, 58, 208, 206)   # cyan #3AD0CE
    $c2 = [System.Drawing.Color]::FromArgb(255, 37, 99, 235)    # blue #2563EB
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 90.0)
    $g.FillPath($brush, $path)

    # white waveform bars (heights as fraction of size)
    $heights = @(0.28, 0.46, 0.62, 0.46, 0.28)
    $barW = [Math]::Max(1.0, $size * 0.085)
    $gap = $size * 0.075
    $totalW = $heights.Count * $barW + ($heights.Count - 1) * $gap
    $x = ($size - $totalW) / 2.0
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    foreach ($h in $heights) {
        $bh = $size * $h
        $y = ($size - $bh) / 2.0
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
