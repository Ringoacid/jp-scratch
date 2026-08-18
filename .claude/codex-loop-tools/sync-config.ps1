[CmdletBinding()]
param(
    [Parameter(Position=0)]
    [string]$Root = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$Root = (Resolve-Path -LiteralPath $Root).Path
$Utf8NoBom = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false

$configPath = Join-Path $Root '.claude\codex-loop.json'
$settingsPath = Join-Path $Root '.claude\settings.json'
$skillPath = Join-Path $Root '.claude\skills\codex-loop\SKILL.md'

function Read-JsonObject([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [pscustomobject]@{}
    }
    $raw = [IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return [pscustomobject]@{}
    }
    $value = $raw | ConvertFrom-Json
    if ($null -eq $value) {
        return [pscustomobject]@{}
    }
    return $value
}

function Get-Prop($Object, [string]$Name, $Default = $null) {
    if ($null -eq $Object) { return $Default }
    $p = $Object.PSObject.Properties[$Name]
    if ($null -eq $p -or $null -eq $p.Value) { return $Default }
    return $p.Value
}

function Set-Prop($Object, [string]$Name, $Value) {
    $p = $Object.PSObject.Properties[$Name]
    if ($null -eq $p) {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
    else {
        $p.Value = $Value
    }
}

function Ensure-ObjectProp($Object, [string]$Name) {
    $current = Get-Prop $Object $Name $null
    if ($null -eq $current -or -not ($current -is [System.Management.Automation.PSCustomObject])) {
        $current = [pscustomobject]@{}
        Set-Prop $Object $Name $current
    }
    return $current
}

if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw "codex-loop config not found: $configPath"
}
if (-not (Test-Path -LiteralPath $skillPath -PathType Leaf)) {
    throw "codex-loop skill not found: $skillPath"
}

$config = Read-JsonObject $configPath
$models = Get-Prop $config 'models' $null
$orch = 'opus'
$advisor = 'fable'
if ($null -ne $models) {
    $v = Get-Prop $models 'orchestrator' $null
    if (-not [string]::IsNullOrWhiteSpace([string]$v)) { $orch = [string]$v }
    $v = Get-Prop $models 'advisor' $null
    if (-not [string]::IsNullOrWhiteSpace([string]$v)) { $advisor = [string]$v }
}

$settings = Read-JsonObject $settingsPath
Set-Prop $settings '$schema' 'https://json.schemastore.org/claude-code-settings.json'
Set-Prop $settings 'model' $orch
Set-Prop $settings 'advisorModel' $advisor
$permissions = Ensure-ObjectProp $settings 'permissions'
$allowValue = Get-Prop $permissions 'allow' @()
$allow = @($allowValue)
if ($allow -notcontains 'mcp__codex__*') { $allow += 'mcp__codex__*' }
Set-Prop $permissions 'allow' $allow

[IO.Directory]::CreateDirectory((Split-Path -Parent $settingsPath)) | Out-Null
[IO.File]::WriteAllText(
    $settingsPath,
    (($settings | ConvertTo-Json -Depth 100) + [Environment]::NewLine),
    $Utf8NoBom
)

$text = [IO.File]::ReadAllText($skillPath)
$normalized = $text -replace "`r`n", "`n"
if (-not $normalized.StartsWith("---`n")) {
    throw 'SKILL.md frontmatter not found'
}
$parts = $normalized -split "---`n", 3
if ($parts.Count -lt 3) {
    throw 'SKILL.md frontmatter is malformed'
}
$front = $parts[1]
if ($front -match '(?m)^model:\s*.*$') {
    $front = [regex]::Replace($front, '(?m)^model:\s*.*$', ('model: ' + $orch), 1)
}
else {
    if (-not $front.EndsWith("`n")) { $front += "`n" }
    $front += 'model: ' + $orch + "`n"
}
$out = "---`n" + $front + "---`n" + $parts[2]
[IO.File]::WriteAllText($skillPath, $out, $Utf8NoBom)

Write-Host "Synced orchestrator=$orch, advisor=$advisor"
