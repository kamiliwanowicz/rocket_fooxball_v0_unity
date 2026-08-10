[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Fast', 'Development', 'ProductionPrepare', 'ProductionValidate')]
    [string]$Mode,
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,
    [string]$EvidenceRoot,
    [string]$AttemptId,
    [string]$LedgerPath,
    [string]$ProbePath,
    [string]$SourceSha,
    [string]$ReviewedSha,
    [string]$Reviewer,
    [string]$Checkpoint = 'CP1',
    [Alias('ReportPath', 'ReviewReportPaths')]
    [string[]]$ReviewReportPath = @(),
    [Alias('ReportSha256', 'ReviewReportSha256s')]
    [string[]]$ReviewReportSha256 = @(),
    [string[]]$FindingDisposition = @(),
    [switch]$ForceFullComparison,
    [switch]$Capture,
    [switch]$PlanOnly,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:UnityVersion = '6000.5.6f1'
$script:UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe'
if ([string]::IsNullOrWhiteSpace($ProjectPath) -or $ProjectPath.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) { throw 'ProjectPath must be non-empty and one line.' }
$script:ProjectRoot = [System.IO.Path]::GetFullPath($ProjectPath)
$script:GitCommonRoot = $null
$script:EvidenceDirectory = $null
$script:ProbeOutputPath = $null
$script:WorkflowStarted = [DateTime]::UtcNow
$script:CommandRecords = New-Object System.Collections.Generic.List[object]
$script:ExecutedCheckIds = New-Object System.Collections.Generic.List[string]
$script:BakeCount = 0
$script:ReleaseProof = New-Object System.Collections.Generic.List[object]
$script:GeneratedRoots = @(
    'Assets/_Game/Generated',
    'Assets/_Game/Lighting',
    'Assets/_Game/Scenes/MovementLab.unity',
    'Assets/_Game/Scenes/MovementLab.unity.meta',
    'Assets/_Game/Scenes/MovementLab',
    'Assets/_Game/Scenes/MovementLab/LightingData.asset',
    'Assets/_Game/Scenes/MovementLab/LightingData.asset.meta'
)

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) {
        throw 'Path values must be non-empty and single-line.'
    }
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-OutsideProject {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Label)
    $full = Get-FullPath $Path
    $projectRoot = $script:ProjectRoot.TrimEnd('\')
    $prefix = $projectRoot + '\'
    if ($full.Equals($projectRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw ($Label + ' must be outside project: ' + $full) }
    if ($full.IndexOf('\Library\', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $full.IndexOf('\Temp\', [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw ($Label + ' cannot use Library or Temp: ' + $full)
    }
    return $full
}

function Assert-DurableEvidencePath {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Label)
    $full = Assert-OutsideProject $Path $Label
    if ([string]::IsNullOrWhiteSpace($script:GitCommonRoot)) { throw 'Git-common evidence destination is not initialized.' }
    $commonRoot = $script:GitCommonRoot.TrimEnd('\')
    if ($full.Equals($commonRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not $full.StartsWith($commonRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw ($Label + ' must remain inside Git-common evidence destination: ' + $full)
    }
    return $full
}

function Assert-OneLineValue {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) {
        throw ($Name + ' must be non-empty and one line.')
    }
    return $Value.Trim()
}

function Invoke-Git {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $priorErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& git -C $script:ProjectRoot @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $priorErrorAction
    }
    $lines = @($output | ForEach-Object { ([string]$_).TrimEnd() } | Where-Object { $_ -and -not $_.StartsWith('warning:', [StringComparison]::OrdinalIgnoreCase) })
    if ($exitCode -ne 0) { throw ('Git command failed (' + ($Arguments -join ' ') + '): ' + ($lines -join "`n")) }
    return (($lines -join "`n").Trim())
}

function Get-HeadSha {
    $sha = (Invoke-Git @('rev-parse', '--verify', 'HEAD')).ToLowerInvariant()
    if ($sha -notmatch '^[0-9a-f]{40}$') { throw 'Unable to resolve exact Git HEAD SHA.' }
    return $sha
}

function Test-GeneratedPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $normalized = $Path.Replace('\', '/').TrimStart('/')
    foreach ($root in $script:GeneratedRoots) {
        $candidate = $root.Replace('\', '/').TrimStart('/')
        if ($normalized.Equals($candidate, [StringComparison]::OrdinalIgnoreCase) -or
            $normalized.StartsWith($candidate.TrimEnd('/') + '/', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Get-NonGeneratedDirtyPaths {
    $trackedText = Invoke-Git @('diff', '--name-only', 'HEAD', '--')
    $untrackedText = Invoke-Git @('ls-files', '--others', '--exclude-standard')
    $tracked = @($trackedText -split "`n")
    $untracked = @($untrackedText -split "`n")
    return @($tracked + $untracked |
        ForEach-Object { ([string]$_).Trim() } |
        Where-Object { $_ -and -not (Test-GeneratedPath $_) } |
        Sort-Object -Unique)
}

function Get-ProjectUnityProcesses {
    $normalized = $script:ProjectRoot.TrimEnd('\').ToLowerInvariant()
    $all = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'")
    return @($all | Where-Object {
        $commandLine = [string]$_.CommandLine
        -not [string]::IsNullOrWhiteSpace($commandLine) -and $commandLine.ToLowerInvariant().Contains($normalized)
    })
}

function Get-LockPaths {
    return @(
        (Join-Path $script:ProjectRoot 'Temp\UnityLockfile'),
        (Join-Path $script:ProjectRoot 'Library\UnityLockfile')
    )
}

function Assert-NoProjectProcessOrLock {
    $active = @(Get-ProjectUnityProcesses)
    if ($active.Count -gt 0) { throw ('Unity process already owns project: ' + $script:ProjectRoot) }
    foreach ($lockPath in Get-LockPaths) {
        if (Test-Path -LiteralPath $lockPath) { throw ('Unity project lock exists: ' + $lockPath) }
    }
}

function Wait-UnityRelease {
    param([Parameter(Mandatory = $true)][string]$Label)
    $releaseSeconds = [Math]::Min([Math]::Max($TimeoutSeconds, 60), 3600)
    $deadline = [DateTime]::UtcNow.AddSeconds($releaseSeconds)
    $released = $false
    do {
        $active = @(Get-ProjectUnityProcesses)
        $locks = @(Get-LockPaths | Where-Object { Test-Path -LiteralPath $_ })
        if ($active.Count -eq 0 -and $locks.Count -eq 0) { $released = $true; break }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $released) { throw ('Unity process or project lock remained after ' + $Label + '.') }
    $script:ReleaseProof.Add([ordered]@{ label = $Label; releasedUtc = [DateTime]::UtcNow.ToString('O'); processCount = 0; lockCount = 0 })
    return $true
}

function Get-GeneratedHashes {
    $trackedText = Invoke-Git @('ls-files', '--full-name', '--')
    $untrackedText = Invoke-Git @('ls-files', '--full-name', '--others', '--exclude-standard', '--')
    $tracked = @($trackedText -split "`n")
    $untracked = @($untrackedText -split "`n")
    $map = [ordered]@{}
    foreach ($relative in @($tracked + $untracked | ForEach-Object { ([string]$_).Trim() } | Sort-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($relative) -or -not (Test-GeneratedPath $relative)) { continue }
        $absolute = Join-Path $script:ProjectRoot $relative
        if (Test-Path -LiteralPath $absolute -PathType Leaf) { $map[$relative] = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash.ToLowerInvariant() }
        else { $map[$relative] = '__MISSING__' }
    }
    return $map
}

function Get-ChangedHashPaths {
    param([Parameter(Mandatory = $true)]$Before, [Parameter(Mandatory = $true)]$After)
    $keys = @($Before.Keys + $After.Keys | Sort-Object -Unique)
    $changed = New-Object System.Collections.Generic.List[string]
    foreach ($key in $keys) {
        $beforeValue = if ($Before.Contains($key)) { [string]$Before[$key] } else { '__MISSING__' }
        $afterValue = if ($After.Contains($key)) { [string]$After[$key] } else { '__MISSING__' }
        if ($beforeValue -cne $afterValue) { $changed.Add($key) }
    }
    return @($changed.ToArray())
}

function Get-EnvironmentFingerprint {
    $value = [ordered]@{
        unityVersion = $script:UnityVersion
        unityPath = $script:UnityPath
        projectPath = $script:ProjectRoot
        libraryPath = Join-Path $script:ProjectRoot 'Library'
        powershell = $PSVersionTable.PSVersion.ToString()
    } | ConvertTo-Json -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($value)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}

function Get-InputDigest {
    param([Parameter(Mandatory = $true)][string[]]$Paths)
    $entries = New-Object System.Collections.Generic.List[string]
    foreach ($relative in $Paths) {
        $relative = Assert-OneLineValue 'Ledger input path' ([string]$relative)
        if ([System.IO.Path]::IsPathRooted($relative)) { throw ('Ledger input path must be project-relative: ' + $relative) }
        $full = [System.IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $relative))
        $projectPrefix = $script:ProjectRoot.TrimEnd('\') + '\'
        if (-not $full.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw ('Ledger input path escapes project: ' + $relative)
        }
        # `.git` can contain hundreds of megabytes of object data. Bind
        # ledger reuse to cheap Git facts instead of recursively hashing it.
        $canonicalRelative = $full.Substring($script:ProjectRoot.Length).TrimStart('\', '/')
        $canonicalRelative = $canonicalRelative.Replace('\', '/')
        if ($canonicalRelative -eq '.git') {
            $entries.Add('.git/HEAD=' + (Get-HeadSha))
            $entries.Add('.git/status=' + (Invoke-Git @('status', '--porcelain=v1', '--untracked-files=all')))
            continue
        }
        if ($canonicalRelative.StartsWith('.git/', [StringComparison]::OrdinalIgnoreCase)) {
            throw ('Ledger input path may not recurse into .git: ' + $relative)
        }
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            $entries.Add($relative.Replace('\', '/') + '=' + (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant())
        } elseif (Test-Path -LiteralPath $full -PathType Container) {
            $files = @(Get-ChildItem -LiteralPath $full -File -Recurse -Force | Sort-Object FullName)
            foreach ($file in $files) {
                $child = $file.FullName.Substring($script:ProjectRoot.Length).TrimStart('\', '/')
                $entries.Add($child.Replace('\', '/') + '=' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())
            }
            if ($files.Count -eq 0) { $entries.Add($relative.Replace('\', '/') + '=__EMPTY__') }
        } else {
            $entries.Add($relative.Replace('\', '/') + '=__MISSING__')
        }
    }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes(($entries -join "`n"))
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}

function New-LedgerRow {
    param(
        [Parameter(Mandatory = $true)][string]$CheckId,
        [Parameter(Mandatory = $true)][ValidateSet('fast', 'development', 'production-final')][string]$Tier,
        [Parameter(Mandatory = $true)][bool]$MutatesProject,
        [Parameter(Mandatory = $true)][string[]]$InputPaths,
        [Parameter(Mandatory = $true)][string[]]$InvalidationPaths,
        [string[]]$Subsumes = @(),
        [Parameter(Mandatory = $true)][string]$RunPoint
    )
    [ordered]@{
        check_id = $CheckId
        owner = 'workflow-orchestrator'
        tier = $Tier
        status = 'pending'
        run_point = $RunPoint
        mutates_project = $MutatesProject
        input_paths = @($InputPaths)
        input_digest = Get-InputDigest $InputPaths
        environment_fingerprint = Get-EnvironmentFingerprint
        invalidation_paths = @($InvalidationPaths)
        subsumes = @($Subsumes)
        executed_sha = $null
        validated_sha = $null
        evidence_path = $null
        evidence_digest = $null
        evidence = [ordered]@{ path = $null; sha256 = $null }
        subsumed_checks = @()
        invalidation_reason = $null
    }
}

function New-CheckLedger {
    $commonInputs = @('Assets/_Game/Editor', 'Tools/Validation', 'ProjectSettings', 'Packages')
    $rows = New-Object System.Collections.Generic.List[object]
    switch ($Mode) {
        'Fast' {
            $rows.Add((New-LedgerRow 'compile' 'fast' $false $commonInputs @('Assets/_Game/Editor', 'Tools/Validation', 'ProjectSettings', 'Packages') @('validator-readonly') 'coding'))
            $rows.Add((New-LedgerRow 'stage-probe' 'fast' $false @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Generated') @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Generated') @() 'coding'))
            $rows.Add((New-LedgerRow 'fast-build' 'fast' $true @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Generated') @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Generated', 'Assets/_Game/Lighting') @() 'coding'))
        }
        'Development' {
            $rows.Add((New-LedgerRow 'fast-build' 'fast' $true @('Assets/_Game/Editor/MovementLab') @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Generated') @() 'coding'))
            $rows.Add((New-LedgerRow 'development-bake' 'development' $true @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Lighting') @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Lighting', 'Assets/_Game/Scenes') @() 'checkpoint'))
        }
        'ProductionPrepare' {
            $rows.Add((New-LedgerRow 'review-marker' 'production-final' $false @('.git') @('.agents', 'Assets', 'ProjectSettings', 'Packages', 'Tools') @() 'source-freeze'))
            $rows.Add((New-LedgerRow 'stage-probe' 'fast' $false @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Generated') @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Generated') @() 'source-freeze'))
            $rows.Add((New-LedgerRow 'prebake-validate' 'production-final' $false @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Generated') @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Generated', 'Assets/_Game/Lighting') @() 'source-freeze'))
            $rows.Add((New-LedgerRow 'production-bake' 'production-final' $true @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Lighting') @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Lighting', 'Assets/_Game/Scenes') @() 'source-freeze'))
            $rows.Add((New-LedgerRow 'build2-noop' 'production-final' $true @('Assets/_Game/Generated', 'Assets/_Game/Scenes') @('Assets/_Game/Editor', 'Assets/_Game/Generated', 'Assets/_Game/Lighting', 'Assets/_Game/Scenes') @() 'final'))
        }
        'ProductionValidate' {
            $rows.Add((New-LedgerRow 'final-source-clean' 'production-final' $false @('.git', 'Assets', 'ProjectSettings', 'Packages', 'Tools') @('.agents', 'Assets', 'ProjectSettings', 'Packages', 'Tools') @() 'final'))
            $rows.Add((New-LedgerRow 'capture-validator' 'production-final' $false @('Assets/_Game/Editor', 'Assets/_Game/Generated', 'Tools/Validation') @('Assets/_Game/Editor', 'Assets/_Game/Generated', 'Assets/_Game/Lighting', 'Assets/_Game/Scenes') @('validator-readonly') 'final'))
        }
    }
    return @($rows.ToArray())
}

function Test-IsAncestor {
    param([Parameter(Mandatory = $true)][string]$Ancestor, [Parameter(Mandatory = $true)][string]$Descendant)
    $priorErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & git -C $script:ProjectRoot merge-base --is-ancestor $Ancestor $Descendant 2>$null
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $priorErrorAction
    }
    return ($exitCode -eq 0)
}

function Get-ChangedPathsSince {
    param([Parameter(Mandatory = $true)][string]$FromSha, [Parameter(Mandatory = $true)][string]$ToSha)
    if ($FromSha -eq $ToSha) { return @() }
    if (-not (Test-IsAncestor $FromSha $ToSha)) { return $null }
    $text = Invoke-Git @('diff', '--name-only', ($FromSha + '..' + $ToSha), '--')
    return @($text -split "`n" | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ })
}

function Test-PathIntersects {
    param([string[]]$ChangedPaths = @(), [string[]]$InvalidationPaths = @())
    foreach ($changed in $ChangedPaths) {
        foreach ($invalid in $InvalidationPaths) {
            $left = $changed.Replace('\', '/').TrimEnd('/')
            $right = $invalid.Replace('\', '/').TrimEnd('/')
            if ($left.Equals($right, [StringComparison]::OrdinalIgnoreCase) -or
                $left.StartsWith($right + '/', [StringComparison]::OrdinalIgnoreCase) -or
                $right.StartsWith($left + '/', [StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
    }
    return $false
}

function Merge-ExistingLedger {
    param([Parameter(Mandatory = $true)][object[]]$Rows, [Parameter(Mandatory = $true)][string]$CurrentSha)
    if ([string]::IsNullOrWhiteSpace($LedgerPath)) { return }
    $full = Assert-DurableEvidencePath $LedgerPath 'LedgerPath'
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw ('Ledger file missing: ' + $full) }
    $parsed = Get-Content -Raw -LiteralPath $full | ConvertFrom-Json
    $hasCheckLedger = $null -ne $parsed -and $parsed.PSObject.Properties.Name -contains 'checkLedger'
    $hasRows = $null -ne $parsed -and $parsed.PSObject.Properties.Name -contains 'rows'
    $priorRows = if ($hasCheckLedger) { @($parsed.checkLedger) } elseif ($hasRows) { @($parsed.rows) } else { @() }
    foreach ($row in $Rows) {
        $prior = $priorRows | Where-Object { [string]$_.check_id -eq [string]$row.check_id } | Select-Object -First 1
        if ($null -eq $prior -or [string]$prior.status -notin @('executed', 'reused')) { continue }
        $evidencePath = [string]$prior.evidence_path
        if ([string]::IsNullOrWhiteSpace($evidencePath) -or -not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) { $row.status = 'invalidated'; continue }
        try { $evidencePath = Assert-DurableEvidencePath $evidencePath 'Ledger evidence' } catch { $row.status = 'invalidated'; $row.invalidation_reason = $_.Exception.Message; continue }
        $evidenceDigest = [string]$prior.evidence_digest
        if ([string]::IsNullOrWhiteSpace($evidenceDigest)) { $row.status = 'invalidated'; $row.invalidation_reason = 'evidence digest missing'; continue }
        if ((Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $evidenceDigest.ToLowerInvariant()) { $row.status = 'invalidated'; $row.invalidation_reason = 'evidence digest mismatch'; continue }
        $priorSha = [string]$prior.validated_sha
        $changed = if ($priorSha) { Get-ChangedPathsSince $priorSha $CurrentSha } else { $null }
        $sameInputs = [string]$prior.input_digest -eq [string]$row.input_digest
        $sameEnvironment = [string]$prior.environment_fingerprint -eq [string]$row.environment_fingerprint
        $exact = $priorSha -eq $CurrentSha
        $pureReattest = (-not [bool]$row.mutates_project) -and $sameInputs -and $sameEnvironment -and $null -ne $changed -and -not (Test-PathIntersects @($changed) @($row.invalidation_paths))
        $manualProof = ([string]$row.check_id -match '(bake|capture|manual)') -or [string]$row.check_id -eq 'review-marker' -or ([string]$row.tier -eq 'production-final' -and [bool]$row.mutates_project)
        if ($exact) {
            if ([string]$row.check_id -eq 'review-marker') {
                if ([string]::IsNullOrWhiteSpace($SourceSha) -or [string]::IsNullOrWhiteSpace($ReviewedSha)) {
                    $row.status = 'invalidated'; $row.invalidation_reason = 'source/reviewed SHA missing for marker reuse'; continue
                }
                try { $null = Read-ReviewMarker $CurrentSha $SourceSha $ReviewedSha }
                catch { $row.status = 'invalidated'; $row.invalidation_reason = 'review marker/report revalidation failed: ' + $_.Exception.Message; continue }
            }
            $row.status = 'reused'
            $row.executed_sha = [string]$prior.executed_sha
            $row.validated_sha = $CurrentSha
            $row.evidence_path = $evidencePath
            $row.evidence_digest = $evidenceDigest
            $row.evidence = [ordered]@{ path = $evidencePath; sha256 = $evidenceDigest }
        } elseif ($pureReattest -and -not $manualProof) {
            if ([string]$row.check_id -eq 'review-marker') {
                if ([string]::IsNullOrWhiteSpace($SourceSha) -or [string]::IsNullOrWhiteSpace($ReviewedSha)) {
                    $row.status = 'invalidated'; $row.invalidation_reason = 'source/reviewed SHA missing for marker reuse'; continue
                }
                try { $null = Read-ReviewMarker $CurrentSha $SourceSha $ReviewedSha }
                catch { $row.status = 'invalidated'; $row.invalidation_reason = 'review marker/report revalidation failed: ' + $_.Exception.Message; continue }
            }
            $row.status = 'reused'
            $row.executed_sha = [string]$prior.executed_sha
            $row.validated_sha = $CurrentSha
            $row.evidence_path = $evidencePath
            $row.evidence_digest = $evidenceDigest
            $row.evidence = [ordered]@{ path = $evidencePath; sha256 = $evidenceDigest }
        } else {
            $row.status = 'invalidated'
            $row.invalidation_reason = if ($null -eq $changed) { 'ancestry or SHA changed' } elseif (Test-PathIntersects @($changed) @($row.invalidation_paths)) { 'input path changed' } else { 'proof cannot reattest' }
        }
    }
}

function Test-CheckPending {
    param([Parameter(Mandatory = $true)][string]$CheckId)
    $row = $script:LedgerRows | Where-Object { [string]$_.check_id -eq $CheckId } | Select-Object -First 1
    return ($null -eq $row -or [string]$row.status -in @('pending', 'invalidated'))
}

function Mark-CheckReused {
    param([Parameter(Mandatory = $true)][string]$CheckId)
    # Keep exact-SHA evidence marked `reused`; never rewrite it as `executed`.
    $null = $script:LedgerRows | Where-Object { [string]$_.check_id -eq $CheckId } | Select-Object -First 1
}

function Add-CommandRecord {
    param([Parameter(Mandatory = $true)]$Record)
    $script:CommandRecords.Add($Record)
}

function Mark-CheckExecuted {
    param([Parameter(Mandatory = $true)][string]$CheckId)
    if (-not $PlanOnly -and -not $script:ExecutedCheckIds.Contains($CheckId)) { $script:ExecutedCheckIds.Add($CheckId) }
}

function Invoke-UnityStep {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Method,
        [string[]]$ExtraArguments = @(),
        [Parameter(Mandatory = $true)][bool]$MutatesProject,
        [switch]$NoGraphics
    )
    $logPath = Join-Path $script:EvidenceDirectory ('unity-' + $Label.ToLowerInvariant() + '.log')
    $arguments = @('-batchmode', '-quit', '-buildTarget', 'StandaloneWindows64', '-projectPath', $script:ProjectRoot, '-executeMethod', $Method, '-logFile', $logPath)
    if ($NoGraphics) { $arguments += '-nographics' }
    if ($ExtraArguments.Count -gt 0) { $arguments += $ExtraArguments }
    $started = [DateTime]::UtcNow
    $exitCode = 0
    $skipped = $false
    if ($PlanOnly) {
        $skipped = $true
    } else {
        Assert-NoProjectProcessOrLock
        if (-not (Test-Path -LiteralPath (Join-Path $script:ProjectRoot 'Library') -PathType Container)) { throw 'Warm private Library is missing.' }
        if ($Method -match 'BakeMovementLabLighting') { $script:BakeCount++ }
        $process = $null
        try {
            $process = Start-Process -FilePath $script:UnityPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
            $exitCode = $process.ExitCode
        } finally {
            Wait-UnityRelease $Label | Out-Null
        }
        if ($exitCode -ne 0) { throw ('Unity step failed: ' + $Label + ' (exit ' + $exitCode + '). Log: ' + $logPath) }
    }
    Add-CommandRecord ([ordered]@{
        label = $Label
        tier = if ($Mode -eq 'Development') { 'development' } elseif ($Mode -eq 'Fast') { 'fast' } else { 'production-final' }
        method = $Method
        arguments = @($arguments)
        exitCode = $exitCode
        skipped = $skipped
        mutatesProject = $MutatesProject
        logPath = $logPath
        elapsedMs = ([DateTime]::UtcNow - $started).TotalMilliseconds
    })
}

function Invoke-ExternalScript {
    param([Parameter(Mandatory = $true)][string]$Label, [Parameter(Mandatory = $true)][string]$ScriptPath, [Parameter(Mandatory = $true)][string[]]$Arguments)
    $started = [DateTime]::UtcNow
    if ($PlanOnly) {
        Add-CommandRecord ([ordered]@{ label = $Label; tier = 'production-final'; method = $ScriptPath; arguments = @($Arguments); exitCode = 0; skipped = $true; mutatesProject = $false; logPath = $null; elapsedMs = 0; output = '{}' })
        return '{}'
    }
    $output = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    Add-CommandRecord ([ordered]@{ label = $Label; tier = 'production-final'; method = $ScriptPath; arguments = @($Arguments); exitCode = $exitCode; skipped = $false; mutatesProject = $false; logPath = $null; elapsedMs = ([DateTime]::UtcNow - $started).TotalMilliseconds; output = ($output -join "`n") })
    if ($exitCode -ne 0) { throw ($Label + ' failed: ' + ($output -join "`n")) }
    return (($output -join "`n").Trim())
}

function Copy-CaptureEvidence {
    param([Parameter(Mandatory = $true)][string]$Output)
    $match = [Regex]::Match($Output, '(?m)^BRIGHT_ARENA_CAPTURE_MANIFEST\s+(.+)$')
    if (-not $match.Success) { throw 'Capture manifest path missing from capture output.' }
    $manifestPath = Assert-OneLineValue 'Capture manifest path' $match.Groups[1].Value
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw ('Capture manifest missing: ' + $manifestPath) }
    $captureDirectory = Join-Path $script:EvidenceDirectory 'capture'
    if (-not (Test-Path -LiteralPath $captureDirectory -PathType Container)) { New-Item -ItemType Directory -Force -Path $captureDirectory | Out-Null }
    $sourceDirectory = Split-Path -Parent $manifestPath
    foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceDirectory -File -Force) {
        Copy-Item -LiteralPath $sourceFile.FullName -Destination (Join-Path $captureDirectory $sourceFile.Name) -Force
    }
    $durableManifest = Join-Path $captureDirectory (Split-Path -Leaf $manifestPath)
    if (-not (Test-Path -LiteralPath $durableManifest -PathType Leaf)) { throw ('Durable capture manifest copy missing: ' + $durableManifest) }
    return [ordered]@{ sourceManifest = $durableManifest; durableManifest = $durableManifest; durableDirectory = $captureDirectory; files = @((Get-ChildItem -LiteralPath $captureDirectory -File -Force | ForEach-Object { [ordered]@{ path = $_.FullName; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })) }
}

function Assert-ProbeString {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)]$Value)
    if ($Value -isnot [string]) { throw ('Stage probe ' + $Name + ' must be a string.') }
    return Assert-OneLineValue ('Stage probe ' + $Name) ([string]$Value)
}

function Assert-ProbeStringArray {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)]$Value)
    if ($Value -is [string] -or $Value -isnot [System.Collections.IEnumerable]) { throw ('Stage probe ' + $Name + ' must be an array of strings.') }
    $values = @($Value)
    foreach ($item in $values) {
        if ($item -isnot [string]) { throw ('Stage probe ' + $Name + ' must contain only strings.') }
        if ([string]$item -and ([string]$item).IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) { throw ('Stage probe ' + $Name + ' contains a multi-line value.') }
    }
    return $values
}

function Read-ProbeContract {
    $path = $script:ProbeOutputPath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return [ordered]@{
            status = 'missing'
            path = $path
            schemaVersion = $null
            sha256 = $null
            gitSha = $null
            unityVersion = $null
            manifestStatus = $null
            staleStages = @()
            staleReasons = @()
            lightingInputDigest = $null
            sourceSignature = $null
            outputFingerprint = $null
            bakedProfile = $null
            fingerprintPaths = @()
            fingerprintHashes = @()
        }
    }
    $raw = Get-Content -Raw -LiteralPath $path
    $probe = $raw | ConvertFrom-Json
    if ($null -eq $probe) { throw 'Stage probe JSON is empty.' }
    $requiredFields = @(
        'schemaVersion', 'gitSha', 'unityVersion', 'manifestStatus', 'staleStages', 'staleReasons',
        'lightingInputDigest', 'sourceSignature', 'outputFingerprint', 'bakedProfile', 'fingerprintPaths', 'fingerprintHashes'
    )
    $propertyNames = @($probe.PSObject.Properties.Name)
    foreach ($field in $requiredFields) {
        if (-not $propertyNames.Contains($field)) { throw ('Stage probe field is missing: ' + $field) }
    }
    $schemaValue = $probe.schemaVersion
    if (($schemaValue -isnot [int16]) -and ($schemaValue -isnot [int32]) -and ($schemaValue -isnot [int64]) -and
        ($schemaValue -isnot [uint16]) -and ($schemaValue -isnot [uint32]) -and ($schemaValue -isnot [uint64])) {
        throw 'Stage probe schemaVersion must be an integer.'
    }
    if ([int64]$schemaValue -ne 1) { throw 'Stage probe schemaVersion must equal 1.' }
    $sha = Assert-ProbeString 'gitSha' $probe.gitSha
    if ($sha -notmatch '^[0-9a-fA-F]{40}$') { throw 'Stage probe gitSha must be an exact 40-character SHA.' }
    $sha = $sha.ToLowerInvariant()
    if ($sha -ne (Get-HeadSha)) { throw 'Stage probe Git SHA does not match current HEAD.' }
    $unityVersion = Assert-ProbeString 'unityVersion' $probe.unityVersion
    if ($unityVersion -ne $script:UnityVersion) { throw ('Stage probe Unity version must equal ' + $script:UnityVersion + '.') }
    $manifestStatus = Assert-ProbeString 'manifestStatus' $probe.manifestStatus
    $stale = @(Assert-ProbeStringArray 'staleStages' $probe.staleStages)
    $staleReasons = @(Assert-ProbeStringArray 'staleReasons' $probe.staleReasons)
    $lighting = Assert-ProbeString 'lightingInputDigest' $probe.lightingInputDigest
    $sourceSignature = Assert-ProbeString 'sourceSignature' $probe.sourceSignature
    $outputFingerprint = Assert-ProbeString 'outputFingerprint' $probe.outputFingerprint
    $bakedProfile = Assert-ProbeString 'bakedProfile' $probe.bakedProfile
    $fingerprintPaths = @(Assert-ProbeStringArray 'fingerprintPaths' $probe.fingerprintPaths)
    $fingerprintHashes = @(Assert-ProbeStringArray 'fingerprintHashes' $probe.fingerprintHashes)
    if ($fingerprintPaths.Count -ne $fingerprintHashes.Count) { throw 'Stage probe fingerprintPaths and fingerprintHashes counts must match.' }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    return [ordered]@{
        status = 'read'
        path = $path
        schemaVersion = [int]$schemaValue
        sha256 = $hash
        gitSha = $sha
        unityVersion = $unityVersion
        manifestStatus = $manifestStatus
        staleStages = $stale
        staleReasons = $staleReasons
        lightingInputDigest = $lighting
        sourceSignature = $sourceSignature
        outputFingerprint = $outputFingerprint
        bakedProfile = $bakedProfile
        fingerprintPaths = $fingerprintPaths
        fingerprintHashes = $fingerprintHashes
        raw = $probe
    }
}

function Read-ReviewMarker {
    param([Parameter(Mandatory = $true)][string]$ProjectShaValue, [Parameter(Mandatory = $true)][string]$SourceShaValue, [Parameter(Mandatory = $true)][string]$ReviewedShaValue)
    $path = Assert-DurableEvidencePath (Join-Path $script:GitCommonRoot ('architecture-evidence\movement-lab-prebake\reviews\' + $ProjectShaValue + '.json')) 'Review marker'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Review marker missing: ' + $path) }
    $marker = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    if ($null -eq $marker) { throw ('Review marker is stale or incomplete: ' + $path) }
    $required = @('schemaVersion', 'gitSha', 'projectSha', 'sourceSha', 'reviewedSha', 'sourceReviewCompleted', 'criticalHighFixesApplied', 'reviewer', 'checkpoint', 'completedUtc', 'reviewReportPaths', 'reviewReportSha256s')
    $markerProperties = @($marker.PSObject.Properties.Name)
    foreach ($field in $required) {
        if (-not $markerProperties.Contains($field)) { throw ('Review marker field is missing: ' + $field) }
    }
    if (($marker.schemaVersion -isnot [int16]) -and ($marker.schemaVersion -isnot [int32]) -and ($marker.schemaVersion -isnot [int64])) { throw 'Review marker schemaVersion must be an integer.' }
    foreach ($boolField in @('sourceReviewCompleted', 'criticalHighFixesApplied')) {
        if ($marker.$boolField -isnot [bool]) { throw ('Review marker ' + $boolField + ' must be boolean.') }
    }
    foreach ($shaField in @('gitSha', 'projectSha', 'sourceSha', 'reviewedSha')) {
        if ($marker.$shaField -isnot [string] -or [string]$marker.$shaField -notmatch '^[0-9a-fA-F]{40}$') { throw ('Review marker ' + $shaField + ' must be an exact SHA.') }
    }
    $reportPaths = if ($marker.PSObject.Properties.Name -contains 'reviewReportPaths') { @($marker.reviewReportPaths) } else { @() }
    $reportShas = if ($marker.PSObject.Properties.Name -contains 'reviewReportSha256s') { @($marker.reviewReportSha256s) } else { @() }
    $findingDispositions = if ($marker.PSObject.Properties.Name -contains 'findingDispositions') { @($marker.findingDispositions) } else { @() }
    if ([int]$marker.schemaVersion -ne 1 -or [string]$marker.gitSha -ne $ProjectShaValue -or [string]$marker.projectSha -ne $ProjectShaValue -or
        [string]$marker.sourceSha -ne $SourceShaValue -or [string]$marker.reviewedSha -ne $ReviewedShaValue -or -not [bool]$marker.sourceReviewCompleted -or
        -not [bool]$marker.criticalHighFixesApplied -or [string]::IsNullOrWhiteSpace([string]$marker.reviewer) -or [string]::IsNullOrWhiteSpace([string]$marker.checkpoint) -or
        [string]::IsNullOrWhiteSpace([string]$marker.completedUtc) -or
        $reportPaths.Count -eq 0 -or $reportPaths.Count -ne $reportShas.Count) {
        throw ('Review marker is stale or incomplete: ' + $path)
    }
    foreach ($stringField in @('reviewer', 'checkpoint', 'completedUtc')) {
        if ($marker.$stringField -isnot [string]) { throw ('Review marker ' + $stringField + ' must be a string.') }
    }
    foreach ($identity in @([string]$marker.reviewer, [string]$marker.checkpoint, [string]$marker.completedUtc)) {
        if ([string]::IsNullOrWhiteSpace($identity) -or $identity.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) { throw ('Review marker identity/time must be one line: ' + $path) }
    }
    for ($index = 0; $index -lt $reportPaths.Count; $index++) {
        $reportPath = Assert-DurableEvidencePath ([string]$reportPaths[$index]) 'Review report'
        if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw ('Review report missing: ' + $reportPath) }
        if ([string]$reportShas[$index] -notmatch '^[0-9a-fA-F]{64}$') { throw ('Review report SHA-256 invalid: ' + $reportPath) }
        $actualReportSha = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualReportSha -ne ([string]$reportShas[$index]).ToLowerInvariant()) { throw ('Review report SHA-256 mismatch: ' + $reportPath) }
    }
    return [ordered]@{ status = 'validated'; markerPath = $path; markerSha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant(); projectSha = $ProjectShaValue; sourceSha = $SourceShaValue; reviewedSha = $ReviewedShaValue; reviewer = [string]$marker.reviewer; checkpoint = [string]$marker.checkpoint; reviewReportPaths = $reportPaths; reviewReportSha256s = $reportShas; findingDispositions = $findingDispositions; gitMutation = $false }
}

function Write-AtomicJson {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)]$Value)
    $directory = Split-Path -Parent $Path
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporary = Join-Path $directory ('.movement-lab-workflow-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $backup = $null
    $json = ($Value | ConvertTo-Json -Depth 14) + "`n"
    try {
        [System.IO.File]::WriteAllText($temporary, $json, (New-Object System.Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            $backup = $Path + '.backup-' + [Guid]::NewGuid().ToString('N')
            [System.IO.File]::Replace($temporary, $Path, $backup)
            if (Test-Path -LiteralPath $backup) { [System.IO.File]::Delete($backup) }
        } else {
            [System.IO.File]::Move($temporary, $Path)
        }
    } finally {
        if (Test-Path -LiteralPath $temporary) { [System.IO.File]::Delete($temporary) }
        if ($null -ne $backup -and (Test-Path -LiteralPath $backup)) { [System.IO.File]::Delete($backup) }
    }
}

if (-not (Test-Path -LiteralPath $script:ProjectRoot -PathType Container)) { throw ('Project path not found: ' + $script:ProjectRoot) }
if ($script:ProjectRoot.Length -gt 80) { throw 'Project path must be short (80 characters or fewer).' }
if (-not [System.IO.Path]::IsPathRooted($ProjectPath)) { throw 'ProjectPath must be absolute.' }
if (-not $PlanOnly -and -not (Test-Path -LiteralPath $script:UnityPath -PathType Leaf)) { throw ('Unity ' + $script:UnityVersion + ' not found: ' + $script:UnityPath) }
if (-not (Test-Path -LiteralPath (Join-Path $script:ProjectRoot 'ProjectSettings\ProjectVersion.txt') -PathType Leaf)) { throw 'ProjectVersion.txt missing.' }
$projectVersion = Get-Content -Raw -LiteralPath (Join-Path $script:ProjectRoot 'ProjectSettings\ProjectVersion.txt')
if ($projectVersion -notmatch ('m_EditorVersion:\s*' + [Regex]::Escape($script:UnityVersion))) { throw ('Project Unity version is not ' + $script:UnityVersion + '.') }
$libraryPath = Get-FullPath (Join-Path $script:ProjectRoot 'Library')
if (-not $PlanOnly) {
    if (-not (Test-Path -LiteralPath $libraryPath -PathType Container)) { throw 'Warm private Library is missing.' }
    $libraryItem = Get-Item -LiteralPath $libraryPath
    if ($libraryItem.LinkType -or ($libraryItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Library must remain private to this project; shared link detected.' }
}

$commonRaw = Invoke-Git @('rev-parse', '--git-common-dir')
$commonGit = if ([System.IO.Path]::IsPathRooted($commonRaw)) { Get-FullPath $commonRaw } else { Get-FullPath (Join-Path $script:ProjectRoot $commonRaw) }
$script:GitCommonRoot = $commonGit
$projectRoot = $script:ProjectRoot.TrimEnd('\')
if ($commonGit.Equals($projectRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $commonGit.StartsWith($projectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw ('Git-common evidence destination must be outside project: ' + $commonGit)
}
$attemptValue = if ([string]::IsNullOrWhiteSpace($AttemptId)) { [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') } else { $AttemptId }
if ($attemptValue -notmatch '^[A-Za-z0-9._-]+$') { throw 'AttemptId must contain only letters, digits, dot, underscore, or hyphen.' }
$defaultEvidence = Join-Path $commonGit ('movement-lab-proof\' + $attemptValue)
$script:EvidenceDirectory = if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) { Assert-DurableEvidencePath (Get-FullPath $defaultEvidence) 'EvidenceRoot' } else { Assert-DurableEvidencePath $EvidenceRoot 'EvidenceRoot' }
if (-not (Test-Path -LiteralPath $script:EvidenceDirectory -PathType Container)) { New-Item -ItemType Directory -Force -Path $script:EvidenceDirectory | Out-Null }
$logs = Join-Path $script:EvidenceDirectory 'logs'
if (-not (Test-Path -LiteralPath $logs -PathType Container)) { New-Item -ItemType Directory -Force -Path $logs | Out-Null }
$script:ProbeOutputPath = if ([string]::IsNullOrWhiteSpace($ProbePath)) { Join-Path $script:EvidenceDirectory 'movement-lab-stage-probe.json' } else { Assert-DurableEvidencePath $ProbePath 'ProbePath' }

$beforeHead = Get-HeadSha
$SourceSha = if ([string]::IsNullOrWhiteSpace($SourceSha)) { '' } else { Assert-OneLineValue 'SourceSha' $SourceSha }
$ReviewedSha = if ([string]::IsNullOrWhiteSpace($ReviewedSha)) { '' } else { Assert-OneLineValue 'ReviewedSha' $ReviewedSha }
foreach ($shaField in @(@('SourceSha', $SourceSha), @('ReviewedSha', $ReviewedSha))) {
    if (-not [string]::IsNullOrWhiteSpace([string]$shaField[1]) -and [string]$shaField[1] -notmatch '^[0-9a-fA-F]{40}$') {
        throw ($shaField[0] + ' must be an exact 40-character Git SHA.')
    }
}
$SourceSha = $SourceSha.ToLowerInvariant()
$ReviewedSha = $ReviewedSha.ToLowerInvariant()
if (-not [string]::IsNullOrWhiteSpace($Reviewer)) { $Reviewer = Assert-OneLineValue 'Reviewer' $Reviewer }
$Checkpoint = Assert-OneLineValue 'Checkpoint' $Checkpoint
$ReviewReportPath = @($ReviewReportPath)
$ReviewReportSha256 = @($ReviewReportSha256)
$FindingDisposition = @($FindingDisposition)
if ($ReviewReportPath.Count -ne $ReviewReportSha256.Count) { throw 'ReviewReportPath and ReviewReportSha256 counts must match.' }
for ($reportIndex = 0; $reportIndex -lt $ReviewReportPath.Count; $reportIndex++) {
    $ReviewReportPath[$reportIndex] = Assert-DurableEvidencePath $ReviewReportPath[$reportIndex] 'Review report'
    if ([string]$ReviewReportSha256[$reportIndex] -notmatch '^[0-9a-fA-F]{64}$') { throw ('Review report SHA-256 invalid: ' + $ReviewReportPath[$reportIndex]) }
}
foreach ($finding in $FindingDisposition) {
    if (-not [string]::IsNullOrWhiteSpace([string]$finding)) { Assert-OneLineValue 'FindingDisposition' ([string]$finding) | Out-Null }
}
if ($Mode -eq 'ProductionPrepare' -and -not [string]::IsNullOrWhiteSpace($SourceSha) -and $SourceSha -ne $beforeHead) { throw 'ProductionPrepare SourceSha must match clean current HEAD.' }
if ($Mode -eq 'ProductionPrepare' -and -not [string]::IsNullOrWhiteSpace($ReviewedSha) -and $ReviewedSha -ne $beforeHead) { throw 'ProductionPrepare ReviewedSha must match clean current HEAD.' }
$dirtyBefore = @(Get-NonGeneratedDirtyPaths)
if ($Mode -in @('ProductionPrepare', 'ProductionValidate') -and $dirtyBefore.Count -gt 0) { throw ('Non-generated source is dirty: ' + ($dirtyBefore -join ', ')) }
Assert-NoProjectProcessOrLock

$beforeHashes = Get-GeneratedHashes
$ledger = New-CheckLedger
$script:LedgerRows = $ledger
Merge-ExistingLedger $ledger $beforeHead
$markerResult = $null
$probeRecord = $null
$captureEvidence = $null

try {
    switch ($Mode) {
        'Fast' {
            if (Test-CheckPending 'compile') { Invoke-UnityStep 'CompileAndProbe' 'RocketFooxball.Editor.MovementLabBuilder.ProbeMovementLabGeneratedState' @('-movementLabProbePath', $script:ProbeOutputPath) $false -NoGraphics; Mark-CheckExecuted 'compile' } else { Mark-CheckReused 'compile' }
            $probeRecord = Read-ProbeContract
            if (Test-CheckPending 'stage-probe') { Mark-CheckExecuted 'stage-probe' } else { Mark-CheckReused 'stage-probe' }
            if (Test-CheckPending 'fast-build') { Invoke-UnityStep 'BuildFast' 'RocketFooxball.Editor.MovementLabBuilder.BuildMovementLabFast' @('-movementLabProbePath', $script:ProbeOutputPath) $true -NoGraphics; Mark-CheckExecuted 'fast-build' } else { Mark-CheckReused 'fast-build' }
        }
        'Development' {
            if (Test-CheckPending 'fast-build') { Invoke-UnityStep 'BuildFast' 'RocketFooxball.Editor.MovementLabBuilder.BuildMovementLabFast' @('-movementLabProbePath', $script:ProbeOutputPath) $true -NoGraphics; Mark-CheckExecuted 'fast-build' } else { Mark-CheckReused 'fast-build' }
            if (Test-CheckPending 'development-bake') { Invoke-UnityStep 'DevelopmentBake' 'RocketFooxball.Editor.MovementLabBuilder.BakeMovementLabLightingDevelopment' @('-movementLabProbePath', $script:ProbeOutputPath) $true; Mark-CheckExecuted 'development-bake' } else { Mark-CheckReused 'development-bake' }
            $probeRecord = Read-ProbeContract
        }
        'ProductionPrepare' {
            if ([string]::IsNullOrWhiteSpace($SourceSha) -or [string]::IsNullOrWhiteSpace($ReviewedSha) -or [string]::IsNullOrWhiteSpace($Reviewer)) { throw 'ProductionPrepare requires SourceSha, ReviewedSha, and Reviewer.' }
            $markerScript = Join-Path $PSScriptRoot 'Write-MovementLabPreBakeReviewMarker.ps1'
            $markerArgs = @('-ProjectPath', $script:ProjectRoot, '-ProjectSha', $beforeHead, '-SourceSha', $SourceSha, '-ReviewedSha', $ReviewedSha, '-Reviewer', $Reviewer, '-Checkpoint', $Checkpoint)
            if ($ReviewReportPath.Count -gt 0) { foreach ($path in $ReviewReportPath) { $markerArgs += @('-ReviewReportPath', $path) } }
            if ($ReviewReportSha256.Count -gt 0) { foreach ($sha in $ReviewReportSha256) { $markerArgs += @('-ReviewReportSha256', $sha) } }
            if ($FindingDisposition.Count -gt 0) { foreach ($finding in $FindingDisposition) { $markerArgs += @('-FindingDisposition', $finding) } }
            $markerReused = $false
            if (Test-CheckPending 'review-marker') {
                $markerText = Invoke-ExternalScript 'ReviewMarker' $markerScript $markerArgs
                $markerResult = $markerText | ConvertFrom-Json
                Mark-CheckExecuted 'review-marker'
            } else {
                Mark-CheckReused 'review-marker'
                $markerReused = $true
                $markerResult = [ordered]@{ status = 'reused'; markerPath = (Join-Path $commonGit ('architecture-evidence\movement-lab-prebake\reviews\' + $beforeHead + '.json')); projectSha = $beforeHead; gitMutation = $false }
            }
            $markerPathForRead = Assert-DurableEvidencePath (Join-Path $script:GitCommonRoot ('architecture-evidence\movement-lab-prebake\reviews\' + $beforeHead + '.json')) 'Review marker'
            if (-not $PlanOnly -or ($markerReused -and (Test-Path -LiteralPath $markerPathForRead -PathType Leaf))) {
                $markerResult = Read-ReviewMarker $beforeHead $SourceSha $ReviewedSha
            }
            if (Test-CheckPending 'stage-probe') { Invoke-UnityStep 'Probe' 'RocketFooxball.Editor.MovementLabBuilder.ProbeMovementLabGeneratedState' @('-movementLabProbePath', $script:ProbeOutputPath) $false -NoGraphics; Mark-CheckExecuted 'stage-probe' } else { Mark-CheckReused 'stage-probe' }
            $probeRecord = Read-ProbeContract
            $productionBakePending = Test-CheckPending 'production-bake'
            if ($productionBakePending) {
                Invoke-UnityStep 'StaleAssembly' 'RocketFooxball.Editor.MovementLabBuilder.AssembleMovementLab' @('-movementLabProbePath', $script:ProbeOutputPath) $true
                if ($ForceFullComparison) {
                    $forcedBefore = Get-GeneratedHashes
                    Invoke-UnityStep 'ForcedFullNonLightingComparison' 'RocketFooxball.Editor.MovementLabBuilder.CompareMovementLabNonLightingBuilds' @('-movementLabProbePath', $script:ProbeOutputPath) $true -NoGraphics
                    $forcedAfter = Get-GeneratedHashes
                    $forcedChanged = @(Get-ChangedHashPaths $forcedBefore $forcedAfter)
                    if ($forcedChanged.Count -gt 0) { throw ('Forced full non-lighting comparison changed generated paths: ' + ($forcedChanged -join ', ')) }
                }
            }
            if (Test-CheckPending 'prebake-validate') { Invoke-UnityStep 'PreBakeValidate' 'RocketFooxball.Editor.MovementLabBuilder.ValidateMovementLabPreBake' @('-movementLabProbePath', $script:ProbeOutputPath) $false -NoGraphics; Mark-CheckExecuted 'prebake-validate' } else { Mark-CheckReused 'prebake-validate' }
            if ($script:BakeCount -gt 0) { throw 'ProductionPrepare attempted a bake before production bake step.' }
            if ($productionBakePending) {
                Invoke-UnityStep 'ProductionBake' 'RocketFooxball.Editor.MovementLabBuilder.BakeMovementLabLighting' @('-movementLabProbePath', $script:ProbeOutputPath) $true
                Mark-CheckExecuted 'production-bake'
                if (-not $PlanOnly -and $script:BakeCount -ne 1) { throw ('Production bake count must equal one: ' + $script:BakeCount) }
            } else {
                Mark-CheckReused 'production-bake'
                if ($script:BakeCount -ne 0) { throw ('Reused production bake row must not invoke a bake: ' + $script:BakeCount) }
            }
            $afterBakeHashes = Get-GeneratedHashes
            $build2Before = $afterBakeHashes
            if (Test-CheckPending 'build2-noop') { Invoke-UnityStep 'Build2NoOp' 'RocketFooxball.Editor.MovementLabBuilder.BuildMovementLab' @('-movementLabProbePath', $script:ProbeOutputPath) $true -NoGraphics; Mark-CheckExecuted 'build2-noop' } else { Mark-CheckReused 'build2-noop' }
            $build2After = Get-GeneratedHashes
            $build2Changed = @(Get-ChangedHashPaths $build2Before $build2After)
            if ($build2Changed.Count -gt 0) { throw ('Build 2 changed generated paths: ' + ($build2Changed -join ', ')) }
        }
        'ProductionValidate' {
            if ($Capture) {
                $captureScript = Join-Path $PSScriptRoot 'Capture-BrightArenaVisuals.ps1'
                $capturePending = Test-CheckPending 'capture-validator'
                if ($capturePending) { $captureOutput = Invoke-ExternalScript 'ProductionCapture' $captureScript @('-ProjectPath', $script:ProjectRoot); $captureEvidence = Copy-CaptureEvidence $captureOutput; Mark-CheckExecuted 'capture-validator' } else { Mark-CheckReused 'capture-validator'; $captureOutput = '' }
                Add-CommandRecord ([ordered]@{ label = 'CaptureManifestOutput'; tier = 'production-final'; method = $captureScript; arguments = @(); exitCode = 0; skipped = (-not $capturePending); mutatesProject = $false; logPath = $null; elapsedMs = 0; output = $captureOutput })
            } else {
                if (Test-CheckPending 'capture-validator') { Invoke-UnityStep 'ProductionValidate' 'RocketFooxball.Editor.MovementLabBuilder.ValidateMovementLab' @('-movementLabProbePath', $script:ProbeOutputPath) $false -NoGraphics; Mark-CheckExecuted 'capture-validator' } else { Mark-CheckReused 'capture-validator' }
            }
            Mark-CheckExecuted 'final-source-clean'
            $probeRecord = Read-ProbeContract
        }
    }
} finally {
    if (-not $PlanOnly) { Wait-UnityRelease 'workflow-final' | Out-Null }
}

$afterHashes = Get-GeneratedHashes
$changedGeneratedPaths = @(Get-ChangedHashPaths $beforeHashes $afterHashes)
$dirtyAfter = @(Get-NonGeneratedDirtyPaths)
if ($Mode -eq 'ProductionPrepare') {
    if ($dirtyAfter.Count -gt 0) { throw ('Production preparation changed non-generated source: ' + ($dirtyAfter -join ', ')) }
    if (($dirtyBefore -join "`n") -cne ($dirtyAfter -join "`n")) { throw 'Production preparation changed non-generated Git status.' }
}
if ($Mode -eq 'ProductionValidate' -and $dirtyBefore.Count -ne $dirtyAfter.Count) { throw 'Non-generated source status changed during production validation.' }
if ($Mode -eq 'ProductionValidate' -and $dirtyAfter.Count -gt 0) { throw ('Non-generated source became dirty during production validation: ' + ($dirtyAfter -join ', ')) }
if ($Mode -eq 'ProductionValidate' -and $changedGeneratedPaths.Count -gt 0) { throw ('Production validation changed generated paths: ' + ($changedGeneratedPaths -join ', ')) }
if ($script:BakeCount -gt 1) { throw ('Workflow bake count exceeded one: ' + $script:BakeCount) }
$afterHead = Get-HeadSha
if ($beforeHead -cne $afterHead) { throw ('Git HEAD changed during workflow: expected ' + $beforeHead + ', observed ' + $afterHead) }

$ledgerEvidencePath = Join-Path $script:EvidenceDirectory 'check-ledger.json'
$ledgerPayloadPath = Join-Path $script:EvidenceDirectory 'check-ledger-payload.json'
foreach ($row in $ledger) {
    if ($PlanOnly) { $row.status = 'deferred' }
    elseif ($script:ExecutedCheckIds.Contains([string]$row.check_id)) {
        $row.status = 'executed'
        $row.executed_sha = $beforeHead
        $row.validated_sha = $afterHead
        # Point rows at immutable payload before writing it. Final ledger then
        # records payload digest without self-referential hashing.
        $row.evidence_path = $ledgerPayloadPath
        $row.evidence_digest = $null
        $row.evidence = [ordered]@{ path = $ledgerPayloadPath; sha256 = $null }
    }
}
Write-AtomicJson $ledgerPayloadPath ([ordered]@{ schemaVersion = 1; exactSha = $afterHead; rows = @($ledger) })
$ledgerEvidenceDigest = (Get-FileHash -LiteralPath $ledgerPayloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
foreach ($row in $ledger) {
    if ($row.status -eq 'executed') {
        $row.evidence_digest = $ledgerEvidenceDigest
        $row.evidence = [ordered]@{ path = $ledgerPayloadPath; sha256 = $ledgerEvidenceDigest }
    }
}
Write-AtomicJson $ledgerEvidencePath ([ordered]@{ schemaVersion = 1; exactSha = $afterHead; rows = @($ledger) })
$ledgerFinalDigest = (Get-FileHash -LiteralPath $ledgerEvidencePath -Algorithm SHA256).Hash.ToLowerInvariant()

$result = [ordered]@{
    schemaVersion = 1
    status = 'planned' # replaced after manifest write
    mode = $Mode
    projectPath = $script:ProjectRoot
    unityVersion = $script:UnityVersion
    unityPath = $script:UnityPath
    exactSha = $afterHead
    sourceSha = $SourceSha
    reviewedSha = $ReviewedSha
    evidenceRoot = $script:EvidenceDirectory
    commands = @($script:CommandRecords.ToArray())
    elapsedMs = ([DateTime]::UtcNow - $script:WorkflowStarted).TotalMilliseconds
    probe = $probeRecord
    bakeCount = $script:BakeCount
    beforeGeneratedHashes = $beforeHashes
    afterGeneratedHashes = $afterHashes
    changedGeneratedPaths = $changedGeneratedPaths
    reviewMarker = $markerResult
    captureEvidence = $captureEvidence
    checkLedgerPath = $ledgerEvidencePath
    checkLedgerPayloadPath = $ledgerPayloadPath
    checkLedgerPayloadSha256 = $ledgerEvidenceDigest
    checkLedgerSha256 = $ledgerFinalDigest
    checkLedger = @($ledger)
    lockReleaseProof = @($script:ReleaseProof.ToArray())
    evidenceManifestPath = $null
    evidenceManifestSha256 = $null
    gitMutation = $false
    noGitMutation = $true
}
$result.status = if ($PlanOnly) { 'planned' } else { 'complete' }
$resultPath = Join-Path $script:EvidenceDirectory 'workflow-result.json'
Write-AtomicJson $resultPath $result
$manifest = [ordered]@{
    schemaVersion = 1
    workflow = 'MovementLab'
    mode = $Mode
    exactSha = $afterHead
    resultPath = $resultPath
    commands = @($script:CommandRecords.ToArray())
    elapsedMs = $result.elapsedMs
    probe = $probeRecord
    bakeCount = $script:BakeCount
    changedGeneratedPaths = $changedGeneratedPaths
    captureEvidence = $captureEvidence
    checkLedgerPath = $ledgerEvidencePath
    checkLedgerPayloadPath = $ledgerPayloadPath
    checkLedgerPayloadSha256 = $ledgerEvidenceDigest
    checkLedgerSha256 = $ledgerFinalDigest
    checkLedger = @($ledger)
    lockReleaseProof = @($script:ReleaseProof.ToArray())
    gitMutation = $false
}
$manifestPath = Join-Path $script:EvidenceDirectory 'evidence-manifest.json'
Write-AtomicJson $manifestPath $manifest
$manifestDigest = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()

$result.outputManifestPath = $manifestPath
$result.evidenceManifestSha256 = $manifestDigest
$result.evidenceManifestPath = $manifestPath
Write-AtomicJson $resultPath $result
[ordered]@{
    schemaVersion = 1
    status = $result.status
    mode = $Mode
    exactSha = $afterHead
    evidenceManifestPath = $manifestPath
    evidenceManifestSha256 = $manifestDigest
    bakeCount = $script:BakeCount
    changedGeneratedPaths = $changedGeneratedPaths
    gitMutation = $false
} | ConvertTo-Json -Depth 14
