$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assetDir = Join-Path $root "assets"
$iconPath = Join-Path $assetDir "app.ico"

New-Item -ItemType Directory -Force -Path $assetDir | Out-Null
Add-Type -AssemblyName System.Drawing

function New-RoundedRectPath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $scale = $Size / 256.0
    $pad = 18 * $scale
    $radius = 42 * $scale
    $shadowPath = New-RoundedRectPath ($pad + 4 * $scale) ($pad + 8 * $scale) ($Size - 2 * $pad) ($Size - 2 * $pad) $radius
    $shadowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(55, 0, 0, 0))
    $g.FillPath($shadowBrush, $shadowPath)
    $shadowBrush.Dispose()
    $shadowPath.Dispose()

    $bgPath = New-RoundedRectPath $pad $pad ($Size - 2 * $pad) ($Size - 2 * $pad) $radius
    $bgRect = New-Object System.Drawing.RectangleF $pad, $pad, ($Size - 2 * $pad), ($Size - 2 * $pad)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $bgRect, ([System.Drawing.Color]::FromArgb(255, 24, 142, 160)), ([System.Drawing.Color]::FromArgb(255, 31, 70, 146)), 45
    $g.FillPath($bgBrush, $bgPath)
    $bgBrush.Dispose()

    $shineRect = New-Object System.Drawing.RectangleF (44 * $scale), (34 * $scale), (168 * $scale), (84 * $scale)
    $shineBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $shineRect, ([System.Drawing.Color]::FromArgb(90, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90
    $g.FillEllipse($shineBrush, $shineRect)
    $shineBrush.Dispose()

    $keyboardPath = New-RoundedRectPath (48 * $scale) (112 * $scale) (160 * $scale) (88 * $scale) (18 * $scale)
    $keyboardBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 248, 252, 255))
    $g.FillPath($keyboardBrush, $keyboardPath)
    $keyboardBrush.Dispose()

    $keyboardPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 32, 62, 100)), (5 * $scale)
    $g.DrawPath($keyboardPen, $keyboardPath)
    $keyboardPen.Dispose()
    $keyboardPath.Dispose()

    $keyBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 31, 70, 146))
    $keyBrushSoft = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 142, 160))
    for ($row = 0; $row -lt 3; $row++) {
        for ($col = 0; $col -lt 5; $col++) {
            $x = (62 + $col * 25) * $scale
            $y = (128 + $row * 18) * $scale
            $w = 15 * $scale
            $h = 8 * $scale
            $brush = if (($row + $col) % 2 -eq 0) { $keyBrush } else { $keyBrushSoft }
            $keyPath = New-RoundedRectPath $x $y $w $h (3 * $scale)
            $g.FillPath($brush, $keyPath)
            $keyPath.Dispose()
        }
    }

    $spacePath = New-RoundedRectPath (86 * $scale) (178 * $scale) (84 * $scale) (10 * $scale) (4 * $scale)
    $g.FillPath($keyBrush, $spacePath)
    $spacePath.Dispose()

    $caretPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 224, 99)), (13 * $scale)
    $caretPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $caretPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($caretPen, (174 * $scale), (56 * $scale), (174 * $scale), (104 * $scale))
    $caretPen.Dispose()

    $tFont = New-Object System.Drawing.Font "Segoe UI", (58 * $scale), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $textBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.DrawString("T", $tFont, $textBrush, (73 * $scale), (48 * $scale))
    $textBrush.Dispose()
    $tFont.Dispose()

    $keyBrush.Dispose()
    $keyBrushSoft.Dispose()
    $bgPath.Dispose()
    $g.Dispose()
    return $bmp
}

$sizes = @(256, 128, 64, 48, 32, 16)
$images = New-Object System.Collections.Generic.List[byte[]]

foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images.Add($stream.ToArray())
    $stream.Dispose()
    $bmp.Dispose()
}

$file = New-Object System.IO.FileStream $iconPath, ([System.IO.FileMode]::Create), ([System.IO.FileAccess]::Write)
$writer = New-Object System.IO.BinaryWriter $file

$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $bytes = $images[$i]
    if ($size -ge 256) {
        $widthByte = [byte]0
        $heightByte = [byte]0
    } else {
        $widthByte = [byte]$size
        $heightByte = [byte]$size
    }

    $writer.Write($widthByte)
    $writer.Write($heightByte)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $bytes.Length
}

for ($i = 0; $i -lt $images.Count; $i++) {
    $writer.Write($images[$i])
}

$writer.Dispose()
$file.Dispose()

Write-Host "Generated $iconPath"
