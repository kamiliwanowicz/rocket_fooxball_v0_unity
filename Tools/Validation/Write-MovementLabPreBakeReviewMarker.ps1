[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,
    [Parameter(Mandatory = $true)]
    [Alias('GitSha', 'ExpectedProjectSha')]
    [string]$ProjectSha,
    [Parameter(Mandatory = $true)]
    [string]$SourceSha,
    [Parameter(Mandatory = $true)]
    [string]$ReviewedSha,
    [Parameter(Mandatory = $true)]
    [string]$Reviewer,
    [string]$ReviewerIdentity,
    [Parameter(Mandatory = $true)]
    [Alias('CheckpointId')]
    [string]$Checkpoint,
    [string]$ReviewCheckpoint,
    [Alias('ReportPath', 'ReviewReportPaths')]
    [string[]]$ReviewReportPath = @(),
    [Alias('ReportSha256', 'ReviewReportSha256s')]
    [string[]]$ReviewReportSha256 = @(),
    [string[]]$FindingDisposition = @(),
    [string]$FindingDispositionPath,
    [string]$OutputPath,
    [string[]]$GeneratedPath = @(),
    [bool]$SourceReviewCompleted = $true,
    [bool]$CriticalHighFixesApplied = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) {
        throw 'Path values must be non-empty and single-line.'
    }
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-DurableEvidencePath {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Label)
    $full = Get-FullPath $Path
    $projectRoot = $script:ProjectRoot.TrimEnd('\')
    if ($full.Equals($projectRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $full.StartsWith($projectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw ($Label + ' must be outside project: ' + $full)
    }
    $commonRoot = $script:GitCommonRoot.TrimEnd('\')
    if ($full.Equals($commonRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not $full.StartsWith($commonRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw ($Label + ' must remain inside Git-common evidence destination: ' + $full)
    }
    if ($full.IndexOf('\Library\', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $full.IndexOf('\Temp\', [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw ($Label + ' cannot use Library or Temp: ' + $full)
    }
    return $full
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

function Assert-ExactSha {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Value)
    if ($Value -notmatch '^[0-9a-fA-F]{40}$') {
        throw ($Name + ' must be an exact 40-character Git SHA.')
    }
    return $Value.ToLowerInvariant()
}

function Assert-Ancestor {
    param([Parameter(Mandatory = $true)][string]$Ancestor, [Parameter(Mandatory = $true)][string]$Descendant, [Parameter(Mandatory = $true)][string]$Label)
    $priorErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & git -C $script:ProjectRoot merge-base --is-ancestor $Ancestor $Descendant 2>$null
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $priorErrorAction
    }
    if ($exitCode -ne 0) {
        throw ($Label + ' is not an ancestor of project SHA.')
    }
}

function Test-GeneratedPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $normalized = $Path.Replace('\', '/').TrimStart('/')
    $roots = @(
        'Assets/_Game/Generated',
        'Assets/_Game/Lighting',
        'Assets/_Game/Scenes/MovementLab.unity',
        'Assets/_Game/Scenes/MovementLab.unity.meta',
        'Assets/_Game/Scenes/MovementLab',
        'Assets/_Game/Scenes/MovementLab/LightingData.asset',
        'Assets/_Game/Scenes/MovementLab/LightingData.asset.meta'
    ) + @($script:GeneratedPathValues | ForEach-Object { ([string]$_).Replace('\', '/').TrimStart('/') })
    foreach ($root in $roots) {
        if ([string]::IsNullOrWhiteSpace($root)) { continue }
        if ($normalized.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or
            $normalized.StartsWith($root.TrimEnd('/') + '/', [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
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

function Assert-ReviewReport {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$ExpectedSha)
    $full = Assert-DurableEvidencePath $Path 'Review report'
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw ('Review report missing: ' + $full) }
    if ($ExpectedSha -notmatch '^[0-9a-fA-F]{64}$') { throw ('Review report SHA-256 invalid: ' + $ExpectedSha) }
    $actual = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha.ToLowerInvariant()) {
        throw ('Review report SHA-256 mismatch: ' + $full)
    }
    return [ordered]@{
        path = $full
        sha256 = $actual
        bytes = (Get-Item -LiteralPath $full).Length
    }
}

function Get-FindingDispositions {
    $values = New-Object System.Collections.Generic.List[object]
    if (-not [string]::IsNullOrWhiteSpace($script:FindingDispositionPath)) {
        $full = Assert-DurableEvidencePath $script:FindingDispositionPath 'Finding disposition path'
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw ('Finding disposition file missing: ' + $full) }
        $parsed = Get-Content -Raw -LiteralPath $full | ConvertFrom-Json
        foreach ($entry in @($parsed)) {
            if ($null -eq $entry -or [string]::IsNullOrWhiteSpace([string]$entry.id) -or [string]::IsNullOrWhiteSpace([string]$entry.disposition)) {
                throw 'Finding disposition entries require id and disposition.'
            }
            foreach ($field in @([string]$entry.id, [string]$entry.disposition)) {
                if ($field.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) { throw 'Finding disposition fields must be one line.' }
            }
            $values.Add([ordered]@{ id = [string]$entry.id; disposition = [string]$entry.disposition })
        }
    }
    foreach ($raw in @($script:FindingDisposition)) {
        if ([string]::IsNullOrWhiteSpace($raw)) { continue }
        if ($raw.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) { throw 'Finding disposition must be one line.' }
        $parts = @($raw -split '=', 2)
        if ($parts.Count -ne 2 -or [string]::IsNullOrWhiteSpace($parts[0]) -or [string]::IsNullOrWhiteSpace($parts[1])) {
            throw ('Finding disposition must use id=disposition: ' + $raw)
        }
        $values.Add([ordered]@{ id = $parts[0].Trim(); disposition = $parts[1].Trim() })
    }
    return @($values.ToArray())
}

function Write-AtomicUtf8 {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Content)
    $directory = Split-Path -Parent $Path
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporary = Join-Path $directory ('.movement-lab-review-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $backup = $Path + '.backup-' + [Guid]::NewGuid().ToString('N')
    try {
        $bytes = New-Object System.Text.UTF8Encoding($false)
        $payload = $bytes.GetBytes($Content)
        $stream = New-Object System.IO.FileStream($temporary, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None, 4096, [System.IO.FileOptions]::WriteThrough)
        try {
            $stream.Write($payload, 0, $payload.Length)
            $stream.Flush($true)
        } finally {
            $stream.Dispose()
        }
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            [System.IO.File]::Replace($temporary, $Path, $backup)
            if (Test-Path -LiteralPath $backup) { [System.IO.File]::Delete($backup) }
        } else {
            [System.IO.File]::Move($temporary, $Path)
        }
    } finally {
        if (Test-Path -LiteralPath $temporary) { [System.IO.File]::Delete($temporary) }
        if (Test-Path -LiteralPath $backup) { [System.IO.File]::Delete($backup) }
    }
}

$script:ProjectRoot = Get-FullPath $ProjectPath
$script:GeneratedPathValues = @($GeneratedPath)
if (-not (Test-Path -LiteralPath $script:ProjectRoot -PathType Container)) { throw ('Project path not found: ' + $script:ProjectRoot) }
$top = Get-FullPath (Invoke-Git @('rev-parse', '--show-toplevel'))
if (-not $top.Equals($script:ProjectRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw ('Project path is not Git worktree root: ' + $script:ProjectRoot)
}

$projectHead = (Invoke-Git @('rev-parse', '--verify', 'HEAD')).ToLowerInvariant()
$projectShaValue = Assert-ExactSha 'ProjectSha' $ProjectSha
$sourceShaValue = Assert-ExactSha 'SourceSha' $SourceSha
$reviewedShaValue = Assert-ExactSha 'ReviewedSha' $ReviewedSha
if ($projectHead -ne $projectShaValue) { throw ('Project HEAD mismatch: expected ' + $projectShaValue + ', observed ' + $projectHead) }
foreach ($sha in @($sourceShaValue, $reviewedShaValue)) {
    $resolved = (Invoke-Git @('rev-parse', '--verify', ($sha + '^{commit}'))).ToLowerInvariant()
    if ($resolved -ne $sha) { throw ('SHA is not a commit object: ' + $sha) }
}
Assert-Ancestor $sourceShaValue $projectShaValue 'Source SHA'
Assert-Ancestor $reviewedShaValue $projectShaValue 'Reviewed SHA'
Assert-Ancestor $sourceShaValue $reviewedShaValue 'Source SHA must be ancestor of reviewed SHA'

$dirty = @(Get-NonGeneratedDirtyPaths)
if ($dirty.Count -gt 0) { throw ('Non-generated source is dirty: ' + ($dirty -join ', ')) }
if (-not $SourceReviewCompleted) { throw 'Source review must be completed before writing a pre-bake marker.' }
if (-not $CriticalHighFixesApplied) { throw 'Critical/High fixes must be applied before writing a pre-bake marker.' }

$commonRaw = Invoke-Git @('rev-parse', '--git-common-dir')
$commonGit = if ([System.IO.Path]::IsPathRooted($commonRaw)) { Get-FullPath $commonRaw } else { Get-FullPath (Join-Path $script:ProjectRoot $commonRaw) }
if (-not (Test-Path -LiteralPath $commonGit -PathType Container)) { throw ('Git-common destination missing: ' + $commonGit) }
$script:GitCommonRoot = $commonGit
$projectRoot = $script:ProjectRoot.TrimEnd('\')
if ($commonGit.Equals($projectRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $commonGit.StartsWith($projectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw ('Git-common evidence destination must be outside project: ' + $commonGit)
}
$expectedPath = Get-FullPath (Join-Path $commonGit ('architecture-evidence\movement-lab-prebake\reviews\' + $projectShaValue + '.json'))
$markerPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) { $expectedPath } else { Get-FullPath $OutputPath }
if (-not $markerPath.Equals($expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw ('Review marker path must use expected Git-common destination: ' + $expectedPath)
}

foreach ($rawIdentity in @(@('Reviewer', $Reviewer), @('ReviewerIdentity', $ReviewerIdentity), @('Checkpoint', $Checkpoint), @('ReviewCheckpoint', $ReviewCheckpoint))) {
    if ($null -ne $rawIdentity[1] -and ([string]$rawIdentity[1]).IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) {
        throw ($rawIdentity[0] + ' must be one line.')
    }
}
$reviewerValue = if ([string]::IsNullOrWhiteSpace($Reviewer)) { '' } else { $Reviewer.Trim() }
$reviewerIdentityValue = if ([string]::IsNullOrWhiteSpace($ReviewerIdentity)) { $reviewerValue } else { $ReviewerIdentity.Trim() }
$checkpointValue = if ([string]::IsNullOrWhiteSpace($Checkpoint)) { '' } else { $Checkpoint.Trim() }
$reviewCheckpointValue = if ([string]::IsNullOrWhiteSpace($ReviewCheckpoint)) { $checkpointValue } else { $ReviewCheckpoint.Trim() }
foreach ($field in @(@('Reviewer', $reviewerValue), @('ReviewerIdentity', $reviewerIdentityValue), @('Checkpoint', $checkpointValue), @('ReviewCheckpoint', $reviewCheckpointValue))) {
    $fieldValue = [string]$field[1]
    if ([string]::IsNullOrWhiteSpace($fieldValue)) { throw ($field[0] + ' is required and must be one line.') }
}

$reviewReportPaths = @($ReviewReportPath)
$reviewReportShas = @($ReviewReportSha256)
if ($reviewReportPaths.Count -ne $reviewReportShas.Count) {
    throw 'ReviewReportPath and ReviewReportSha256 counts must match.'
}
if ($reviewReportPaths.Count -eq 0) { throw 'At least one durable review report is required.' }
$reportRecords = New-Object System.Collections.Generic.List[object]
for ($index = 0; $index -lt $reviewReportPaths.Count; $index++) {
    $reportRecords.Add((Assert-ReviewReport $reviewReportPaths[$index] $reviewReportShas[$index]))
}
$dispositions = @(Get-FindingDispositions)
$dispositionIds = @($dispositions | ForEach-Object { [string]$_.id })
if (@($dispositionIds | Sort-Object -Unique).Count -ne $dispositionIds.Count) { throw 'Finding disposition IDs must be unique.' }

# Recheck source boundary immediately before marker write; review evidence must
# bind the same clean commit observed during validation.
$latestHead = (Invoke-Git @('rev-parse', '--verify', 'HEAD')).ToLowerInvariant()
if ($latestHead -ne $projectShaValue) { throw ('Project HEAD changed before marker write: expected ' + $projectShaValue + ', observed ' + $latestHead) }
$dirty = @(Get-NonGeneratedDirtyPaths)
if ($dirty.Count -gt 0) { throw ('Non-generated source became dirty before marker write: ' + ($dirty -join ', ')) }
foreach ($report in @($reportRecords.ToArray())) { Assert-ReviewReport $report.path $report.sha256 | Out-Null }

$marker = [ordered]@{
    schemaVersion = 1
    gitSha = $projectShaValue
    projectSha = $projectShaValue
    sourceSha = $sourceShaValue
    reviewedSha = $reviewedShaValue
    sourceReviewCompleted = [bool]$SourceReviewCompleted
    criticalHighFixesApplied = [bool]$CriticalHighFixesApplied
    reviewer = $reviewerValue
    reviewerIdentity = $reviewerIdentityValue
    checkpoint = $checkpointValue
    checkpointId = $checkpointValue
    reviewCheckpoint = $reviewCheckpointValue
    reviewReports = @($reportRecords.ToArray())
    reviewReportPaths = @($reportRecords.ToArray() | ForEach-Object { $_.path })
    reviewReportSha256s = @($reportRecords.ToArray() | ForEach-Object { $_.sha256 })
    findingDispositions = @($dispositions)
    completedUtc = [DateTime]::UtcNow.ToString('O')
}
$json = ($marker | ConvertTo-Json -Depth 12) + "`n"
Write-AtomicUtf8 $markerPath $json
$markerHash = (Get-FileHash -LiteralPath $markerPath -Algorithm SHA256).Hash.ToLowerInvariant()

[ordered]@{
    schemaVersion = 1
    status = 'created'
    markerPath = $markerPath
    markerSha256 = $markerHash
    projectSha = $projectShaValue
    sourceSha = $sourceShaValue
    reviewedSha = $reviewedShaValue
    reviewer = $reviewerValue
    checkpoint = $checkpointValue
    reportCount = $reportRecords.Count
    findingDispositionCount = $dispositions.Count
    gitMutation = $false
} | ConvertTo-Json -Depth 12
