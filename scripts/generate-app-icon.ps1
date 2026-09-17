# ScreenPinNotes - a desktop sticky notes app for Windows 11
# Copyright (C) 2026 umineko73
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

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
    param([int]$Size, [switch]$Dark)

    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Draw in a 256x256 space; the transform scales stroke widths with it.
    $g.ScaleTransform([float]($Size / 256.0), [float]($Size / 256.0))

    # A compact landscape memo: cyan spine, heading and three bulleted rows.
    $bodyColor = if ($Dark) { '#204B7A' } else { '#245FA8' }
    $body = [System.Drawing.SolidBrush]::new((Get-Color $bodyColor))
    $ink = [System.Drawing.SolidBrush]::new((Get-Color '#F0F6FF'))
    $accent = [System.Drawing.SolidBrush]::new((Get-Color '#65C9EB'))
    $note = New-RoundedPath 8 26 240 204 9
    $g.FillPath($body, $note)
    $note.Dispose()

    # At tray sizes use pixel-aligned strokes and size-specific row spacing.
    # Larger artwork follows the approved thin-stroke proportions.
    if ($Size -le 32) {
        $g.ResetTransform()
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Default
        $stroke = [Math]::Max(1, [Math]::Floor($Size / 24.0))
        $step = [Math]::Max(3, [Math]::Round($Size * 0.13))
        $top = [Math]::Floor(($Size - 3 * $step - $stroke) / 2)
        $left = [Math]::Round($Size * 0.14)
        $bullet = [Math]::Round($Size * 0.29)
        $text = $bullet + 2 * $stroke
        $right = $Size - [Math]::Max(2, [Math]::Round($Size * 0.12))
        $g.FillRectangle($accent, $left, $top, $stroke, 3 * $step + $stroke)
        $g.FillRectangle($ink, $bullet, $top, $right - $bullet, $stroke)
        $lengths = @(0.72, 1.0, 0.65)
        for ($row = 0; $row -lt 3; $row++) {
            $y = $top + ($row + 1) * $step
            $g.FillRectangle($ink, $bullet, $y, $stroke, $stroke)
            $g.FillRectangle($ink, $text, $y, [Math]::Max(2, [Math]::Round(($right - $text) * $lengths[$row])), $stroke)
        }
    } else {
        $spine = New-RoundedPath 34 63 12 130 6
        $g.FillPath($accent, $spine)
        $spine.Dispose()
        $line = [System.Drawing.Pen]::new((Get-Color '#F0F6FF'), 10)
        $line.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLine($line, 70, 82, 208, 82)
        $ends = @(178, 218, 174)
        for ($row = 0; $row -lt 3; $row++) {
            $y = 112 + $row * 30
            $g.FillEllipse($ink, 65, $y - 7, 14, 14)
            $g.DrawLine($line, 94, $y, $ends[$row], $y)
        }
        $line.Dispose()
    }
    $body.Dispose()
    $ink.Dispose()
    $accent.Dispose()
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
        [int[]]$Sizes, [switch]$Dark
    )

    $images = foreach ($size in $Sizes) {
        $bitmap = New-IconBitmap -Size $size -Dark:$Dark
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

$darkOutput = Join-Path ([System.IO.Path]::GetDirectoryName($resolvedOutput)) 'app-dark.ico'
Write-IconFile -Path $darkOutput -Sizes @(16, 20, 24, 32, 40, 48, 64, 128, 256) -Dark
$darkPreview = New-IconBitmap -Size 256 -Dark
try {
    $darkPreview.Save((Join-Path ([System.IO.Path]::GetDirectoryName($resolvedPreview)) 'app-icon-dark-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
} finally { $darkPreview.Dispose() }
Write-Output "Wrote $darkOutput"
