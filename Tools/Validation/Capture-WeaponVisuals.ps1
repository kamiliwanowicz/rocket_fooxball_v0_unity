[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][Alias('weaponCaptureEvidenceRoot')][string]$EvidenceRoot,
    [Parameter(Mandatory = $true)][Alias('weaponCaptureAttemptId')][string]$AttemptId,
    [Parameter(Mandatory = $true)][ValidateSet('Rocket', 'Shotgun')][Alias('weaponCaptureWeapon')][string]$Weapon,
    [Parameter(Mandatory = $true)][ValidateSet('Fast', 'Persisted')][Alias('weaponCaptureMode')][string]$Mode,
    [Parameter(Mandatory = $true)][Alias('weaponCaptureReferenceManifest', 'ReferenceManifestPath')][string]$ReferenceManifest,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [int]$TimeoutSeconds = 900,
    [Alias('ExpectedHeadSha')][string]$ExpectedSourceSha = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:UnityVersion = '6000.5.6f1'
$script:ProjectRoot = $null
$script:CanonicalProjectRoot = $null
$script:GitCommonRoot = $null
$script:EvidenceDirectory = $null
$script:WrapperDirectory = $null
$script:LeasePath = $null
$script:LeaseToken = $null
$script:LeaseAcquired = $false
$script:LeaseReleaseProofPath = $null
$script:Reference = $null
$script:ExpectedSourceSha = $null
$script:ReleaseProof = New-Object System.Collections.Generic.List[object]

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) {
        throw 'Path values must be non-empty and single-line.'
    }
    return [IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Assert-ShortWorkspacePath {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Label)
    $full = Get-FullPath $Path
    if (-not $full.StartsWith('C:\wt\', [StringComparison]::OrdinalIgnoreCase)) {
        throw ($Label + ' must be an existing C:\wt descendant: ' + $full)
    }
    return $full
}

function Assert-OutsideProject {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Label)
    $full = Get-FullPath $Path
    $project = $script:ProjectRoot.TrimEnd('\')
    if ($full.Equals($project, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($project + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw ($Label + ' must be outside the project: ' + $full)
    }
    if ($full.IndexOf('\Library\', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $full.IndexOf('\Temp\', [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw ($Label + ' cannot use Library or Temp: ' + $full)
    }
    return $full
}

function Get-CanonicalPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $full = Get-FullPath $Path
    $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw ('Project path may not be a junction or alias: ' + $full)
    }
    return ([IO.Path]::GetFullPath((Resolve-Path -LiteralPath $item.FullName -ErrorAction Stop).Path)).TrimEnd('\')
}

function Get-StringSha256 {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
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

function Get-ProjectUnityProcesses {
    $normalized = @($script:ProjectRoot.TrimEnd('\').ToLowerInvariant())
    if (-not [string]::IsNullOrWhiteSpace($script:CanonicalProjectRoot)) {
        $normalized += $script:CanonicalProjectRoot.TrimEnd('\').ToLowerInvariant()
    }
    $normalized = @($normalized | Sort-Object -Unique)
    $all = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe' OR Name = 'LightBaker.exe' OR Name = 'UnityShaderCompiler.exe'")
    return @($all | Where-Object {
        $commandLine = [string]$_.CommandLine
        if ([string]::IsNullOrWhiteSpace($commandLine)) { return $true }
        return (@($normalized | Where-Object { $commandLine.ToLowerInvariant().Contains($_) }).Count -gt 0)
    })
}

function Get-LockPaths {
    $root = if ([string]::IsNullOrWhiteSpace($script:CanonicalProjectRoot)) { $script:ProjectRoot } else { $script:CanonicalProjectRoot }
    return @(
        (Join-Path $root 'Temp\UnityLockfile'),
        (Join-Path $root 'Library\UnityLockfile')
    )
}

function Assert-NoProjectProcessOrLock {
    $processes = @(Get-ProjectUnityProcesses)
    if ($processes.Count -gt 0) { throw ('Active Unity process already owns project: ' + $script:ProjectRoot) }
    foreach ($lockPath in @(Get-LockPaths)) {
        if (Test-Path -LiteralPath $lockPath) { throw ('Unity project lock exists: ' + $lockPath) }
    }
}

function Wait-ProjectRelease {
    param([Parameter(Mandatory = $true)][string]$Label)
    $seconds = [Math]::Min([Math]::Max($TimeoutSeconds, 60), 3600)
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $active = @(Get-ProjectUnityProcesses)
        $locks = @(Get-LockPaths | Where-Object { Test-Path -LiteralPath $_ })
        if ($active.Count -eq 0 -and $locks.Count -eq 0) {
            $script:ReleaseProof.Add([ordered]@{ label = $Label; releasedUtc = [DateTime]::UtcNow.ToString('O'); processCount = 0; lockCount = 0 })
            return
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw ('Unity process or project lock remained after ' + $Label + '.')
}

function Get-GitSha {
    $sha = (Invoke-Git @('rev-parse', '--verify', 'HEAD')).ToLowerInvariant()
    if ($sha -notmatch '^[0-9a-f]{40}$') { throw 'Unable to resolve exact Git HEAD SHA.' }
    return $sha
}

function Get-ProjectStatus {
    # Capture is valid only at an exact committed source boundary. Keep this
    # unscoped so changes outside the product roots cannot hide in the index
    # or worktree while Unity is running.
    return Invoke-Git @('status', '--porcelain=v1', '--untracked-files=all')
}

function Get-GitCommonRoot {
    $raw = (Invoke-Git @('rev-parse', '--git-common-dir')).Trim()
    if ([string]::IsNullOrWhiteSpace($raw)) { throw 'Git common directory is empty.' }
    $full = if ([IO.Path]::IsPathRooted($raw)) { Get-FullPath $raw } else { Get-FullPath (Join-Path $script:ProjectRoot $raw) }
    if (-not (Test-Path -LiteralPath $full -PathType Container)) { throw ('Git common directory is not a directory: ' + $full) }
    return Get-CanonicalPath $full
}

function Get-IndexDigest {
    # `git ls-files` resolves the linked-worktree .git file and reads its index
    # without assuming that the project has an in-tree .git directory.
    return Get-StringSha256 (Invoke-Git @('ls-files', '--stage', '--full-name'))
}

function Get-RelativeProjectPath {
    param([Parameter(Mandatory = $true)][string]$FullPath)
    $root = $script:ProjectRoot.TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath($FullPath)
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw ('Project path escaped root: ' + $full) }
    return $full.Substring($root.Length).Replace('\', '/')
}

function Get-ProductFileSnapshot {
    $records = [ordered]@{}
    foreach ($relativeRoot in @('Assets', 'Packages', 'ProjectSettings', 'Tools')) {
        $root = Join-Path $script:ProjectRoot $relativeRoot
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw ('Authoritative product root missing: ' + $root) }
        $rootItem = Get-Item -LiteralPath $root -Force -ErrorAction Stop
        if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Authoritative product root may not be a reparse point: ' + $root) }
        foreach ($directory in @(Get-ChildItem -LiteralPath $root -Directory -Recurse -Force -ErrorAction Stop)) {
            if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Authoritative product directory may not be a reparse point: ' + $directory.FullName) }
        }
        foreach ($item in @(Get-ChildItem -LiteralPath $root -File -Recurse -Force -ErrorAction Stop | Sort-Object FullName)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Authoritative product file may not be a reparse point: ' + $item.FullName) }
            $relative = Get-RelativeProjectPath $item.FullName
            $records[$relative] = [ordered]@{ bytes = [int64]$item.Length; sha256 = Get-Hash $item.FullName }
        }
    }
    $manifestKey = 'Assets/_Game/Generated/MovementLabBuildManifest.json'
    if (-not $records.Contains($manifestKey)) { throw ('Authoritative generated manifest is missing from product snapshot: ' + $manifestKey) }
    $lines = @($records.Keys | Sort-Object | ForEach-Object { $record = $records[$_]; [string]$_ + '|' + [string]$record.bytes + '|' + [string]$record.sha256 })
    return [pscustomobject]@{ files = $records; sha256 = Get-StringSha256 ($lines -join "`n") }
}

function Get-ReferenceSnapshot {
    param([Parameter(Mandatory = $true)]$Reference)
    $records = [ordered]@{}
    $paths = New-Object System.Collections.Generic.List[string]
    $paths.Add([string]$Reference.path) | Out-Null
    foreach ($entry in @($Reference.entries)) {
        $paths.Add([string]$entry.originalPath) | Out-Null
        $paths.Add([string]$entry.copiedEvidencePath) | Out-Null
    }
    foreach ($path in @($paths | Sort-Object -Unique)) {
        $full = [IO.Path]::GetFullPath($path)
        $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Reference file may not be a reparse point: ' + $full) }
        $records[$full] = [ordered]@{ bytes = [int64]$item.Length; sha256 = Get-Hash $full }
    }
    $lines = @($records.Keys | Sort-Object | ForEach-Object { $record = $records[$_]; [string]$_ + '|' + [string]$record.bytes + '|' + [string]$record.sha256 })
    return [pscustomobject]@{ files = $records; sha256 = Get-StringSha256 ($lines -join "`n") }
}

function Assert-SnapshotEqual {
    param([Parameter(Mandatory = $true)]$Before, [Parameter(Mandatory = $true)]$After, [Parameter(Mandatory = $true)][string]$Label)
    if ([string]$Before.sha256 -cne [string]$After.sha256) {
        $keys = @($Before.files.Keys + $After.files.Keys | Sort-Object -Unique)
        $changed = New-Object System.Collections.Generic.List[string]
        foreach ($key in $keys) {
            $left = if ($Before.files.Contains($key)) { [string]$Before.files[$key].bytes + '|' + [string]$Before.files[$key].sha256 } else { '__MISSING__' }
            $right = if ($After.files.Contains($key)) { [string]$After.files[$key].bytes + '|' + [string]$After.files[$key].sha256 } else { '__MISSING__' }
            if ($left -cne $right) { $changed.Add([string]$key) | Out-Null }
        }
        throw ($Label + ' content digest changed: ' + (($changed | Select-Object -First 12) -join ', '))
    }
}

function Get-CaptureSourceSnapshot {
    $head = Get-GitSha
    if (-not [string]::IsNullOrWhiteSpace($script:ExpectedSourceSha) -and $head -cne $script:ExpectedSourceSha) {
        throw ('Capture HEAD does not match expected committed source SHA: expected ' + $script:ExpectedSourceSha + ', observed ' + $head)
    }
    $status = Get-ProjectStatus
    if (-not [string]::IsNullOrWhiteSpace($status)) {
        throw ('Capture requires a clean committed worktree before Unity; observed Git status: ' + $status)
    }
    $product = Get-ProductFileSnapshot
    $reference = Get-ReferenceSnapshot $script:Reference
    return [pscustomobject]@{ sourceSha = $head; indexSha256 = Get-IndexDigest; status = $status; product = $product; reference = $reference }
}

function Get-CurrentProcessStartUtc {
    return (Get-Process -Id $PID -ErrorAction Stop).StartTime.ToUniversalTime().ToString('O')
}

function Read-LeaseRecord {
    param([Parameter(Mandatory = $true)][string]$Path)
    try { return (Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -ErrorAction Stop) }
    catch { throw ('Project lease is malformed; refusing stale recovery: ' + $Path) }
}

function Assert-LeasePath {
    if ([string]::IsNullOrWhiteSpace($script:GitCommonRoot) -or [string]::IsNullOrWhiteSpace($script:CanonicalProjectRoot)) {
        throw 'Canonical project and Git-common roots are required for lease handling.'
    }
    $leaseRoot = Join-Path $script:GitCommonRoot 'movement-lab-proof\leases'
    $leaseRootItem = Get-Item -LiteralPath $leaseRoot -Force -ErrorAction Stop
    if (($leaseRootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Lease destination may not be a junction or alias: ' + $leaseRoot) }
    $canonicalLeaseRoot = Get-CanonicalPath $leaseRoot
    if (-not $canonicalLeaseRoot.StartsWith($script:GitCommonRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw ('Lease destination escaped canonical Git-common root: ' + $canonicalLeaseRoot)
    }
    return $leaseRoot
}

function Acquire-ProjectLease {
    $script:CanonicalProjectRoot = Get-CanonicalPath $script:ProjectRoot
    $leaseRoot = Join-Path $script:GitCommonRoot 'movement-lab-proof\leases'
    [IO.Directory]::CreateDirectory($leaseRoot) | Out-Null
    $null = Assert-LeasePath
    $leaseName = Get-StringSha256 $script:CanonicalProjectRoot
    $script:LeasePath = Join-Path $leaseRoot ($leaseName + '.lease')
    $script:LeaseToken = [Guid]::NewGuid().ToString('N')
    $record = [ordered]@{
        schemaVersion = 1
        leaseToken = $script:LeaseToken
        canonicalProjectRoot = $script:CanonicalProjectRoot
        ownerPid = [int]$PID
        ownerProcessStartUtc = Get-CurrentProcessStartUtc
        acquiredUtc = [DateTime]::UtcNow.ToString('O')
    }
    $json = ($record | ConvertTo-Json -Depth 8) + "`n"
    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        try {
            $stream = New-Object IO.FileStream($script:LeasePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                $bytes = [Text.Encoding]::UTF8.GetBytes($json)
                $stream.Write($bytes, 0, $bytes.Length)
                $stream.Flush($true)
            } finally { $stream.Dispose() }
            $script:LeaseAcquired = $true
            return $true
        } catch [IO.IOException] {
            if (-not (Test-Path -LiteralPath $script:LeasePath -PathType Leaf)) { throw }
            $existing = Read-LeaseRecord $script:LeasePath
            $validIdentity = $existing.schemaVersion -eq 1 -and [string]$existing.canonicalProjectRoot -eq $script:CanonicalProjectRoot -and
                [string]$existing.leaseToken -match '^[A-Za-z0-9]{32}$' -and [int]$existing.ownerPid -gt 0 -and
                [string]$existing.ownerProcessStartUtc -match '^\d{4}-\d{2}-\d{2}T.+Z$'
            if (-not $validIdentity) { throw ('Project lease identity is invalid; refusing recovery: ' + $script:LeasePath) }
            $owner = $null
            try { $owner = Get-Process -Id ([int]$existing.ownerPid) -ErrorAction Stop }
            catch {
                if ($_.Exception.Message -notmatch '(?i)no process|cannot find') { throw ('Unable to prove lease owner is dead; refusing stale recovery: ' + $script:LeasePath) }
            }
            if ($null -ne $owner -and $owner.StartTime.ToUniversalTime().ToString('O') -eq [string]$existing.ownerProcessStartUtc) {
                throw ('Project lease is held by live PID ' + $existing.ownerPid + ': ' + $script:CanonicalProjectRoot)
            }
            # Stale recovery is deliberately fail-closed. A read/re-read/delete
            # sequence cannot atomically bind deletion to the stale token: a
            # contender may acquire the path between those operations, leaving
            # this process able to delete another owner's lease. Recovery must
            # therefore be an explicit operator action after verifying the owner.
            throw ('Project lease is stale; refusing automatic recovery: ' + $script:LeasePath)
        }
    }
    throw ('Unable to acquire canonical project lease: ' + $script:LeasePath)
}

function Remove-ZeroByteUnityLockSentinel {
    param([Parameter(Mandatory = $true)][string]$Path)
    $expected = @(Get-LockPaths)
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if (@($expected | Where-Object { [IO.Path]::GetFullPath($_).TrimEnd('\').Equals($full, [StringComparison]::OrdinalIgnoreCase) }).Count -ne 1) {
        throw ('Unity lock cleanup path is outside the canonical set: ' + $Path)
    }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { return $false }
    $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or $item.Length -ne 0) {
        throw ('Unity lock sentinel is not a zero-byte regular file: ' + $full)
    }
    if (@(Get-ProjectUnityProcesses).Count -gt 0) { throw 'Unity process appeared during lock release.' }
    [IO.File]::Delete($full)
    if (Test-Path -LiteralPath $full) { throw ('Unity lock sentinel remained after release: ' + $full) }
    return $true
}

function Wait-ProjectRelease {
    param([Parameter(Mandatory = $true)][string]$Label)
    $seconds = [Math]::Min([Math]::Max($TimeoutSeconds, 60), 3600)
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $active = @(Get-ProjectUnityProcesses)
        if ($active.Count -eq 0) {
            foreach ($lockPath in @(Get-LockPaths)) {
                if (Test-Path -LiteralPath $lockPath) { Remove-ZeroByteUnityLockSentinel $lockPath | Out-Null }
            }
        }
        $activeAfter = @(Get-ProjectUnityProcesses)
        $locks = @(Get-LockPaths | Where-Object { Test-Path -LiteralPath $_ })
        if ($activeAfter.Count -eq 0 -and $locks.Count -eq 0) {
            $script:ReleaseProof.Add([ordered]@{ label = $Label; releasedUtc = [DateTime]::UtcNow.ToString('O'); processCount = 0; lockCount = 0 })
            return $true
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw ('Unity process or project lock remained after ' + $Label + '.')
}

function Release-ProjectLease {
    if (-not $script:LeaseAcquired -or [string]::IsNullOrWhiteSpace($script:LeasePath)) { return }
    $proof = [ordered]@{ path = $script:LeasePath; leaseToken = $script:LeaseToken; canonicalProjectRoot = $script:CanonicalProjectRoot; releasedUtc = $null; verifiedAbsent = $false }
    try {
        Wait-ProjectRelease 'weapon-capture-lease-release' | Out-Null
        if (-not (Test-Path -LiteralPath $script:LeasePath -PathType Leaf)) { throw 'Project lease disappeared before release.' }
        $record = Read-LeaseRecord $script:LeasePath
        if ([string]$record.leaseToken -ne $script:LeaseToken -or [string]$record.canonicalProjectRoot -ne $script:CanonicalProjectRoot) { throw 'Project lease identity changed before release.' }
        Remove-Item -LiteralPath $script:LeasePath -Force -ErrorAction Stop
        if (Test-Path -LiteralPath $script:LeasePath) { throw 'Project lease remained after release.' }
        $proof.releasedUtc = [DateTime]::UtcNow.ToString('O')
        $proof.verifiedAbsent = $true
        $script:ReleaseProof.Add($proof)
    } catch {
        $script:ReleaseProof.Add($proof)
        throw
    } finally {
        $script:LeaseAcquired = $false
    }
}

function Get-Hash {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw ('Required file missing: ' + $Path) }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Property {
    param([AllowNull()]$Object, [Parameter(Mandatory = $true)][string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Assert-ExactJsonProperties {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )
    if ($null -eq $Object -or $Object -is [string] -or $Object -is [ValueType]) {
        throw ($Label + ' must be a JSON object.')
    }
    $actual = @($Object.PSObject.Properties | ForEach-Object { [string]$_.Name })
    if ($actual.Count -ne $Expected.Count -or
        @($actual | Where-Object { $Expected -cnotcontains $_ }).Count -gt 0 -or
        @($Expected | Where-Object { $actual -cnotcontains $_ }).Count -gt 0) {
        throw ($Label + ' has unexpected fields; expected exactly: ' + ($Expected -join ', '))
    }
}

function Resolve-ReferencePath {
    param([Parameter(Mandatory = $true)][string]$Value, [Parameter(Mandatory = $true)][string]$ManifestPath)
    if ([IO.Path]::IsPathRooted($Value)) { return [IO.Path]::GetFullPath($Value) }
    $normalized = $Value.Replace('/', '\')
    if ($normalized.StartsWith('Assets\', [StringComparison]::OrdinalIgnoreCase) -or
        $normalized.StartsWith('graphics references\', [StringComparison]::OrdinalIgnoreCase)) {
        return [IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $normalized))
    }
    return [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $ManifestPath) $normalized))
}

function Read-ReferenceManifest {
    param([Parameter(Mandatory = $true)][string]$Path)
    $full = Get-FullPath $Path
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw ('Reference manifest missing: ' + $full) }
    $raw = [IO.File]::ReadAllText($full)
    try { $manifest = $raw | ConvertFrom-Json -ErrorAction Stop } catch { throw ('Reference manifest JSON is invalid: ' + $_.Exception.Message) }
    Assert-ExactJsonProperties $manifest @('schemaVersion', 'references') 'Reference manifest'
    if ([int](Get-Property $manifest 'schemaVersion') -ne 1) { throw 'Reference manifest schemaVersion must equal 1.' }
    $references = Get-Property $manifest 'references'
    if ($null -eq $references -or $references -is [string]) { throw 'Reference manifest references must be an array.' }
    $entries = @($references)
    if ($entries.Count -ne 3) { throw 'Reference manifest must contain exactly three schema-1 references.' }
    $expectedIds = @('game-bright', 'game-dark', 'quake-hires')
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $records = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $entries) {
        $entryLabel = 'Reference manifest reference'
        Assert-ExactJsonProperties $entry @('logicalId', 'originalPath', 'copiedEvidencePath', 'byteLength', 'sha256') $entryLabel
        $logicalId = [string](Get-Property $entry 'logicalId')
        if (@($expectedIds | Where-Object { $_ -ceq $logicalId }).Count -ne 1 -or -not $seen.Add($logicalId)) {
            throw ('Reference manifest logicalId is invalid or duplicated: ' + $logicalId)
        }
        $original = [string](Get-Property $entry 'originalPath')
        $copiedEvidence = [string](Get-Property $entry 'copiedEvidencePath')
        $byteLengthValue = Get-Property $entry 'byteLength'
        if ($byteLengthValue -isnot [byte] -and $byteLengthValue -isnot [sbyte] -and
            $byteLengthValue -isnot [int16] -and $byteLengthValue -isnot [uint16] -and
            $byteLengthValue -isnot [int32] -and $byteLengthValue -isnot [uint32] -and
            $byteLengthValue -isnot [int64] -and $byteLengthValue -isnot [uint64]) {
            throw ('Reference manifest byteLength must be an integer: ' + $logicalId)
        }
        $bytes = [int64]$byteLengthValue
        $sha = [string](Get-Property $entry 'sha256')
        if ([string]::IsNullOrWhiteSpace($original) -or [string]::IsNullOrWhiteSpace($copiedEvidence) -or $bytes -le 0 -or $sha -notmatch '^[0-9a-fA-F]{64}$') {
            throw ('Reference manifest reference is incomplete: ' + $logicalId)
        }
        $originalPath = Resolve-ReferencePath $original $full
        $copiedEvidencePath = Resolve-ReferencePath $copiedEvidence $full
        foreach ($fileRecord in @(
            [ordered]@{ label = $logicalId + '.originalPath'; path = $originalPath },
            [ordered]@{ label = $logicalId + '.copiedEvidencePath'; path = $copiedEvidencePath }
        )) {
            if (-not (Test-Path -LiteralPath $fileRecord.path -PathType Leaf)) { throw ('Reference file missing: ' + $fileRecord.path) }
            if ((Get-Item -LiteralPath $fileRecord.path).Length -ne $bytes -or (Get-Hash $fileRecord.path) -ne $sha.ToLowerInvariant()) {
                throw ('Reference hash mismatch: ' + $fileRecord.label)
            }
        }
        $records.Add([ordered]@{
                logicalId = $logicalId
                originalPath = $originalPath
                copiedEvidencePath = $copiedEvidencePath
                byteLength = $bytes
                sha256 = $sha.ToLowerInvariant()
            }) | Out-Null
    }
    foreach ($logicalId in $expectedIds) { if (-not $seen.Contains($logicalId)) { throw ('Reference manifest missing logicalId: ' + $logicalId) } }
    return [pscustomobject]@{ path = $full; sha256 = Get-Hash $full; entries = @($records.ToArray()) }
}

function Assert-EvidencePathBudget {
    param(
        [string]$EvidenceDirectory = $script:EvidenceDirectory,
        [switch]$SkipProbe
    )
    $deepest = Join-Path (Join-Path $EvidenceDirectory 'logs') 'weapon-shotgun-awaylight-low.png'
    if ($deepest.Length -ge 260) { throw ('Weapon evidence path exceeds Windows 260-character limit: ' + $deepest) }
    if (-not $SkipProbe) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $deepest) | Out-Null
        $probe = Join-Path (Split-Path -Parent $deepest) '.path-probe'
        try { [IO.File]::WriteAllText($probe, 'probe'); Remove-Item -LiteralPath $probe -Force } catch { throw ('Weapon evidence path is not writable: ' + $_.Exception.Message) }
    }
    return $deepest
}

function Assert-EvidencePathLength {
    param([Parameter(Mandatory = $true)][string]$EvidenceDirectory)
    $deepest = Join-Path $EvidenceDirectory 'shotgun-awaylight-low.png'
    if ($deepest.Length -ge 260) { throw ('Weapon capture path exceeds Windows 260-character limit: ' + $deepest) }
    return $deepest
}

function Read-PngDimensions {
    param([Parameter(Mandatory = $true)][string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 24 -or $bytes[0] -ne 137 -or $bytes[1] -ne 80 -or $bytes[2] -ne 78 -or $bytes[3] -ne 71) { throw ('Invalid PNG signature: ' + $Path) }
    $width = ([int]$bytes[16] -shl 24) -bor ([int]$bytes[17] -shl 16) -bor ([int]$bytes[18] -shl 8) -bor [int]$bytes[19]
    $height = ([int]$bytes[20] -shl 24) -bor ([int]$bytes[21] -shl 16) -bor ([int]$bytes[22] -shl 8) -bor [int]$bytes[23]
    if ($width -ne 1920 -or $height -ne 1080) { throw ('Weapon image must be 1920x1080: ' + $Path) }
    return [pscustomobject]@{ width = $width; height = $height }
}

function Assert-NumericClose {
    param([Parameter(Mandatory = $true)][double]$Actual, [Parameter(Mandatory = $true)][double]$Expected, [Parameter(Mandatory = $true)][double]$Tolerance, [Parameter(Mandatory = $true)][string]$Label)
    if ([Math]::Abs($Actual - $Expected) -gt $Tolerance) { throw ($Label + ' mismatch: expected ' + $Expected + ', observed ' + $Actual) }
}

function Assert-Vector {
    param([AllowNull()]$Value, [Parameter(Mandatory = $true)][double[]]$Expected, [Parameter(Mandatory = $true)][double]$Tolerance, [Parameter(Mandatory = $true)][string]$Label)
    $x = Get-Property $Value 'x'; $y = Get-Property $Value 'y'; $z = Get-Property $Value 'z'
    if ($null -eq $x -or $null -eq $y -or $null -eq $z) { throw ($Label + ' is missing x/y/z.') }
    Assert-NumericClose ([double]$x) $Expected[0] $Tolerance ($Label + '.x')
    Assert-NumericClose ([double]$y) $Expected[1] $Tolerance ($Label + '.y')
    Assert-NumericClose ([double]$z) $Expected[2] $Tolerance ($Label + '.z')
}

function Assert-WeaponManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Reference,
        [Parameter(Mandatory = $true)][string]$ExpectedSha,
        [Parameter(Mandatory = $true)][string]$ExpectedGeneratedManifestSha
    )
    $raw = [IO.File]::ReadAllText($Path)
    try { $manifest = $raw | ConvertFrom-Json -ErrorAction Stop } catch { throw ('Weapon capture manifest JSON is invalid: ' + $_.Exception.Message) }
    if ([int](Get-Property $manifest 'schemaVersion') -ne 1) { throw 'Weapon capture manifest schemaVersion must equal 1.' }
    if ([string](Get-Property $manifest 'attemptId') -cne $AttemptId -or [string](Get-Property $manifest 'weaponCaptureAttemptId') -cne $AttemptId) { throw 'Weapon capture attempt binding failed.' }
    if ([string](Get-Property $manifest 'weapon') -cne '' -and [string](Get-Property $manifest 'weapon') -cne $Weapon) { throw 'Weapon capture weapon binding failed.' }
    if ([string](Get-Property $manifest 'weaponCaptureWeapon') -cne $Weapon) { throw 'Weapon capture CLI weapon binding failed.' }
    if ([string](Get-Property $manifest 'weaponCaptureMode') -cne $Mode) { throw 'Weapon capture CLI mode binding failed.' }
    if ([string](Get-Property $manifest 'sourceSha').ToLowerInvariant() -ne $ExpectedSha) { throw 'Weapon capture source SHA binding failed.' }
    if ([string](Get-Property $manifest 'generatedManifestSha256').ToLowerInvariant() -ne $ExpectedGeneratedManifestSha) { throw 'Generated MovementLabBuildManifest hash binding failed.' }
    if ([IO.Path]::GetFullPath([string](Get-Property $manifest 'referenceManifestPath')) -ine $Reference.path) { throw 'Reference manifest path binding failed.' }
    if ([string](Get-Property $manifest 'referenceManifestSha256').ToLowerInvariant() -ne $Reference.sha256) { throw 'Reference manifest hash binding failed.' }
    if (-not [bool](Get-Property $manifest 'pass')) { throw 'Weapon capture manifest pass=false.' }

    $referenceHashes = @(Get-Property $manifest 'referenceHashes')
    if ($referenceHashes.Count -ne $Reference.entries.Count) { throw 'Weapon capture reference hash count mismatch.' }
    foreach ($entry in $Reference.entries) {
        $actual = @($referenceHashes | Where-Object { [string](Get-Property $_ 'id') -ceq $entry.logicalId })
        if ($actual.Count -ne 1 -or [string](Get-Property $actual[0] 'sha256').ToLowerInvariant() -ne $entry.sha256) { throw ('Weapon capture reference hash mismatch: ' + $entry.logicalId) }
    }

    $expectedNames = @(
        ($Weapon.ToLowerInvariant() + '-sunward-high.png'), ($Weapon.ToLowerInvariant() + '-sunward-low.png'),
        ($Weapon.ToLowerInvariant() + '-crosslight-high.png'), ($Weapon.ToLowerInvariant() + '-crosslight-low.png'),
        ($Weapon.ToLowerInvariant() + '-awaylight-high.png'), ($Weapon.ToLowerInvariant() + '-awaylight-low.png')
    )
    $items = @(Get-Property $manifest 'images')
    if ($items.Count -ne 6) { throw 'Weapon capture manifest must contain exactly six images.' }
    $records = New-Object System.Collections.Generic.List[object]
    $hashes = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
    for ($index = 0; $index -lt $items.Count; $index++) {
        $item = $items[$index]
        $filename = [string](Get-Property $item 'filename')
        if ($filename -cne $expectedNames[$index]) { throw ('Weapon image order/name mismatch at index ' + $index + ': ' + $filename) }
        $imagePath = [IO.Path]::GetFullPath([string](Get-Property $item 'path'))
        if (-not $imagePath.StartsWith($script:EvidenceDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw ('Weapon image escaped attempt directory: ' + $imagePath) }
        if (-not (Test-Path -LiteralPath $imagePath -PathType Leaf)) { throw ('Weapon image missing: ' + $imagePath) }
        $dimensions = Read-PngDimensions $imagePath
        if ([int](Get-Property $item 'width') -ne 1920 -or [int](Get-Property $item 'height') -ne 1080) { throw ('Weapon manifest dimensions invalid: ' + $filename) }
        $hash = Get-Hash $imagePath
        if ($hash -ne [string](Get-Property $item 'sha256').ToLowerInvariant()) { throw ('Weapon image hash mismatch: ' + $filename) }
        if (-not $hashes.Add($hash)) { throw ('Weapon image hashes are not unique: ' + $filename) }
        if ([string](Get-Property $item 'visualMode') -cne $Mode -or [string](Get-Property $item 'qualityLevel') -notin @('High', 'Low')) { throw ('Weapon image quality/mode mismatch: ' + $filename) }
        if (-not [bool](Get-Property $item 'pass') -or -not [bool](Get-Property $item 'controlRendered')) { throw ('Weapon image technical pass/control missing: ' + $filename) }
        if ($Mode -eq 'Fast' -and -not [bool](Get-Property $item 'fastSessionApplied')) { throw ('Fast session was not applied: ' + $filename) }
        if ($Mode -eq 'Persisted' -and [bool](Get-Property $item 'fastSessionApplied')) { throw ('Persisted image entered Fast mode: ' + $filename) }
        $expectedAngle = if ($index -lt 2) { 0 } elseif ($index -lt 4) { 90 } else { 180 }
        Assert-NumericClose ([double](Get-Property $item 'targetAngle')) $expectedAngle 0.001 ($filename + '.targetAngle')
        if ([double](Get-Property $item 'angleDelta') -gt 0.25) { throw ('Weapon image angle tolerance failed: ' + $filename) }
        Assert-NumericClose ([double](Get-Property $item 'fieldOfView')) 75 0.001 ($filename + '.fieldOfView')
        Assert-Vector (Get-Property $item 'playerPosition') @(0, 0, 0) 0.001 ($filename + '.playerPosition')
        Assert-Vector (Get-Property $item 'cameraLocalEulerAngles') @(8, 0, 0) 0.01 ($filename + '.cameraLocalEulerAngles')
        if ([string](Get-Property $item 'maskOrigin') -cne 'top-left' -or [string](Get-Property $item 'maskRowOrigin') -cne 'bottom-left') { throw ('Weapon mask origin mismatch: ' + $filename) }
        if ([int](Get-Property $item 'maskXMin') -ne 64 -or [int](Get-Property $item 'maskXMax') -ne 1855 -or [int](Get-Property $item 'maskYMin') -ne 540 -or [int](Get-Property $item 'maskYMax') -ne 1079 -or [int](Get-Property $item 'maskRowMin') -ne 0 -or [int](Get-Property $item 'maskRowMax') -ne 539) { throw ('Weapon mask range mismatch: ' + $filename) }
        if ([int](Get-Property $item 'differencePixelCount') -lt 10000 -or [double](Get-Property $item 'differenceMeanAbsRgb') -lt 0.01 -or [int](Get-Property $item 'differencePixelThreshold') -ne 10000 -or [int](Get-Property $item 'differenceChannelThreshold') -ne 8) { throw ('Weapon visibility difference threshold failed: ' + $filename) }
        $records.Add([pscustomobject]@{ item = $item; path = $imagePath; hash = $hash }) | Out-Null
    }
    if (-not [bool](Get-Property $manifest 'pass')) { throw 'Weapon capture top-level pass reduction failed.' }
    foreach ($pair in @(@(0, 1), @(2, 3), @(4, 5))) {
        foreach ($field in @('targetAngle', 'fieldOfView')) {
            Assert-NumericClose ([double](Get-Property $records[$pair[0]].item $field)) ([double](Get-Property $records[$pair[1]].item $field)) 0.001 ('High/Low pose parity ' + $field)
        }
        Assert-Vector (Get-Property $records[$pair[0]].item 'playerPosition') @(0, 0, 0) 0.001 'High/Low player position'
        Assert-Vector (Get-Property $records[$pair[1]].item 'playerPosition') @(0, 0, 0) 0.001 'High/Low player position'
        Assert-Vector (Get-Property $records[$pair[0]].item 'cameraLocalEulerAngles') @(8, 0, 0) 0.01 'High/Low camera pose'
        Assert-Vector (Get-Property $records[$pair[1]].item 'cameraLocalEulerAngles') @(8, 0, 0) 0.01 'High/Low camera pose'
    }
    return @($records.ToArray())
}

function Write-ImmutableJson {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)]$Value)
    if (Test-Path -LiteralPath $Path) { throw ('Immutable evidence path already exists: ' + $Path) }
    $directory = Split-Path -Parent $Path
    $temporary = Join-Path $directory ('.weapon-capture-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, (($Value | ConvertTo-Json -Depth 16) + [Environment]::NewLine), (New-Object Text.UTF8Encoding($false)))
        [IO.File]::Move($temporary, $Path)
    } finally {
        if (Test-Path -LiteralPath $temporary) { [IO.File]::Delete($temporary) }
    }
}

function New-ExclusiveWrapperDirectory {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$AttemptId)
    if (Test-Path -LiteralPath $Path) { throw ('Wrapper evidence directory already exists; refusing overwrite: ' + $Path) }
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw ('Wrapper evidence parent is missing: ' + $parent) }
    $reservation = Join-Path $parent ('.' + (Split-Path -Leaf $Path) + '.reservation')
    $record = [ordered]@{
        schemaVersion = 1
        owner = 'Capture-WeaponVisuals.ps1'
        attemptId = $AttemptId
        reservedUtc = [DateTime]::UtcNow.ToString('O')
    }
    $reservationCreated = $false
    try {
        $stream = New-Object IO.FileStream($reservation, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $reservationCreated = $true
        try {
            $bytes = [Text.Encoding]::UTF8.GetBytes(($record | ConvertTo-Json -Depth 8) + "`n")
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        } finally { $stream.Dispose() }
        if (Test-Path -LiteralPath $Path) { throw ('Wrapper evidence directory appeared during reservation: ' + $Path) }
        [IO.Directory]::CreateDirectory($Path) | Out-Null
        if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw ('Wrapper evidence directory was not created: ' + $Path) }
        # Retain an immutable ownership record inside the runner directory;
        # wrapper logs/results are never written into the C# capture leaf.
        Write-ImmutableJson (Join-Path $Path '.wrapper-reservation.json') $record
    } catch {
        throw ('Unable to atomically reserve wrapper evidence directory: ' + $Path + ': ' + $_.Exception.Message)
    } finally {
        # The directory and its immutable record are the durable reservation;
        # remove only this invocation's short-lived sibling lock.
        if ($reservationCreated -and (Test-Path -LiteralPath $reservation)) { Remove-Item -LiteralPath $reservation -Force -ErrorAction SilentlyContinue }
    }
    return $Path
}

$script:ProjectRoot = Get-FullPath $ProjectPath
if (-not (Test-Path -LiteralPath $script:ProjectRoot -PathType Container)) { throw ('ProjectPath not found: ' + $script:ProjectRoot) }
if ($script:ProjectRoot.Length -gt 80) { throw 'ProjectPath must be 80 characters or fewer.' }
$script:ProjectRoot = Get-CanonicalPath $script:ProjectRoot
$evidenceBase = Assert-ShortWorkspacePath $EvidenceRoot 'EvidenceRoot'
if (-not (Test-Path -LiteralPath $evidenceBase -PathType Container)) { throw ('EvidenceRoot must already exist: ' + $evidenceBase) }
$evidenceBase = Get-CanonicalPath $evidenceBase
if (-not $evidenceBase.StartsWith('C:\wt\', [StringComparison]::OrdinalIgnoreCase)) { throw ('EvidenceRoot canonical path escaped C:\wt: ' + $evidenceBase) }
$evidenceBase = Assert-OutsideProject $evidenceBase 'EvidenceRoot'
if ($AttemptId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' -or $AttemptId -in @('.', '..')) { throw 'AttemptId must be 1-64 filename-safe characters.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be greater than zero.' }
if (-not [string]::IsNullOrWhiteSpace($ExpectedSourceSha)) {
    $script:ExpectedSourceSha = $ExpectedSourceSha.ToLowerInvariant()
    if ($script:ExpectedSourceSha -notmatch '^[0-9a-f]{40}$') { throw 'ExpectedSourceSha must be an exact 40-character Git commit SHA.' }
}

# The Editor owns this leaf. The wrapper only checks that it is absent and
# supplies an external log/result directory, so wrapper outputs can never be
# confused with the immutable image evidence produced by WeaponVisualCapture.
$script:EvidenceDirectory = [IO.Path]::GetFullPath((Join-Path $evidenceBase $AttemptId)).TrimEnd('\')
if (Test-Path -LiteralPath $script:EvidenceDirectory) { throw ('Weapon evidence attempt already exists: ' + $script:EvidenceDirectory) }
Assert-EvidencePathLength $script:EvidenceDirectory | Out-Null

$script:GitCommonRoot = Get-GitCommonRoot
$script:Reference = Read-ReferenceManifest $ReferenceManifest
$reference = $script:Reference
$unityPath = Join-Path ${env:ProgramFiles} ('Unity\Hub\Editor\' + $script:UnityVersion + '\Editor\Unity.exe')
if (-not (Test-Path -LiteralPath $unityPath -PathType Leaf)) { throw ('Unity executable missing: ' + $unityPath) }
$libraryPath = Join-Path $script:ProjectRoot 'Library'
if (-not (Test-Path -LiteralPath $libraryPath -PathType Container)) { throw 'Warm private Library is missing.' }
$libraryItem = Get-Item -LiteralPath $libraryPath -Force
if ($libraryItem.LinkType -or (($libraryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) { throw 'Library must remain private.' }
$versionPath = Join-Path $script:ProjectRoot 'ProjectSettings\ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $versionPath -PathType Leaf) -or (Get-Content -Raw -LiteralPath $versionPath) -notmatch ('m_EditorVersion:\s*' + [Regex]::Escape($script:UnityVersion))) { throw 'Project Unity version mismatch.' }

# This is deliberately before any wrapper directory, harness summary, log, or
# capture leaf is created. HEAD, the linked-worktree index, every product file,
# the generated build manifest, and every immutable reference are bound here.
$beforeSnapshot = Get-CaptureSourceSnapshot
$beforeSha = [string]$beforeSnapshot.sourceSha
$beforeIndexDigest = [string]$beforeSnapshot.indexSha256
$beforeProductDigest = [string]$beforeSnapshot.product.sha256
$beforeReferenceDigest = [string]$beforeSnapshot.reference.sha256
$generatedManifestPath = Join-Path $script:ProjectRoot 'Assets\_Game\Generated\MovementLabBuildManifest.json'
$beforeGeneratedManifestHash = [string]$beforeSnapshot.product.files['Assets/_Game/Generated/MovementLabBuildManifest.json'].sha256
Assert-NoProjectProcessOrLock
Acquire-ProjectLease | Out-Null

$wrapperDirectory = Join-Path $evidenceBase ($AttemptId + '-wrapper')
$workflowError = $null
$process = $null
$harness = $null
$manifestPath = $null
$imageRecords = @()
$unityStopwatch = [Diagnostics.Stopwatch]::StartNew()
$logPath = $null
$resultPath = $null
$harnessEvidence = $null
try {
    Assert-NoProjectProcessOrLock
    $lockedSnapshot = Get-CaptureSourceSnapshot
    if ($lockedSnapshot.sourceSha -cne $beforeSha -or $lockedSnapshot.indexSha256 -cne $beforeIndexDigest) { throw 'Capture source changed while acquiring project lease.' }
    Assert-SnapshotEqual $beforeSnapshot.product $lockedSnapshot.product 'Capture product source'
    Assert-SnapshotEqual $beforeSnapshot.reference $lockedSnapshot.reference 'Capture reference source'

    # Reserve wrapper-owned evidence atomically, outside the C# capture leaf.
    New-ExclusiveWrapperDirectory $wrapperDirectory $AttemptId | Out-Null
    Assert-EvidencePathBudget -EvidenceDirectory $wrapperDirectory | Out-Null
    $script:WrapperDirectory = $wrapperDirectory
    $script:LeaseReleaseProofPath = Join-Path $wrapperDirectory 'lease-release-proof.json'
    $logPath = Join-Path $wrapperDirectory 'weapon-capture-unity.log'
    $resultPath = Join-Path $wrapperDirectory 'CaptureResult.json'
    $harnessEvidence = Join-Path $wrapperDirectory 'harness'
    New-Item -ItemType Directory -Force -Path $harnessEvidence | Out-Null

    $harnessArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $script:ProjectRoot 'Tools\Tests\Invoke-HarnessTests.ps1'), '-EvidenceRoot', $harnessEvidence)
    $harness = Start-Process -FilePath 'powershell.exe' -ArgumentList $harnessArgs -WindowStyle Hidden -Wait -PassThru
    if ($harness.ExitCode -ne 0) { throw ('Harness pre-gate failed with exit code ' + $harness.ExitCode + '.') }
    Assert-NoProjectProcessOrLock
    $afterHarnessSnapshot = Get-CaptureSourceSnapshot
    if ($afterHarnessSnapshot.sourceSha -cne $beforeSha -or $afterHarnessSnapshot.indexSha256 -cne $beforeIndexDigest) { throw 'Capture source HEAD/index drifted before Unity.' }
    Assert-SnapshotEqual $beforeSnapshot.product $afterHarnessSnapshot.product 'Capture product source before Unity'
    Assert-SnapshotEqual $beforeSnapshot.reference $afterHarnessSnapshot.reference 'Capture reference source before Unity'

    $arguments = @(
        '-batchmode', '-quit', '-projectPath', ('"' + $script:ProjectRoot + '"'),
        '-executeMethod', 'RocketFooxball.Editor.WeaponVisualCapture.Capture',
        '-weaponCaptureEvidenceRoot', ('"' + $evidenceBase + '"'), '-weaponCaptureAttemptId', $AttemptId,
        '-weaponCaptureWeapon', $Weapon, '-weaponCaptureMode', $Mode,
        '-weaponCaptureReferenceManifest', ('"' + $reference.path + '"'), '-logFile', ('"' + $logPath + '"')
    )
    Assert-NoProjectProcessOrLock
    $process = Start-Process -FilePath $unityPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw ('Weapon visual capture failed with exit code ' + $process.ExitCode + '. Log: ' + $logPath) }
    Wait-ProjectRelease 'weapon-capture' | Out-Null
    $unityStopwatch.Stop()

    if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) { throw ('Unity capture log missing: ' + $logPath) }
    $marker = [Regex]::Match((Get-Content -Raw -LiteralPath $logPath), '(?m)^WEAPON_VISUAL_CAPTURE_PASS\s+(.+?)\s*$')
    if (-not $marker.Success) { throw ('Weapon capture pass marker missing from log: ' + $logPath) }
    $manifestPath = [IO.Path]::GetFullPath($marker.Groups[1].Value.Trim())
    $expectedManifestPath = Join-Path $script:EvidenceDirectory 'WeaponVisualManifest.json'
    if (-not $manifestPath.Equals($expectedManifestPath, [StringComparison]::OrdinalIgnoreCase)) { throw ('Unexpected weapon manifest path: ' + $manifestPath) }
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw ('Weapon capture manifest missing: ' + $manifestPath) }

    # Rebind every product/reference byte and Git identity after Unity. A
    # porcelain-only check is insufficient for ignored or linked-worktree data.
    $afterSnapshot = Get-CaptureSourceSnapshot
    if ($afterSnapshot.sourceSha -cne $beforeSha) { throw 'Git HEAD changed during weapon capture.' }
    if ($afterSnapshot.indexSha256 -cne $beforeIndexDigest) { throw 'Git index changed during weapon capture.' }
    if ([string]$afterSnapshot.status -cne [string]$beforeSnapshot.status) { throw 'Git status changed during non-mutating weapon capture.' }
    Assert-SnapshotEqual $beforeSnapshot.product $afterSnapshot.product 'Capture product source after Unity'
    Assert-SnapshotEqual $beforeSnapshot.reference $afterSnapshot.reference 'Capture reference source after Unity'
    $expectedGeneratedManifestHash = [string]$afterSnapshot.product.files['Assets/_Game/Generated/MovementLabBuildManifest.json'].sha256
    if ($expectedGeneratedManifestHash -ne $beforeGeneratedManifestHash) { throw 'Generated MovementLabBuildManifest changed during capture.' }
    $afterProductDigest = [string]$afterSnapshot.product.sha256
    $afterReferenceDigest = [string]$afterSnapshot.reference.sha256
    $afterGeneratedManifestHash = [string]$afterSnapshot.product.files['Assets/_Game/Generated/MovementLabBuildManifest.json'].sha256
    $afterReferenceManifestHash = [string]$afterSnapshot.reference.files[[IO.Path]::GetFullPath($reference.path)].sha256
    $imageRecords = Assert-WeaponManifest $manifestPath $reference $beforeSha $expectedGeneratedManifestHash

    $result = [ordered]@{
        schemaVersion = 1
        pass = $true
        projectPath = $script:ProjectRoot
        evidenceDirectory = $script:EvidenceDirectory
        wrapperDirectory = $wrapperDirectory
        attemptId = $AttemptId
        weapon = $Weapon
        mode = $Mode
        sourceSha = $beforeSha
        expectedSourceSha = if ([string]::IsNullOrWhiteSpace($ExpectedSourceSha)) { $beforeSha } else { $script:ExpectedSourceSha }
        sourceShaIsCleanHead = $true
        indexSha256 = $beforeIndexDigest
        productContentSha256 = $beforeProductDigest
        referenceContentSha256 = $beforeReferenceDigest
        generatedManifestSha256 = $beforeGeneratedManifestHash
        afterSourceSha = [string]$afterSnapshot.sourceSha
        afterIndexSha256 = [string]$afterSnapshot.indexSha256
        afterProductContentSha256 = $afterProductDigest
        afterReferenceContentSha256 = $afterReferenceDigest
        afterGeneratedManifestSha256 = $afterGeneratedManifestHash
        afterReferenceManifestSha256 = $afterReferenceManifestHash
        referenceManifestPath = $reference.path
        referenceManifestSha256 = $reference.sha256
        manifestPath = $manifestPath
        manifestSha256 = Get-Hash $manifestPath
        logPath = $logPath
        unityExitCode = [int]$process.ExitCode
        harnessEvidence = $harnessEvidence
        harnessExitCode = [int]$harness.ExitCode
        unityMilliseconds = [Math]::Round($unityStopwatch.Elapsed.TotalMilliseconds)
        images = @($imageRecords | ForEach-Object { [ordered]@{ filename = [string](Get-Property $_.item 'filename'); path = $_.path; sha256 = $_.hash; width = 1920; height = 1080 } })
        noGitMutation = $true
        lockReleaseProof = @($script:ReleaseProof.ToArray())
        leasePath = $script:LeasePath
        leaseReleaseProofPath = $script:LeaseReleaseProofPath
    }
    Write-ImmutableJson $resultPath $result
    Write-Output ('WEAPON_VISUAL_CAPTURE_RESULT ' + $resultPath)
    Write-Output (($result | ConvertTo-Json -Depth 12 -Compress))
}
catch {
    $workflowError = $_.Exception
}
finally {
    try {
        if ($script:LeaseAcquired) { Release-ProjectLease }
    }
    catch {
        if ($null -ne $workflowError) { $workflowError.Data['MovementLab.Cleanup.weapon-capture-lease'] = $_.Exception.ToString() }
        else { $workflowError = $_.Exception }
    }
    try {
        if (-not [string]::IsNullOrWhiteSpace($script:LeaseReleaseProofPath) -and $script:ReleaseProof.Count -gt 0 -and -not (Test-Path -LiteralPath $script:LeaseReleaseProofPath)) {
            Write-ImmutableJson $script:LeaseReleaseProofPath ([ordered]@{ schemaVersion = 1; attemptId = $AttemptId; leasePath = $script:LeasePath; leaseToken = $script:LeaseToken; canonicalProjectRoot = $script:CanonicalProjectRoot; releaseProof = @($script:ReleaseProof.ToArray()) })
        }
    }
    catch {
        if ($null -ne $workflowError) { $workflowError.Data['MovementLab.Cleanup.weapon-capture-lease-proof'] = $_.Exception.ToString() }
        else { $workflowError = $_.Exception }
    }
}
$unityStopwatch.Stop()
if ($null -ne $workflowError) { throw $workflowError }
