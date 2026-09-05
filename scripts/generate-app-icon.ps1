param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\src\app.ico"),
    [string]$PreviewPath = (Join-Path $PSScriptRoot "..\docs\app-icon-preview.png")
)

Add-Type -AssemblyName System.Drawing

function Get-Color([string]$hex) { return [System.Drawing.ColorTranslator]::FromHtml($hex) }

# Rounded rectangle in the 256x256 design space.
function New-RoundedPath($x, $y, $w, $h, $r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $path.AddArc([float]$x, [float]$y, [float]$d, [float]$d, 180, 90)
    $path.AddArc([float]($x + $w - $d), [float]$y, [float]$d, [float]$d, 270, 90)
    $path.AddArc([float]($x + $w - $d), [float]($y + $h - $d), [float]$d, [float]$d, 0, 90)
    $path.AddArc([float]$x, [float]($y + $h - $d), [float]$d, [float]$d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Draw in a 256x256 space; the transform scales stroke widths with it.
    $g.ScaleTransform([float]($Size / 256.0), [float]($Size / 256.0))

    # Monoline note: thick rounded strokes only, no fill and no outline colour,
    # so it reads as a drawn glyph rather than a sticker on both taskbar themes.
    # The pushpin is the one solid accent, in amber, so the icon does not
    # disappear among the mostly blue/grey neighbours in the tray.
    $green = Get-Color "#0B8A5C"
    $amber = Get-Color "#FFB020"

    # The glyph fills ~92% of the canvas. A monoline mark needs the extra size
    # and stroke weight to hold its own next to the solid tray icons around it.
    $note = New-RoundedPath 26 52 204 182 34
    $frame = [System.Drawing.Pen]::new($green, 32)
    $frame.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($frame, $note)

    # Two text lines. Different lengths so the glyph reads as written-on paper.
    $line = [System.Drawing.Pen]::new($green, 28)
    $line.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($line, 82, 120, 170, 120)
    $g.DrawLine($line, 82, 174, 138, 174)

    # Pushpin head. The white ring keeps it separated from the frame stroke.
    $ring = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $head = [System.Drawing.SolidBrush]::new($amber)
    $g.FillEllipse($ring, 84, 8, 88, 88)
    $g.FillEllipse($head, 96, 20, 64, 64)

    $note.Dispose()
    $frame.Dispose()
    $line.Dispose()
    $ring.Dispose()
    $head.Dispose()
    $g.Dispose()
    return $bmp
}

function Convert-BitmapToPngBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $stream = [System.IO.MemoryStream]::new()
    $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return ,$bytes
}

function Write-IconFile {
    param(
        [string]$Path,
        [int[]]$Sizes
    )

    $images = foreach ($size in $Sizes) {
        $bitmap = New-IconBitmap -Size $size
        try {
            [pscustomobject]@{
                Size = $size
                Bytes = Convert-BitmapToPngBytes -Bitmap $bitmap
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }

    $directorySize = 6 + (16 * $images.Count)
    $offset = $directorySize
    $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    $writer = [System.IO.BinaryWriter]::new($fs)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$images.Count)

        foreach ($image in $images) {
            $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$image.Bytes.Length)
            $writer.Write([UInt32]$offset)
            $offset += $image.Bytes.Length
        }

        foreach ($image in $images) {
            $writer.Write($image.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $fs.Dispose()
    }
}

$resolvedOutput = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$resolvedPreview = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PreviewPath)

Write-IconFile -Path $resolvedOutput -Sizes @(16, 20, 24, 32, 40, 48, 64, 128, 256)

$preview = New-IconBitmap -Size 256
try {
    $preview.Save($resolvedPreview, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $preview.Dispose()
}

Write-Output "Wrote $resolvedOutput"
Write-Output "Wrote $resolvedPreview"
