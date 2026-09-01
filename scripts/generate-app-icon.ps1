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

    $fitScale = 1.18
    $fitMatrix = [System.Drawing.Drawing2D.Matrix]::new()
    $fitMatrix.Translate(-(S 128), -(S 128), [System.Drawing.Drawing2D.MatrixOrder]::Append)
    $fitMatrix.Scale($fitScale, $fitScale, [System.Drawing.Drawing2D.MatrixOrder]::Append)
    $fitMatrix.Translate((S 128), (S 128), [System.Drawing.Drawing2D.MatrixOrder]::Append)
    $g.Transform = $fitMatrix

    $shadow = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $shadow.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new((S 55), (S 50)),
        [System.Drawing.PointF]::new((S 203), (S 66)),
        [System.Drawing.PointF]::new((S 189), (S 218)),
        [System.Drawing.PointF]::new((S 40), (S 203))
    ))
    $m = [System.Drawing.Drawing2D.Matrix]::new()
    $m.RotateAt(-5.0, [System.Drawing.PointF]::new((S 128), (S 132)))
    $shadow.Transform($m)
    $g.FillPath([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(44, 0, 0, 0)), $shadow)

    $note = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $note.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new((S 42), (S 42)),
        [System.Drawing.PointF]::new((S 199), (S 42)),
        [System.Drawing.PointF]::new((S 215), (S 195)),
        [System.Drawing.PointF]::new((S 167), (S 219)),
        [System.Drawing.PointF]::new((S 43), (S 203))
    ))
    $noteMatrix = [System.Drawing.Drawing2D.Matrix]::new()
    $noteMatrix.RotateAt(-5.0, [System.Drawing.PointF]::new((S 128), (S 132)))
    $note.Transform($noteMatrix)

    $bounds = [System.Drawing.RectangleF]::new((S 34), (S 34), (S 188), (S 190))
    $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $bounds,
        [System.Drawing.Color]::FromArgb(255, 170, 232, 190),
        [System.Drawing.Color]::FromArgb(255, 74, 181, 126),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($brush, $note)
    $g.DrawPath([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(190, 20, 105, 70), [Math]::Max(1.0, (S 7))), $note)

    $fold = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $fold.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new((S 166), (S 176)),
        [System.Drawing.PointF]::new((S 215), (S 195)),
        [System.Drawing.PointF]::new((S 168), (S 219))
    ))
    $fold.Transform($noteMatrix)
    $g.FillPath([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 211, 248, 224)), $fold)
    $g.DrawPath([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(90, 14, 96, 63), [Math]::Max(1.0, (S 4))), $fold)

    if ($Size -ge 24) {
        $pinShadow = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(70, 0, 0, 0), [Math]::Max(1.0, (S 10)))
        $g.DrawLine($pinShadow, (S 120), (S 61), (S 158), (S 131))
    }

    $pin = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 13, 104, 84), [Math]::Max(1.4, (S 9)))
    $pin.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pin.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pin, (S 113), (S 55), (S 151), (S 129))

    $pinHeadSize = [Math]::Max(5.0, (S 58))
    $pinHead = [System.Drawing.RectangleF]::new((S 80), (S 27), $pinHeadSize, $pinHeadSize)
    $headBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $pinHead,
        [System.Drawing.Color]::FromArgb(255, 52, 211, 153),
        [System.Drawing.Color]::FromArgb(255, 5, 120, 92),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillEllipse($headBrush, $pinHead)
    $g.DrawEllipse([System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(210, 4, 92, 72), [Math]::Max(1.0, (S 6))), $pinHead)

    if ($Size -ge 32) {
        $highlight = [System.Drawing.RectangleF]::new((S 96), (S 42), (S 15), (S 15))
        $g.FillEllipse([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(145, 255, 255, 255)), $highlight)
    }

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
