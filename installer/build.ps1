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
    powershell -File installer\build.ps1 -SelfContained -Sign -CertificateThumbprint '<thumbprint>'
#>
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [string]$Version,
    [string]$Configuration = 'Release',
    [switch]$Sign,
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'jp-scratch.csproj'

# バージョンは jp-scratch.csproj の <Version> を正とする。
# ここに既定値を直書きすると、csproj を上げても MSI は古いバージョンのまま出荷され、
# WiX の MajorUpgrade が「同じバージョン」と判断して上書きインストールできなくなる。
# -Version で明示的に上書きしたときだけ csproj より優先する。
if ([string]::IsNullOrWhiteSpace($Version)) {
    $csprojXml = [xml](Get-Content $project -Raw)
    $Version = ($csprojXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw "jp-scratch.csproj から <Version> を読み取れませんでした。-Version で明示してください。"
    }
    $Version = $Version.Trim()
    Write-Host "==> version $Version (jp-scratch.csproj の <Version> から)" -ForegroundColor Cyan
}
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

$signTool = $null
if ($Sign) {
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw '-Sign を指定する場合は -CertificateThumbprint を指定してください。'
    }

    $CertificateThumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
    $certificate = Get-ChildItem "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $certificate) {
        throw "現在のユーザーの証明書ストアに署名証明書が見つかりません: $CertificateThumbprint"
    }
    if (-not $certificate.HasPrivateKey) {
        throw "署名証明書に秘密鍵がありません: $CertificateThumbprint"
    }

    $signToolCommand = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -eq $signToolCommand) {
        throw 'signtool.exe が見つかりません。Windows SDK の署名ツールをPATHへ追加してください。'
    }
    $signTool = $signToolCommand.Source
}

if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw 'WiX が見つかりません。dotnet tool install --global wix を実行してください。'
}

Write-Host "==> publish ($flavor)" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

$selfContainedArg = 'false'
if ($SelfContained) { $selfContainedArg = 'true' }

dotnet publish $project -c $Configuration -r win-x64 --self-contained $selfContainedArg -p:Version=$Version -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish に失敗しました' }

# バイナリと同じ配布物に、JP Scratch 本体および同梱ライブラリの
# ライセンス・著作権表示を必ず含める。Package.wxs は publishDir 全体を
# MSI に取り込むため、ここへ置けば ZIP 相当の発行物と MSI の双方に入る。
$licenseFile = Join-Path $root 'LICENSE'
$thirdPartyNoticesFile = Join-Path $root 'THIRD-PARTY-NOTICES.md'
foreach ($requiredLegalFile in @($licenseFile, $thirdPartyNoticesFile)) {
    if (-not (Test-Path -LiteralPath $requiredLegalFile -PathType Leaf)) {
        throw "配布に必要なライセンス文書が見つかりません: $requiredLegalFile"
    }
}
Copy-Item -LiteralPath $licenseFile -Destination (Join-Path $publishDir 'LICENSE.txt') -Force
Copy-Item -LiteralPath $thirdPartyNoticesFile -Destination (Join-Path $publishDir 'THIRD-PARTY-NOTICES.md') -Force

if ($SelfContained) {
    # 自己完結版は .NET Runtime / Windows Desktop Runtime を再頒布する。
    # 使用した dotnet インストールに付属する正本をコピーし、SDK更新時にも
    # 実際に同梱したランタイムと通知文がずれないようにする。
    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    $dotnetRoot = Split-Path -Parent $dotnetCommand.Source
    $dotnetLicense = Join-Path $dotnetRoot 'LICENSE.txt'
    $dotnetNotices = Join-Path $dotnetRoot 'ThirdPartyNotices.txt'
    foreach ($requiredDotnetFile in @($dotnetLicense, $dotnetNotices)) {
        if (-not (Test-Path -LiteralPath $requiredDotnetFile -PathType Leaf)) {
            throw "自己完結版の配布に必要な .NET ライセンス文書が見つかりません: $requiredDotnetFile"
        }
    }
    Copy-Item -LiteralPath $dotnetLicense -Destination (Join-Path $publishDir 'DOTNET-LICENSE.txt') -Force
    Copy-Item -LiteralPath $dotnetNotices -Destination (Join-Path $publishDir 'DOTNET-THIRD-PARTY-NOTICES.txt') -Force
}

