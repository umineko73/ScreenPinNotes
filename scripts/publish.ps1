<#
.SYNOPSIS
    リリース用の実行ファイルを2種類ビルドし、zip にまとめます。

.DESCRIPTION
    artifacts/ に以下を出力します。

      ScreenStickyNotes-<version>-win-x64.zip            自己完結型（約68MB）
      ScreenStickyNotes-<version>-win-x64-runtime.zip    ランタイム必須（約220KB）

    それぞれの zip の中身は ScreenStickyNotes.exe と、初回起動時に
    サンプル付箋としてコピーされる SampleNotes\ フォルダです
    （SampleNoteFactory.cs 参照）。展開してそのまま使えるように、
    あらかじめ同じフォルダにまとめてあります。

    exe 自体のファイル名にはバージョンを含めません（zip 名にのみ含めます）。
    自己完結型は .NET のインストールが不要で、Windows は既定で .NET 8 を
    同梱していないため、こちらが既定の配布物です。

.EXAMPLE
    pwsh scripts/publish.ps1
    pwsh scripts/publish.ps1 -Version 1.1.0
#>
param(
    [string]$Version = "",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root    = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "src"
$outDir  = Join-Path $root "artifacts"

# バージョン指定がなければ csproj の <Version> を使う
if (-not $Version) {
    $csproj = Get-Content (Join-Path $project "ScreenStickyNotes.csproj") -Raw
    if ($csproj -match '<Version>([^<]+)</Version>') { $Version = $Matches[1] }
    else { $Version = "0.0.0" }
}
Write-Host ("version: {0}   runtime: {1}" -f $Version, $Runtime) -ForegroundColor Cyan

if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Publish-Variant {
    param([string]$Name, [string]$Suffix, [string[]]$ExtraArgs)

    $stage = Join-Path $outDir "_$Name"
    Write-Host ""
    Write-Host ("building {0} ..." -f $Name) -ForegroundColor Cyan

    $args = @(
        "publish", $project,
        "-c", "Release",
        "-r", $Runtime,
        "-p:PublishSingleFile=true",
        "-p:DebugType=none",
        "-o", $stage
    ) + $ExtraArgs

    & dotnet @args | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "publish failed: $Name" }

    $exe = Join-Path $stage "ScreenStickyNotes.exe"
    if (-not (Test-Path $exe)) { throw "exe not produced: $Name" }

    # SampleNotes\ は単一ファイル化の対象外として意図的に exe の隣に残るファイル
    # （csproj の CopyToOutputDirectory）。zip に含めて配布する。
    $sampleNotesSrc = Join-Path $stage "SampleNotes"
    $hadSampleNotes = Test-Path $sampleNotesSrc

    # 単一ファイルにならなかった場合（SampleNotes 以外の取りこぼし）を知らせる
    $leftovers = Get-ChildItem $stage -File | Where-Object { $_.Name -ne "ScreenStickyNotes.exe" }
    if ($leftovers.Count -gt 0) {
        Write-Host ("  WARNING: {0} extra file(s) left beside the exe:" -f $leftovers.Count) -ForegroundColor Yellow
        $leftovers | ForEach-Object { Write-Host ("    " + $_.Name) -ForegroundColor Yellow }
    }

    $zipName = "ScreenStickyNotes-{0}-{1}{2}.zip" -f $Version, $Runtime, $Suffix
    $zipPath = Join-Path $outDir $zipName
    $zipItems = @($exe)
    if ($hadSampleNotes) { $zipItems += $sampleNotesSrc }
    Compress-Archive -Path $zipItems -DestinationPath $zipPath -Force
    Remove-Item $stage -Recurse -Force

    $mb = (Get-Item $zipPath).Length / 1MB
    Write-Host ("  -> {0}  ({1:N1} MB)" -f $zipName, $mb) -ForegroundColor Green
}

# 自己完結型: .NET のインストール不要。ネイティブライブラリも exe に埋め込む
Publish-Variant -Name "self-contained" -Suffix "" -ExtraArgs @(
    "--self-contained", "true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true"
)

# ランタイム必須: .NET 8 Desktop Runtime が入っている環境向け
Publish-Variant -Name "framework-dependent" -Suffix "-runtime" -ExtraArgs @(
    "--self-contained", "false"
)

Write-Host ""
Write-Host "artifacts:" -ForegroundColor Cyan
Get-ChildItem $outDir -File | ForEach-Object {
    "  {0,-40} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)
}
