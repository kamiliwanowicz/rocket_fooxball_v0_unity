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
    [string[]]$GeneratedPath = @(),
    [switch]$PlanOnly,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:UnityVersion = '6000.5.6f1'
$script:UnityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe'
$script:ProjectRoot = $null
$script:ProjectInputRoot = $ProjectPath
$script:GitCommonRoot = $null
$script:CanonicalProjectRoot = $null
$script:EvidenceDirectory = $null
$script:ProbeOutputPath = $null
$script:LeasePath = $null
$script:LeaseToken = $null
$script:LeaseReleaseProofPath = $null
$script:InvocationId = $null
$script:PriorLedgerHistory = New-Object System.Collections.Generic.List[object]
$script:PriorManifestHistory = New-Object System.Collections.Generic.List[object]
$script:ProbeInventoryPaths = @()
$script:RequestedInventoryPaths = @()
$script:WorkflowStarted = [DateTime]::UtcNow
$script:CommandRecords = New-Object System.Collections.Generic.List[object]
$script:ExecutedCheckIds = New-Object System.Collections.Generic.List[string]
$script:ProductionBakeReuseCandidates = @{}
$script:BakeCount = 0
$script:ReleaseProof = New-Object System.Collections.Generic.List[object]
$script:GeneratedRoots = @(
    'Assets/_Game/Generated',
    'Assets/_Game/Prefabs',
    'Assets/_Game/Materials',
    'Assets/_Game/Animations',
    'Assets/_Game/Lighting',
    'Assets/_Game/Scenes/MovementLab.unity',
    'Assets/_Game/Scenes/MovementLab.unity.meta',
    'Assets/_Game/Scenes/MovementLab',
    'Assets/_Game/Scenes/MovementLab/LightingData.asset',
    'Assets/_Game/Scenes/MovementLab/LightingData.asset.meta',
    'Assets/Settings',
    'ProjectSettings'
)
$script:AuthoritativeInventory = @(
    'Assets/_Game/Generated',
    'Assets/_Game/Prefabs',
    'Assets/_Game/Materials',
    'Assets/_Game/Animations',
    'Assets/_Game/Scenes/MovementLab.unity',
    'Assets/_Game/Scenes/MovementLab.unity.meta',
    'Assets/_Game/Scenes/MovementLab',
    'Assets/_Game/Lighting',
    'Assets/Settings',
    'ProjectSettings'
)
# T1 deliberately keeps Models/Textures source binaries outside the closed
# generated inventory. T4 probes still fingerprint these exact importer metas,
# so compatibility is an explicit path set rather than a caller-expandable root.
$script:ClosedImporterMetadataPaths = @(
    'Assets/_Game/Models/LowPolyRocket.fbx.meta', 'Assets/_Game/Models/ArenaKit.fbx.meta', 'Assets/_Game/Models/LowPolyCharacter.fbx.meta', 'Assets/_Game/Models/FpsKickRig.fbx.meta', 'Assets/_Game/Models/FpsRocketLauncher.fbx.meta',
    'Assets/_Game/Textures/RetroGrass.png.meta', 'Assets/_Game/Textures/RetroGrass_Normal.png.meta', 'Assets/_Game/Textures/RetroGrass_MetallicSmoothness.png.meta', 'Assets/_Game/Textures/RetroGrass_Occlusion.png.meta',
    'Assets/_Game/Textures/RetroWall.png.meta', 'Assets/_Game/Textures/RetroWall_Normal.png.meta', 'Assets/_Game/Textures/RetroWall_MetallicSmoothness.png.meta', 'Assets/_Game/Textures/RetroWall_Occlusion.png.meta',
    'Assets/_Game/Textures/RetroTrim.png.meta', 'Assets/_Game/Textures/RetroTrim_Normal.png.meta', 'Assets/_Game/Textures/RetroTrim_MetallicSmoothness.png.meta', 'Assets/_Game/Textures/RetroTrim_Occlusion.png.meta',
    'Assets/_Game/Textures/RetroHazard.png.meta', 'Assets/_Game/Textures/RetroHazard_Normal.png.meta', 'Assets/_Game/Textures/RetroHazard_MetallicSmoothness.png.meta', 'Assets/_Game/Textures/RetroHazard_Occlusion.png.meta',
    'Assets/_Game/Textures/RetroDetailNormal.png.meta', 'Assets/_Game/Textures/RetroShield.png.meta',
    'Assets/_Game/Textures/RetroBall.png.meta', 'Assets/_Game/Textures/RetroBall_Normal.png.meta', 'Assets/_Game/Textures/RetroBall_MetallicSmoothness.png.meta', 'Assets/_Game/Textures/RetroBall_Occlusion.png.meta',
    'Assets/_Game/Textures/RetroWeaponMetal.png.meta', 'Assets/_Game/Textures/RetroWeaponMetal_Normal.png.meta', 'Assets/_Game/Textures/RetroWeaponMetal_MetallicSmoothness.png.meta', 'Assets/_Game/Textures/RetroWeaponMetal_Occlusion.png.meta',
    'Assets/_Game/Textures/RetroWeaponDark.png.meta', 'Assets/_Game/Textures/RetroWeaponDark_Normal.png.meta', 'Assets/_Game/Textures/RetroWeaponDark_MetallicSmoothness.png.meta', 'Assets/_Game/Textures/RetroWeaponDark_Occlusion.png.meta',
    'Assets/_Game/Textures/RetroWeaponAccent.png.meta', 'Assets/_Game/Textures/RetroWeaponAccent_Normal.png.meta', 'Assets/_Game/Textures/RetroWeaponAccent_MetallicSmoothness.png.meta', 'Assets/_Game/Textures/RetroWeaponAccent_Occlusion.png.meta', 'Assets/_Game/Textures/RetroWeaponAccent_Emission.png.meta',
    'Assets/_Game/Textures/RetroRocket.png.meta', 'Assets/_Game/Textures/RetroRocket_Normal.png.meta', 'Assets/_Game/Textures/RetroRocket_MetallicSmoothness.png.meta', 'Assets/_Game/Textures/RetroRocket_Occlusion.png.meta', 'Assets/_Game/Textures/RetroRocket_Emission.png.meta', 'Assets/_Game/Textures/RetroRocketGlow.png.meta',
    'Assets/_Game/Textures/RetroExplosion.png.meta', 'Assets/_Game/Textures/RetroSmoke.png.meta', 'Assets/_Game/Textures/RetroSunnySky.png.meta'
)
$script:BuilderOutputContract = @(
    'Assets/_Game/Generated/MovementLabBuildManifest.json',
    'Assets/_Game/Prefabs/Player.prefab', 'Assets/_Game/Prefabs/Ball.prefab', 'Assets/_Game/Prefabs/Rocket.prefab', 'Assets/_Game/Prefabs/ExplosionVfx.prefab',
    'Assets/_Game/Animations/WorldCharacter.controller', 'Assets/_Game/Animations/FpsKick.controller',
    'Assets/_Game/Scenes/MovementLab.unity', 'Assets/_Game/Scenes/MovementLab/LightingData.asset',
    'Assets/_Game/Lighting/MovementLabVolumeProfile.asset', 'Assets/_Game/Lighting/MovementLabLightingSettings.asset', 'Assets/_Game/Lighting/MovementLabLightingManifest.json',
    'Assets/_Game/Lighting/ReflectionProbe_Center.exr', 'Assets/_Game/Lighting/ReflectionProbe_WestGoal.exr', 'Assets/_Game/Lighting/ReflectionProbe_EastGoal.exr',
    'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_dir.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_light.exr', 'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_shadowmask.png',
    'Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_dir.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_light.exr', 'Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_shadowmask.png',
    'Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_dir.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_light.exr', 'Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_shadowmask.png',
    'Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_dir.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_light.exr', 'Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_shadowmask.png',
    'Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_dir.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_light.exr', 'Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_shadowmask.png',
    'Assets/_Game/Scenes/MovementLab/ReflectionProbe-0.exr', 'Assets/_Game/Scenes/MovementLab/ReflectionProbe-1.exr', 'Assets/_Game/Scenes/MovementLab/ReflectionProbe-2.exr', 'Assets/_Game/Scenes/MovementLab/ReflectionProbe-3.exr',
    'Assets/Settings/PC_Iteration_RPAsset.asset', 'Assets/Settings/PC_Iteration_Renderer.asset',
    'ProjectSettings/QualitySettings.asset', 'ProjectSettings/GraphicsSettings.asset', 'ProjectSettings/ProjectSettings.asset',
    'Assets/_Game/Generated/MovementLabBuildManifest.json.meta',
    'Assets/_Game/Prefabs/Player.prefab.meta', 'Assets/_Game/Prefabs/Ball.prefab.meta', 'Assets/_Game/Prefabs/Rocket.prefab.meta', 'Assets/_Game/Prefabs/ExplosionVfx.prefab.meta',
    'Assets/_Game/Animations/WorldCharacter.controller.meta', 'Assets/_Game/Animations/FpsKick.controller.meta',
    'Assets/_Game/Scenes/MovementLab.unity.meta', 'Assets/_Game/Scenes/MovementLab/LightingData.asset.meta',
    'Assets/Settings/PC_Iteration_RPAsset.asset.meta', 'Assets/Settings/PC_Iteration_Renderer.asset.meta'
)

function New-WorkflowViolationList {
    return ,(New-Object System.Collections.Generic.List[object])
}

function Add-WorkflowViolation {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Violations,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Message
    )
    $text = if ([string]::IsNullOrWhiteSpace($Message)) { 'validation failed' } else { $Message.Trim() }
    $Violations.Add([ordered]@{ label = $Label; message = $text }) | Out-Null
}

function Complete-WorkflowValidationPhase {
    param(
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Violations
    )
    if ($Violations.Count -eq 0) { return }
    $lines = New-Object System.Collections.Generic.List[string]
    for ($index = 0; $index -lt $Violations.Count; $index++) {
        $violation = $Violations[$index]
        $lines.Add(('[' + ($index + 1) + '] ' + [string]$violation.label + ': ' + [string]$violation.message)) | Out-Null
    }
    throw ('Workflow validation phase ' + $Phase + ' failed with ' + $Violations.Count + ' violation(s):' + [Environment]::NewLine + ($lines -join [Environment]::NewLine))
}

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) {
        throw 'Path values must be non-empty and single-line.'
    }
    return [System.IO.Path]::GetFullPath($Path)
}

