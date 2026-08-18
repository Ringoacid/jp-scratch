[CmdletBinding()]
param(
    [Parameter(Position=0, Mandatory=$true)]
    [ValidateSet('create','list','show','update','add-plan','approve-plan','verify-plan','add-review','add-report','set-luna','clear-luna','add-approval','recover')]
    [string]$Command,

    [string]$Repo,
    [string]$Name,
    [string]$Task,
    [string]$File,
    [string]$Kind,
    [string]$Status,
    [string]$Phase,
    [string]$NextAction,
    [string]$ExecutionMode,
    [string]$Note,
    [string]$ApprovalType,
    [string]$Model,
    [string]$ThreadId,
    [string]$ThreadCreatedAt,
    [string]$Target,
    [switch]$IncludeCompleted,
    [switch]$IncludeCancelled,
    [switch]$Complete
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$Utf8NoBom = New-Object -TypeName System.Text.UTF8Encoding -ArgumentList $false
$Statuses = @('active','paused','cancelled','completed')
$Phases = @('clarifying','planning','awaiting-plan-approval','implementing','testing','reviewing','adjudicating-review','awaiting-fix-approval','fixing','awaiting-completion-approval','blocked','completed','paused','cancelled')

function Get-IsoNow { return [DateTimeOffset]::Now.ToString('yyyy-MM-ddTHH:mm:sszzz') }
function Get-Stamp { return [DateTimeOffset]::Now.ToString('yyyyMMdd-HHmmss') }

function Add-OrSetProperty($Object, [string]$PropertyName, $Value) {
    $p = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $p) { $Object | Add-Member -NotePropertyName $PropertyName -NotePropertyValue $Value }
    else { $p.Value = $Value }
}

function ConvertTo-SafeName([string]$Value) {
    $v = $Value -replace '[\\/:*?"<>|\x00-\x1F]', '_'
    $v = ($v -replace '\s+', ' ').Trim().TrimEnd([char[]]@('.', ' '))
    if ([string]::IsNullOrWhiteSpace($v)) { $v = 'タスク' }
    $reserved = @('CON','PRN','AUX','NUL','COM1','COM2','COM3','COM4','COM5','COM6','COM7','COM8','COM9','LPT1','LPT2','LPT3','LPT4','LPT5','LPT6','LPT7','LPT8','LPT9')
    if ($reserved -contains $v.ToUpperInvariant()) { $v = '_' + $v }
    if ($v.Length -gt 80) { $v = $v.Substring(0,80).TrimEnd([char[]]@('.', ' ')) }
    return $v
}

function Assert-State($State) {
    if ($State.schemaVersion -ne 1) { throw "Unsupported schemaVersion: $($State.schemaVersion)" }
    if ($Statuses -notcontains [string]$State.status) { throw "Invalid status: $($State.status)" }
    if ($Phases -notcontains [string]$State.phase) { throw "Invalid phase: $($State.phase)" }
}

function Read-State([string]$TaskDir, [bool]$Recover=$true) {
    $statePath = Join-Path $TaskDir 'state.json'
    try {
        $raw = [IO.File]::ReadAllText($statePath)
        $s = $raw | ConvertFrom-Json
        Assert-State $s
        return $s
    }
    catch {
        if (-not $Recover) { throw }
        $bak = Join-Path $TaskDir 'state.json.bak'
        if (-not (Test-Path -LiteralPath $bak -PathType Leaf)) { throw }
        $raw = [IO.File]::ReadAllText($bak)
        $s = $raw | ConvertFrom-Json
        Assert-State $s
        $tmp = Join-Path $TaskDir ("state.recover-{0}.tmp" -f $PID)
        [IO.File]::WriteAllText($tmp, (($s | ConvertTo-Json -Depth 100) + [Environment]::NewLine), $Utf8NoBom)
        Move-Item -LiteralPath $tmp -Destination $statePath -Force
        return $s
    }
}

