[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Fast', 'Development', 'ProductionPrepare', 'ProductionValidate')]
    [string]$Mode,
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,
    [string]$EvidenceRoot,
    [string]$AttemptId,
    [string]$ProbePath,
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
$script:WorkflowStarted = [DateTime]::UtcNow
$script:CommandRecords = New-Object System.Collections.Generic.List[object]
$script:ExecutedCheckIds = New-Object System.Collections.Generic.List[string]
$script:BakeCount = 0
$script:ReleaseProof = New-Object System.Collections.Generic.List[object]
$script:AppendixAPaths = @(
    # importer metas
    'Assets/_Game/Models/LowPolyRocket.fbx.meta', 'Assets/_Game/Models/ArenaKit.fbx.meta', 'Assets/_Game/Models/LowPolyCharacter.fbx.meta', 'Assets/_Game/Models/FpsKickRig.fbx.meta', 'Assets/_Game/Models/FpsRocketLauncher.fbx.meta', 'Assets/_Game/Models/FpsShotgun.fbx.meta', 'Assets/_Game/Models/Shotgun.fbx.meta',
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
    'Assets/_Game/Textures/RetroExplosion.png.meta', 'Assets/_Game/Textures/RetroSmoke.png.meta', 'Assets/_Game/Textures/RetroSunnySky.png.meta',
    'Assets/_Game/Textures/FpsRocketLauncher_BaseColor.png.meta', 'Assets/_Game/Textures/FpsRocketLauncher_Normal.png.meta', 'Assets/_Game/Textures/FpsRocketLauncher_MetallicSmoothness.png.meta', 'Assets/_Game/Textures/FpsRocketLauncher_Occlusion.png.meta', 'Assets/_Game/Textures/FpsRocketLauncher_Emission.png.meta',
    # prefab/controller pairs
    'Assets/_Game/Prefabs/Player.prefab', 'Assets/_Game/Prefabs/Player.prefab.meta', 'Assets/_Game/Prefabs/Ball.prefab', 'Assets/_Game/Prefabs/Ball.prefab.meta', 'Assets/_Game/Prefabs/Rocket.prefab', 'Assets/_Game/Prefabs/Rocket.prefab.meta', 'Assets/_Game/Prefabs/ExplosionVfx.prefab', 'Assets/_Game/Prefabs/ExplosionVfx.prefab.meta', 'Assets/_Game/Prefabs/HealthPickup.prefab', 'Assets/_Game/Prefabs/HealthPickup.prefab.meta', 'Assets/_Game/Prefabs/ShotgunPickup.prefab', 'Assets/_Game/Prefabs/ShotgunPickup.prefab.meta', 'Assets/_Game/Prefabs/AmmoPickup.prefab', 'Assets/_Game/Prefabs/AmmoPickup.prefab.meta',
    'Assets/_Game/Animations/WorldCharacter.controller', 'Assets/_Game/Animations/WorldCharacter.controller.meta', 'Assets/_Game/Animations/FpsKick.controller', 'Assets/_Game/Animations/FpsKick.controller.meta',
    # material pairs
    'Assets/_Game/Materials/Floor.mat', 'Assets/_Game/Materials/Floor.mat.meta', 'Assets/_Game/Materials/Wall.mat', 'Assets/_Game/Materials/Wall.mat.meta', 'Assets/_Game/Materials/Trim.mat', 'Assets/_Game/Materials/Trim.mat.meta', 'Assets/_Game/Materials/Hazard.mat', 'Assets/_Game/Materials/Hazard.mat.meta', 'Assets/_Game/Materials/Marking.mat', 'Assets/_Game/Materials/Marking.mat.meta', 'Assets/_Game/Materials/Ball.mat', 'Assets/_Game/Materials/Ball.mat.meta', 'Assets/_Game/Materials/Rocket.mat', 'Assets/_Game/Materials/Rocket.mat.meta', 'Assets/_Game/Materials/RocketHot.mat', 'Assets/_Game/Materials/RocketHot.mat.meta', 'Assets/_Game/Materials/ProjectileGlow.mat', 'Assets/_Game/Materials/ProjectileGlow.mat.meta', 'Assets/_Game/Materials/GoalFrame.mat', 'Assets/_Game/Materials/GoalFrame.mat.meta', 'Assets/_Game/Materials/Shield.mat', 'Assets/_Game/Materials/Shield.mat.meta', 'Assets/_Game/Materials/ShieldBlue.mat', 'Assets/_Game/Materials/ShieldBlue.mat.meta', 'Assets/_Game/Materials/ShieldRed.mat', 'Assets/_Game/Materials/ShieldRed.mat.meta', 'Assets/_Game/Materials/ArenaPrimary.mat', 'Assets/_Game/Materials/ArenaPrimary.mat.meta', 'Assets/_Game/Materials/ArenaTrim.mat', 'Assets/_Game/Materials/ArenaTrim.mat.meta', 'Assets/_Game/Materials/ArenaHazard.mat', 'Assets/_Game/Materials/ArenaHazard.mat.meta', 'Assets/_Game/Materials/ArenaGlow.mat', 'Assets/_Game/Materials/ArenaGlow.mat.meta', 'Assets/_Game/Materials/BallSurface.physicMaterial', 'Assets/_Game/Materials/BallSurface.physicMaterial.meta', 'Assets/_Game/Materials/Explosion.mat', 'Assets/_Game/Materials/Explosion.mat.meta', 'Assets/_Game/Materials/ExplosionAdditive.mat', 'Assets/_Game/Materials/ExplosionAdditive.mat.meta', 'Assets/_Game/Materials/ExplosionSparks.mat', 'Assets/_Game/Materials/ExplosionSparks.mat.meta', 'Assets/_Game/Materials/Smoke.mat', 'Assets/_Game/Materials/Smoke.mat.meta', 'Assets/_Game/Materials/ContainmentGridCeiling.mat', 'Assets/_Game/Materials/ContainmentGridCeiling.mat.meta', 'Assets/_Game/Materials/ContainmentGridLongWall.mat', 'Assets/_Game/Materials/ContainmentGridLongWall.mat.meta', 'Assets/_Game/Materials/ContainmentGridEndWall.mat', 'Assets/_Game/Materials/ContainmentGridEndWall.mat.meta', 'Assets/_Game/Materials/RetroSunnySky.mat', 'Assets/_Game/Materials/RetroSunnySky.mat.meta', 'Assets/_Game/Materials/CharacterRed.mat', 'Assets/_Game/Materials/CharacterRed.mat.meta', 'Assets/_Game/Materials/CharacterBlack.mat', 'Assets/_Game/Materials/CharacterBlack.mat.meta', 'Assets/_Game/Materials/CharacterCream.mat', 'Assets/_Game/Materials/CharacterCream.mat.meta', 'Assets/_Game/Materials/CharacterEye.mat', 'Assets/_Game/Materials/CharacterEye.mat.meta', 'Assets/_Game/Materials/WeaponMetal.mat', 'Assets/_Game/Materials/WeaponMetal.mat.meta', 'Assets/_Game/Materials/WeaponDark.mat', 'Assets/_Game/Materials/WeaponDark.mat.meta', 'Assets/_Game/Materials/WeaponAccentCore.mat', 'Assets/_Game/Materials/WeaponAccentCore.mat.meta', 'Assets/_Game/Materials/WeaponAccent.mat', 'Assets/_Game/Materials/WeaponAccent.mat.meta', 'Assets/_Game/Materials/ShotgunMetal.mat', 'Assets/_Game/Materials/ShotgunMetal.mat.meta', 'Assets/_Game/Materials/ShotgunDark.mat', 'Assets/_Game/Materials/ShotgunDark.mat.meta', 'Assets/_Game/Materials/ShotgunAccentCore.mat', 'Assets/_Game/Materials/ShotgunAccentCore.mat.meta', 'Assets/_Game/Materials/ShotgunAccent.mat', 'Assets/_Game/Materials/ShotgunAccent.mat.meta', 'Assets/_Game/Materials/TeamBlue.mat', 'Assets/_Game/Materials/TeamBlue.mat.meta', 'Assets/_Game/Materials/TeamRed.mat', 'Assets/_Game/Materials/TeamRed.mat.meta', 'Assets/_Game/Materials/TeamBlueShield.mat', 'Assets/_Game/Materials/TeamBlueShield.mat.meta', 'Assets/_Game/Materials/TeamRedShield.mat', 'Assets/_Game/Materials/TeamRedShield.mat.meta', 'Assets/_Game/Materials/TeamBlueTrail.mat', 'Assets/_Game/Materials/TeamBlueTrail.mat.meta', 'Assets/_Game/Materials/TeamRedTrail.mat', 'Assets/_Game/Materials/TeamRedTrail.mat.meta', 'Assets/_Game/Materials/HealthPickup.mat', 'Assets/_Game/Materials/HealthPickup.mat.meta', 'Assets/_Game/Materials/AmmoShell.mat', 'Assets/_Game/Materials/AmmoShell.mat.meta',
    # generated pairs
    'Assets/_Game/Generated/BlueCircleCueMesh.asset', 'Assets/_Game/Generated/BlueCircleCueMesh.asset.meta', 'Assets/_Game/Generated/RedTriangleCueMesh.asset', 'Assets/_Game/Generated/RedTriangleCueMesh.asset.meta', 'Assets/_Game/Generated/MovementLabBuildManifest.json', 'Assets/_Game/Generated/MovementLabBuildManifest.json.meta',
    # gameplay, quality, and project settings
    'Assets/_Game/Scenes/MovementLab.unity', 'Assets/_Game/Scenes/MovementLab.unity.meta', 'ProjectSettings/EditorBuildSettings.asset', 'ProjectSettings/DynamicsManager.asset', 'ProjectSettings/TimeManager.asset', 'ProjectSettings/TagManager.asset',
    'Assets/Settings/PC_RPAsset.asset', 'Assets/Settings/PC_RPAsset.asset.meta', 'Assets/Settings/PC_Renderer.asset', 'Assets/Settings/PC_Renderer.asset.meta', 'Assets/Settings/PC_Low_RPAsset.asset', 'Assets/Settings/PC_Low_RPAsset.asset.meta', 'Assets/Settings/PC_Low_Renderer.asset', 'Assets/Settings/PC_Low_Renderer.asset.meta', 'Assets/Settings/PC_Iteration_RPAsset.asset', 'Assets/Settings/PC_Iteration_RPAsset.asset.meta', 'Assets/Settings/PC_Iteration_Renderer.asset', 'Assets/Settings/PC_Iteration_Renderer.asset.meta', 'ProjectSettings/QualitySettings.asset', 'ProjectSettings/ProjectSettings.asset',
    # lighting pairs
    'Assets/_Game/Lighting/MovementLabLightingSettings.asset', 'Assets/_Game/Lighting/MovementLabLightingSettings.asset.meta', 'Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset', 'Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset.meta', 'Assets/_Game/Lighting/MovementLabVolumeProfile.asset', 'Assets/_Game/Lighting/MovementLabVolumeProfile.asset.meta', 'Assets/_Game/Lighting/MovementLabLightingManifest.json', 'Assets/_Game/Lighting/MovementLabLightingManifest.json.meta',
    # baked pairs
    'Assets/_Game/Scenes/MovementLab/LightingData.asset', 'Assets/_Game/Scenes/MovementLab/LightingData.asset.meta',
    'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_dir.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_dir.png.meta', 'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_light.exr', 'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_light.exr.meta', 'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_shadowmask.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_shadowmask.png.meta',
    'Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_dir.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_dir.png.meta', 'Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_light.exr', 'Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_light.exr.meta', 'Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_shadowmask.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_shadowmask.png.meta',
    'Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_dir.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_dir.png.meta', 'Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_light.exr', 'Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_light.exr.meta', 'Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_shadowmask.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_shadowmask.png.meta',
    'Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_dir.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_dir.png.meta', 'Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_light.exr', 'Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_light.exr.meta', 'Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_shadowmask.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_shadowmask.png.meta',
    'Assets/_Game/Scenes/MovementLab/ReflectionProbe-0.exr', 'Assets/_Game/Scenes/MovementLab/ReflectionProbe-0.exr.meta', 'Assets/_Game/Scenes/MovementLab/ReflectionProbe-1.exr', 'Assets/_Game/Scenes/MovementLab/ReflectionProbe-1.exr.meta', 'Assets/_Game/Scenes/MovementLab/ReflectionProbe-2.exr', 'Assets/_Game/Scenes/MovementLab/ReflectionProbe-2.exr.meta', 'Assets/_Game/Scenes/MovementLab/ReflectionProbe-3.exr', 'Assets/_Game/Scenes/MovementLab/ReflectionProbe-3.exr.meta'
)
$script:BuilderOutputContract = $script:AppendixAPaths

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

function Assert-ShortWorkspacePath {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Label)
    $full = Get-FullPath $Path
    $workspaceRoot = 'C:\wt'
    if (-not $full.StartsWith($workspaceRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw ($Label + ' must be under C:\wt: ' + $full)
    }
    return $full
}

function Assert-EvidencePathBudget {
    param([Parameter(Mandatory = $true)][string]$EvidenceDirectory)
    $deepest = Join-Path (Join-Path $EvidenceDirectory 'logs') 'movement-lab-stage-probe.json'
    if ($deepest.Length -ge 260) {
        throw ('Evidence path exceeds Windows 260-character limit (' + $deepest.Length + ' chars); use a shorter evidence root: ' + $deepest)
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $deepest) | Out-Null
    try {
        [IO.File]::WriteAllText($deepest, 'probe')
        Remove-Item -LiteralPath $deepest -Force
    } catch {
        throw ('Evidence path not writable at deepest expected path: ' + $deepest + ' -> ' + $_.Exception.Message)
    }
    return $deepest
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
    return @($script:BuilderOutputContract | Where-Object {
        ([string]$_).Equals($normalized, [StringComparison]::OrdinalIgnoreCase)
    }).Count -eq 1
}

function Test-ProbeInventoryMember {
    param([Parameter(Mandatory = $true)][string]$Path)
    return Test-InventoryMember $Path
}

function Get-AuthoritativeGeneratedInventory {
    $paths = New-Object System.Collections.Generic.List[string]
    foreach ($relative in @($script:BuilderOutputContract | Sort-Object -Unique)) {
        $full = Join-Path $script:ProjectRoot ([string]$relative)
        if (Test-Path -LiteralPath $full -PathType Leaf) { $paths.Add(([string]$relative).Replace('\', '/')) }
        else { $paths.Add(([string]$relative).Replace('\', '/') + '=__MISSING__') }
    }
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

function Test-AppendixAPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $normalized = $Path.Replace('\', '/').TrimStart('/')
    return Test-InventoryMember $normalized
}

function Get-NonGeneratedDirtyPaths {
    $trackedText = Invoke-Git @('diff', '--name-only', 'HEAD', '--')
    $untrackedText = Invoke-Git @('ls-files', '--others', '--exclude-standard')
    $tracked = @($trackedText -split "`n")
    $untracked = @($untrackedText -split "`n")
    return @($tracked + $untracked |
        ForEach-Object { ([string]$_).Trim() } |
        Where-Object { $_ -and -not (Test-AppendixAPath $_) } |
        Sort-Object -Unique)
}

function Get-UntrackedPaths {
    $text = Invoke-Git @('ls-files', '--others', '--exclude-standard')
    return @($text -split "`n" |
        ForEach-Object { ([string]$_).Trim().Replace('\', '/') } |
        Where-Object { $_ } |
        Sort-Object -Unique)
}

function Remove-NewUnityRootIdeChurn {
    param([AllowEmptyCollection()][string[]]$InitialUntrackedPaths)
    $canonicalRoot = ([string]$script:CanonicalProjectRoot).TrimEnd('\')
    if ([string]::IsNullOrWhiteSpace($canonicalRoot)) { return @() }
    $projectLeaf = [IO.Path]::GetFileName($canonicalRoot)
    if ([string]::IsNullOrWhiteSpace($projectLeaf)) { return @() }
    $candidateName = $projectLeaf + '.slnx'
    $candidateRelative = $candidateName.Replace('\', '/')
    $initial = @($InitialUntrackedPaths | ForEach-Object { ([string]$_).Trim().Replace('\', '/') } | Where-Object { $_ })
    if (@($initial | Where-Object { $_.Equals($candidateRelative, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) { return @() }
    $current = @(Get-UntrackedPaths)
    if (@($current | Where-Object { $_.Equals($candidateRelative, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 0) { return @() }

    $candidateFull = [IO.Path]::GetFullPath((Join-Path $canonicalRoot $candidateName))
    $rootPrefix = $canonicalRoot + '\'
    if (-not $candidateFull.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetDirectoryName($candidateFull)).Equals($canonicalRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($candidateFull)).Equals($candidateName, [StringComparison]::OrdinalIgnoreCase)) { return @() }
    if (-not (Test-Path -LiteralPath $candidateFull -PathType Leaf)) { return @() }

    [IO.File]::Delete($candidateFull)
    if (Test-Path -LiteralPath $candidateFull) { throw ('Unity IDE churn cleanup did not remove: ' + $candidateFull) }
    return @($candidateRelative)
}

function Get-ProjectUnityProcesses {
    $normalized = @($script:ProjectRoot.TrimEnd('\').ToLowerInvariant(), $script:ProjectInputRoot.TrimEnd('\').ToLowerInvariant())
    if (-not [string]::IsNullOrWhiteSpace($script:CanonicalProjectRoot)) { $normalized += $script:CanonicalProjectRoot.TrimEnd('\').ToLowerInvariant() }
    $normalized = @($normalized | Sort-Object -Unique)
    $all = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe' OR Name = 'LightBaker.exe' OR Name = 'UnityShaderCompiler.exe'")
    return @($all | Where-Object {
        $commandLine = [string]$_.CommandLine
        # Fail closed: an unreadable command line (CIM permission, or a race during Editor start)
        # must count as owning the project, otherwise a live lock looks abandoned and gets deleted.
        if ([string]::IsNullOrWhiteSpace($commandLine)) { return $true }
        return (@($normalized | Where-Object { $commandLine.ToLowerInvariant().Contains($_) }).Count -gt 0)
    })
}

function Get-LockPaths {
    if ([string]::IsNullOrWhiteSpace($script:CanonicalProjectRoot)) { throw 'Canonical project root is required for lock handling.' }
    return @(
        (Join-Path $script:CanonicalProjectRoot 'Temp\UnityLockfile'),
        (Join-Path $script:CanonicalProjectRoot 'Library\UnityLockfile')
    )
}

function Ensure-LockSentinelNative {
    if ($null -ne ('RocketFooxball.Validation.LockSentinelNative' -as [type])) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RocketFooxball.Validation {
    public static class LockSentinelNative {
        public const uint Delete = 0x00010000;
        public const uint ReadAttributes = 0x00000080;
        public const uint ShareRead = 0x00000001;
        public const uint ShareWrite = 0x00000002;
        public const uint ShareDelete = 0x00000004;
        public const uint OpenExisting = 3;
        public const uint OpenReparsePoint = 0x00200000;
        public const uint BackupSemantics = 0x02000000;
        public const int FileDispositionInfoClass = 4;
        public const uint FileAttributeDirectory = 0x00000010;
        public const uint FileAttributeReparsePoint = 0x00000400;

        [StructLayout(LayoutKind.Sequential)]
        public struct ByHandleFileInformation {
            public uint FileAttributes;
            public uint CreationTimeLow;
            public uint CreationTimeHigh;
            public uint LastAccessTimeLow;
            public uint LastAccessTimeHigh;
            public uint LastWriteTimeLow;
            public uint LastWriteTimeHigh;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FileDispositionInfo {
            public byte DeleteFile;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        public static extern SafeFileHandle CreateFile(
            string path,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetFileInformationByHandle(
            SafeFileHandle file,
            out ByHandleFileInformation information);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetFileInformationByHandle(
            SafeFileHandle file,
            int fileInformationClass,
            ref FileDispositionInfo information,
            uint bufferSize);
    }
}
'@ -Language CSharp
}

function Assert-LockSentinelAncestors {
    param([Parameter(Mandatory = $true)][string]$Path)
    $root = $script:CanonicalProjectRoot.TrimEnd('\')
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $expected = @(
        (Join-Path $root 'Temp\UnityLockfile')
        (Join-Path $root 'Library\UnityLockfile')
    )
    if (-not (@($expected | Where-Object { $_.Equals($full, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 1)) {
        throw ('Lock sentinel path is outside canonical cleanup set: ' + $Path)
    }
    $current = Split-Path -Parent $full
    try { $parentItem = Get-Item -LiteralPath $current -Force -ErrorAction Stop }
    catch {
        $errorId = [string]$_.FullyQualifiedErrorId
        $nativeError = ([int]$_.Exception.HResult) -band 0xffff
        if ($errorId.StartsWith('PathNotFound', [StringComparison]::OrdinalIgnoreCase) -or $nativeError -eq 2 -or $nativeError -eq 3) { return $null }
        throw
    }
    if (($parentItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw ('Lock sentinel ancestor may not be a reparse point: ' + $current)
    }
    try { $sentinelItem = Get-Item -LiteralPath $full -Force -ErrorAction Stop }
    catch {
        $errorId = [string]$_.FullyQualifiedErrorId
        $nativeError = ([int]$_.Exception.HResult) -band 0xffff
        if ($errorId.StartsWith('PathNotFound', [StringComparison]::OrdinalIgnoreCase) -or $nativeError -eq 2 -or $nativeError -eq 3) { return $null }
        throw
    }
    while ($true) {
        $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw ('Lock sentinel ancestor may not be a reparse point: ' + $current)
        }
        if ($current.Equals($root, [StringComparison]::OrdinalIgnoreCase)) { break }
        if (-not $current.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw ('Lock sentinel ancestor escaped canonical project root: ' + $current)
        }
        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent.Equals($current, [StringComparison]::OrdinalIgnoreCase)) {
            throw ('Unable to walk canonical lock sentinel ancestors: ' + $current)
        }
        $current = $parent
    }
    return $full
}

function Remove-LockSentinelExact {
    param([Parameter(Mandatory = $true)][string]$Path)
    $canonicalPath = Assert-LockSentinelAncestors $Path
    if ($null -eq $canonicalPath) { return $false }
    Ensure-LockSentinelNative
    $access = [RocketFooxball.Validation.LockSentinelNative]::Delete -bor [RocketFooxball.Validation.LockSentinelNative]::ReadAttributes
    $share = [RocketFooxball.Validation.LockSentinelNative]::ShareRead -bor [RocketFooxball.Validation.LockSentinelNative]::ShareWrite -bor [RocketFooxball.Validation.LockSentinelNative]::ShareDelete
    $flags = [RocketFooxball.Validation.LockSentinelNative]::OpenReparsePoint -bor [RocketFooxball.Validation.LockSentinelNative]::BackupSemantics
    $handle = [RocketFooxball.Validation.LockSentinelNative]::CreateFile($canonicalPath, $access, $share, [IntPtr]::Zero, [RocketFooxball.Validation.LockSentinelNative]::OpenExisting, $flags, [IntPtr]::Zero)
    if ($handle.IsInvalid) {
        $nativeError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        $handle.Dispose()
        if ($nativeError -eq 2 -or $nativeError -eq 3) { return $false }
        throw ('Unable to open exact lock sentinel (Win32 ' + $nativeError + '): ' + $canonicalPath)
    }
    try {
        $information = New-Object RocketFooxball.Validation.LockSentinelNative+ByHandleFileInformation
        if (-not [RocketFooxball.Validation.LockSentinelNative]::GetFileInformationByHandle($handle, [ref]$information)) {
            $nativeError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            throw ('Unable to inspect exact lock sentinel (Win32 ' + $nativeError + '): ' + $canonicalPath)
        }
        $attributes = [uint32]$information.FileAttributes
        $zeroLength = ([uint32]$information.FileSizeHigh -eq 0 -and [uint32]$information.FileSizeLow -eq 0)
        if (($attributes -band [RocketFooxball.Validation.LockSentinelNative]::FileAttributeDirectory) -ne 0 -or
            ($attributes -band [RocketFooxball.Validation.LockSentinelNative]::FileAttributeReparsePoint) -ne 0 -or -not $zeroLength) {
            throw ('Lock sentinel identity is not a zero-byte regular file: ' + $canonicalPath)
        }
        if (@(Get-ProjectUnityProcesses).Count -gt 0) { return }
        $disposition = New-Object RocketFooxball.Validation.LockSentinelNative+FileDispositionInfo
        $disposition.DeleteFile = 1
        if (-not [RocketFooxball.Validation.LockSentinelNative]::SetFileInformationByHandle($handle, [RocketFooxball.Validation.LockSentinelNative]::FileDispositionInfoClass, [ref]$disposition, 1)) {
            $nativeError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            throw ('Unable to delete exact lock sentinel (Win32 ' + $nativeError + '): ' + $canonicalPath)
        }
    } finally {
        $handle.Dispose()
    }
    if (Test-Path -LiteralPath $canonicalPath) { throw ('Lock sentinel remained after exact deletion: ' + $canonicalPath) }
    return $true
}

function Remove-ZeroByteUnityLockSentinels {
    foreach ($lockPath in @(Get-LockPaths)) { Remove-LockSentinelExact $lockPath | Out-Null }
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
        $locksRemain = $false
        if ($active.Count -eq 0) {
            foreach ($lockPath in Get-LockPaths) {
                $deleteResult = Remove-LockSentinelExact $lockPath
                if ($null -eq $deleteResult) { $locksRemain = $true }
            }
        } else {
            $locksRemain = $true
        }
        $activeAfter = @(Get-ProjectUnityProcesses)
        if ($activeAfter.Count -eq 0 -and -not $locksRemain) { $released = $true; break }
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
    } | ConvertTo-Json -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($value)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}

function New-LedgerRow {
    param(
        [Parameter(Mandatory = $true)][string]$CheckId,
        [Parameter(Mandatory = $true)][ValidateSet('fast', 'development', 'production-final')][string]$Tier,
        [Parameter(Mandatory = $true)][bool]$MutatesProject,
        [Parameter(Mandatory = $true)][string]$RunPoint
    )
    $generatedInventoryArray = [string[]]@(Get-AuthoritativeGeneratedInventory)
    $generatedHashes = Get-GeneratedHashes
    [ordered]@{
        invocation_id = $script:InvocationId
        check_id = $CheckId
        owner = 'workflow-orchestrator'
        tier = $Tier
        status = 'pending'
        run_point = $RunPoint
        mutates_project = $MutatesProject
        environment_fingerprint = Get-EnvironmentFingerprint
        generated_inventory = $generatedInventoryArray
        generated_hashes = $generatedHashes
        generated_hash_digest = Get-GeneratedHashDigest $generatedHashes
        working_tree_digest = Get-WorkingTreeDigest
        executed_sha = $null
        validated_sha = $null
        evidence_path = $null
        evidence_digest = $null
        evidence = [ordered]@{ path = $null; sha256 = $null }
        subsumed_checks = @()
        invalidation_reason = $null
        bake_count = $null
        bake_marker = $null
        probe = $null
    }
}

function New-CheckLedger {
    $rows = New-Object System.Collections.Generic.List[object]
    switch ($Mode) {
        'Fast' {
            $rows.Add((New-LedgerRow -CheckId 'compile' -Tier 'fast' -MutatesProject $false -RunPoint 'coding'))
            $rows.Add((New-LedgerRow -CheckId 'stage-probe' -Tier 'fast' -MutatesProject $false -RunPoint 'coding'))
            $rows.Add((New-LedgerRow -CheckId 'fast-build' -Tier 'fast' -MutatesProject $true -RunPoint 'coding'))
        }
        'Development' {
            $rows.Add((New-LedgerRow -CheckId 'fast-build' -Tier 'fast' -MutatesProject $true -RunPoint 'coding'))
            $rows.Add((New-LedgerRow -CheckId 'development-bake' -Tier 'development' -MutatesProject $true -RunPoint 'checkpoint'))
        }
        'ProductionPrepare' {
            $rows.Add((New-LedgerRow -CheckId 'stage-probe' -Tier 'fast' -MutatesProject $false -RunPoint 'source-freeze'))
            $rows.Add((New-LedgerRow -CheckId 'production-bake' -Tier 'production-final' -MutatesProject $true -RunPoint 'source-freeze'))
        }
        'ProductionValidate' {
            $rows.Add((New-LedgerRow -CheckId 'production-validator' -Tier 'production-final' -MutatesProject $false -RunPoint 'final'))
        }
    }
    return ,([object[]]$rows.ToArray())
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

function Add-CommandRecord {
    param([Parameter(Mandatory = $true)]$Record)
    $script:CommandRecords.Add($Record)
}

function Add-CleanupFailureDiagnostic {
    param(
        [Parameter(Mandatory = $true)][System.Exception]$Primary,
        [Parameter(Mandatory = $true)][System.Exception]$Cleanup,
        [Parameter(Mandatory = $true)][string]$Label
    )
    $key = 'MovementLab.Cleanup.' + $Label
    $detail = $Cleanup.ToString()
    try {
        if ($Primary.Data.Contains($key)) { $Primary.Data[$key] = ([string]$Primary.Data[$key] + "`n" + $detail) }
        else { $Primary.Data[$key] = $detail }
    } catch {
        try { $Primary.Data['MovementLab.Cleanup'] = ($Label + ': ' + $detail) } catch { }
    }
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
        if ($Method -ceq 'RocketFooxball.Editor.MovementLabBuilder.BakeMovementLabLighting') { $script:BakeCount++ }
        $process = $null
        $primaryError = $null
        try {
            try {
                $process = Start-Process -FilePath $script:UnityPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
                $exitCode = $process.ExitCode
                if ($exitCode -ne 0) { throw ('Unity step failed: ' + $Label + ' (exit ' + $exitCode + '). Log: ' + $logPath) }
            } catch {
                $primaryError = $_.Exception
            }
        } finally {
            try { Wait-UnityRelease $Label | Out-Null }
            catch {
                if ($null -ne $primaryError) { Add-CleanupFailureDiagnostic $primaryError $_.Exception ('unity-' + $Label) }
                else { $primaryError = $_.Exception }
            }
        }
        if ($null -ne $primaryError) { throw $primaryError }
    }
    Add-CommandRecord ([ordered]@{
        label = $Label
        tier = if ($Mode -eq 'Development') { 'development' } elseif ($Mode -eq 'Fast') { 'fast' } else { 'production-final' }
        method = $Method
        arguments = @($arguments)
        startedUtc = $started.ToString('o')
        completedUtc = [DateTime]::UtcNow.ToString('o')
        exitCode = $exitCode
        skipped = $skipped
        mutatesProject = $MutatesProject
        logPath = $logPath
        elapsedMs = ([DateTime]::UtcNow - $started).TotalMilliseconds
    })
}

function Resolve-ProductionBakeOutcome {
    $commands = @($script:CommandRecords.ToArray() | Where-Object {
        [string]$_.label -eq 'ProductionBake'
    })
    if ($commands.Count -ne 1) { throw 'production-bake.result.missing: expected exactly one completed ProductionBake command record.' }
    $command = $commands[0]
    if ([string]$command.method -cne 'RocketFooxball.Editor.MovementLabBuilder.BakeMovementLabLighting') {
        throw 'production-bake.result.method: ProductionBake command method is not the authoritative builder bake.'
    }
    if ([bool]$command.skipped) {
        $command['bakeOutcome'] = 'planned'
        $command['markerLine'] = $null
        return [ordered]@{ status = 'planned'; markerLine = $null; logPath = $null }
    }
    if ([int]$command.exitCode -ne 0) { throw 'production-bake.result.exitCode: completed ProductionBake result was not successful.' }
    $logPath = [string]$command.logPath
    if ([string]::IsNullOrWhiteSpace($logPath) -or -not (Test-Path -LiteralPath $logPath -PathType Leaf)) {
        throw 'production-bake.result.logMissing: completed ProductionBake log is missing.'
    }
    try { $logPath = Assert-DurableEvidencePath $logPath 'ProductionBake log' } catch { throw ('production-bake.result.logPath: ' + $_.Exception.Message) }
    $evidencePrefix = $script:EvidenceDirectory.TrimEnd('\') + '\'
    if (-not $logPath.StartsWith($evidencePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'production-bake.result.logBinding: ProductionBake log is outside this invocation evidence directory.'
    }
    try {
        $startedUtc = [DateTime]::Parse([string]$command.startedUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
        if ((Get-Item -LiteralPath $logPath -Force).LastWriteTimeUtc -lt $startedUtc) { throw 'production-bake.result.logStale: ProductionBake log predates this invocation.' }
    } catch [System.FormatException] {
        throw 'production-bake.result.binding: ProductionBake command start time is invalid.'
    } catch {
        if ($_.Exception.Message -like 'production-bake.result.logStale:*') { throw $_.Exception }
        throw ('production-bake.result.binding: ' + $_.Exception.Message)
    }
    $markerPattern = '^\[MovementLab\] production bake skipped: lighting inputs current \(digest [0-9a-fA-F]{64}\)$'
    $markerLines = @(Get-Content -LiteralPath $logPath -ErrorAction Stop | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -match $markerPattern })
    if ($markerLines.Count -gt 1) { throw 'production-bake.marker.duplicate: completed ProductionBake log contains multiple exact skip markers.' }
    if ($markerLines.Count -eq 1) {
        if ($script:BakeCount -ne 1) { throw ('production-bake.marker.bakeCount: exact skip marker requires one ProductionBake invocation before classification; observed ' + $script:BakeCount + '.') }
        $script:BakeCount = 0
        $command['bakeOutcome'] = 'reused'
        $command['markerLine'] = [string]$markerLines[0]
        return [ordered]@{ status = 'reused'; markerLine = [string]$markerLines[0]; logPath = $logPath }
    }
    if ($script:BakeCount -ne 1) { throw ('production-bake.executed.bakeCount: absent skip marker requires bakeCount exactly one; observed ' + $script:BakeCount + '.') }
    $command['bakeOutcome'] = 'executed'
    $command['markerLine'] = $null
    return [ordered]@{ status = 'executed'; markerLine = $null; logPath = $logPath }
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
    if ($WorkflowMode -eq 'Development' -and [string]$Probe.bakedProfile -ine 'development') { Add-WorkflowViolation $violations 'mode.bakedProfile.development' 'Development workflow requires bakedProfile=development.' }
    if ($WorkflowMode -in @('ProductionValidate', 'ProductionPrepareFinal') -and [string]$Probe.bakedProfile -ine 'production') { Add-WorkflowViolation $violations 'mode.bakedProfile.production' ($WorkflowMode + ' requires bakedProfile=production.') }
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
foreach ($pathArgument in @(
    @{ name = 'EvidenceRoot'; value = [string]$EvidenceRoot },
    @{ name = 'ProbePath'; value = [string]$ProbePath }
)) {
    if (-not [string]::IsNullOrWhiteSpace($pathArgument.value) -and $pathArgument.value.IndexOfAny(@([char]0, [char]10, [char]13)) -ge 0) {
        Add-WorkflowViolation $preflightViolations ('preflight.argument.' + $pathArgument.name + '.shape') ($pathArgument.name + ' must be one line.')
        if ($pathArgument.name -eq 'EvidenceRoot') { $evidenceRootPathSafe = $false }
        elseif ($pathArgument.name -eq 'ProbePath') { $probePathPathSafe = $false }
    }
}
$evidenceBase = if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    Assert-DurableEvidencePath (Get-FullPath (Join-Path $commonGit ('movement-lab-proof\' + $attemptPathValue))) 'EvidenceRoot'
} elseif ($evidenceRootPathSafe) {
    Assert-DurableEvidencePath (Assert-ShortWorkspacePath $EvidenceRoot 'EvidenceRoot') 'EvidenceRoot'
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
if (-not $PlanOnly -and (Test-Path -LiteralPath $script:ProbeOutputPath)) { throw ('Probe path already exists; refusing overwrite: ' + $script:ProbeOutputPath) }

$beforeHead = Get-HeadSha
$initialUntrackedPaths = @(Get-UntrackedPaths)
$dirtyBefore = @(Get-NonGeneratedDirtyPaths)
if ($Mode -eq 'ProductionPrepare' -and $dirtyBefore.Count -gt 0) { Add-WorkflowViolation $preflightViolations 'preflight.repository.dirtyScope' ('Non-generated source is dirty: ' + ($dirtyBefore -join ', ')) }
Complete-WorkflowValidationPhase 'preflight' $preflightViolations

Assert-EvidencePathBudget $script:EvidenceDirectory | Out-Null
$evidenceItem = Get-Item -LiteralPath $script:EvidenceDirectory -Force
if (($evidenceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Evidence invocation path may not be a junction or alias: ' + $script:EvidenceDirectory) }
$canonicalEvidenceParent = Get-CanonicalPath (Split-Path -Parent $script:EvidenceDirectory)
if ($canonicalEvidenceParent.Equals($script:CanonicalProjectRoot, [StringComparison]::OrdinalIgnoreCase) -or $canonicalEvidenceParent.StartsWith($script:CanonicalProjectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Evidence destination resolves inside project.' }
$script:LeaseReleaseProofPath = Join-Path $script:EvidenceDirectory 'lease-release-proof.json'
$logs = Join-Path $script:EvidenceDirectory 'logs'
if (-not (Test-Path -LiteralPath $logs -PathType Container)) { New-Item -ItemType Directory -Force -Path $logs | Out-Null }
Acquire-ProjectLease | Out-Null
$workflowPrimaryError = $null
try {
Assert-NoProjectProcessOrLock

$beforeHashes = Get-GeneratedHashes
$ledger = New-CheckLedger
$script:LedgerRows = $ledger
$probeRecord = $null

try {
    switch ($Mode) {
        'Fast' {
            Invoke-UnityStep 'CompileAndProbe' 'RocketFooxball.Editor.MovementLabBuilder.ProbeMovementLabGeneratedState' @('-movementLabProbePath', $script:ProbeOutputPath) $false -NoGraphics; Mark-CheckExecuted 'compile'
            $probeRecord = Read-ProbeContract
            Assert-ProbeContractForMode $probeRecord 'Fast'
            Mark-CheckExecuted 'stage-probe'
            Invoke-UnityStep 'BuildFast' 'RocketFooxball.Editor.MovementLabBuilder.BuildMovementLabFast' @('-movementLabProbePath', $script:ProbeOutputPath) $true -NoGraphics; Mark-CheckExecuted 'fast-build'
        }
        'Development' {
            Invoke-UnityStep 'BuildFast' 'RocketFooxball.Editor.MovementLabBuilder.BuildMovementLabFast' @('-movementLabProbePath', $script:ProbeOutputPath) $true -NoGraphics; Mark-CheckExecuted 'fast-build'
            Invoke-UnityStep 'DevelopmentBake' 'RocketFooxball.Editor.MovementLabBuilder.BakeMovementLabLightingDevelopment' @('-movementLabProbePath', $script:ProbeOutputPath) $true; Mark-CheckExecuted 'development-bake'
            $probeRecord = Read-ProbeContract
            Assert-ProbeContractForMode $probeRecord 'Development'
        }
        'ProductionPrepare' {
            # PlanOnly can validate an existing probe before recording the
            # single combined prepare/bake Unity step.
            if ($PlanOnly -and (Test-Path -LiteralPath $script:ProbeOutputPath -PathType Leaf)) {
                $probeRecord = Read-ProbeContract
                Assert-ProbeContractForMode $probeRecord 'ProductionPrepare'
            }
            Invoke-UnityStep 'ProductionBake' 'RocketFooxball.Editor.MovementLabBuilder.BakeMovementLabLighting' @('-movementLabPrepareProduction', '-movementLabProbePath', $script:ProbeOutputPath) $true
            Mark-CheckExecuted 'stage-probe'
            Mark-CheckExecuted 'production-bake'
            $productionBakeOutcome = Resolve-ProductionBakeOutcome
            $productionBakeRow = $script:LedgerRows | Where-Object { [string]$_.check_id -eq 'production-bake' } | Select-Object -First 1
            if ($null -eq $productionBakeRow) { throw 'production-bake.result.ledger: production-bake ledger row missing.' }
            if ($PlanOnly) {
                $productionBakeRow.status = 'pending'
            } else {
                $productionBakeRow.status = [string]$productionBakeOutcome.status
                $productionBakeRow.bake_count = $script:BakeCount
                $productionBakeRow.bake_marker = $productionBakeOutcome.markerLine
            }
            $probeRecord = Read-ProbeContract
            if (-not $PlanOnly) { $productionBakeRow.probe = $probeRecord }
            Assert-ProbeContractForMode $probeRecord 'ProductionPrepareFinal'
        }
        'ProductionValidate' {
            Invoke-UnityStep 'ProductionValidate' 'RocketFooxball.Editor.MovementLabBuilder.ValidateMovementLab' @('-movementLabProbePath', $script:ProbeOutputPath) $false -NoGraphics; Mark-CheckExecuted 'production-validator'
            $probeRecord = Read-ProbeContract
            Assert-ProbeContractForMode $probeRecord 'ProductionValidate'
        }
    }
} catch {
    $workflowPrimaryError = $_.Exception
} finally {
    if (-not $PlanOnly) {
        try { Wait-UnityRelease 'workflow-final' | Out-Null }
        catch {
            if ($null -ne $workflowPrimaryError) { Add-CleanupFailureDiagnostic $workflowPrimaryError $_.Exception 'workflow-final' }
            else { $workflowPrimaryError = $_.Exception }
        }
    }
}
if ($null -ne $workflowPrimaryError) { throw $workflowPrimaryError }

if (-not $PlanOnly) { $null = Remove-NewUnityRootIdeChurn $initialUntrackedPaths }
$afterHashes = Get-GeneratedHashes
$changedGeneratedPaths = @(Get-ChangedHashPaths $beforeHashes $afterHashes)
$afterGeneratedHashDigest = Get-GeneratedHashDigest $afterHashes
$afterWorkingTreeDigest = Get-WorkingTreeDigest
$dirtyAfter = @(Get-NonGeneratedDirtyPaths)
$predicateClassification = [ordered]@{
    preBake = @(
        [ordered]@{ predicate = 'ProductionPrepare mode contract'; phase = 'pre-bake'; location = 'Assert-ProbeContractForMode immediately after probe read'; inputs = @('Mode', 'Probe') },
        [ordered]@{ predicate = 'config/argument shape'; phase = 'pre-bake'; location = 'preflight'; inputs = @('workflow arguments', 'project configuration') },
        [ordered]@{ predicate = 'probe-derived facts'; phase = 'pre-bake'; location = 'Assert-ProbeContractForMode'; inputs = @('stage probe fields') }
    )
    postflight = @(
        [ordered]@{ predicate = 'ProductionPrepareFinal output contract'; phase = 'postflight'; reason = 'requires post-bake probe profile and manifest' },
        [ordered]@{ predicate = 'clean-tree'; phase = 'postflight'; reason = 'requires before/after Git status' },
        [ordered]@{ predicate = 'generated inventory'; phase = 'postflight'; reason = 'requires post-step generated hashes and paths' },
        [ordered]@{ predicate = 'bake count'; phase = 'postflight'; reason = 'requires Unity invocation count' },
        [ordered]@{ predicate = 'HEAD stability'; phase = 'postflight'; reason = 'requires final Git HEAD' },
        [ordered]@{ predicate = 'evidence digests'; phase = 'postflight'; reason = 'requires post-step hash evidence' }
    )
}
$postflightViolations = New-WorkflowViolationList
if ($Mode -eq 'ProductionPrepare') {
    if ($dirtyAfter.Count -gt 0) { Add-WorkflowViolation $postflightViolations 'postflight.dirtyScope.nonGenerated' ('Production preparation changed non-generated source: ' + ($dirtyAfter -join ', ')) }
    if (($dirtyBefore -join "`n") -cne ($dirtyAfter -join "`n")) { Add-WorkflowViolation $postflightViolations 'postflight.dirtyScope.stability' 'Production preparation changed non-generated Git status.' }
}
if ($script:BakeCount -gt 1) { Add-WorkflowViolation $postflightViolations 'postflight.bakeCount.max' ('Workflow bake count exceeded one: ' + $script:BakeCount) }
for ($generatedIndex = 0; $generatedIndex -lt $changedGeneratedPaths.Count; $generatedIndex++) {
    if (-not (Test-AppendixAPath ([string]$changedGeneratedPaths[$generatedIndex]))) {
        Add-WorkflowViolation $postflightViolations ('postflight.generatedScope[' + $generatedIndex + ']') ('Changed generated path is outside authoritative generated scope: ' + [string]$changedGeneratedPaths[$generatedIndex])
    }
}
if ([string]$afterGeneratedHashDigest -notmatch '^[0-9a-fA-F]{64}$') { Add-WorkflowViolation $postflightViolations 'postflight.evidence.generatedHashDigest' 'Generated hash digest is not a SHA-256 value.' }
if ([string]$afterWorkingTreeDigest -notmatch '^[0-9a-fA-F]{64}$') { Add-WorkflowViolation $postflightViolations 'postflight.evidence.workingTreeDigest' 'Working-tree digest is not a SHA-256 value.' }
$afterHead = Get-HeadSha
if ($beforeHead -cne $afterHead) { Add-WorkflowViolation $postflightViolations 'postflight.head.stability' ('Git HEAD changed during workflow: expected ' + $beforeHead + ', observed ' + $afterHead) }
Complete-WorkflowValidationPhase 'postflight' $postflightViolations

$ledgerEvidencePath = Join-Path $script:EvidenceDirectory 'check-ledger.json'
$ledgerPayloadPath = Join-Path $script:EvidenceDirectory 'check-ledger-payload.json'
foreach ($row in $ledger) {
    if ($PlanOnly -and [string]$row.status -eq 'pending') { $row.status = 'deferred' }
    elseif ($script:ExecutedCheckIds.Contains([string]$row.check_id)) {
        # ProductionBake is classified by the builder's exact skip marker. Do not
        # replace its authoritative reused/executed result with a generic ledger state.
        if ([string]$row.check_id -ne 'production-bake') { $row.status = 'executed' }
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
Write-AtomicJson $ledgerPayloadPath ([ordered]@{ schemaVersion = 1; exactSha = $afterHead; invocationId = $script:InvocationId; rows = @($ledger) })
$ledgerEvidenceDigest = (Get-FileHash -LiteralPath $ledgerPayloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
foreach ($row in $ledger) {
    if ($script:ExecutedCheckIds.Contains([string]$row.check_id)) {
        $row.evidence_digest = $ledgerEvidenceDigest
        $row.evidence = [ordered]@{ path = $ledgerPayloadPath; sha256 = $ledgerEvidenceDigest }
    }
}
Write-AtomicJson $ledgerEvidencePath ([ordered]@{ schemaVersion = 1; exactSha = $afterHead; invocationId = $script:InvocationId; rows = @($ledger) })
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
    predicateClassification = $predicateClassification
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
    lockReleaseProof = @($script:ReleaseProof.ToArray())
    predicateClassification = $predicateClassification
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
    predicateClassification = $predicateClassification
    gitMutation = $false
} | ConvertTo-Json -Depth 14
} catch {
    $workflowPrimaryError = $_.Exception
    throw
} finally {
    $leaseCleanupError = $null
    $proofCleanupError = $null
    try { Release-ProjectLease }
    catch { $leaseCleanupError = $_.Exception }
    try {
        if ($script:ReleaseProof.Count -gt 0 -and -not (Test-Path -LiteralPath $script:LeaseReleaseProofPath)) {
            Write-AtomicJson $script:LeaseReleaseProofPath ([ordered]@{ schemaVersion = 1; invocationId = $script:InvocationId; canonicalProjectRoot = $script:CanonicalProjectRoot; leasePath = $script:LeasePath; releaseProof = @($script:ReleaseProof.ToArray()) })
        }
    }
    catch { $proofCleanupError = $_.Exception }
    if ($null -ne $leaseCleanupError) {
        if ($null -ne $workflowPrimaryError) { Add-CleanupFailureDiagnostic $workflowPrimaryError $leaseCleanupError 'lease-release' }
        elseif ($null -ne $proofCleanupError) { Add-CleanupFailureDiagnostic $leaseCleanupError $proofCleanupError 'lease-release-proof'; throw $leaseCleanupError }
        else { throw $leaseCleanupError }
    } elseif ($null -ne $proofCleanupError) {
        if ($null -ne $workflowPrimaryError) { Add-CleanupFailureDiagnostic $workflowPrimaryError $proofCleanupError 'lease-release-proof' }
        else { throw $proofCleanupError }
    }
}
