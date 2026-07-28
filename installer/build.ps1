<#
.SYNOPSIS
    JP Scratch を発行し、ユーザー単位の MSI にまとめる。

.DESCRIPTION
    既定はフレームワーク依存（約 5 MB。.NET 10 Desktop Runtime が必要）。
    -SelfContained を付けるとランタイムを同梱する（約 70 MB。前提条件なし）。

    注意: このファイルは UTF-8 (BOM 付き) で保存すること。
    BOM が無いと Windows PowerShell 5.1 が日本語を CP932 として読み、構文エラーになる。

.EXAMPLE
    powershell -File installer\build.ps1
    powershell -File installer\build.ps1 -SelfContained
#>
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [string]$Version = '0.1.0',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'jp-scratch.csproj'
$iconFile = Join-Path $root 'Assets\app.ico'
$outputDir = Join-Path $root 'publish\msi'

if ($SelfContained) {
    $flavor = 'self-contained'
    $publishDir = Join-Path $root 'publish\scd'
    $msiName = "JpScratch-$Version-selfcontained.msi"
}
else {
    $flavor = 'framework-dependent'
    $publishDir = Join-Path $root 'publish\fdd'
    $msiName = "JpScratch-$Version.msi"
}
$msiPath = Join-Path $outputDir $msiName

if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw 'WiX が見つかりません。dotnet tool install --global wix を実行してください。'
}

Write-Host "==> publish ($flavor)" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

$selfContainedArg = 'false'
if ($SelfContained) { $selfContainedArg = 'true' }

dotnet publish $project -c $Configuration -r win-x64 --self-contained $selfContainedArg -p:Version=$Version -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish に失敗しました' }

Write-Host '==> wix build' -ForegroundColor Cyan
New-Item -ItemType Directory -Force $outputDir | Out-Null

wix build (Join-Path $PSScriptRoot 'Package.wxs') -arch x64 -d Version=$Version -d PublishDir=$publishDir -d IconFile=$iconFile -o $msiPath
if ($LASTEXITCODE -ne 0) { throw 'wix build に失敗しました' }

$sizeMb = [math]::Round((Get-Item $msiPath).Length / 1MB, 1)
Write-Host ''
Write-Host "MSI: $msiPath ($sizeMb MB)" -ForegroundColor Green
Write-Host 'インストール先: %LOCALAPPDATA%\Programs\JP Scratch (ユーザー単位・管理者権限不要)'
if (-not $SelfContained) {
    Write-Host '前提: .NET 10 Desktop Runtime (x64)。未導入の環境では初回起動時に入手先が案内されます。' -ForegroundColor Yellow
}
