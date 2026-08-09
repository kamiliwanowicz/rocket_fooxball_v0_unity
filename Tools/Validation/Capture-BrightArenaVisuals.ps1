[CmdletBinding()]
param(
    [string]$ProjectPath,
    [switch]$FinalizeEvidence,
    [string]$SourceDirectory,
    [string]$TargetDirectory,
    [int]$ParentPid,
    [string]$FinalizerLog
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrEmpty($ProjectPath)) { $ProjectPath = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }

if ($FinalizeEvidence) {
    try {
        if ([string]::IsNullOrEmpty($SourceDirectory) -or [string]::IsNullOrEmpty($TargetDirectory) -or $ParentPid -le 0) { throw 'FinalizeEvidence requires source/target/parent.' }
        $manifestSource = Join-Path $SourceDirectory 'BrightArenaVisualManifest.json'
        $readDeadline = [DateTime]::UtcNow.AddSeconds(45)
        while (-not (Test-Path -LiteralPath $manifestSource -PathType Leaf) -and $null -ne (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $readDeadline) { Start-Sleep -Milliseconds 50 }
        if (-not (Test-Path -LiteralPath $manifestSource -PathType Leaf)) { throw 'Capture manifest was not ready before Unity parent exited.' }
        $payload = [ordered]@{}
        foreach ($sourceFile in Get-ChildItem -LiteralPath $SourceDirectory -File -Force) {
            $payload[$sourceFile.Name] = [IO.File]::ReadAllBytes($sourceFile.FullName)
        }
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        while ($null -ne (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
        if ($null -ne (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue)) { throw 'Unity parent remained during evidence finalization.' }
        if (Test-Path -LiteralPath $TargetDirectory) {
            foreach ($existingFile in Get-ChildItem -LiteralPath $TargetDirectory -File -Force) { [IO.File]::SetAttributes($existingFile.FullName, [IO.FileAttributes]::Normal) }
            [IO.File]::SetAttributes($TargetDirectory, [IO.FileAttributes]::Directory)
        } else {
            [IO.Directory]::CreateDirectory($TargetDirectory) | Out-Null
        }
        foreach ($name in $payload.Keys) { [IO.File]::WriteAllBytes((Join-Path $TargetDirectory $name), $payload[$name]) }
        if (-not [string]::IsNullOrEmpty($FinalizerLog)) { [IO.File]::WriteAllText($FinalizerLog, 'FINALIZER_PASS') }
        exit 0
    } catch {
        if (-not [string]::IsNullOrEmpty($FinalizerLog)) { [IO.File]::WriteAllText($FinalizerLog, ($_ | Out-String)) }
        exit 1
    }
}

$ProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
$UnityVersion = '6000.5.6f1'
$UnityPath = Join-Path ${env:ProgramFiles} ('Unity\Hub\Editor\' + $UnityVersion + '\Editor\Unity.exe')
$LogDirectory = Join-Path $ProjectPath 'Temp/BrightArenaVisuals'
$LogPath = Join-Path $LogDirectory ('Capture-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '.log')
$LockPaths = @(
    (Join-Path $ProjectPath 'Temp/UnityLockfile'),
    (Join-Path $ProjectPath 'Library/UnityLockfile')
)

function Get-ProjectUnityProcesses {
    $normalized = $ProjectPath.TrimEnd('\').ToLowerInvariant()
    Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object {
        $commandLine = [string]$_.CommandLine
        if ([string]::IsNullOrEmpty($commandLine)) { return $false }
        $commandLine.ToLowerInvariant().Contains($normalized)
    }
}

function Assert-NoProjectProcessOrLock {
    $active = @(Get-ProjectUnityProcesses)
    if ($active.Count -gt 0) {
        throw "Active Unity process already owns project: $ProjectPath"
    }
    foreach ($lockPath in $LockPaths) {
        if (Test-Path -LiteralPath $lockPath) {
            throw "Unity project lock exists: $lockPath"
        }
    }
}

function Get-GitStatus {
    $status = & git -C $ProjectPath status --porcelain --untracked-files=all
    if ($LASTEXITCODE -ne 0) { throw 'git status failed.' }
    return (($status -join "`n").TrimEnd())
}

function Get-TrackedHashes {
    $paths = @(& git -C $ProjectPath ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
    $map = [ordered]@{}
    foreach ($relativePath in $paths) {
        if ([string]::IsNullOrWhiteSpace($relativePath)) { continue }
        $absolutePath = Join-Path $ProjectPath $relativePath
        if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) { throw "Tracked file missing before/after capture: $relativePath" }
        $map[$relativePath] = (Get-FileHash -LiteralPath $absolutePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $map
}

function Wait-ProjectRelease {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $active = @(Get-ProjectUnityProcesses)
        $lockFound = $false
        foreach ($lockPath in $LockPaths) {
            if (Test-Path -LiteralPath $lockPath) {
                if ($active.Count -eq 0) {
                    # Unity 6000 can leave zero-byte lock sentinel after a
                    # batch exit. No process owns it now; remove only this
                    # validated project-local sentinel, then verify absence.
                    [System.IO.File]::Delete((Resolve-Path -LiteralPath $lockPath).Path)
                }
                $lockFound = $lockFound -or (Test-Path -LiteralPath $lockPath)
            }
        }
        if ($active.Count -eq 0 -and -not $lockFound) { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Unity process or project lock remained after capture.'
}

if (-not (Test-Path -LiteralPath $ProjectPath -PathType Container)) { throw "Project path not found: $ProjectPath" }
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity $UnityVersion not found: $UnityPath" }
if (-not (Test-Path -LiteralPath (Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt') -PathType Leaf)) { throw 'ProjectVersion.txt missing.' }
$projectVersion = Get-Content -Raw (Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt')
if ($projectVersion -notmatch ('m_EditorVersion:\s*' + [Regex]::Escape($UnityVersion))) { throw "Project Unity version is not $UnityVersion." }
New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null
Assert-NoProjectProcessOrLock
$beforeStatus = Get-GitStatus
$beforeHashes = Get-TrackedHashes
$arguments = @(
    '-batchmode',
    '-quit',
    '-projectPath', $ProjectPath,
    '-executeMethod', 'RocketFooxball.Editor.BrightArenaVisualCapture.Capture',
    '-logFile', $LogPath
)
Write-Output ('BRIGHT_ARENA_CAPTURE_UNITY ' + $UnityPath)
Write-Output ('BRIGHT_ARENA_CAPTURE_LOG ' + $LogPath)
$unityProcess = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
$exitCode = $unityProcess.ExitCode
Wait-ProjectRelease
if ($exitCode -ne 0) {
    throw "Unity capture failed with exit code $exitCode. See $LogPath"
}
if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) { throw "Unity log missing: $LogPath" }
$logText = Get-Content -Raw -LiteralPath $LogPath
$marker = [Regex]::Match($logText, 'BRIGHT_ARENA_CAPTURE_PASS\s+(.+)')
if (-not $marker.Success) { throw "Capture pass marker missing from Unity log: $LogPath" }
$manifestPath = $marker.Groups[1].Value.Trim()
for ($i = 0; $i -lt 120 -and -not (Test-Path -LiteralPath $manifestPath -PathType Leaf); $i++) { Start-Sleep -Milliseconds 250 }
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Capture manifest missing: $manifestPath" }
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
if (-not $manifest.pass) { throw 'Capture manifest pass=false.' }
if ($null -eq $manifest.images -or $manifest.images.Count -ne 6) { throw 'Capture manifest must contain six images.' }
foreach ($image in $manifest.images) {
    if (-not (Test-Path -LiteralPath $image.path -PathType Leaf)) { throw "Capture image missing: $($image.path)" }
    $hash = (Get-FileHash -LiteralPath $image.path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $image.sha256.ToLowerInvariant()) { throw "Capture image SHA-256 mismatch: $($image.path)" }
    if ((Get-Item -LiteralPath $image.path).Length -le 0) { throw "Capture image is empty: $($image.path)" }
}
$afterStatus = Get-GitStatus
$afterHashes = Get-TrackedHashes
if ($beforeStatus -cne $afterStatus) { throw 'Git status changed during non-mutating capture.' }
if ($beforeHashes.Count -ne $afterHashes.Count) { throw 'Tracked file set changed during capture.' }
foreach ($key in $beforeHashes.Keys) {
    if (-not $afterHashes.Contains($key) -or $beforeHashes[$key] -cne $afterHashes[$key]) { throw "Tracked file hash changed during capture: $key" }
}
Write-Output ('BRIGHT_ARENA_CAPTURE_EVIDENCE ' + $manifest.evidenceDirectory)
Write-Output ('BRIGHT_ARENA_CAPTURE_MANIFEST ' + $manifestPath)
Write-Output ('BRIGHT_ARENA_CAPTURE_MANIFEST_SHA256 ' + (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant())
Write-Output 'BRIGHT_ARENA_CAPTURE_SOURCE_GENERATED_HASHES_UNCHANGED true'
Write-Output ('BRIGHT_ARENA_CAPTURE_EXIT ' + $exitCode)
