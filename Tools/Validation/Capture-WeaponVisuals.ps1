[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][Alias('weaponCaptureEvidenceRoot')][string]$EvidenceRoot,
    [Parameter(Mandatory = $true)][Alias('weaponCaptureAttemptId')][string]$AttemptId,
    [Parameter(Mandatory = $true)][ValidateSet('Rocket', 'Shotgun')][Alias('weaponCaptureWeapon')][string]$Weapon,
    [Parameter(Mandatory = $true)][ValidateSet('Fast', 'Persisted')][Alias('weaponCaptureMode')][string]$Mode,
    [Parameter(Mandatory = $true)][Alias('weaponCaptureReferenceManifest', 'ReferenceManifestPath')][string]$ReferenceManifest,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:UnityVersion = '6000.5.6f1'
$script:ProjectRoot = $null
$script:EvidenceDirectory = $null
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

function Get-ProjectUnityProcesses {
    $normalized = $script:ProjectRoot.TrimEnd('\').ToLowerInvariant()
    $all = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe' OR Name = 'LightBaker.exe' OR Name = 'UnityShaderCompiler.exe'")
    return @($all | Where-Object {
        $commandLine = [string]$_.CommandLine
        [string]::IsNullOrWhiteSpace($commandLine) -or $commandLine.ToLowerInvariant().Contains($normalized)
    })
}

function Get-LockPaths {
    return @(
        (Join-Path $script:ProjectRoot 'Temp\UnityLockfile'),
        (Join-Path $script:ProjectRoot 'Library\UnityLockfile')
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
    $sha = (& git -C $script:ProjectRoot rev-parse --verify HEAD | Out-String).Trim().ToLowerInvariant()
    if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40}$') { throw 'Unable to resolve exact Git HEAD SHA.' }
    return $sha
}

function Get-ProjectStatus {
    $value = (& git -C $script:ProjectRoot status --porcelain=v1 --untracked-files=all -- Assets Tools ProjectSettings Packages | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Git status failed.' }
    return $value
}

function Get-Hash {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw ('Required file missing: ' + $Path) }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-Property {
    param([AllowNull()]$Object, [Parameter(Mandatory = $true)][string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
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
    if ([int](Get-Property $manifest 'schemaVersion') -ne 1) { throw 'Reference manifest schemaVersion must equal 1.' }
    $arrayValues = New-Object System.Collections.Generic.List[object]
    foreach ($arrayName in @('entries', 'references', 'items')) {
        $arrayValue = Get-Property $manifest $arrayName
        if ($null -ne $arrayValue) { $arrayValues.Add($arrayValue) | Out-Null }
    }
    $arrays = @($arrayValues.ToArray())
    if ($arrays.Count -ne 1 -or @($arrays[0]).Count -ne 3) { throw 'Reference manifest must contain exactly three schema-1 entries.' }
    $expectedIds = @('game-bright', 'game-dark', 'quake-hires')
    $seen = @{}
    $records = New-Object System.Collections.Generic.List[object]
    foreach ($entry in @($arrays[0])) {
        $id = [string](Get-Property $entry 'id')
        if ($expectedIds -notcontains $id -or $seen.ContainsKey($id)) { throw ('Reference manifest id is invalid or duplicated: ' + $id) }
        $seen[$id] = $true
        $original = [string](Get-Property $entry 'originalPath')
        $evidence = [string](Get-Property $entry 'evidencePath')
        $bytes = [int64](Get-Property $entry 'bytes')
        $sha = [string](Get-Property $entry 'sha256')
        if ([string]::IsNullOrWhiteSpace($original) -or [string]::IsNullOrWhiteSpace($evidence) -or $bytes -le 0 -or $sha -notmatch '^[0-9a-fA-F]{64}$') {
            throw ('Reference manifest entry is incomplete: ' + $id)
        }
        $originalPath = Resolve-ReferencePath $original $full
        $evidencePath = Resolve-ReferencePath $evidence $full
        foreach ($fileRecord in @(
            [ordered]@{ label = $id + '.originalPath'; path = $originalPath },
            [ordered]@{ label = $id + '.evidencePath'; path = $evidencePath }
        )) {
            if (-not (Test-Path -LiteralPath $fileRecord.path -PathType Leaf)) { throw ('Reference file missing: ' + $fileRecord.path) }
            if ((Get-Item -LiteralPath $fileRecord.path).Length -ne $bytes -or (Get-Hash $fileRecord.path) -ne $sha.ToLowerInvariant()) {
                throw ('Reference hash mismatch: ' + $fileRecord.label)
            }
        }
        $records.Add([ordered]@{ id = $id; sha256 = $sha.ToLowerInvariant(); bytes = $bytes; originalPath = $originalPath; evidencePath = $evidencePath }) | Out-Null
    }
    foreach ($id in $expectedIds) { if (-not $seen.ContainsKey($id)) { throw ('Reference manifest missing id: ' + $id) } }
    return [pscustomobject]@{ path = $full; sha256 = Get-Hash $full; entries = @($records.ToArray()) }
}

function Assert-EvidencePathBudget {
    $deepest = Join-Path (Join-Path $script:EvidenceDirectory 'logs') 'weapon-shotgun-awaylight-low.png'
    if ($deepest.Length -ge 260) { throw ('Weapon evidence path exceeds Windows 260-character limit: ' + $deepest) }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $deepest) | Out-Null
    $probe = Join-Path (Split-Path -Parent $deepest) '.path-probe'
    try { [IO.File]::WriteAllText($probe, 'probe'); Remove-Item -LiteralPath $probe -Force } catch { throw ('Weapon evidence path is not writable: ' + $_.Exception.Message) }
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
        $actual = @($referenceHashes | Where-Object { [string](Get-Property $_ 'id') -ceq $entry.id })
        if ($actual.Count -ne 1 -or [string](Get-Property $actual[0] 'sha256').ToLowerInvariant() -ne $entry.sha256) { throw ('Weapon capture reference hash mismatch: ' + $entry.id) }
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

$script:ProjectRoot = Get-FullPath $ProjectPath
if (-not (Test-Path -LiteralPath $script:ProjectRoot -PathType Container)) { throw ('ProjectPath not found: ' + $script:ProjectRoot) }
if ($script:ProjectRoot.Length -gt 80) { throw 'ProjectPath must be 80 characters or fewer.' }
$script:ProjectRoot = [IO.Path]::GetFullPath($script:ProjectRoot).TrimEnd('\')
$evidenceBase = Assert-ShortWorkspacePath $EvidenceRoot 'EvidenceRoot'
if (-not (Test-Path -LiteralPath $evidenceBase -PathType Container)) { throw ('EvidenceRoot must already exist: ' + $evidenceBase) }
$evidenceBase = Assert-OutsideProject $evidenceBase 'EvidenceRoot'
$script:EvidenceDirectory = [IO.Path]::GetFullPath((Join-Path $evidenceBase $AttemptId)).TrimEnd('\')
if ($AttemptId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' -or $AttemptId -in @('.', '..')) { throw 'AttemptId must be 1-64 filename-safe characters.' }
if (Test-Path -LiteralPath $script:EvidenceDirectory) { throw ('Weapon evidence attempt already exists: ' + $script:EvidenceDirectory) }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be greater than zero.' }
$reference = Read-ReferenceManifest $ReferenceManifest
$unityPath = Join-Path ${env:ProgramFiles} ('Unity\Hub\Editor\' + $script:UnityVersion + '\Editor\Unity.exe')
if (-not (Test-Path -LiteralPath $unityPath -PathType Leaf)) { throw ('Unity executable missing: ' + $unityPath) }
$libraryPath = Join-Path $script:ProjectRoot 'Library'
if (-not (Test-Path -LiteralPath $libraryPath -PathType Container)) { throw 'Warm private Library is missing.' }
if ((Get-Item -LiteralPath $libraryPath).LinkType -or ((Get-Item -LiteralPath $libraryPath).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Library must remain private.' }
$versionPath = Join-Path $script:ProjectRoot 'ProjectSettings\ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $versionPath -PathType Leaf) -or (Get-Content -Raw -LiteralPath $versionPath) -notmatch ('m_EditorVersion:\s*' + [Regex]::Escape($script:UnityVersion))) { throw 'Project Unity version mismatch.' }
Assert-NoProjectProcessOrLock

New-Item -ItemType Directory -Force -Path $script:EvidenceDirectory | Out-Null
Assert-EvidencePathBudget
$logPath = Join-Path $script:EvidenceDirectory 'weapon-capture-unity.log'
$resultPath = Join-Path $script:EvidenceDirectory 'CaptureResult.json'
$harnessEvidence = Join-Path $script:EvidenceDirectory 'harness'
New-Item -ItemType Directory -Force -Path $harnessEvidence | Out-Null
$harnessArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $script:ProjectRoot 'Tools\Tests\Invoke-HarnessTests.ps1'), '-EvidenceRoot', $harnessEvidence)
$harness = Start-Process -FilePath 'powershell.exe' -ArgumentList $harnessArgs -WindowStyle Hidden -Wait -PassThru
if ($harness.ExitCode -ne 0) { throw ('Harness pre-gate failed with exit code ' + $harness.ExitCode + '.') }
Assert-NoProjectProcessOrLock

$beforeStatus = Get-ProjectStatus
$beforeSha = Get-GitSha
$generatedManifestPath = Join-Path $script:ProjectRoot 'Assets\_Game\Generated\MovementLabBuildManifest.json'
$beforeGeneratedManifestHash = Get-Hash $generatedManifestPath
$arguments = @(
    '-batchmode', '-quit', '-projectPath', ('"' + $script:ProjectRoot + '"'),
    '-executeMethod', 'RocketFooxball.Editor.WeaponVisualCapture.Capture',
    '-weaponCaptureEvidenceRoot', ('"' + $evidenceBase + '"'), '-weaponCaptureAttemptId', $AttemptId,
    '-weaponCaptureWeapon', $Weapon, '-weaponCaptureMode', $Mode,
    '-weaponCaptureReferenceManifest', ('"' + $reference.path + '"'), '-logFile', ('"' + $logPath + '"')
)
$process = $null
$primaryError = $null
$unityStopwatch = [Diagnostics.Stopwatch]::StartNew()
try {
    Assert-NoProjectProcessOrLock
    $process = Start-Process -FilePath $unityPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw ('Weapon visual capture failed with exit code ' + $process.ExitCode + '. Log: ' + $logPath) }
} catch { $primaryError = $_.Exception }
finally {
    try { Wait-ProjectRelease 'weapon-capture' }
    catch {
        if ($null -ne $primaryError) { $primaryError.Data['MovementLab.Cleanup.weapon-capture'] = $_.Exception.ToString() }
        else { $primaryError = $_.Exception }
    }
}
$unityStopwatch.Stop()
if ($null -ne $primaryError) { throw $primaryError }
if (-not (Test-Path -LiteralPath $logPath -PathType Leaf)) { throw ('Unity capture log missing: ' + $logPath) }
$marker = [Regex]::Match((Get-Content -Raw -LiteralPath $logPath), '(?m)^WEAPON_VISUAL_CAPTURE_PASS\s+(.+?)\s*$')
if (-not $marker.Success) { throw ('Weapon capture pass marker missing from log: ' + $logPath) }
$manifestPath = [IO.Path]::GetFullPath($marker.Groups[1].Value.Trim())
$expectedManifestPath = Join-Path $script:EvidenceDirectory 'WeaponVisualManifest.json'
if (-not $manifestPath.Equals($expectedManifestPath, [StringComparison]::OrdinalIgnoreCase)) { throw ('Unexpected weapon manifest path: ' + $manifestPath) }
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw ('Weapon capture manifest missing: ' + $manifestPath) }
$expectedGeneratedManifestHash = Get-Hash $generatedManifestPath
if ($expectedGeneratedManifestHash -ne $beforeGeneratedManifestHash) { throw 'Generated MovementLabBuildManifest changed during capture.' }
$imageRecords = Assert-WeaponManifest $manifestPath $reference $beforeSha $expectedGeneratedManifestHash
if ((Get-ProjectStatus) -cne $beforeStatus) { throw 'Git status changed during non-mutating weapon capture.' }
if ((Get-GitSha) -cne $beforeSha) { throw 'Git HEAD changed during weapon capture.' }
if ((Get-Hash $reference.path) -ne $reference.sha256) { throw 'Reference manifest changed during capture.' }

$result = [ordered]@{
    schemaVersion = 1
    pass = $true
    projectPath = $script:ProjectRoot
    evidenceDirectory = $script:EvidenceDirectory
    attemptId = $AttemptId
    weapon = $Weapon
    mode = $Mode
    sourceSha = $beforeSha
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
}
Write-ImmutableJson $resultPath $result
Write-Output ('WEAPON_VISUAL_CAPTURE_RESULT ' + $resultPath)
Write-Output (($result | ConvertTo-Json -Depth 12 -Compress))
