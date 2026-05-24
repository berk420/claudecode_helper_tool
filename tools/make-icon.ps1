Add-Type -AssemblyName System.Drawing

function New-Layer([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

    # Rounded dark background
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = [int]([Math]::Round($size * 0.18))
    $rect = New-Object System.Drawing.Rectangle 0,0,$size,$size
    $bg.AddArc($rect.X, $rect.Y, $r*2, $r*2, 180, 90)
    $bg.AddArc($rect.Right-$r*2, $rect.Y, $r*2, $r*2, 270, 90)
    $bg.AddArc($rect.Right-$r*2, $rect.Bottom-$r*2, $r*2, $r*2, 0, 90)
    $bg.AddArc($rect.X, $rect.Bottom-$r*2, $r*2, $r*2, 90, 90)
    $bg.CloseFigure()

    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point 0,0),
        (New-Object System.Drawing.Point 0,$size),
        [System.Drawing.Color]::FromArgb(255, 30, 30, 32),
        [System.Drawing.Color]::FromArgb(255, 18, 18, 20))
    $g.FillPath($gradient, $bg)

    # Subtle border
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(120, 79, 195, 247), [float]([Math]::Max(1, $size/96)))
    $g.DrawPath($pen, $bg)

    # 4 buttons diamond (Xbox A/B/X/Y)
    $cx = $size/2.0
    $cy = $size/2.0 + $size*0.02
    $br = $size*0.13       # button radius
    $offset = $size*0.22   # diamond offset from center

    $buttons = @(
        @{ X = $cx;          Y = $cy - $offset; Color = [System.Drawing.Color]::FromArgb(255, 255, 209, 67);  Label = "Y" } # top yellow
        @{ X = $cx + $offset; Y = $cy;          Color = [System.Drawing.Color]::FromArgb(255, 233, 71, 70);   Label = "B" } # right red
        @{ X = $cx;          Y = $cy + $offset; Color = [System.Drawing.Color]::FromArgb(255, 87, 195, 105);  Label = "A" } # bottom green
        @{ X = $cx - $offset; Y = $cy;          Color = [System.Drawing.Color]::FromArgb(255, 65, 145, 233);  Label = "X" } # left blue
    )

    $font = New-Object System.Drawing.Font('Segoe UI Black', [float]($size*0.10), [System.Drawing.FontStyle]::Bold)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center

    foreach ($b in $buttons) {
        $brush = New-Object System.Drawing.SolidBrush($b.Color)
        $shadow = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(110, 0, 0, 0))
        $g.FillEllipse($shadow, [float]($b.X - $br + $size*0.012), [float]($b.Y - $br + $size*0.016), [float]($br*2), [float]($br*2))
        $g.FillEllipse($brush, [float]($b.X - $br), [float]($b.Y - $br), [float]($br*2), [float]($br*2))

        # glossy highlight
        $highlight = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF([float]($b.X-$br),[float]($b.Y-$br))),
            (New-Object System.Drawing.PointF([float]($b.X-$br),[float]($b.Y))),
            [System.Drawing.Color]::FromArgb(80, 255, 255, 255),
            [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
        $g.FillEllipse($highlight, [float]($b.X - $br*0.85), [float]($b.Y - $br*0.92), [float]($br*1.7), [float]($br*1.2))

        $textRect = New-Object System.Drawing.RectangleF([float]($b.X - $br), [float]($b.Y - $br), [float]($br*2), [float]($br*2))
        $g.DrawString($b.Label, $font, [System.Drawing.Brushes]::White, $textRect, $sf)

        $brush.Dispose(); $shadow.Dispose(); $highlight.Dispose()
    }

    $g.Dispose()
    return $bmp
}

function Convert-PngToIcoBytes([byte[][]] $pngs, [int[]] $sizes) {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $count = $pngs.Length

    # ICONDIR
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$count)

    $offset = 6 + 16 * $count
    for ($i = 0; $i -lt $count; $i++) {
        $sz = $sizes[$i]
        $w = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
        $h = $w
        $bw.Write($w)
        $bw.Write($h)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$pngs[$i].Length)
        $bw.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    for ($i = 0; $i -lt $count; $i++) {
        $bw.Write($pngs[$i])
    }
    $bw.Flush()
    return $ms.ToArray()
}

$sizes = @(16, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-Layer -size $s
    $tmp = New-Object System.IO.MemoryStream
    $bmp.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,$tmp.ToArray()
    $bmp.Dispose()
    $tmp.Dispose()
}

$icoBytes = Convert-PngToIcoBytes $pngs $sizes
$outPath = Join-Path $PSScriptRoot '..\src\CCXboxController\app.ico'
$outPath = [System.IO.Path]::GetFullPath($outPath)
[System.IO.File]::WriteAllBytes($outPath, $icoBytes)
Write-Host "Icon written: $outPath ($($icoBytes.Length) bytes)"