function Write-State([string]$TaskDir, $State) {
    Assert-State $State
    Add-OrSetProperty $State 'updatedAt' (Get-IsoNow)
    $statePath = Join-Path $TaskDir 'state.json'
    $bakPath = Join-Path $TaskDir 'state.json.bak'
    $tmpPath = Join-Path $TaskDir ("state.{0}.{1}.tmp" -f $PID, [Guid]::NewGuid().ToString('N'))
    $json = ($State | ConvertTo-Json -Depth 100) + [Environment]::NewLine
    [IO.File]::WriteAllText($tmpPath, $json, $Utf8NoBom)
    if (Test-Path -LiteralPath $statePath -PathType Leaf) {
        try {
            [IO.File]::Replace($tmpPath, $statePath, $bakPath, $true)
        }
        catch {
            Copy-Item -LiteralPath $statePath -Destination $bakPath -Force
            Move-Item -LiteralPath $tmpPath -Destination $statePath -Force
        }
    }
    else {
        Move-Item -LiteralPath $tmpPath -Destination $statePath
    }
}

function Resolve-TaskArtifact([string]$TaskDir, [string]$Artifact) {
    $taskFull = [IO.Path]::GetFullPath($TaskDir).TrimEnd([char[]]@('\','/'))
    $candidate = if ([IO.Path]::IsPathRooted($Artifact)) { [IO.Path]::GetFullPath($Artifact) } else { [IO.Path]::GetFullPath((Join-Path $TaskDir $Artifact)) }
    $prefix = $taskFull + [IO.Path]::DirectorySeparatorChar
    if (-not ($candidate.Equals($taskFull, [StringComparison]::OrdinalIgnoreCase) -or $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase))) {
        throw 'Artifact file must be inside the task directory'
    }
    $rel = $candidate.Substring($taskFull.Length).TrimStart([char[]]@('\','/')).Replace('\','/')
    return @($candidate, $rel)
}

function Get-FileSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Ensure-ArrayProperty($Object, [string]$PropertyName) {
    $p = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $p -or $null -eq $p.Value) { Add-OrSetProperty $Object $PropertyName @() }
    else { $p.Value = @($p.Value) }
}

function Find-Plan($State, [string]$Rel) {
    foreach ($p in @($State.plans)) { if ([string]$p.file -eq $Rel) { return $p } }
    return $null
}

function Out-Result($Value) {
    ConvertTo-Json -InputObject $Value -Depth 100
}

switch ($Command) {
    'create' {
        if (-not $Repo -or -not $Name) { throw 'create requires -Repo and -Name' }
        $repoFull = (Resolve-Path -LiteralPath $Repo).Path
        $root = Join-Path $repoFull '.codex-loop\tasks'
        New-Item -ItemType Directory -Path $root -Force | Out-Null
        $base = "$(ConvertTo-SafeName $Name)-$(Get-Stamp)"
        $taskDir = Join-Path $root $base
        $n = 2
        while (Test-Path -LiteralPath $taskDir) { $taskDir = Join-Path $root ("{0}-{1}" -f $base,$n); $n++ }
        foreach ($sub in @('plans','reviews','reports','screenshots','artifacts','temp')) { New-Item -ItemType Directory -Path (Join-Path $taskDir $sub) -Force | Out-Null }
        $created = Get-IsoNow
        $state = [pscustomobject]@{
            schemaVersion = 1; taskId = (Split-Path -Leaf $taskDir); taskName = $Name; createdAt = $created; updatedAt = $created;
            status = 'active'; phase = 'clarifying'; nextAction = '要件を確認して実装プランを作成する'; executionMode = 'mcp'; suspendedPhase=$null;
            luna = [pscustomobject]@{ model=$null; threadId=$null; threadCreatedAt=$null; contextResetCount=0 };
            plans = @(); reviews = @(); reports = @(); approvals = @(); latestPlan=$null; latestReview=$null; latestReport=$null; notes=@()
        }
        Write-State $taskDir $state
        Out-Result ([pscustomobject]@{taskDir=$taskDir; state=$state})
    }
    'list' {
        if (-not $Repo) { throw 'list requires -Repo' }
        $repoFull = (Resolve-Path -LiteralPath $Repo).Path
        $root = Join-Path $repoFull '.codex-loop\tasks'
        $rows = @()
        if (Test-Path -LiteralPath $root -PathType Container) {
            foreach ($d in Get-ChildItem -LiteralPath $root -Directory) {
                try {
                    $s = Read-State $d.FullName
                    if (-not $IncludeCompleted -and $s.status -eq 'completed') { continue }
                    if (-not $IncludeCancelled -and $s.status -eq 'cancelled') { continue }
                    $rows += [pscustomobject]@{taskDir=$d.FullName;taskName=$s.taskName;status=$s.status;phase=$s.phase;updatedAt=$s.updatedAt;nextAction=$s.nextAction}
                } catch {
                    $rows += [pscustomobject]@{taskDir=$d.FullName;error=$_.Exception.Message}
                }
            }
        }
        Out-Result @($rows | Sort-Object updatedAt -Descending)
    }
    'show' { if (-not $Task) { throw 'show requires -Task' }; Out-Result (Read-State ([IO.Path]::GetFullPath($Task))) }
    'recover' { if (-not $Task) { throw 'recover requires -Task' }; Out-Result (Read-State ([IO.Path]::GetFullPath($Task)) $true) }
    'update' {
        if (-not $Task) { throw 'update requires -Task' }
        $taskDir=[IO.Path]::GetFullPath($Task); $s=Read-State $taskDir
        if ($Status) {
            if ($Statuses -notcontains $Status) { throw "Invalid status: $Status" }
            if (@('paused','cancelled') -contains $Status -and @('paused','cancelled','completed') -notcontains [string]$s.phase) { Add-OrSetProperty $s 'suspendedPhase' $s.phase }
            if ($Status -eq 'active' -and -not $Phase -and @('paused','cancelled') -contains [string]$s.phase) {
                $restore = if ($s.PSObject.Properties['suspendedPhase'] -and $s.suspendedPhase) { [string]$s.suspendedPhase } else { 'clarifying' }
                $s.phase=$restore; Add-OrSetProperty $s 'suspendedPhase' $null
            }
            $s.status=$Status
        }
        if ($Phase) { if ($Phases -notcontains $Phase) { throw "Invalid phase: $Phase" }; $s.phase=$Phase }
        if ($PSBoundParameters.ContainsKey('NextAction')) { $s.nextAction=$NextAction }
        if ($ExecutionMode) { if (@('mcp','codex-exec') -notcontains $ExecutionMode) { throw 'Invalid ExecutionMode' }; $s.executionMode=$ExecutionMode }
        if ($Note) { Ensure-ArrayProperty $s 'notes'; $s.notes=@($s.notes)+[pscustomobject]@{at=(Get-IsoNow);text=$Note} }
        Write-State $taskDir $s; Out-Result $s
    }
    'add-plan' {
        if (-not $Task -or -not $File -or -not $Kind) { throw 'add-plan requires -Task -File -Kind' }
        if (@('implementation','fix') -notcontains $Kind) { throw 'Kind must be implementation or fix' }
        $taskDir=[IO.Path]::GetFullPath($Task); $s=Read-State $taskDir; Ensure-ArrayProperty $s 'plans'
        $r=Resolve-TaskArtifact $taskDir $File; $full=$r[0]; $rel=$r[1]
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "File not found: $full" }
        if ($null -ne (Find-Plan $s $rel)) { throw "Plan already registered: $rel" }
        $entry=[pscustomobject]@{kind=$Kind;file=$rel;createdAt=(Get-IsoNow);approved=$false;approvedAt=$null;approvedSha256=$null}
        $s.plans=@($s.plans)+$entry; $s.latestPlan=$rel
        if ($Kind -eq 'implementation') { $s.phase='awaiting-plan-approval';$s.nextAction='ユーザーの実装プラン承認待ち' }
        else { $s.phase='awaiting-fix-approval';$s.nextAction='ユーザーの修正プラン承認待ち' }
        Write-State $taskDir $s; Out-Result $entry
    }
    'approve-plan' {
        if (-not $Task -or -not $File -or -not $ApprovalType -or -not $Phase -or -not $PSBoundParameters.ContainsKey('NextAction')) { throw 'approve-plan requires -Task -File -ApprovalType -Phase -NextAction' }
        if (@('implementation_plan','fix_plan') -notcontains $ApprovalType) { throw 'Invalid ApprovalType' }
        $taskDir=[IO.Path]::GetFullPath($Task); $s=Read-State $taskDir; Ensure-ArrayProperty $s 'approvals'
        $r=Resolve-TaskArtifact $taskDir $File; $full=$r[0]; $rel=$r[1]; if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "File not found: $full" }
        $plan=Find-Plan $s $rel; if ($null -eq $plan) { throw "Plan is not registered: $rel" }
        $digest=Get-FileSha256 $full; $approvedAt=Get-IsoNow
        $plan.approved=$true; $plan.approvedAt=$approvedAt; $plan.approvedSha256=$digest
        $entry=[pscustomobject]@{type=$ApprovalType;target=$rel;sha256=$digest;approvedAt=$approvedAt;source='explicit-user-confirmation'}
        $s.approvals=@($s.approvals)+$entry; $s.phase=$Phase; $s.nextAction=$NextAction
        Write-State $taskDir $s; Out-Result $entry
    }
    'verify-plan' {
        if (-not $Task -or -not $File) { throw 'verify-plan requires -Task -File' }
        $taskDir=[IO.Path]::GetFullPath($Task); $s=Read-State $taskDir
        $r=Resolve-TaskArtifact $taskDir $File; $full=$r[0]; $rel=$r[1]
        $plan=Find-Plan $s $rel; if ($null -eq $plan -or -not $plan.approved -or -not $plan.approvedSha256) { throw "Plan is not approved: $rel" }
        $actual=Get-FileSha256 $full; $valid=($actual -eq [string]$plan.approvedSha256)
        $result=[pscustomobject]@{file=$rel;expectedSha256=$plan.approvedSha256;actualSha256=$actual;valid=$valid}; Out-Result $result
        if (-not $valid) { exit 3 }
    }
    'add-review' {
        if (-not $Task -or -not $File -or -not $Model) { throw 'add-review requires -Task -File -Model' }
        $taskDir=[IO.Path]::GetFullPath($Task); $s=Read-State $taskDir; Ensure-ArrayProperty $s 'reviews'
        $r=Resolve-TaskArtifact $taskDir $File; $full=$r[0]; $rel=$r[1]; if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "File not found: $full" }
        $entry=[pscustomobject]@{file=$rel;createdAt=(Get-IsoNow);model=$Model;threadId=$ThreadId}
        $s.reviews=@($s.reviews)+$entry;$s.latestReview=$rel;$s.phase='adjudicating-review';$s.nextAction='Claude + Advisorでレビューの妥当性を検証する'
        Write-State $taskDir $s; Out-Result $entry
    }
    'add-report' {
        if (-not $Task -or -not $File) { throw 'add-report requires -Task -File' }
        $taskDir=[IO.Path]::GetFullPath($Task); $s=Read-State $taskDir; Ensure-ArrayProperty $s 'reports'
        $r=Resolve-TaskArtifact $taskDir $File; $full=$r[0]; $rel=$r[1]; if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "File not found: $full" }
        $entry=[pscustomobject]@{file=$rel;createdAt=(Get-IsoNow)};$s.reports=@($s.reports)+$entry;$s.latestReport=$rel
        if ($Complete) { $s.status='completed';$s.phase='completed';$s.nextAction='完了' }
        Write-State $taskDir $s; Out-Result $entry
    }
    'set-luna' {
        if (-not $Task -or -not $ThreadId -or -not $Model) { throw 'set-luna requires -Task -ThreadId -Model' }
        $taskDir=[IO.Path]::GetFullPath($Task);$s=Read-State $taskDir
        if ($s.luna.threadId -and $s.luna.threadId -ne $ThreadId) { $s.luna.contextResetCount=[int]$s.luna.contextResetCount+1 }
        $s.luna.model=$Model;$s.luna.threadId=$ThreadId;$s.luna.threadCreatedAt=if($ThreadCreatedAt){$ThreadCreatedAt}else{Get-IsoNow}
        Write-State $taskDir $s; Out-Result $s.luna
    }
    'clear-luna' {
        if (-not $Task) { throw 'clear-luna requires -Task' }
        $taskDir=[IO.Path]::GetFullPath($Task);$s=Read-State $taskDir
        if ($s.luna.threadId) { $s.luna.contextResetCount=[int]$s.luna.contextResetCount+1 }
        $s.luna.threadId=$null;$s.luna.threadCreatedAt=$null;Write-State $taskDir $s;Out-Result $s.luna
    }
    'add-approval' {
        if (-not $Task -or -not $ApprovalType) { throw 'add-approval requires -Task -ApprovalType' }
        $taskDir=[IO.Path]::GetFullPath($Task);$s=Read-State $taskDir;Ensure-ArrayProperty $s 'approvals'
        $entry=[pscustomobject]@{type=$ApprovalType;target=$Target;sha256=$null;approvedAt=(Get-IsoNow);source='explicit-user-confirmation'}
        $s.approvals=@($s.approvals)+$entry;if($Phase){$s.phase=$Phase};if($PSBoundParameters.ContainsKey('NextAction')){$s.nextAction=$NextAction}
        Write-State $taskDir $s;Out-Result $entry
    }
}