function Get-CanonicalPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $full = Get-FullPath $Path
    $item = Get-Item -LiteralPath $full -Force -ErrorAction Stop
    $resolved = (Resolve-Path -LiteralPath $item.FullName -ErrorAction Stop).Path
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        $targetProperty = $item.PSObject.Properties['Target']
        $target = if ($null -ne $targetProperty) { [string]$targetProperty.Value } else { '' }
        if ([string]::IsNullOrWhiteSpace($target)) { throw ('Unable to resolve project junction/reparse target: ' + $full) }
        if (-not [IO.Path]::IsPathRooted($target)) { $target = Join-Path (Split-Path -Parent $full) $target }
        $resolved = (Resolve-Path -LiteralPath $target -ErrorAction Stop).Path
    }
    return ([IO.Path]::GetFullPath($resolved)).TrimEnd('\')
}

function Get-StringSha256 {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
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
    return $full
}

function Assert-OneLineValue {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) {
        throw ($Name + ' must be non-empty and one line.')
    }
    return $Value.Trim()
}

function Test-InventoryMember {
    param([Parameter(Mandatory = $true)][string]$Path)
    $normalized = $Path.Replace('\', '/').TrimStart('/')
    foreach ($root in $script:AuthoritativeInventory) {
        $candidate = $root.Replace('\', '/').TrimStart('/')
        if ($normalized.Equals($candidate, [StringComparison]::OrdinalIgnoreCase) -or
            $normalized.StartsWith($candidate.TrimEnd('/') + '/', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Test-ProbeInventoryMember {
    param([Parameter(Mandatory = $true)][string]$Path)
    $normalized = $Path.Replace('\', '/').TrimStart('/')
    if (Test-InventoryMember $normalized) { return $true }
    # Importer probes may contribute only the exact .meta paths owned by the
    # T4 importer contract. Source binaries and arbitrary probe/caller paths
    # remain outside the closed inventory.
    return @($script:ClosedImporterMetadataPaths | Where-Object {
        $_.Equals($normalized, [StringComparison]::OrdinalIgnoreCase)
    }).Count -eq 1
}

function Get-AuthoritativeGeneratedInventory {
    $paths = New-Object System.Collections.Generic.List[string]
    $requestedSelection = New-Object System.Collections.Generic.List[string]
    foreach ($root in $script:AuthoritativeInventory) {
        $full = Join-Path $script:ProjectRoot $root
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            $paths.Add($root.Replace('\', '/'))
        } elseif (Test-Path -LiteralPath $full -PathType Container) {
            foreach ($file in @(Get-ChildItem -LiteralPath $full -File -Recurse -Force | Sort-Object FullName)) {
                $relative = $file.FullName.Substring($script:ProjectRoot.Length).TrimStart('\', '/').Replace('\', '/')
                $paths.Add($relative)
            }
            if (@(Get-ChildItem -LiteralPath $full -File -Recurse -Force).Count -eq 0) { $paths.Add($root.Replace('\', '/') + '=__EMPTY__') }
        } else {
            $paths.Add($root.Replace('\', '/') + '=__MISSING__')
        }
    }
    foreach ($contractPath in $script:BuilderOutputContract) {
        $full = Join-Path $script:ProjectRoot $contractPath
        if ((Test-Path -LiteralPath $full -PathType Leaf) -and -not $paths.Contains($contractPath)) { $paths.Add($contractPath) }
        elseif (-not (Test-Path -LiteralPath $full)) { $paths.Add($contractPath + '=__MISSING__') }
    }
    foreach ($probePath in @($script:ProbeInventoryPaths)) {
        $normalizedProbePath = ([string]$probePath).Replace('\', '/').TrimStart('/')
        if (-not (Test-ProbeInventoryMember $normalizedProbePath)) { throw ('Probe inventory path expands closed inventory: ' + $normalizedProbePath) }
        if (-not $paths.Contains($normalizedProbePath)) { $paths.Add($normalizedProbePath) }
    }
    foreach ($requested in @($GeneratedPath)) {
        $value = Assert-OneLineValue 'GeneratedPath' ([string]$requested)
        if ([IO.Path]::IsPathRooted($value) -or -not (Test-InventoryMember $value)) { throw ('GeneratedPath is outside authoritative inventory: ' + $value) }
        if (-not $requestedSelection.Contains($value.Replace('\', '/').TrimStart('/'))) { $requestedSelection.Add($value.Replace('\', '/').TrimStart('/')) }
    }
    $script:RequestedInventoryPaths = @($requestedSelection.ToArray())
    return @($paths.ToArray() | Sort-Object -Unique)
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
    $normalized = @($script:ProjectRoot.TrimEnd('\').ToLowerInvariant(), $script:ProjectInputRoot.TrimEnd('\').ToLowerInvariant()) | Sort-Object -Unique
    $all = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'")
    return @($all | Where-Object {
        $commandLine = [string]$_.CommandLine
        -not [string]::IsNullOrWhiteSpace($commandLine) -and (@($normalized | Where-Object { $commandLine.ToLowerInvariant().Contains($_) }).Count -gt 0)
    })
}

function Get-LockPaths {
    return @(
        (Join-Path $script:ProjectRoot 'Temp\UnityLockfile'),
        (Join-Path $script:ProjectRoot 'Library\UnityLockfile')
    )
}

function Get-CurrentProcessStartUtc {
    $process = Get-Process -Id $PID -ErrorAction Stop
    return $process.StartTime.ToUniversalTime().ToString('O')
}

function Read-LeaseRecord {
    param([Parameter(Mandatory = $true)][string]$Path)
    try { return (Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json) } catch { throw ('Project lease is malformed; refusing stale recovery: ' + $Path) }
}

function Acquire-ProjectLease {
    $canonical = Get-CanonicalPath $script:ProjectRoot
    $script:CanonicalProjectRoot = $canonical
    $leaseRoot = Join-Path $script:GitCommonRoot 'movement-lab-proof\leases'
    [IO.Directory]::CreateDirectory($leaseRoot) | Out-Null
    $leaseRootItem = Get-Item -LiteralPath $leaseRoot -Force -ErrorAction Stop
    if (($leaseRootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Lease destination may not be a junction or alias: ' + $leaseRoot) }
    $canonicalLeaseRoot = Get-CanonicalPath $leaseRoot
    if (-not $canonicalLeaseRoot.StartsWith($script:GitCommonRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw ('Lease destination escaped canonical Git-common root: ' + $canonicalLeaseRoot) }
    $leaseName = Get-StringSha256 $canonical
    $script:LeasePath = Join-Path $leaseRoot ($leaseName + '.lease')
    $script:LeaseToken = [Guid]::NewGuid().ToString('N')
    $record = [ordered]@{
        schemaVersion = 1
        leaseToken = $script:LeaseToken
        canonicalProjectRoot = $canonical
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
            return $true
        } catch [IO.IOException] {
            if (-not (Test-Path -LiteralPath $script:LeasePath -PathType Leaf)) { throw }
            $existing = Read-LeaseRecord $script:LeasePath
            $validIdentity = $existing.schemaVersion -eq 1 -and [string]$existing.canonicalProjectRoot -eq $canonical -and
                [string]$existing.leaseToken -match '^[A-Za-z0-9]{32}$' -and [int]$existing.ownerPid -gt 0 -and
                [string]$existing.ownerProcessStartUtc -match '^\d{4}-\d{2}-\d{2}T.+Z$'
            if (-not $validIdentity) { throw ('Project lease identity is invalid; refusing recovery: ' + $script:LeasePath) }
            $owner = $null
            try { $owner = Get-Process -Id ([int]$existing.ownerPid) -ErrorAction Stop }
            catch {
                if ($_.Exception.Message -notmatch '(?i)no process|cannot find') { throw ('Unable to prove lease owner is dead; refusing stale recovery: ' + $script:LeasePath) }
            }
            if ($null -ne $owner) {
                $ownerStart = $owner.StartTime.ToUniversalTime().ToString('O')
                if ($ownerStart -eq [string]$existing.ownerProcessStartUtc) { throw ('Project lease is held by live PID ' + $existing.ownerPid + ': ' + $canonical) }
            }
            Remove-Item -LiteralPath $script:LeasePath -Force -ErrorAction Stop
        }
    }
    throw ('Unable to acquire canonical project lease: ' + $script:LeasePath)
}

function Release-ProjectLease {
    if ([string]::IsNullOrWhiteSpace($script:LeasePath)) { return }
    $proof = [ordered]@{ path = $script:LeasePath; leaseToken = $script:LeaseToken; canonicalProjectRoot = $script:CanonicalProjectRoot; releasedUtc = $null; verifiedAbsent = $false }
    try {
        if (-not $PlanOnly) { Wait-UnityRelease 'lease-release' | Out-Null } else { Assert-NoProjectProcessOrLock }
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
    }
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
    $inventory = @(Get-AuthoritativeGeneratedInventory)
    $map = [ordered]@{}
    foreach ($relative in $inventory) {
        $relative = ([string]$relative).Trim()
        if ([string]::IsNullOrWhiteSpace($relative)) { continue }
        if ($relative.EndsWith('=__EMPTY__', [StringComparison]::Ordinal)) { $map[$relative.Substring(0, $relative.IndexOf('=__EMPTY__', [StringComparison]::Ordinal))] = '__EMPTY__'; continue }
        if ($relative.EndsWith('=__MISSING__', [StringComparison]::Ordinal)) { $map[$relative.Substring(0, $relative.IndexOf('=__MISSING__', [StringComparison]::Ordinal))] = '__MISSING__'; continue }
        $absolute = Join-Path $script:ProjectRoot $relative
        if (Test-Path -LiteralPath $absolute -PathType Leaf) { $map[$relative] = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash.ToLowerInvariant() }
        else { $map[$relative] = '__MISSING__' }
    }
    return $map
}

function Get-GeneratedHashDigest {
    param([Parameter(Mandatory = $true)]$Hashes)
    $lines = @($Hashes.Keys | Sort-Object | ForEach-Object { [string]$_ + '=' + [string]$Hashes[$_] })
    return Get-StringSha256 ($lines -join "`n")
}

function Get-WorkingTreeDigest {
    return Get-StringSha256 ((Invoke-Git @('status', '--porcelain=v1', '--untracked-files=all')).Trim())
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
        os = [Environment]::OSVersion.VersionString
        machine = [Environment]::MachineName
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
    $generatedHashes = Get-GeneratedHashes
    [ordered]@{
        invocation_id = $script:InvocationId
        check_id = $CheckId
        owner = 'workflow-orchestrator'
        tier = $Tier
        status = 'pending'
        run_point = $RunPoint
        mutates_project = $MutatesProject
        input_paths = @($InputPaths)
        input_digest = Get-InputDigest $InputPaths
        environment_fingerprint = Get-EnvironmentFingerprint
        generated_inventory = @(Get-AuthoritativeGeneratedInventory)
        requested_inventory = @($script:RequestedInventoryPaths)
        generated_hashes = $generatedHashes
        generated_hash_digest = Get-GeneratedHashDigest $generatedHashes
        working_tree_digest = Get-WorkingTreeDigest
        invalidation_paths = @($InvalidationPaths)
        subsumes = @($Subsumes)
        executed_sha = $null
        validated_sha = $null
        evidence_path = $null
        evidence_digest = $null
        evidence = [ordered]@{ path = $null; sha256 = $null }
        subsumed_checks = @()
        invalidation_reason = $null
        bake_count = $null
        probe = $null
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
            $rows.Add((New-LedgerRow 'stage-probe' 'fast' $false @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Generated') @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Generated') @() 'source-freeze'))
            $rows.Add((New-LedgerRow 'prebake-validate' 'production-final' $false @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Generated') @('Assets/_Game/Editor/MovementLab', 'Assets/_Game/Generated', 'Assets/_Game/Lighting') @() 'source-freeze'))
            $productionBakeInputs = @(
                'Assets/_Game/Editor/MovementLab/MovementLabContract.cs',
                'Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs',
                'Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs',
                'Assets/_Game/Editor/MovementLab/MovementLabLightingProfiles.cs',
                'Assets/_Game/Editor/MovementLab/MovementLabMaterialPipeline.cs',
                'Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs',
                'Assets/_Game/Editor/MovementLab/MovementLabSceneComposer.cs',
                'Assets/_Game/Editor/MovementLab/MovementLabArenaPipeline.cs',
                'Assets/_Game/Editor/GraphicsQualityConfigurator.cs',
                'Assets/InputSystem_Actions.inputactions',
                'Assets/InputSystem_Actions.inputactions.meta',
                'Assets/_Game/Lighting/MovementLabLightingSettings.asset',
                'Assets/_Game/Lighting/MovementLabLightingSettings.asset.meta',
                'Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset',
                'Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset.meta',
                'Assets/_Game/Lighting/MovementLabVolumeProfile.asset',
                'Assets/_Game/Lighting/MovementLabVolumeProfile.asset.meta',
                'Packages/manifest.json',
                'Packages/packages-lock.json',
                'ProjectSettings/ProjectVersion.txt',
                'Tools/Validation/Invoke-MovementLabWorkflow.ps1'
            )
            $rows.Add((New-LedgerRow 'production-bake' 'production-final' $true $productionBakeInputs $productionBakeInputs @() 'source-freeze'))
        }
        'ProductionValidate' {
            $productionValidatorInputs = @('Assets/_Game/Editor', 'Assets/_Game/Scripts/Runtime', 'Assets/_Game/Generated', 'Tools/Validation', 'Packages', 'ProjectSettings')
            $productionValidatorInvalidationPaths = @('Assets/_Game/Editor', 'Assets/_Game/Scripts/Runtime', 'Assets/_Game/Generated', 'Tools/Validation', 'Packages', 'ProjectSettings', 'Assets/_Game/Lighting', 'Assets/_Game/Scenes')
            $rows.Add((New-LedgerRow 'production-validator' 'production-final' $false $productionValidatorInputs $productionValidatorInvalidationPaths @('validator-readonly') 'final'))
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

function Get-ObjectPropertyValue {
    param([AllowNull()]$Object, [Parameter(Mandatory = $true)][string]$Name)
    if ($null -eq $Object -or -not ($Object.PSObject.Properties.Name -contains $Name)) { return $null }
    return $Object.PSObject.Properties[$Name].Value
}

function Get-ObjectPropertyText {
    param([AllowNull()]$Object, [Parameter(Mandatory = $true)][string]$Name)
    $value = Get-ObjectPropertyValue $Object $Name
    if ($null -eq $value) { return '' }
    return [string]$value
}

function Test-RowReuseProof {
    param([Parameter(Mandatory = $true)]$Prior, [Parameter(Mandatory = $true)]$Row)
    if ((Get-ObjectPropertyText $Prior 'input_digest') -ne (Get-ObjectPropertyText $Row 'input_digest')) { return @{ valid = $false; reason = 'input digest changed' } }
    if ((Get-ObjectPropertyText $Prior 'environment_fingerprint') -ne (Get-ObjectPropertyText $Row 'environment_fingerprint')) { return @{ valid = $false; reason = 'environment fingerprint changed' } }
    return @{ valid = $true; reason = $null }
}

function Test-StringSetEqual {
    param([AllowNull()][object[]]$Left, [AllowNull()][object[]]$Right)
    $leftValues = @($Left | ForEach-Object { [string]$_ } | Sort-Object -Unique)
    $rightValues = @($Right | ForEach-Object { [string]$_ } | Sort-Object -Unique)
    if ($leftValues.Count -ne $rightValues.Count) { return $false }
    for ($index = 0; $index -lt $leftValues.Count; $index++) {
        if (-not $leftValues[$index].Equals($rightValues[$index], [StringComparison]::OrdinalIgnoreCase)) { return $false }
    }
    return $true
}

function Test-GeneratedHashMapsEqual {
    param([AllowNull()]$Left, [AllowNull()]$Right)
    if ($null -eq $Left -or $null -eq $Right) { return $false }
    $leftProperties = if ($Left -is [System.Collections.IDictionary]) { @($Left.Keys | ForEach-Object { [string]$_ } | Sort-Object -Unique) } else { @($Left.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object -Unique) }
    $rightProperties = if ($Right -is [System.Collections.IDictionary]) { @($Right.Keys | ForEach-Object { [string]$_ } | Sort-Object -Unique) } else { @($Right.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object -Unique) }
    if (-not (Test-StringSetEqual $leftProperties $rightProperties)) { return $false }
    foreach ($name in $leftProperties) {
        $leftValue = if ($Left -is [System.Collections.IDictionary]) { [string]$Left[$name] } else { [string]$Left.PSObject.Properties[$name].Value }
        $rightValue = if ($Right -is [System.Collections.IDictionary]) { [string]$Right[$name] } else { [string]$Right.PSObject.Properties[$name].Value }
        if ($leftValue -cne $rightValue) { return $false }
    }
    return $true
}

function Get-PriorManifestForRow {
    param([Parameter(Mandatory = $true)]$Prior)
    $validatedSha = Get-ObjectPropertyText $Prior 'validated_sha'
    if ([string]::IsNullOrWhiteSpace($validatedSha)) { return $null }
    return @($script:PriorManifestHistory | Where-Object {
        $null -ne $_ -and (Get-ObjectPropertyText $_ 'exactSha') -eq $validatedSha
    } | Select-Object -First 1)
}

function Test-ProductionBakeReuseProof {
    param(
        [Parameter(Mandatory = $true)]$Prior,
        [Parameter(Mandatory = $true)]$Row,
        [Parameter(Mandatory = $true)][string]$CurrentSha,
        [string[]]$ChangedPaths = @(),
        [switch]$RequireDescendant,
        $PriorManifest
    )
    if ([string]$Prior.check_id -ne 'production-bake' -or [string]$Row.check_id -ne 'production-bake') { return @{ valid = $false; reason = 'production-bake row identity mismatch' } }
    if ([string]$Prior.status -notin @('executed', 'reused')) { return @{ valid = $false; reason = 'prior production bake was not executed' } }
    $priorSha = Get-ObjectPropertyText $Prior 'validated_sha'
    if ([string]::IsNullOrWhiteSpace($priorSha)) { return @{ valid = $false; reason = 'prior production bake SHA missing' } }
    if ((Get-ObjectPropertyText $Prior 'input_digest') -ne (Get-ObjectPropertyText $Row 'input_digest')) { return @{ valid = $false; reason = 'production bake input digest changed' } }
    if ((Get-ObjectPropertyText $Prior 'environment_fingerprint') -ne (Get-ObjectPropertyText $Row 'environment_fingerprint')) { return @{ valid = $false; reason = 'production bake environment changed' } }
    if (-not (Test-StringSetEqual @(Get-ObjectPropertyValue $Prior 'input_paths') @(Get-ObjectPropertyValue $Row 'input_paths'))) { return @{ valid = $false; reason = 'production bake input coverage changed' } }

    if ($RequireDescendant) {
        if ($null -eq $ChangedPaths -or @($ChangedPaths).Count -eq 0) { return @{ valid = $false; reason = 'production bake descendant has no generated change' } }
        if (-not (Test-IsAncestor $priorSha $CurrentSha)) { return @{ valid = $false; reason = 'production bake prior SHA is not an ancestor' } }
        foreach ($changedPath in @($ChangedPaths)) {
            if (-not (Test-GeneratedPath ([string]$changedPath))) { return @{ valid = $false; reason = 'production bake descendant changed non-generated source' } }
        }
        if (Test-PathIntersects @($ChangedPaths) @(Get-ObjectPropertyValue $Row 'invalidation_paths')) { return @{ valid = $false; reason = 'production bake input path changed' } }
    }

    $evidencePath = Get-ObjectPropertyText $Prior 'evidence_path'
    $evidenceDigest = Get-ObjectPropertyText $Prior 'evidence_digest'
    if ([string]::IsNullOrWhiteSpace($evidencePath) -or -not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) { return @{ valid = $false; reason = 'production bake evidence missing' } }
    try { $evidencePath = Assert-DurableEvidencePath $evidencePath 'Ledger evidence' } catch { return @{ valid = $false; reason = $_.Exception.Message } }
    if ([string]::IsNullOrWhiteSpace($evidenceDigest)) { return @{ valid = $false; reason = 'production bake evidence digest missing' } }
    if ((Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $evidenceDigest.ToLowerInvariant()) { return @{ valid = $false; reason = 'production bake evidence digest mismatch' } }

    $bakeCountText = Get-ObjectPropertyText $Prior 'bake_count'
    if ([string]::IsNullOrWhiteSpace($bakeCountText)) { $bakeCountText = Get-ObjectPropertyText $Prior 'bakeCount' }
    if ([string]::IsNullOrWhiteSpace($bakeCountText) -and $null -ne $PriorManifest) { $bakeCountText = Get-ObjectPropertyText $PriorManifest 'bakeCount' }
    $bakeCount = 0
    try { $bakeCount = [int]$bakeCountText } catch { return @{ valid = $false; reason = 'prior production bake count is invalid' } }
    if ($bakeCount -ne 1) { return @{ valid = $false; reason = 'prior production bake count was not exactly one' } }

    $priorProbe = $null
    if ($Prior.PSObject.Properties.Name -contains 'probe') { $priorProbe = $Prior.probe }
    if ($null -eq $priorProbe -and $Prior.PSObject.Properties.Name -contains 'probe_state') { $priorProbe = $Prior.probe_state }
    if ($null -eq $priorProbe -and $null -ne $PriorManifest -and $PriorManifest.PSObject.Properties.Name -contains 'probe') { $priorProbe = $PriorManifest.probe }
    if ($null -eq $priorProbe) { return @{ valid = $false; reason = 'prior production bake probe state missing' } }
    if ((Get-ObjectPropertyText $priorProbe 'status') -ne 'read') { return @{ valid = $false; reason = 'prior production bake probe was not read' } }
    foreach ($probeField in @('schemaVersion', 'gitSha', 'unityVersion', 'manifestStatus', 'lightingInputDigest', 'sourceSignature', 'outputFingerprint', 'bakedProfile')) {
        if ([string]::IsNullOrWhiteSpace((Get-ObjectPropertyText $priorProbe $probeField))) { return @{ valid = $false; reason = 'prior production bake probe field missing: ' + $probeField } }
    }
    if (@(Get-ObjectPropertyValue $priorProbe 'fingerprintPaths').Count -eq 0 -or @((Get-ObjectPropertyValue $priorProbe 'fingerprintPaths')).Count -ne @((Get-ObjectPropertyValue $priorProbe 'fingerprintHashes')).Count) { return @{ valid = $false; reason = 'prior production bake probe fingerprint coverage incomplete' } }
    return @{ valid = $true; reason = $null; priorProbe = $priorProbe }
}

function Test-ProductionBakeOutputReuseProof {
    param([Parameter(Mandatory = $true)]$Prior, [Parameter(Mandatory = $true)]$Row, [Parameter(Mandatory = $true)]$CurrentProbe)
    if ([string]$CurrentProbe.status -eq 'missing' -and $PlanOnly) { return @{ valid = $true; reason = $null } }
    $priorInventory = @(Get-ObjectPropertyValue $Prior 'generated_inventory')
    $currentInventory = @(Get-ObjectPropertyValue $Row 'generated_inventory')
    if ($priorInventory.Count -eq 0 -or $currentInventory.Count -eq 0 -or -not (Test-StringSetEqual $priorInventory $currentInventory)) { return @{ valid = $false; reason = 'production bake generated inventory changed' } }
    if ([string]::IsNullOrWhiteSpace((Get-ObjectPropertyText $Prior 'generated_hash_digest')) -or
        (Get-ObjectPropertyText $Prior 'generated_hash_digest') -ne (Get-ObjectPropertyText $Row 'generated_hash_digest')) { return @{ valid = $false; reason = 'production bake generated output digest changed' } }
    if (-not (Test-GeneratedHashMapsEqual (Get-ObjectPropertyValue $Prior 'generated_hashes') (Get-ObjectPropertyValue $Row 'generated_hashes'))) { return @{ valid = $false; reason = 'production bake generated output hashes changed' } }
    return @{ valid = $true; reason = $null }
}

function Test-ProductionBakeProbeReuseProof {
    param([Parameter(Mandatory = $true)]$PriorProbe, [Parameter(Mandatory = $true)]$CurrentProbe)
    if ([string]$CurrentProbe.status -eq 'missing' -and $PlanOnly) { return @{ valid = $true; reason = $null } }
    if ([string]$CurrentProbe.status -ne 'read') { return @{ valid = $false; reason = 'current production bake probe was not read' } }
    foreach ($probeField in @('schemaVersion', 'unityVersion', 'manifestStatus', 'lightingInputDigest', 'sourceSignature', 'outputFingerprint', 'bakedProfile')) {
        if ((Get-ObjectPropertyText $PriorProbe $probeField) -cne (Get-ObjectPropertyText $CurrentProbe $probeField)) { return @{ valid = $false; reason = 'production bake probe state changed: ' + $probeField } }
    }
    if (-not (Test-StringSetEqual @(Get-ObjectPropertyValue $PriorProbe 'fingerprintPaths') @(Get-ObjectPropertyValue $CurrentProbe 'fingerprintPaths')) -or
        -not (Test-StringSetEqual @(Get-ObjectPropertyValue $PriorProbe 'fingerprintHashes') @(Get-ObjectPropertyValue $CurrentProbe 'fingerprintHashes'))) { return @{ valid = $false; reason = 'production bake probe output fingerprint changed' } }
    return @{ valid = $true; reason = $null }
}

function Confirm-ProductionBakeReuse {
    param([Parameter(Mandatory = $true)]$Probe)
    if (-not $script:ProductionBakeReuseCandidates.ContainsKey('production-bake')) { return }
    $row = $script:LedgerRows | Where-Object { [string]$_.check_id -eq 'production-bake' } | Select-Object -First 1
    if ($null -eq $row -or [string]$row.status -ne 'reused') { return }
    $candidate = $script:ProductionBakeReuseCandidates['production-bake']
    $proof = Test-ProductionBakeProbeReuseProof $candidate.priorProbe $Probe
    if ($proof.valid) { $proof = Test-ProductionBakeOutputReuseProof $candidate.prior $row $Probe }
    if (-not $proof.valid) {
        $row.status = 'invalidated'
        $row.invalidation_reason = [string]$proof.reason
    }
}

function Refresh-ProductionBakeOutputState {
    $row = $script:LedgerRows | Where-Object { [string]$_.check_id -eq 'production-bake' } | Select-Object -First 1
    if ($null -eq $row) { return }
    $hashes = Get-GeneratedHashes
    $row.generated_inventory = @(Get-AuthoritativeGeneratedInventory)
    $row.generated_hashes = $hashes
    $row.generated_hash_digest = Get-GeneratedHashDigest $hashes
}

function Merge-ExistingLedger {
    param([Parameter(Mandatory = $true)][object[]]$Rows, [Parameter(Mandatory = $true)][string]$CurrentSha)
    if ([string]::IsNullOrWhiteSpace($LedgerPath)) { return }
    $full = Assert-DurableEvidencePath $LedgerPath 'LedgerPath'
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw ('Ledger file missing: ' + $full) }
    $priorManifestPath = Join-Path (Split-Path -Parent $full) 'evidence-manifest.json'
    if (Test-Path -LiteralPath $priorManifestPath -PathType Leaf) {
        try { $priorManifest = Get-Content -Raw -LiteralPath $priorManifestPath | ConvertFrom-Json; $script:PriorManifestHistory.Add($priorManifest) } catch { throw ('Prior evidence manifest is malformed: ' + $priorManifestPath) }
    }
    $parsed = Get-Content -Raw -LiteralPath $full | ConvertFrom-Json
    if ($parsed.PSObject.Properties.Name -contains 'history') {
        foreach ($entry in @($parsed.history)) { $script:PriorLedgerHistory.Add($entry) }
    }
    $hasCheckLedger = $null -ne $parsed -and $parsed.PSObject.Properties.Name -contains 'checkLedger'
    $hasRows = $null -ne $parsed -and $parsed.PSObject.Properties.Name -contains 'rows'
    $priorRows = if ($hasCheckLedger) { @($parsed.checkLedger) } elseif ($hasRows) { @($parsed.rows) } else { @() }
    foreach ($entry in $priorRows) { $script:PriorLedgerHistory.Add($entry) }
    foreach ($row in $Rows) {
        $prior = $priorRows | Where-Object { [string]$_.check_id -eq [string]$row.check_id } | Select-Object -First 1
        if ($null -eq $prior -or [string]$prior.status -notin @('executed', 'reused')) { continue }
        # ProductionValidate is a fresh semantic proof. Historical rows remain
        # history only; no prior evidence may suppress this invocation.
        if ([string]$row.check_id -eq 'production-validator') { continue }
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
        $manualProof = ([string]$row.check_id -match '(bake|manual)') -or ([string]$row.tier -eq 'production-final' -and [bool]$row.mutates_project)
        $reuseProof = Test-RowReuseProof $prior $row
        $isProductionBake = [string]$row.check_id -eq 'production-bake'
        $priorManifestForRow = if ($isProductionBake) { Get-PriorManifestForRow $prior } else { $null }
        $productionBakeProof = $null
        if ($isProductionBake) {
            $productionBakeProof = if ($exact) {
                Test-ProductionBakeReuseProof $prior $row $CurrentSha -PriorManifest $priorManifestForRow
            } elseif ($null -ne $changed) {
                Test-ProductionBakeReuseProof $prior $row $CurrentSha -ChangedPaths @($changed) -RequireDescendant -PriorManifest $priorManifestForRow
            } else {
                @{ valid = $false; reason = 'production bake ancestry or SHA changed' }
            }
        }
        if ($exact) {
            if (-not $sameInputs -or -not $sameEnvironment -or -not $reuseProof.valid -or ($isProductionBake -and -not $productionBakeProof.valid)) {
                $row.status = 'invalidated'
                $row.invalidation_reason = if (-not $sameInputs) { 'input digest changed' } elseif (-not $sameEnvironment) { 'environment fingerprint changed' } elseif ($isProductionBake) { [string]$productionBakeProof.reason } else { [string]$reuseProof.reason }
                continue
            }
            $row.status = 'reused'
            $row.executed_sha = [string]$prior.executed_sha
            $row.validated_sha = $CurrentSha
            $row.evidence_path = $evidencePath
            $row.evidence_digest = $evidenceDigest
            $row.evidence = [ordered]@{ path = $evidencePath; sha256 = $evidenceDigest }
            if ($isProductionBake) {
                $row.generated_inventory = @(Get-ObjectPropertyValue $prior 'generated_inventory')
                $priorBakeCountText = Get-ObjectPropertyText $prior 'bake_count'
                if ([string]::IsNullOrWhiteSpace($priorBakeCountText)) { $priorBakeCountText = Get-ObjectPropertyText $prior 'bakeCount' }
                if ([string]::IsNullOrWhiteSpace($priorBakeCountText) -and $null -ne $priorManifestForRow) { $priorBakeCountText = Get-ObjectPropertyText $priorManifestForRow 'bakeCount' }
                $row.bake_count = [int]$priorBakeCountText
                $row.probe = $productionBakeProof.priorProbe
                $script:ProductionBakeReuseCandidates['production-bake'] = [ordered]@{ priorProbe = $productionBakeProof.priorProbe; prior = $prior; manifest = $priorManifestForRow }
            }
        } elseif ($isProductionBake -and $productionBakeProof.valid) {
            $row.status = 'reused'
            $row.executed_sha = [string]$prior.executed_sha
            $row.validated_sha = $CurrentSha
            $row.evidence_path = $evidencePath
            $row.evidence_digest = $evidenceDigest
            $row.evidence = [ordered]@{ path = $evidencePath; sha256 = $evidenceDigest }
            $row.generated_inventory = @(Get-ObjectPropertyValue $prior 'generated_inventory')
            $priorBakeCountText = Get-ObjectPropertyText $prior 'bake_count'
            if ([string]::IsNullOrWhiteSpace($priorBakeCountText)) { $priorBakeCountText = Get-ObjectPropertyText $prior 'bakeCount' }
            if ([string]::IsNullOrWhiteSpace($priorBakeCountText) -and $null -ne $priorManifestForRow) { $priorBakeCountText = Get-ObjectPropertyText $priorManifestForRow 'bakeCount' }
            $row.bake_count = [int]$priorBakeCountText
            $row.probe = $productionBakeProof.priorProbe
            $script:ProductionBakeReuseCandidates['production-bake'] = [ordered]@{ priorProbe = $productionBakeProof.priorProbe; prior = $prior; manifest = $priorManifestForRow }
        } elseif ($pureReattest -and -not $manualProof) {
            $row.status = 'reused'
            $row.executed_sha = [string]$prior.executed_sha
            $row.validated_sha = $CurrentSha
            $row.evidence_path = $evidencePath
            $row.evidence_digest = $evidenceDigest
            $row.evidence = [ordered]@{ path = $evidencePath; sha256 = $evidenceDigest }
        } elseif ($pureReattest -and $manualProof -and $reuseProof.valid) {
            $row.status = 'reused'
            $row.executed_sha = [string]$prior.executed_sha
            $row.validated_sha = $CurrentSha
            $row.evidence_path = $evidencePath
            $row.evidence_digest = $evidenceDigest
            $row.evidence = [ordered]@{ path = $evidencePath; sha256 = $evidenceDigest }
        } else {
            $row.status = 'invalidated'
            $row.invalidation_reason = if ($null -eq $changed) { 'ancestry or SHA changed' } elseif (Test-PathIntersects @($changed) @($row.invalidation_paths)) { 'input path changed' } elseif ($manualProof -and -not $reuseProof.valid) { [string]$reuseProof.reason } else { 'proof cannot reattest' }
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

function Get-ProbeStringValue {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Violations,
        [Parameter(Mandatory = $true)]$Probe,
        [Parameter(Mandatory = $true)][string]$Field,
        [Parameter(Mandatory = $true)][string]$Label
    )
    $value = $Probe.PSObject.Properties[$Field].Value
    if ($value -isnot [string]) {
        Add-WorkflowViolation $Violations ($Label + '.type') ('Stage probe ' + $Field + ' must be a string.')
        return $null
    }
    $text = [string]$value
    if ([string]::IsNullOrWhiteSpace($text) -or $text.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) {
        Add-WorkflowViolation $Violations ($Label + '.shape') ('Stage probe ' + $Field + ' must be non-empty and one line.')
        return $null
    }
    return $text.Trim()
}

function Get-ProbeStringArrayValue {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Violations,
        [Parameter(Mandatory = $true)]$Probe,
        [Parameter(Mandatory = $true)][string]$Field,
        [Parameter(Mandatory = $true)][string]$Label
    )
    $value = $Probe.PSObject.Properties[$Field].Value
    if ($null -eq $value -or $value -is [string] -or $value -isnot [System.Collections.IEnumerable]) {
        Add-WorkflowViolation $Violations ($Label + '.type') ('Stage probe ' + $Field + ' must be an array of strings.')
        return $null
    }
    $values = @($value)
    for ($index = 0; $index -lt $values.Count; $index++) {
        $item = $values[$index]
        if ($item -isnot [string]) {
            Add-WorkflowViolation $Violations ($Label + '.item[' + $index + '].type') ('Stage probe ' + $Field + ' must contain only strings.')
            continue
        }
        if ([string]::IsNullOrWhiteSpace([string]$item) -or ([string]$item).IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) {
            Add-WorkflowViolation $Violations ($Label + '.item[' + $index + '].shape') ('Stage probe ' + $Field + ' contains an empty or multi-line value.')
        }
    }
    return ,$values
}

function Read-ProbeContract {
    $path = $script:ProbeOutputPath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $script:ProbeInventoryPaths = @()
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

    $violations = New-WorkflowViolationList
    $raw = $null
    try { $raw = Get-Content -Raw -LiteralPath $path -ErrorAction Stop } catch {
        Add-WorkflowViolation $violations 'probe.root.read' ('Stage probe could not be read: ' + $_.Exception.Message)
        Complete-WorkflowValidationPhase 'probe' $violations
    }
    $probe = $null
    try { $probe = $raw | ConvertFrom-Json -ErrorAction Stop } catch {
        Add-WorkflowViolation $violations 'probe.root.parse' ('Stage probe JSON is unparseable: ' + $_.Exception.Message)
        Complete-WorkflowValidationPhase 'probe' $violations
    }
    if ($null -eq $probe -or $probe -isnot [pscustomobject]) {
        Add-WorkflowViolation $violations 'probe.root.shape' 'Stage probe JSON root must be an object.'
        Complete-WorkflowValidationPhase 'probe' $violations
    }

    $requiredFields = @(
        'schemaVersion', 'gitSha', 'unityVersion', 'manifestStatus', 'staleStages', 'staleReasons',
        'lightingInputDigest', 'sourceSignature', 'outputFingerprint', 'bakedProfile', 'fingerprintPaths', 'fingerprintHashes'
    )
    $propertyNames = @($probe.PSObject.Properties.Name)
    foreach ($field in $requiredFields) {
        if (-not $propertyNames.Contains($field)) {
            Add-WorkflowViolation $violations ('probe.field.' + $field + '.missing') ('Stage probe field is missing: ' + $field)
        }
    }
    $schemaValue = $null
    if ($propertyNames.Contains('schemaVersion')) {
        $schemaValue = $probe.schemaVersion
        $schemaValid = ($schemaValue -is [int16]) -or ($schemaValue -is [int32]) -or ($schemaValue -is [int64]) -or
            ($schemaValue -is [uint16]) -or ($schemaValue -is [uint32]) -or ($schemaValue -is [uint64])
        if (-not $schemaValid) {
            Add-WorkflowViolation $violations 'probe.schemaVersion.type' 'Stage probe schemaVersion must be an integer.'
        } elseif ([int64]$schemaValue -ne 1) {
            Add-WorkflowViolation $violations 'probe.schemaVersion.value' 'Stage probe schemaVersion must equal 1.'
        }
    }
    $sha = if ($propertyNames.Contains('gitSha')) { Get-ProbeStringValue $violations $probe 'gitSha' 'probe.gitSha' } else { $null }
    if ($null -ne $sha -and $sha -notmatch '^[0-9a-fA-F]{40}$') {
        Add-WorkflowViolation $violations 'probe.gitSha.shape' 'Stage probe gitSha must be an exact 40-character SHA.'
        $sha = $null
    } elseif ($null -ne $sha) {
        $sha = $sha.ToLowerInvariant()
        if ($sha -ne (Get-HeadSha)) { Add-WorkflowViolation $violations 'probe.gitSha.binding' 'Stage probe Git SHA does not match current HEAD.' }
    }
    $unityVersion = if ($propertyNames.Contains('unityVersion')) { Get-ProbeStringValue $violations $probe 'unityVersion' 'probe.unityVersion' } else { $null }
    if ($null -ne $unityVersion -and $unityVersion -cne $script:UnityVersion) { Add-WorkflowViolation $violations 'probe.unityVersion.binding' ('Stage probe Unity version must equal ' + $script:UnityVersion + '.') }
    $manifestStatus = if ($propertyNames.Contains('manifestStatus')) { Get-ProbeStringValue $violations $probe 'manifestStatus' 'probe.manifestStatus' } else { $null }
    if ($null -ne $manifestStatus -and $manifestStatus -notin @('current', 'stale')) { Add-WorkflowViolation $violations 'probe.manifestStatus.value' ('Stage probe manifestStatus is invalid: ' + $manifestStatus) }
    $stale = if ($propertyNames.Contains('staleStages')) { Get-ProbeStringArrayValue $violations $probe 'staleStages' 'probe.staleStages' } else { $null }
    $staleReasons = if ($propertyNames.Contains('staleReasons')) { Get-ProbeStringArrayValue $violations $probe 'staleReasons' 'probe.staleReasons' } else { $null }
    if ($null -ne $stale -and $null -ne $staleReasons -and $stale.Count -ne $staleReasons.Count) { Add-WorkflowViolation $violations 'probe.stale.countParity' 'Stage probe staleStages and staleReasons counts must match.' }
    $lighting = if ($propertyNames.Contains('lightingInputDigest')) { Get-ProbeStringValue $violations $probe 'lightingInputDigest' 'probe.lightingInputDigest' } else { $null }
    $sourceSignature = if ($propertyNames.Contains('sourceSignature')) { Get-ProbeStringValue $violations $probe 'sourceSignature' 'probe.sourceSignature' } else { $null }
    $outputFingerprint = if ($propertyNames.Contains('outputFingerprint')) { Get-ProbeStringValue $violations $probe 'outputFingerprint' 'probe.outputFingerprint' } else { $null }
    $bakedProfile = if ($propertyNames.Contains('bakedProfile')) { Get-ProbeStringValue $violations $probe 'bakedProfile' 'probe.bakedProfile' } else { $null }
    if ($null -ne $bakedProfile -and $bakedProfile -notin @('none', 'development', 'production')) { Add-WorkflowViolation $violations 'probe.bakedProfile.value' ('Stage probe bakedProfile is invalid: ' + $bakedProfile) }
    $fingerprintPaths = if ($propertyNames.Contains('fingerprintPaths')) { Get-ProbeStringArrayValue $violations $probe 'fingerprintPaths' 'probe.fingerprintPaths' } else { $null }
    $fingerprintHashes = if ($propertyNames.Contains('fingerprintHashes')) { Get-ProbeStringArrayValue $violations $probe 'fingerprintHashes' 'probe.fingerprintHashes' } else { $null }
    if ($null -ne $fingerprintPaths -and $null -ne $fingerprintHashes -and $fingerprintPaths.Count -ne $fingerprintHashes.Count) { Add-WorkflowViolation $violations 'probe.fingerprint.countParity' 'Stage probe fingerprintPaths and fingerprintHashes counts must match.' }
    foreach ($hashRecord in @(
        @{ name = 'lightingInputDigest'; value = $lighting },
        @{ name = 'sourceSignature'; value = $sourceSignature },
        @{ name = 'outputFingerprint'; value = $outputFingerprint }
    )) {
        if ($null -ne $hashRecord.value -and [string]$hashRecord.value -notmatch '^[0-9a-fA-F]{32,128}$') { Add-WorkflowViolation $violations ('probe.' + $hashRecord.name + '.shape') ('Stage probe ' + $hashRecord.name + ' must be a hexadecimal hash.') }
    }
    if ($null -ne $fingerprintHashes) {
        for ($index = 0; $index -lt $fingerprintHashes.Count; $index++) {
            if ([string]$fingerprintHashes[$index] -notmatch '^[0-9a-fA-F]{32,128}$') { Add-WorkflowViolation $violations ('probe.fingerprintHashes.item[' + $index + '].shape') 'Stage probe fingerprint hash must be hexadecimal.' }
        }
    }
    Complete-WorkflowValidationPhase 'probe' $violations
    $script:ProbeInventoryPaths = @($fingerprintPaths)
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    return [ordered]@{
        status = 'read'; path = $path; schemaVersion = [int]$schemaValue; sha256 = $hash; gitSha = $sha; unityVersion = $unityVersion; manifestStatus = $manifestStatus
        staleStages = @($stale); staleReasons = @($staleReasons); lightingInputDigest = $lighting; sourceSignature = $sourceSignature; outputFingerprint = $outputFingerprint
        bakedProfile = $bakedProfile; fingerprintPaths = @($fingerprintPaths); fingerprintHashes = @($fingerprintHashes); raw = $probe
    }
}

function Assert-ProbeContractForMode {
    param([Parameter(Mandatory = $true)]$Probe, [Parameter(Mandatory = $true)][string]$WorkflowMode)
    $violations = New-WorkflowViolationList
    if ([string]$Probe.status -eq 'missing') {
        if (-not $PlanOnly) { Add-WorkflowViolation $violations 'mode.probe.required' ('Stage probe is required for executing ' + $WorkflowMode + ' workflow; missing probe is allowed only for PlanOnly.'); Complete-WorkflowValidationPhase ('mode-' + $WorkflowMode) $violations }
        return
    }
    if ([string]$Probe.status -ne 'read') { Add-WorkflowViolation $violations 'mode.probe.status' 'Stage probe must be read before workflow execution.'; Complete-WorkflowValidationPhase ('mode-' + $WorkflowMode) $violations }
    if ([int]$Probe.schemaVersion -ne 1) { Add-WorkflowViolation $violations 'mode.schemaVersion.value' 'Stage probe schemaVersion 1 is required before workflow execution.' }
    if ([string]$Probe.gitSha -ne (Get-HeadSha)) { Add-WorkflowViolation $violations 'mode.gitSha.binding' 'Stage probe SHA binding failed.' }
    if ([string]$Probe.unityVersion -cne $script:UnityVersion) { Add-WorkflowViolation $violations 'mode.unityVersion.binding' ('Stage probe Unity version must equal ' + $script:UnityVersion + '.') }
    if ([string]$Probe.manifestStatus -notin @('current', 'stale')) { Add-WorkflowViolation $violations 'mode.manifestStatus.value' ('Stage probe manifestStatus is invalid: ' + [string]$Probe.manifestStatus) }
    $staleStages = @($Probe.staleStages); $staleReasons = @($Probe.staleReasons)
    if ($staleStages.Count -ne $staleReasons.Count) { Add-WorkflowViolation $violations 'mode.stale.countParity' 'Stage probe staleStages/staleReasons coverage mismatch.' }
    if ([string]$Probe.manifestStatus -eq 'current' -and $staleStages.Count -gt 0) { Add-WorkflowViolation $violations 'mode.stale.currentCoverage' 'Stage probe current manifest cannot list stale stages.' }
    if ([string]$Probe.manifestStatus -eq 'stale' -and $staleStages.Count -eq 0) { Add-WorkflowViolation $violations 'mode.stale.requiredCoverage' 'Stage probe stale manifest must list stale stages and reasons.' }
    for ($index = 0; $index -lt $staleStages.Count; $index++) { if ([string]::IsNullOrWhiteSpace([string]$staleStages[$index])) { Add-WorkflowViolation $violations ('mode.staleStages.item[' + $index + ']') 'Stage probe staleStages cannot contain empty values.' } }
    for ($index = 0; $index -lt $staleReasons.Count; $index++) { if ([string]::IsNullOrWhiteSpace([string]$staleReasons[$index])) { Add-WorkflowViolation $violations ('mode.staleReasons.item[' + $index + ']') 'Stage probe staleReasons cannot contain empty values.' } }
    $fingerprintPaths = @($Probe.fingerprintPaths); $fingerprintHashes = @($Probe.fingerprintHashes)
    if ($fingerprintPaths.Count -eq 0) { Add-WorkflowViolation $violations 'mode.fingerprint.coverage' 'Stage probe fingerprint coverage is incomplete.' }
    if ($fingerprintPaths.Count -ne $fingerprintHashes.Count) { Add-WorkflowViolation $violations 'mode.fingerprint.countParity' 'Stage probe fingerprintPaths/fingerprintHashes coverage mismatch.' }
    $seen = @{}
    for ($index = 0; $index -lt $fingerprintPaths.Count; $index++) {
        $normalized = ([string]$fingerprintPaths[$index]).Replace('\', '/').TrimStart('/')
        if (-not (Test-ProbeInventoryMember $normalized)) { Add-WorkflowViolation $violations ('mode.fingerprintPaths.item[' + $index + '].scope') ('Stage probe fingerprint path is outside closed inventory: ' + $normalized) }
        elseif ($seen.ContainsKey($normalized)) { Add-WorkflowViolation $violations ('mode.fingerprintPaths.item[' + $index + '].duplicate') ('Stage probe fingerprint path is duplicated: ' + $normalized) }
        else { $seen[$normalized] = $true }
    }
    for ($index = 0; $index -lt $fingerprintHashes.Count; $index++) { if ([string]$fingerprintHashes[$index] -notmatch '^[0-9a-fA-F]{32,128}$') { Add-WorkflowViolation $violations ('mode.fingerprintHashes.item[' + $index + '].shape') 'Stage probe fingerprint hash is invalid.' } }
    if ($WorkflowMode -in @('Development', 'ProductionValidate', 'ProductionPrepareFinal') -and [string]$Probe.manifestStatus -ne 'current') { Add-WorkflowViolation $violations 'mode.manifestStatus.currentRequired' ($WorkflowMode + ' requires a current stage probe manifest.') }
    if ($WorkflowMode -eq 'Development' -and [string]$Probe.bakedProfile -cne 'development') { Add-WorkflowViolation $violations 'mode.bakedProfile.development' 'Development workflow requires bakedProfile=development.' }
    if ($WorkflowMode -in @('ProductionValidate', 'ProductionPrepareFinal') -and [string]$Probe.bakedProfile -cne 'production') { Add-WorkflowViolation $violations 'mode.bakedProfile.production' ($WorkflowMode + ' requires bakedProfile=production.') }
    Complete-WorkflowValidationPhase ('mode-' + $WorkflowMode) $violations
}

function Write-AtomicJson {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)]$Value)
    $directory = Split-Path -Parent $Path
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporary = Join-Path $directory ('.movement-lab-workflow-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $json = ($Value | ConvertTo-Json -Depth 14) + "`n"
    try {
        [System.IO.File]::WriteAllText($temporary, $json, (New-Object System.Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $Path) { throw ('Immutable evidence path already exists: ' + $Path) }
        [System.IO.File]::Move($temporary, $Path)
    } finally {
        if (Test-Path -LiteralPath $temporary) { [System.IO.File]::Delete($temporary) }
    }
}

$preflightViolations = New-WorkflowViolationList
$projectPathText = [string]$ProjectPath
$projectPathShapeValid = $true
if ([string]::IsNullOrWhiteSpace($projectPathText) -or $projectPathText.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) {
    Add-WorkflowViolation $preflightViolations 'preflight.argument.ProjectPath.shape' 'ProjectPath must be non-empty and one line.'
    $projectPathShapeValid = $false
}
if ($projectPathShapeValid -and -not [System.IO.Path]::IsPathRooted($projectPathText)) {
    Add-WorkflowViolation $preflightViolations 'preflight.argument.ProjectPath.absolute' 'ProjectPath must be absolute.'
    $projectPathShapeValid = $false
}
if ($TimeoutSeconds -le 0) { Add-WorkflowViolation $preflightViolations 'preflight.argument.TimeoutSeconds' 'TimeoutSeconds must be greater than zero.' }
if ($projectPathShapeValid) {
    $projectInputRoot = $null
    try { $projectInputRoot = [System.IO.Path]::GetFullPath($projectPathText) } catch { throw ('ProjectPath canonicalization failed: ' + $_.Exception.Message) }
    $script:ProjectInputRoot = $projectInputRoot
    if (-not (Test-Path -LiteralPath $projectInputRoot -PathType Container)) {
        Add-WorkflowViolation $preflightViolations 'preflight.repository.ProjectPath.exists' ('Project path not found: ' + $projectInputRoot)
    } else {
        try { $script:ProjectRoot = Get-CanonicalPath $projectInputRoot } catch { throw ('ProjectPath canonicalization failed: ' + $_.Exception.Message) }
        $script:CanonicalProjectRoot = $script:ProjectRoot
        if ($script:ProjectRoot.Length -gt 80) { Add-WorkflowViolation $preflightViolations 'preflight.repository.ProjectPath.length' 'Project path must be short (80 characters or fewer).' }
    }
}
if ($null -eq $script:ProjectRoot) { Complete-WorkflowValidationPhase 'preflight' $preflightViolations }
$projectVersionPath = Join-Path $script:ProjectRoot 'ProjectSettings\ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $projectVersionPath -PathType Leaf)) {
    Add-WorkflowViolation $preflightViolations 'preflight.config.ProjectVersionPath' 'ProjectVersion.txt missing.'
} else {
    try { $projectVersion = Get-Content -Raw -LiteralPath $projectVersionPath -ErrorAction Stop } catch { throw ('ProjectVersion.txt could not be read: ' + $_.Exception.Message) }
    if ($projectVersion -notmatch ('m_EditorVersion:\s*' + [Regex]::Escape($script:UnityVersion))) { Add-WorkflowViolation $preflightViolations 'preflight.config.unityVersion' ('Project Unity version is not ' + $script:UnityVersion + '.') }
}
if (-not $PlanOnly -and -not (Test-Path -LiteralPath $script:UnityPath -PathType Leaf)) { Add-WorkflowViolation $preflightViolations 'preflight.config.unityPath' ('Unity ' + $script:UnityVersion + ' not found: ' + $script:UnityPath) }
$libraryPath = Get-FullPath (Join-Path $script:ProjectRoot 'Library')
if (-not $PlanOnly) {
    if (-not (Test-Path -LiteralPath $libraryPath -PathType Container)) { Add-WorkflowViolation $preflightViolations 'preflight.config.library.missing' 'Warm private Library is missing.' }
    else {
        $libraryItem = Get-Item -LiteralPath $libraryPath
        if ($libraryItem.LinkType -or ($libraryItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) { Add-WorkflowViolation $preflightViolations 'preflight.config.library.private' 'Library must remain private to this project; shared link detected.' }
    }
}

$commonRaw = Invoke-Git @('rev-parse', '--git-common-dir')
$commonGit = if ([System.IO.Path]::IsPathRooted($commonRaw)) { Get-FullPath $commonRaw } else { Get-FullPath (Join-Path $script:ProjectRoot $commonRaw) }
try { $script:GitCommonRoot = Get-CanonicalPath $commonGit } catch { throw ('Git-common path canonicalization failed: ' + $_.Exception.Message) }
$commonGit = $script:GitCommonRoot
$projectRoot = $script:ProjectRoot.TrimEnd('\')
if ($commonGit.Equals($projectRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $commonGit.StartsWith($projectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw ('Git-common evidence destination must be outside project: ' + $commonGit)
}
$attemptValue = if ([string]::IsNullOrWhiteSpace($AttemptId)) { [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') } else { [string]$AttemptId }
$attemptPathSafe = $true
if (-not [string]::IsNullOrWhiteSpace($AttemptId)) {
    if ($attemptValue.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) { Add-WorkflowViolation $preflightViolations 'preflight.argument.AttemptId.shape' 'AttemptId must be non-empty and one line.'; $attemptPathSafe = $false }
    elseif ($attemptValue -notmatch '^[A-Za-z0-9._-]+$') { Add-WorkflowViolation $preflightViolations 'preflight.argument.AttemptId.characters' 'AttemptId must contain only letters, digits, dot, underscore, or hyphen.'; $attemptPathSafe = $false }
    if ($attemptValue.Length -gt 80) { Add-WorkflowViolation $preflightViolations 'preflight.argument.AttemptId.length' 'AttemptId must be 80 characters or fewer.'; $attemptPathSafe = $false }
}
$attemptPathValue = if ($attemptPathSafe) { $attemptValue } else { [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') }
$script:InvocationId = $attemptPathValue + '-' + [Guid]::NewGuid().ToString('N')
$evidenceRootPathSafe = $true
$probePathPathSafe = $true
$ledgerPathPathSafe = $true
foreach ($pathArgument in @(
    @{ name = 'EvidenceRoot'; value = [string]$EvidenceRoot },
    @{ name = 'ProbePath'; value = [string]$ProbePath },
    @{ name = 'LedgerPath'; value = [string]$LedgerPath }
)) {
    if (-not [string]::IsNullOrWhiteSpace($pathArgument.value) -and $pathArgument.value.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) {
        Add-WorkflowViolation $preflightViolations ('preflight.argument.' + $pathArgument.name + '.shape') ($pathArgument.name + ' must be one line.')
        if ($pathArgument.name -eq 'EvidenceRoot') { $evidenceRootPathSafe = $false }
        elseif ($pathArgument.name -eq 'ProbePath') { $probePathPathSafe = $false }
        elseif ($pathArgument.name -eq 'LedgerPath') { $ledgerPathPathSafe = $false }
    }
}
for ($generatedIndex = 0; $generatedIndex -lt @($GeneratedPath).Count; $generatedIndex++) {
    $generatedValue = [string]$GeneratedPath[$generatedIndex]
    if ([string]::IsNullOrWhiteSpace($generatedValue) -or $generatedValue.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) { Add-WorkflowViolation $preflightViolations ('preflight.argument.GeneratedPath[' + $generatedIndex + '].shape') 'GeneratedPath must be non-empty and one line.' }
    elseif ([IO.Path]::IsPathRooted($generatedValue) -or -not (Test-InventoryMember $generatedValue)) { Add-WorkflowViolation $preflightViolations ('preflight.repository.GeneratedPath[' + $generatedIndex + '].scope') ('GeneratedPath is outside authoritative inventory: ' + $generatedValue) }
}
$evidenceBase = if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    Assert-DurableEvidencePath (Get-FullPath (Join-Path $commonGit ('movement-lab-proof\' + $attemptPathValue))) 'EvidenceRoot'
} elseif ($evidenceRootPathSafe) {
    Assert-DurableEvidencePath $EvidenceRoot 'EvidenceRoot'
} else {
    Assert-DurableEvidencePath (Get-FullPath (Join-Path $commonGit ('movement-lab-proof\' + $attemptPathValue))) 'EvidenceRoot'
}
$script:EvidenceDirectory = Assert-DurableEvidencePath (Join-Path $evidenceBase ('invocation-' + $script:InvocationId)) 'InvocationEvidenceRoot'
if (Test-Path -LiteralPath $script:EvidenceDirectory) { throw ('Evidence invocation path already exists; refusing overwrite: ' + $script:EvidenceDirectory) }
 $script:ProbeOutputPath = if ([string]::IsNullOrWhiteSpace($ProbePath) -or -not $probePathPathSafe) {
    Join-Path $script:EvidenceDirectory 'movement-lab-stage-probe.json'
} else {
    Assert-DurableEvidencePath $ProbePath 'ProbePath'
}
if (-not [string]::IsNullOrWhiteSpace($LedgerPath) -and $ledgerPathPathSafe) { $null = Assert-DurableEvidencePath $LedgerPath 'LedgerPath' }
if (-not $PlanOnly -and (Test-Path -LiteralPath $script:ProbeOutputPath)) { throw ('Probe path already exists; refusing overwrite: ' + $script:ProbeOutputPath) }

$beforeHead = Get-HeadSha
$dirtyBefore = @(Get-NonGeneratedDirtyPaths)
if ($Mode -eq 'ProductionPrepare' -and $dirtyBefore.Count -gt 0) { Add-WorkflowViolation $preflightViolations 'preflight.repository.dirtyScope' ('Non-generated source is dirty: ' + ($dirtyBefore -join ', ')) }
Complete-WorkflowValidationPhase 'preflight' $preflightViolations

New-Item -ItemType Directory -Force -Path $script:EvidenceDirectory | Out-Null
$evidenceItem = Get-Item -LiteralPath $script:EvidenceDirectory -Force
if (($evidenceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Evidence invocation path may not be a junction or alias: ' + $script:EvidenceDirectory) }
$canonicalEvidenceParent = Get-CanonicalPath (Split-Path -Parent $script:EvidenceDirectory)
if ($canonicalEvidenceParent.Equals($script:CanonicalProjectRoot, [StringComparison]::OrdinalIgnoreCase) -or $canonicalEvidenceParent.StartsWith($script:CanonicalProjectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence destination resolves inside project.' }
$script:LeaseReleaseProofPath = Join-Path $script:EvidenceDirectory 'lease-release-proof.json'
$logs = Join-Path $script:EvidenceDirectory 'logs'
if (-not (Test-Path -LiteralPath $logs -PathType Container)) { New-Item -ItemType Directory -Force -Path $logs | Out-Null }
Acquire-ProjectLease | Out-Null
try {
Assert-NoProjectProcessOrLock

$beforeHashes = Get-GeneratedHashes
$ledger = New-CheckLedger
$script:LedgerRows = $ledger
Merge-ExistingLedger $ledger $beforeHead
$probeRecord = $null

try {
    switch ($Mode) {
        'Fast' {
            if (Test-CheckPending 'compile') { Invoke-UnityStep 'CompileAndProbe' 'RocketFooxball.Editor.MovementLabBuilder.ProbeMovementLabGeneratedState' @('-movementLabProbePath', $script:ProbeOutputPath) $false -NoGraphics; Mark-CheckExecuted 'compile' } else { Mark-CheckReused 'compile' }
            $probeRecord = Read-ProbeContract
            Assert-ProbeContractForMode $probeRecord 'Fast'
            if (Test-CheckPending 'stage-probe') { Mark-CheckExecuted 'stage-probe' } else { Mark-CheckReused 'stage-probe' }
            if (Test-CheckPending 'fast-build') { Invoke-UnityStep 'BuildFast' 'RocketFooxball.Editor.MovementLabBuilder.BuildMovementLabFast' @('-movementLabProbePath', $script:ProbeOutputPath) $true -NoGraphics; Mark-CheckExecuted 'fast-build' } else { Mark-CheckReused 'fast-build' }
        }
        'Development' {
            if (Test-CheckPending 'fast-build') { Invoke-UnityStep 'BuildFast' 'RocketFooxball.Editor.MovementLabBuilder.BuildMovementLabFast' @('-movementLabProbePath', $script:ProbeOutputPath) $true -NoGraphics; Mark-CheckExecuted 'fast-build' } else { Mark-CheckReused 'fast-build' }
            if (Test-CheckPending 'development-bake') { Invoke-UnityStep 'DevelopmentBake' 'RocketFooxball.Editor.MovementLabBuilder.BakeMovementLabLightingDevelopment' @('-movementLabProbePath', $script:ProbeOutputPath) $true; Mark-CheckExecuted 'development-bake' } else { Mark-CheckReused 'development-bake' }
            $probeRecord = Read-ProbeContract
            Assert-ProbeContractForMode $probeRecord 'Development'
        }
        'ProductionPrepare' {
            if (Test-CheckPending 'stage-probe') { Invoke-UnityStep 'Probe' 'RocketFooxball.Editor.MovementLabBuilder.ProbeMovementLabGeneratedState' @('-movementLabProbePath', $script:ProbeOutputPath) $false -NoGraphics; Mark-CheckExecuted 'stage-probe' } else { Mark-CheckReused 'stage-probe' }
            $probeRecord = Read-ProbeContract
            Assert-ProbeContractForMode $probeRecord 'ProductionPrepare'
            Refresh-ProductionBakeOutputState
            Confirm-ProductionBakeReuse $probeRecord
            $productionBakePending = Test-CheckPending 'production-bake'
            if ($productionBakePending) {
                Invoke-UnityStep 'StaleAssembly' 'RocketFooxball.Editor.MovementLabBuilder.AssembleMovementLab' @('-movementLabProbePath', $script:ProbeOutputPath) $true
            }
            if (Test-CheckPending 'prebake-validate') { Invoke-UnityStep 'PreBakeValidate' 'RocketFooxball.Editor.MovementLabBuilder.ValidateMovementLabPreBake' @('-movementLabProbePath', $script:ProbeOutputPath) $false -NoGraphics; Mark-CheckExecuted 'prebake-validate' } else { Mark-CheckReused 'prebake-validate' }
            if ($script:BakeCount -gt 0) { throw 'ProductionPrepare attempted a bake before production bake step.' }
            $productionBakePending = Test-CheckPending 'production-bake'
            if ($productionBakePending) {
                Invoke-UnityStep 'ProductionBake' 'RocketFooxball.Editor.MovementLabBuilder.BakeMovementLabLighting' @('-movementLabProbePath', $script:ProbeOutputPath) $true
                Mark-CheckExecuted 'production-bake'
                if (-not $PlanOnly -and $script:BakeCount -ne 1) { throw ('Production bake count must equal one: ' + $script:BakeCount) }
            } else {
                Mark-CheckReused 'production-bake'
                if ($script:BakeCount -ne 0) { throw ('Reused production bake row must not invoke a bake: ' + $script:BakeCount) }
            }
            $probeRecord = Read-ProbeContract
            Assert-ProbeContractForMode $probeRecord 'ProductionPrepareFinal'
        }
        'ProductionValidate' {
            if (Test-CheckPending 'production-validator') { Invoke-UnityStep 'ProductionValidate' 'RocketFooxball.Editor.MovementLabBuilder.ValidateMovementLab' @('-movementLabProbePath', $script:ProbeOutputPath) $false -NoGraphics; Mark-CheckExecuted 'production-validator' } else { Mark-CheckReused 'production-validator' }
            $probeRecord = Read-ProbeContract
            Assert-ProbeContractForMode $probeRecord 'ProductionValidate'
        }
    }
} finally {
    if (-not $PlanOnly) { Wait-UnityRelease 'workflow-final' | Out-Null }
}

$afterHashes = Get-GeneratedHashes
$changedGeneratedPaths = @(Get-ChangedHashPaths $beforeHashes $afterHashes)
$afterGeneratedHashDigest = Get-GeneratedHashDigest $afterHashes
$afterWorkingTreeDigest = Get-WorkingTreeDigest
$dirtyAfter = @(Get-NonGeneratedDirtyPaths)
$postflightViolations = New-WorkflowViolationList
if ($Mode -eq 'ProductionPrepare') {
    if ($dirtyAfter.Count -gt 0) { Add-WorkflowViolation $postflightViolations 'postflight.dirtyScope.nonGenerated' ('Production preparation changed non-generated source: ' + ($dirtyAfter -join ', ')) }
    if (($dirtyBefore -join "`n") -cne ($dirtyAfter -join "`n")) { Add-WorkflowViolation $postflightViolations 'postflight.dirtyScope.stability' 'Production preparation changed non-generated Git status.' }
}
if ($script:BakeCount -gt 1) { Add-WorkflowViolation $postflightViolations 'postflight.bakeCount.max' ('Workflow bake count exceeded one: ' + $script:BakeCount) }
for ($generatedIndex = 0; $generatedIndex -lt $changedGeneratedPaths.Count; $generatedIndex++) {
    if (-not (Test-GeneratedPath ([string]$changedGeneratedPaths[$generatedIndex]))) {
        Add-WorkflowViolation $postflightViolations ('postflight.generatedScope[' + $generatedIndex + ']') ('Changed generated path is outside authoritative generated scope: ' + [string]$changedGeneratedPaths[$generatedIndex])
    }
}
if ([string]$afterGeneratedHashDigest -notmatch '^[0-9a-fA-F]{64}$') { Add-WorkflowViolation $postflightViolations 'postflight.evidence.generatedHashDigest' 'Generated hash digest is not a SHA-256 value.' }
if ([string]$afterWorkingTreeDigest -notmatch '^[0-9a-fA-F]{64}$') { Add-WorkflowViolation $postflightViolations 'postflight.evidence.workingTreeDigest' 'Working-tree digest is not a SHA-256 value.' }
$expectedChangedGeneratedPaths = @(Get-ChangedHashPaths $beforeHashes $afterHashes)
if (-not (Test-StringSetEqual $changedGeneratedPaths $expectedChangedGeneratedPaths)) { Add-WorkflowViolation $postflightViolations 'postflight.evidence.generatedChanges' 'Generated change inventory is inconsistent with before/after hashes.' }
$afterHead = Get-HeadSha
if ($beforeHead -cne $afterHead) { Add-WorkflowViolation $postflightViolations 'postflight.head.stability' ('Git HEAD changed during workflow: expected ' + $beforeHead + ', observed ' + $afterHead) }
Complete-WorkflowValidationPhase 'postflight' $postflightViolations

$ledgerEvidencePath = Join-Path $script:EvidenceDirectory 'check-ledger.json'
$ledgerPayloadPath = Join-Path $script:EvidenceDirectory 'check-ledger-payload.json'
foreach ($row in $ledger) {
    if ($PlanOnly) { $row.status = 'deferred' }
    elseif ($script:ExecutedCheckIds.Contains([string]$row.check_id)) {
        $row.status = 'executed'
        $row.executed_sha = $beforeHead
        $row.validated_sha = $afterHead
        $row.generated_hashes = $afterHashes
        $row.generated_hash_digest = $afterGeneratedHashDigest
        $row.working_tree_digest = $afterWorkingTreeDigest
        if ([string]$row.check_id -eq 'production-bake') {
            $row.generated_inventory = @(Get-AuthoritativeGeneratedInventory)
            $row.bake_count = $script:BakeCount
            $row.probe = $probeRecord
        }
        $row.invocation_id = $script:InvocationId
        # Point rows at immutable payload before writing it. Final ledger then
        # records payload digest without self-referential hashing.
        $row.evidence_path = $ledgerPayloadPath
        $row.evidence_digest = $null
        $row.evidence = [ordered]@{ path = $ledgerPayloadPath; sha256 = $null }
    }
}
Write-AtomicJson $ledgerPayloadPath ([ordered]@{ schemaVersion = 1; exactSha = $afterHead; invocationId = $script:InvocationId; history = @($script:PriorLedgerHistory.ToArray()); rows = @($ledger) })
$ledgerEvidenceDigest = (Get-FileHash -LiteralPath $ledgerPayloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
foreach ($row in $ledger) {
    if ($row.status -eq 'executed') {
        $row.evidence_digest = $ledgerEvidenceDigest
        $row.evidence = [ordered]@{ path = $ledgerPayloadPath; sha256 = $ledgerEvidenceDigest }
    }
}
Write-AtomicJson $ledgerEvidencePath ([ordered]@{ schemaVersion = 1; exactSha = $afterHead; invocationId = $script:InvocationId; history = @($script:PriorLedgerHistory.ToArray()); rows = @($ledger) })
$ledgerFinalDigest = (Get-FileHash -LiteralPath $ledgerEvidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ((Get-FileHash -LiteralPath $ledgerPayloadPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $ledgerEvidenceDigest) { throw 'Ledger payload digest changed after final writes.' }
if ((Get-FileHash -LiteralPath $ledgerEvidencePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $ledgerFinalDigest) { throw 'Ledger evidence digest changed after final writes.' }

$manifestPath = Join-Path $script:EvidenceDirectory 'evidence-manifest.json'
$resultPath = Join-Path $script:EvidenceDirectory 'workflow-result.json'
$result = [ordered]@{
    schemaVersion = 1
    status = if ($PlanOnly) { 'planned' } else { 'complete' }
    mode = $Mode
    projectPath = $script:ProjectRoot
    unityVersion = $script:UnityVersion
    unityPath = $script:UnityPath
    exactSha = $afterHead
    evidenceRoot = $script:EvidenceDirectory
    commands = @($script:CommandRecords.ToArray())
    elapsedMs = ([DateTime]::UtcNow - $script:WorkflowStarted).TotalMilliseconds
    probe = $probeRecord
    bakeCount = $script:BakeCount
    beforeGeneratedHashes = $beforeHashes
    afterGeneratedHashes = $afterHashes
    changedGeneratedPaths = $changedGeneratedPaths
    checkLedgerPath = $ledgerEvidencePath
    checkLedgerPayloadPath = $ledgerPayloadPath
    checkLedgerPayloadSha256 = $ledgerEvidenceDigest
    checkLedgerSha256 = $ledgerFinalDigest
    checkLedger = @($ledger)
    lockReleaseProof = @($script:ReleaseProof.ToArray())
    evidenceManifestPath = $manifestPath
    evidenceManifestSha256 = $null
    outputManifestPath = $manifestPath
    invocationId = $script:InvocationId
    canonicalProjectRoot = $script:CanonicalProjectRoot
    leasePath = $script:LeasePath
    leaseReleaseProofPath = $script:LeaseReleaseProofPath
    gitMutation = $false
    noGitMutation = $true
}
$manifest = [ordered]@{
    schemaVersion = 1
    workflow = 'MovementLab'
    mode = $Mode
    exactSha = $afterHead
    invocationId = $script:InvocationId
    canonicalProjectRoot = $script:CanonicalProjectRoot
    leasePath = $script:LeasePath
    leaseReleaseProofPath = $script:LeaseReleaseProofPath
    resultPath = $resultPath
    commands = @($script:CommandRecords.ToArray())
    elapsedMs = $result.elapsedMs
    probe = $probeRecord
    bakeCount = $script:BakeCount
    changedGeneratedPaths = $changedGeneratedPaths
    checkLedgerPath = $ledgerEvidencePath
    checkLedgerPayloadPath = $ledgerPayloadPath
    checkLedgerPayloadSha256 = $ledgerEvidenceDigest
    checkLedgerSha256 = $ledgerFinalDigest
    checkLedger = @($ledger)
    history = @($script:PriorManifestHistory.ToArray())
    lockReleaseProof = @($script:ReleaseProof.ToArray())
    gitMutation = $false
}
Write-AtomicJson $manifestPath $manifest
$manifestDigest = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$result.evidenceManifestSha256 = $manifestDigest
Write-AtomicJson $resultPath $result
$resultDigest = (Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifestDigest) { throw 'Evidence manifest digest changed after final writes.' }
if ((Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $resultDigest) { throw 'Workflow result digest changed after final writes.' }
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
} finally {
    try { Release-ProjectLease } finally {
        if ($script:ReleaseProof.Count -gt 0 -and -not (Test-Path -LiteralPath $script:LeaseReleaseProofPath)) {
            Write-AtomicJson $script:LeaseReleaseProofPath ([ordered]@{ schemaVersion = 1; invocationId = $script:InvocationId; canonicalProjectRoot = $script:CanonicalProjectRoot; leasePath = $script:LeasePath; releaseProof = @($script:ReleaseProof.ToArray()) })
        }
    }
}
