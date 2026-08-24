<#
.SYNOPSIS
    リリース用の実行ファイルを2種類ビルドします。

.DESCRIPTION
    artifacts/ に以下を出力します。

      ScreenStickyNotes-<version>-win-x64.exe            自己完結型（約68MB）
      ScreenStickyNotes-<version>-win-x64-runtime.exe    ランタイム必須（約220KB）

    どちらも単一ファイルです。自己完結型は .NET のインストールが不要で、
    Windows は既定で .NET 8 を同梱していないため、こちらが既定の配布物です。

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

    $final = Join-Path $outDir ("ScreenStickyNotes-{0}-{1}{2}.exe" -f $Version, $Runtime, $Suffix)
    Move-Item $exe $final -Force

    # 単一ファイルにならなかった場合は取りこぼしを知らせる
    $leftovers = Get-ChildItem $stage -Recurse -File
    if ($leftovers.Count -gt 0) {
        Write-Host ("  WARNING: {0} extra file(s) left beside the exe:" -f $leftovers.Count) -ForegroundColor Yellow
        $leftovers | ForEach-Object { Write-Host ("    " + $_.Name) -ForegroundColor Yellow }
    }
    Remove-Item $stage -Recurse -Force

    $mb = (Get-Item $final).Length / 1MB
    Write-Host ("  -> {0}  ({1:N1} MB)" -f (Split-Path $final -Leaf), $mb) -ForegroundColor Green
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
    "  {0,-52} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB)
}
