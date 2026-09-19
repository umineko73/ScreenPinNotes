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

# 付箋に画鋲を1本刺した絵。過去に試した黄色い付箋は「新着メールの通知」と
# 見間違えるという声があり、そのときの絵は右上に三角の折り返しを付けていた。
# 黄色い長方形の上辺に三角 = 開いた封筒そのものなので、ここでは
#   * 折り返しは右下だけ（上辺に三角を作らない）
#   * 画鋲は上辺の中央（右上に赤い丸を置くと未読バッジに見える）
#   * 地色はフォルダーの黄色から離した琥珀
# を守る。輪郭で見分ける16pxのために、画鋲は上辺からはみ出させる。

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

    # ダークテーマ用はわずかに沈ませる。明るいテーマ用は白い背景でも輪郭が立つ濃さ。
    $body  = [System.Drawing.SolidBrush]::new((Get-Color $(if ($Dark) { '#DE8413' } else { '#F0911C' })))
    $fold  = [System.Drawing.SolidBrush]::new((Get-Color $(if ($Dark) { '#AE6409' } else { '#C66F0E' })))
    $ink   = [System.Drawing.SolidBrush]::new((Get-Color '#FFFFFF'))
    $pin   = [System.Drawing.SolidBrush]::new((Get-Color '#DE3E36'))
    $ring  = [System.Drawing.SolidBrush]::new((Get-Color '#A3241F'))
    $shine = [System.Drawing.SolidBrush]::new((Get-Color '#FF9690'))

    if ($Size -le 32) {
        # トレイの大きさでは、紙と行はドットの境目に合わせて置く。
        # 縮小任せにすると1pxの線が灰色ににじんで、ただの塊になる。
        $g.ResetTransform()
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Default

        # トレイでは枠いっぱいまで使う。ここで余白を取ると、周りのアイコンより
        # ひと回り小さく見える（トレイが使うのは 16/20/24/32 の4枚だけ）。
        $margin = [Math]::Floor($Size / 24.0)
        $left = $margin
        $right = $Size - $margin
        $top = [Math]::Round($Size * 0.20)
        $bottom = $Size - $margin
        $cut = [Math]::Max(2, [Math]::Round($Size * 0.26))

        $g.FillRectangle($body, $left, $top, $right - $left, $bottom - $top)

        # 右下を三角に削ってから、折り返しの色を置く。
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $clear = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::Transparent)
        $g.FillPolygon($clear, @(
            [System.Drawing.Point]::new($right - $cut, $bottom),
            [System.Drawing.Point]::new($right, $bottom - $cut),
            [System.Drawing.Point]::new($right, $bottom)))
        $clear.Dispose()
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $g.FillPolygon($fold, @(
            [System.Drawing.Point]::new($right - $cut, $bottom),
            [System.Drawing.Point]::new($right, $bottom - $cut),
            [System.Drawing.Point]::new($right - [Math]::Round($cut * 0.34), $bottom - [Math]::Round($cut * 0.34))))

        # 白い2行。3行だとこの大きさでは団子になる。
        $stroke = [Math]::Max(1, [Math]::Round($Size / 9.0))
        $gap = [Math]::Max(2, [Math]::Round($Size * 0.13))
        $inset = [Math]::Max(1, [Math]::Round($Size * 0.11))
        $lineLeft = $left + $inset
        $lineRight = $right - $inset
        $blockTop = $top + [Math]::Round((($bottom - $top) - (2 * $stroke + $gap)) / 2) - [Math]::Round($Size * 0.02)
        $g.FillRectangle($ink, $lineLeft, $blockTop, $lineRight - $lineLeft, $stroke)
        $g.FillRectangle($ink, $lineLeft, $blockTop + $stroke + $gap,
            [Math]::Max(2, [Math]::Round(($lineRight - $lineLeft) * 0.72)), $stroke)

        # 画鋲。丸なのでここだけ滑らかに描く。小さいうちは輪郭も光も省き、
        # 赤い円として残す方を選ぶ（この赤が16pxでの見分けを担っている）。
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $pinSize = [Math]::Max(5, [Math]::Round($Size * 0.36))
        $pinLeft = [Math]::Round(($Size - $pinSize) / 2.0)
        $pinTop = 0
        if ($Size -ge 24) {
            $g.FillEllipse($ring, $pinLeft, $pinTop, $pinSize, $pinSize)
            $inner = [Math]::Round($pinSize * 0.72)
            $g.FillEllipse($pin, $pinLeft + [Math]::Round(($pinSize - $inner) / 2.0),
                $pinTop + [Math]::Round(($pinSize - $inner) / 2.0), $inner, $inner)
        }
        else {
            $g.FillEllipse($pin, $pinLeft, $pinTop, $pinSize, $pinSize)
        }
    }
    else {
        # 256x256 の下絵として描き、変換で縮める（線の太さも一緒に縮む）。
        $g.ScaleTransform([float]($Size / 256.0), [float]($Size / 256.0))

        $x0 = 28; $y0 = 59; $x1 = 228; $y1 = 238
        $cut = 67
        $paper = New-RoundedPath $x0 $y0 ($x1 - $x0) ($y1 - $y0) 15
        $g.FillPath($body, $paper)
        $paper.Dispose()

        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $clear = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::Transparent)
        $g.FillPolygon($clear, @(
            [System.Drawing.Point]::new($x1 - $cut, $y1),
            [System.Drawing.Point]::new($x1, $y1 - $cut),
            [System.Drawing.Point]::new($x1, $y1)))
        $clear.Dispose()
        $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $g.FillPolygon($fold, @(
            [System.Drawing.Point]::new($x1 - $cut, $y1),
            [System.Drawing.Point]::new($x1, $y1 - $cut),
            [System.Drawing.Point]::new($x1 - 20, $y1 - 20)))

        $stroke = 17
        $lineLeft = 56; $lineRight = 200
        $blockTop = 125
        $g.FillRectangle($ink, $lineLeft, $blockTop, $lineRight - $lineLeft, $stroke)
        $g.FillRectangle($ink, $lineLeft, $blockTop + $stroke + 22,
            [Math]::Round(($lineRight - $lineLeft) * 0.78), $stroke)

        # 画鋲は上辺をまたいで置く。輪郭が四角のままだと、小さいときに
        # フォルダーとも封筒とも紛れる。
        $pinRadius = 33
        $pinCx = 128; $pinCy = 49
        $g.FillEllipse($ring, $pinCx - $pinRadius, $pinCy - $pinRadius, $pinRadius * 2, $pinRadius * 2)
        $innerRadius = [Math]::Round($pinRadius * 0.72)
        $g.FillEllipse($pin, $pinCx - $innerRadius, $pinCy - $innerRadius, $innerRadius * 2, $innerRadius * 2)
        $g.FillEllipse($shine, $pinCx - [Math]::Round($pinRadius * 0.34), $pinCy - [Math]::Round($pinRadius * 0.44),
            [Math]::Round($pinRadius * 0.36), [Math]::Round($pinRadius * 0.36))
    }

    $body.Dispose()
    $fold.Dispose()
    $ink.Dispose()
    $pin.Dispose()
    $ring.Dispose()
    $shine.Dispose()
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
