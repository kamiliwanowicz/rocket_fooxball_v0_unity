[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$EvidenceRoot,
    [Parameter(Mandatory = $true)][string]$AttemptId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ProjectPath = [System.IO.Path]::GetFullPath($ProjectPath).TrimEnd('\')
$EvidenceRoot = [System.IO.Path]::GetFullPath($EvidenceRoot).TrimEnd('\')
if ($AttemptId -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$' -or $AttemptId -in @('.', '..')) {
    throw 'AttemptId must be 1-64 filename-safe characters.'
}
if ($EvidenceRoot.StartsWith('C:\', [StringComparison]::OrdinalIgnoreCase) -and
    -not $EvidenceRoot.StartsWith('C:\wt\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'EvidenceRoot on C: must be below C:\wt.'
}

$EvidenceDirectory = Join-Path $EvidenceRoot $AttemptId
$LogPath = Join-Path $EvidenceRoot ("Capture-$AttemptId.log")
$ResultPath = Join-Path $EvidenceDirectory 'CaptureResult.json'
$LockPaths = @((Join-Path $ProjectPath 'Temp/UnityLockfile'), (Join-Path $ProjectPath 'Library/UnityLockfile'))
$SourceScopeRoots = @('Assets', 'Tools', 'ProjectSettings', 'Packages')
$RequiredSourceFiles = @('Assets/_Game/Editor/BrightArenaVisualCapture.cs', 'Tools/Validation/Capture-BrightArenaVisuals.ps1')

function Get-ProjectUnityProcesses {
    $normalized = $ProjectPath.ToLowerInvariant()
    @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object {
        $commandLine = [string]$_.CommandLine
        -not [string]::IsNullOrEmpty($commandLine) -and $commandLine.ToLowerInvariant().Contains($normalized)
    })
}

function Assert-NoProjectProcessOrLock {
    if (@(Get-ProjectUnityProcesses).Count -gt 0) { throw "Active Unity process already owns project: $ProjectPath" }
    foreach ($lockPath in $LockPaths) {
        if (Test-Path -LiteralPath $lockPath) { throw "Unity project lock exists: $lockPath" }
    }
}

function Wait-ProjectRelease {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $active = @(Get-ProjectUnityProcesses)
        $locks = @($LockPaths | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
        if ($active.Count -eq 0 -and $locks.Count -eq 0) { return }
        if ($active.Count -eq 0) {
            foreach ($lockPath in $locks) {
                if ((Get-Item -LiteralPath $lockPath).Length -eq 0) { Remove-Item -LiteralPath $lockPath -Force }
            }
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Unity process or project lock remained after capture.'
}

function Get-ScopedGitStatus {
    $status = & git -C $ProjectPath status --porcelain=v1 --untracked-files=all -- $SourceScopeRoots
    if ($LASTEXITCODE -ne 0) { throw 'git status failed.' }
    (($status -join "`n").Trim())
}

function Get-HeadSha {
    $sha = (& git -C $ProjectPath rev-parse --verify HEAD | Out-String).Trim().ToLowerInvariant()
    if ($LASTEXITCODE -ne 0 -or $sha -notmatch '^[0-9a-f]{40}$') { throw 'Unable to resolve exact Git HEAD SHA.' }
    $sha
}

function Get-ScopedHashes {
    $map = [ordered]@{}
    foreach ($relativePath in $RequiredSourceFiles) {
        $absolutePath = Join-Path $ProjectPath $relativePath
        if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) { throw "Required capture source missing: $relativePath" }
        $map[$relativePath] = (Get-FileHash -LiteralPath $absolutePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $map
}

function Assert-ManifestSource {
    param([Parameter(Mandatory = $true)]$Manifest, [Parameter(Mandatory = $true)][string]$ExpectedSha, [Parameter(Mandatory = $true)]$ExpectedHashes)
    if ($null -eq $Manifest.source -or [string]$Manifest.source.gitSha -ne $ExpectedSha) { throw 'Capture manifest Git provenance mismatch.' }
    foreach ($relativePath in $RequiredSourceFiles) {
        $entry = @($Manifest.source.fileHashes) | Where-Object { [string]$_.path -eq $relativePath } | Select-Object -First 1
        if ($null -eq $entry -or [string]$entry.sha256 -ne [string]$ExpectedHashes[$relativePath]) { throw "Capture source hash mismatch: $relativePath" }
    }
}

function Assert-PngDimensions {
    param([Parameter(Mandatory = $true)][string]$Path)
    Add-Type -AssemblyName System.Drawing
    $image = [System.Drawing.Image]::FromFile($Path)
    try {
        if ($image.Width -ne 1920 -or $image.Height -ne 1080) { throw "Capture image is not 1920x1080: $Path" }
    } finally { $image.Dispose() }
}

if (-not (Test-Path -LiteralPath $ProjectPath -PathType Container)) { throw "Project path not found: $ProjectPath" }
if (Test-Path -LiteralPath $EvidenceDirectory) { throw "Attempt evidence already exists: $EvidenceDirectory" }
New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null
$versionFile = Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) { throw 'ProjectVersion.txt missing.' }
$versionMatch = [Regex]::Match((Get-Content -Raw -LiteralPath $versionFile), 'm_EditorVersion:\s*([^\r\n]+)')
if (-not $versionMatch.Success) { throw 'Unable to read Unity editor version.' }
$UnityVersion = $versionMatch.Groups[1].Value.Trim()
$UnityPath = Join-Path ${env:ProgramFiles} ("Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe")
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity $UnityVersion not found: $UnityPath" }

Assert-NoProjectProcessOrLock
$harnessStopwatch = [Diagnostics.Stopwatch]::StartNew()
$harnessArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + (Join-Path $ProjectPath 'Tools/Tests/Invoke-HarnessTests.ps1') + '"'))
$harnessProcess = Start-Process -FilePath 'powershell.exe' -ArgumentList $harnessArguments -WindowStyle Hidden -PassThru
$harnessTimedOut = -not $harnessProcess.WaitForExit(90000)
if ($harnessTimedOut) {
    try {
        if (-not $harnessProcess.HasExited) { $harnessProcess.Kill() }
    } catch [InvalidOperationException] {
        if (-not $harnessProcess.HasExited) { throw }
    }
}
$harnessProcess.WaitForExit()
$harnessProcess.Refresh()
$harnessStopwatch.Stop()
if ($harnessTimedOut) { throw 'Harness pre-gate exceeded 90 seconds and was terminated.' }
$harnessExitCode = $harnessProcess.ExitCode
if ($harnessExitCode -ne 0) { throw "Harness pre-gate failed with exit code $harnessExitCode." }
Assert-NoProjectProcessOrLock

$beforeStatus = Get-ScopedGitStatus
$expectedGitSha = Get-HeadSha
$beforeHashes = Get-ScopedHashes
$arguments = @(
    '-batchmode', '-quit',
    '-projectPath', ('"' + $ProjectPath + '"'),
    '-executeMethod', 'RocketFooxball.Editor.BrightArenaVisualCapture.Capture',
    '-captureEvidenceRoot', ('"' + $EvidenceRoot + '"'),
    '-captureAttemptId', $AttemptId,
    '-logFile', ('"' + $LogPath + '"')
)
Write-Output ('MOVEMENT_LAB_CAPTURE_UNITY ' + $UnityPath)
Write-Output ('MOVEMENT_LAB_CAPTURE_LOG ' + $LogPath)
$unityStopwatch = [Diagnostics.Stopwatch]::StartNew()
$unityProcess = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
$unityStopwatch.Stop()
$exitCode = $unityProcess.ExitCode
Wait-ProjectRelease
if ($exitCode -ne 0) { throw "Unity capture failed with exit code $exitCode. See $LogPath" }
if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) { throw "Unity log missing: $LogPath" }

$marker = [Regex]::Match((Get-Content -Raw -LiteralPath $LogPath), 'BRIGHT_ARENA_CAPTURE_PASS\s+(.+)')
if (-not $marker.Success) { throw "Capture pass marker missing from Unity log: $LogPath" }
$manifestPath = [System.IO.Path]::GetFullPath($marker.Groups[1].Value.Trim())
$expectedManifestPath = Join-Path $EvidenceDirectory 'BrightArenaVisualManifest.json'
if (-not $manifestPath.Equals($expectedManifestPath, [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected manifest path: $manifestPath" }
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Capture manifest missing: $manifestPath" }
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if (-not $manifest.pass) { throw 'Capture manifest pass=false.' }
Assert-ManifestSource -Manifest $manifest -ExpectedSha $expectedGitSha -ExpectedHashes $beforeHashes
if ($null -eq $manifest.images -or @($manifest.images).Count -ne 6) { throw 'Capture manifest must contain six images.' }

$qualityCounts = @{ High = 0; Low = 0 }
$resultImages = @()
foreach ($item in @($manifest.images)) {
    $imagePath = [System.IO.Path]::GetFullPath([string]$item.path)
    if (-not $imagePath.StartsWith($EvidenceDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Image escaped attempt directory: $imagePath" }
    if (-not (Test-Path -LiteralPath $imagePath -PathType Leaf)) { throw "Capture image missing: $imagePath" }
    Assert-PngDimensions -Path $imagePath
    if ([int]$item.width -ne 1920 -or [int]$item.height -ne 1080) { throw "Manifest dimensions invalid: $imagePath" }
    $hash = (Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne ([string]$item.sha256).ToLowerInvariant()) { throw "Capture hash mismatch: $imagePath" }
    $quality = [string]$item.qualityLevel
    if (-not $qualityCounts.ContainsKey($quality)) { throw "Unexpected quality: $quality" }
    $qualityCounts[$quality]++
    $resultImages += [ordered]@{ view = [string]$item.view; quality = $quality; path = $imagePath; sha256 = $hash; width = 1920; height = 1080 }
}
if ($qualityCounts.High -ne 3 -or $qualityCounts.Low -ne 3) { throw 'Capture must contain three High and three Low images.' }
if ($beforeStatus -cne (Get-ScopedGitStatus)) { throw 'Git status changed during non-mutating capture.' }
if ($expectedGitSha -cne (Get-HeadSha)) { throw 'Git HEAD changed during capture.' }

$result = [ordered]@{
    schemaVersion = 1; pass = $true; attemptId = $AttemptId; projectPath = $ProjectPath
    evidenceDirectory = $EvidenceDirectory; manifestPath = $manifestPath; logPath = $LogPath; unityExitCode = $exitCode
    harnessMilliseconds = [Math]::Round($harnessStopwatch.Elapsed.TotalMilliseconds)
    unityMilliseconds = [Math]::Round($unityStopwatch.Elapsed.TotalMilliseconds); images = $resultImages
}
[System.IO.File]::WriteAllText($ResultPath, ($result | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Output ('MOVEMENT_LAB_CAPTURE_RESULT ' + $ResultPath)
Write-Output (($result | ConvertTo-Json -Depth 5 -Compress))