function Sign-File([string]$path) {
    Write-Host "==> sign $([System.IO.Path]::GetFileName($path))" -ForegroundColor Cyan
    & $signTool sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $path
    if ($LASTEXITCODE -ne 0) { throw "署名に失敗しました: $path" }
}

if ($Sign) {
    # self-contained版に含まれるネイティブDLLも含め、実行可能な配布バイナリへ署名する。
    Get-ChildItem $publishDir -Recurse -File |
        Where-Object { @('.exe', '.dll') -contains $_.Extension.ToLowerInvariant() } |
        ForEach-Object { Sign-File $_.FullName }
}

# インストーラーの使用許諾画面に出す RTF を LICENSE から生成する。
# WiX 既定の WixUILicenseRtf はダミーテキスト (Lorem ipsum) のため、必ず差し替える。
# LICENSE を正とし、RTF を手で維持しない。非 ASCII 文字は \uN? 形式にエスケープする。
function ConvertTo-RtfText([string]$text) {
    $builder = New-Object System.Text.StringBuilder
    foreach ($ch in $text.ToCharArray()) {
        $code = [int]$ch
        if ($ch -eq '\' -or $ch -eq '{' -or $ch -eq '}') { [void]$builder.Append('\').Append($ch) }
        elseif ($code -gt 127) {
            if ($code -gt 32767) { $code -= 65536 }
            [void]$builder.Append('\u').Append($code).Append('?')
        }
        else { [void]$builder.Append($ch) }
    }
    $builder.ToString()
}

$licenseParagraphs = (Get-Content -LiteralPath $licenseFile -Raw) -split '(\r?\n){2,}' |
    Where-Object { $_.Trim() } |
    ForEach-Object { ($_ -split '\r?\n' | ForEach-Object { $_.Trim() }) -join ' ' }

$rtfNote = 'JP Scratch は MIT ライセンスのもとで提供されます。以下はその原文（英語）で、法的効力を持つのは原文です。同梱ライブラリのライセンスは、インストール先の THIRD-PARTY-NOTICES.md に記載しています。'
$rtfBody = New-Object System.Collections.Generic.List[string]
$rtfBody.Add((ConvertTo-RtfText $rtfNote))
foreach ($paragraph in $licenseParagraphs) { $rtfBody.Add((ConvertTo-RtfText $paragraph)) }

$licenseRtf = Join-Path $outputDir 'License.rtf'
New-Item -ItemType Directory -Force $outputDir | Out-Null
$rtf = '{\rtf1\ansi\ansicpg932\uc1\deff0{\fonttbl{\f0\fnil\fcharset128 Yu Gothic UI;}}\viewkind4\f0\fs20 ' +
    ($rtfBody -join '\par\par ') + '\par}'
[System.IO.File]::WriteAllText($licenseRtf, $rtf, [System.Text.Encoding]::ASCII)

Write-Host '==> wix build' -ForegroundColor Cyan

wix build (Join-Path $PSScriptRoot 'Package.wxs') -arch x64 -culture ja-JP `
    -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
    -d Version=$Version -d PublishDir=$publishDir -d IconFile=$iconFile -d LicenseRtf=$licenseRtf -o $msiPath
if ($LASTEXITCODE -ne 0) { throw 'wix build に失敗しました' }

if ($Sign) {
    Sign-File $msiPath
    & $signTool verify /pa /all $msiPath
    if ($LASTEXITCODE -ne 0) { throw "MSI署名の検証に失敗しました: $msiPath" }
}

$hashFile = "$msiPath.sha256"
$hash = (Get-FileHash -LiteralPath $msiPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $hashFile -Value "$hash  $([System.IO.Path]::GetFileName($msiPath))" -Encoding ASCII

$sizeMb = [math]::Round((Get-Item $msiPath).Length / 1MB, 1)
Write-Host ''
Write-Host "MSI: $msiPath ($sizeMb MB)" -ForegroundColor Green
Write-Host "SHA-256: $hashFile" -ForegroundColor Green
if ($Sign) {
    Write-Host "署名: あり（証明書 $CertificateThumbprint）" -ForegroundColor Green
}
else {
    Write-Host '署名: なし（公開配布時は -Sign を推奨）' -ForegroundColor Yellow
}
Write-Host 'インストール先: %LOCALAPPDATA%\Programs\JP Scratch (ユーザー単位・管理者権限不要)'
if (-not $SelfContained) {
    Write-Host '前提: .NET 10 Desktop Runtime (x64)。未導入の環境では初回起動時に入手先が案内されます。' -ForegroundColor Yellow
}
