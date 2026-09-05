param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\src\app.ico"),
    [string]$PreviewPath = (Join-Path $PSScriptRoot "..\docs\app-icon-preview.png")
)

Add-Type -AssemblyName System.Drawing

function New-IconBitmap {
    param([int]$Size)

    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $scale = $Size / 256.0

    function S([float]$v) { return [float]($v * $scale) }

    # D: graphite note with a mint pin. Flat geometry remains clear at 16px.
    $note = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $note.AddArc((S 24), (S 58), (S 24), (S 24), 180, 90)
    $note.AddLine((S 36), (S 58), (S 220), (S 58))
    $note.AddArc((S 208), (S 58), (S 24), (S 24), 270, 90)
    $note.AddLine((S 232), (S 70), (S 232), (S 170))
    $note.AddArc((S 170), (S 170), (S 62), (S 62), 0, 90)
    $note.AddLine((S 201), (S 232), (S 24), (S 232))
    $note.CloseFigure()
    $bodyBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#373B40"))
    $outline = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml("#A9B2B7"), (S 3))
    $stem = [System.Drawing.Pen]::new([System.Drawing.Color]::White, (S 10))
    $stem.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $mint = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml("#39CCA0"))
    $rim = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $g.FillPath($bodyBrush, $note)
    $g.DrawPath($outline, $note)
    $g.DrawLine($stem, (S 128), (S 46), (S 128), (S 100))
    $g.FillEllipse($rim, (S 99), (S 15), (S 58), (S 58))
    $g.FillEllipse($mint, (S 103), (S 19), (S 50), (S 50))
    $note.Dispose()
    $bodyBrush.Dispose()
    $outline.Dispose()
    $stem.Dispose()
    $mint.Dispose()
    $rim.Dispose()
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
