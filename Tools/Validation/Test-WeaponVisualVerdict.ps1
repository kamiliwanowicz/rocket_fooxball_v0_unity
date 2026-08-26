[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][Alias('weaponVisualVerdictPath')][string]$VerdictPath,
    [Parameter(Mandatory = $true)][Alias('agentId')][string]$ExpectedAgentId,
    [Parameter(Mandatory = $true)][Alias('sourceSha')][string]$ExpectedSourceSha,
    [Parameter(Mandatory = $true)][Alias('ReferenceManifest')][string]$ReferenceManifestPath,
    [Parameter(Mandatory = $false)][Alias('RocketManifestPath')][string]$RocketCaptureManifest,
    [Parameter(Mandatory = $false)][Alias('ShotgunManifestPath')][string]$ShotgunCaptureManifest,
    [Parameter(Mandatory = $false)][string[]]$CaptureManifestPaths = @(),
    [string]$ExpectedPriorVerdictHash = '',
    [string]$ResultPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Property {
    param([AllowNull()]$Object, [Parameter(Mandatory = $true)][string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-Hash {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw ('Required evidence file missing: ' + $Path) }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Resolve-EvidencePath {
    param([Parameter(Mandatory = $true)][string]$Value, [Parameter(Mandatory = $true)][string]$BaseDirectory)
    if ([IO.Path]::IsPathRooted($Value)) { return [IO.Path]::GetFullPath($Value) }
    return [IO.Path]::GetFullPath((Join-Path $BaseDirectory ($Value.Replace('/', '\'))))
}

function Read-ReferenceHashes {
    param([Parameter(Mandatory = $true)][string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw ('Reference manifest missing: ' + $full) }
    $manifestHash = Get-Hash $full
    try { $manifest = Get-Content -Raw -LiteralPath $full | ConvertFrom-Json -ErrorAction Stop } catch { throw ('Reference manifest JSON is invalid: ' + $_.Exception.Message) }
    if ([int](Get-Property $manifest 'schemaVersion') -ne 1) { throw 'Reference manifest schemaVersion must equal 1.' }
    $arrays = New-Object System.Collections.Generic.List[object]
    foreach ($name in @('entries', 'references', 'items')) {
        $value = Get-Property $manifest $name
        if ($null -ne $value) { $arrays.Add($value) | Out-Null }
    }
    if ($arrays.Count -ne 1 -or @($arrays[0]).Count -ne 3) { throw 'Reference manifest must contain exactly three entries.' }
    $expectedIds = @('game-bright', 'game-dark', 'quake-hires')
    $result = [ordered]@{}
    foreach ($entry in @($arrays[0])) {
        $id = [string](Get-Property $entry 'id')
        $bytes = [int64](Get-Property $entry 'bytes')
        $sha = [string](Get-Property $entry 'sha256').ToLowerInvariant()
        if ($expectedIds -notcontains $id -or $result.Contains($id) -or $bytes -le 0 -or $sha -notmatch '^[0-9a-f]{64}$') { throw ('Reference manifest entry is invalid: ' + $id) }
        $original = [string](Get-Property $entry 'originalPath')
        $evidence = [string](Get-Property $entry 'evidencePath')
        if ([string]::IsNullOrWhiteSpace($original) -or [string]::IsNullOrWhiteSpace($evidence)) { throw ('Reference manifest paths missing: ' + $id) }
        foreach ($file in @(
            [ordered]@{ label = 'originalPath'; path = Resolve-EvidencePath $original (Split-Path -Parent $full) },
            [ordered]@{ label = 'evidencePath'; path = Resolve-EvidencePath $evidence (Split-Path -Parent $full) }
        )) {
            if (-not (Test-Path -LiteralPath $file.path -PathType Leaf) -or (Get-Item -LiteralPath $file.path).Length -ne $bytes -or (Get-Hash $file.path) -ne $sha) {
                throw ('Reference ' + $id + ' ' + $file.label + ' hash mismatch.')
            }
        }
        $result[$id] = $sha
    }
    foreach ($id in $expectedIds) { if (-not $result.Contains($id)) { throw ('Reference manifest missing id: ' + $id) } }
    return [pscustomobject]@{ path = $full; sha256 = $manifestHash; hashes = $result }
}

function Read-CaptureManifest {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$ExpectedWeapon)
    $full = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw ('Capture manifest missing: ' + $full) }
    $hash = Get-Hash $full
    try { $manifest = Get-Content -Raw -LiteralPath $full | ConvertFrom-Json -ErrorAction Stop } catch { throw ('Capture manifest JSON is invalid: ' + $_.Exception.Message) }
    if ([int](Get-Property $manifest 'schemaVersion') -ne 1 -or [string](Get-Property $manifest 'weaponCaptureWeapon') -cne $ExpectedWeapon) { throw ('Capture manifest schema/weapon mismatch: ' + $full) }
    if ([string](Get-Property $manifest 'sourceSha').ToLowerInvariant() -ne $ExpectedSourceSha.ToLowerInvariant()) { throw ('Capture manifest source SHA mismatch: ' + $full) }
    $items = @(Get-Property $manifest 'images')
    if ($items.Count -ne 6) { throw ('Capture manifest must contain six images: ' + $full) }
    $map = [ordered]@{}
    foreach ($item in $items) {
        $pathValue = [string](Get-Property $item 'path')
        $imagePath = Resolve-EvidencePath $pathValue (Split-Path -Parent $full)
        $filename = [string](Get-Property $item 'filename')
        $declared = [string](Get-Property $item 'sha256').ToLowerInvariant()
        if ([string]::IsNullOrWhiteSpace($filename) -or $declared -notmatch '^[0-9a-f]{64}$') { throw ('Capture image entry is incomplete: ' + $full) }
        $actual = Get-Hash $imagePath
        if ($actual -ne $declared) { throw ('Capture image hash mismatch: ' + $imagePath) }
        if ($map.Contains($imagePath) -or $map.Contains($filename)) { throw ('Capture image key duplicated: ' + $filename) }
        $record = [pscustomobject]@{ filename = $filename; path = $imagePath; sha256 = $actual }
        $map[$imagePath] = $record
        $map[$filename] = $record
    }
    return [pscustomobject]@{ path = $full; sha256 = $hash; weapon = $ExpectedWeapon; images = $map }
}

function Get-CaptureArguments {
    $paths = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($RocketCaptureManifest)) { $paths.Add($RocketCaptureManifest) | Out-Null }
    if (-not [string]::IsNullOrWhiteSpace($ShotgunCaptureManifest)) { $paths.Add($ShotgunCaptureManifest) | Out-Null }
    foreach ($path in @($CaptureManifestPaths)) { if (-not [string]::IsNullOrWhiteSpace($path)) { $paths.Add($path) | Out-Null } }
    if ($paths.Count -ne 2) { throw 'Verdict validation requires exactly one Rocket and one Shotgun capture manifest.' }
    return @($paths.ToArray())
}

function Read-CaptureSet {
    $rocket = $null
    $shotgun = $null
    foreach ($path in @(Get-CaptureArguments)) {
        $probe = $null
        try { $probe = Get-Content -Raw -LiteralPath ([IO.Path]::GetFullPath($path)) | ConvertFrom-Json -ErrorAction Stop } catch { throw ('Capture manifest JSON is invalid: ' + $path) }
        $weapon = [string](Get-Property $probe 'weaponCaptureWeapon')
        if ($weapon -eq 'Rocket') {
            if ($null -ne $rocket) { throw 'Duplicate Rocket capture manifest.' }
            $rocket = Read-CaptureManifest $path 'Rocket'
        } elseif ($weapon -eq 'Shotgun') {
            if ($null -ne $shotgun) { throw 'Duplicate Shotgun capture manifest.' }
            $shotgun = Read-CaptureManifest $path 'Shotgun'
        } else { throw ('Capture manifest weapon is invalid: ' + $weapon) }
    }
    if ($null -eq $rocket -or $null -eq $shotgun) { throw 'Exactly one Rocket and one Shotgun capture manifest are required.' }
    return [pscustomobject]@{ Rocket = $rocket; Shotgun = $shotgun }
}

function Read-CaptureManifestHashRecords {
    param([AllowNull()]$Value)
    $records = [ordered]@{}
    if ($null -eq $Value) { return $records }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        foreach ($item in @($Value)) {
            $weapon = [string](Get-Property $item 'weapon')
            $hash = [string](Get-Property $item 'sha256').ToLowerInvariant()
            if ([string]::IsNullOrWhiteSpace($weapon) -or $hash -notmatch '^[0-9a-f]{64}$' -or $records.Contains($weapon)) { throw 'Verdict captureManifestHashes array is invalid.' }
            $records[$weapon] = $hash
        }
    } else {
        foreach ($property in @($Value.PSObject.Properties)) {
            $hash = [string]$property.Value
            if ($hash -notmatch '^[0-9a-fA-F]{64}$' -or $records.Contains([string]$property.Name)) { throw 'Verdict captureManifestHashes object is invalid.' }
            $records[[string]$property.Name] = $hash.ToLowerInvariant()
        }
    }
    return $records
}

function Read-ReferenceHashRecords {
    param([AllowNull()]$Value)
    $records = [ordered]@{}
    if ($null -eq $Value) { return $records }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        foreach ($item in @($Value)) {
            $id = [string](Get-Property $item 'id')
            $hash = [string](Get-Property $item 'sha256').ToLowerInvariant()
            if ([string]::IsNullOrWhiteSpace($id) -or $hash -notmatch '^[0-9a-f]{64}$' -or $records.Contains($id)) { throw 'Verdict referenceHashes array is invalid.' }
            $records[$id] = $hash
        }
    } else {
        foreach ($property in @($Value.PSObject.Properties)) {
            $hash = [string]$property.Value
            if ($hash -notmatch '^[0-9a-fA-F]{64}$' -or $records.Contains([string]$property.Name)) { throw 'Verdict referenceHashes object is invalid.' }
            $records[[string]$property.Name] = $hash.ToLowerInvariant()
        }
    }
    return $records
}

function Get-EvidenceImageRecord {
    param([Parameter(Mandatory = $true)]$Value, [Parameter(Mandatory = $true)]$CaptureSet, [Parameter(Mandatory = $true)][string]$BaseDirectory)
    $pathText = if ($Value -is [string]) { [string]$Value } else { [string](Get-Property $Value 'path') }
    if ([string]::IsNullOrWhiteSpace($pathText) -and $Value -isnot [string]) { $pathText = [string](Get-Property $Value 'imagePath') }
    if ([string]::IsNullOrWhiteSpace($pathText)) { throw 'Verdict evidenceImages item has no path.' }
    $path = Resolve-EvidencePath $pathText $BaseDirectory
    $declared = if ($Value -is [string]) { '' } else { [string](Get-Property $Value 'sha256') }
    if ([string]::IsNullOrWhiteSpace($declared) -and $Value -isnot [string]) { $declared = [string](Get-Property $Value 'hash') }
    $record = $null
    foreach ($capture in @($CaptureSet.Rocket, $CaptureSet.Shotgun)) {
        if ($capture.images.Contains($path)) { $record = $capture.images[$path]; break }
        $name = [IO.Path]::GetFileName($path)
        if ($capture.images.Contains($name)) { $record = $capture.images[$name]; break }
    }
    if ($null -eq $record) { throw ('Verdict evidence image is not one of the current capture images: ' + $path) }
    $actual = Get-Hash $record.path
    if (-not [string]::IsNullOrWhiteSpace($declared) -and $declared.ToLowerInvariant() -ne $actual) { throw ('Verdict evidence image hash mismatch: ' + $path) }
    return [ordered]@{ path = $record.path; sha256 = $actual }
}

function Write-ImmutableJson {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)]$Value)
    if (Test-Path -LiteralPath $Path) { throw ('Validation result already exists: ' + $Path) }
    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $temporary = Join-Path $directory ('.weapon-verdict-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temporary, (($Value | ConvertTo-Json -Depth 16) + [Environment]::NewLine), (New-Object Text.UTF8Encoding($false)))
        [IO.File]::Move($temporary, $Path)
    } finally {
        if (Test-Path -LiteralPath $temporary) { [IO.File]::Delete($temporary) }
    }
}

$verdictFullPath = [IO.Path]::GetFullPath($VerdictPath)
if (-not (Test-Path -LiteralPath $verdictFullPath -PathType Leaf)) { throw ('Verdict JSON missing: ' + $verdictFullPath) }
$reference = Read-ReferenceHashes $ReferenceManifestPath
$captureSet = Read-CaptureSet
$verdictBeforeHash = Get-Hash $verdictFullPath
try { $verdict = Get-Content -Raw -LiteralPath $verdictFullPath | ConvertFrom-Json -ErrorAction Stop } catch { throw ('Verdict JSON is invalid: ' + $_.Exception.Message) }
if ([int](Get-Property $verdict 'schemaVersion') -ne 1) { throw 'Verdict schemaVersion must equal 1.' }
if ([string](Get-Property $verdict 'agentId') -cne $ExpectedAgentId) { throw 'Verdict agentId binding failed.' }
if ([string](Get-Property $verdict 'profile') -cne 'sol_high' -or [string](Get-Property $verdict 'role') -cne 'weapon-visual-verifier') { throw 'Verdict profile/role contract failed.' }
if ([string](Get-Property $verdict 'sourceSha').ToLowerInvariant() -ne $ExpectedSourceSha.ToLowerInvariant()) { throw 'Verdict sourceSha binding failed.' }
if ($ExpectedSourceSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'ExpectedSourceSha must be an exact 40-character SHA.' }
if ((Get-Hash $ReferenceManifestPath) -ne $reference.sha256) { throw 'Reference manifest changed during verdict validation.' }

$actualReferenceHashes = Read-ReferenceHashRecords (Get-Property $verdict 'referenceHashes')
if ($actualReferenceHashes.Count -ne $reference.hashes.Count) { throw 'Verdict reference hash count mismatch.' }
foreach ($id in @('game-bright', 'game-dark', 'quake-hires')) {
    if (-not $actualReferenceHashes.Contains($id) -or $actualReferenceHashes[$id] -ne $reference.hashes[$id]) { throw ('Verdict reference hash mismatch: ' + $id) }
}

$actualCaptureHashes = Read-CaptureManifestHashRecords (Get-Property $verdict 'captureManifestHashes')
if ($actualCaptureHashes.Count -ne 2 -or -not $actualCaptureHashes.Contains('Rocket') -or -not $actualCaptureHashes.Contains('Shotgun') -or
    $actualCaptureHashes['Rocket'] -ne $captureSet.Rocket.sha256 -or $actualCaptureHashes['Shotgun'] -ne $captureSet.Shotgun.sha256) { throw 'Verdict capture manifest hash binding failed.' }
$prior = [string](Get-Property $verdict 'acceptedPriorVerdictHash')
if ([string]::IsNullOrWhiteSpace($ExpectedPriorVerdictHash)) {
    if (-not [string]::IsNullOrEmpty($prior)) { throw 'Verdict unexpectedly binds an accepted prior verdict.' }
} elseif ($prior.ToLowerInvariant() -ne $ExpectedPriorVerdictHash.ToLowerInvariant() -or $prior -notmatch '^[0-9a-fA-F]{64}$') {
    throw 'Verdict acceptedPriorVerdictHash binding failed.'
}

$expectedPredicateIds = @('high.sunward.readable', 'high.crosslight.readable', 'high.awaylight.readable', 'surface-marks-fixed', 'palette-warm-no-blue', 'scratches-physical', 'framing-silhouette', 'low-material-hierarchy')
$predicates = @(Get-Property $verdict 'predicates')
if ($predicates.Count -ne 16) { throw 'Verdict predicates must contain exactly 16 records.' }
$seen = @{}
$evidenceRecords = New-Object System.Collections.Generic.List[object]
for ($index = 0; $index -lt $predicates.Count; $index++) {
    $predicate = $predicates[$index]
    $weapon = [string](Get-Property $predicate 'weapon')
    $id = [string](Get-Property $predicate 'id')
    if ($weapon -notin @('Rocket', 'Shotgun') -or $expectedPredicateIds -notcontains $id) { throw ('Verdict predicate id/weapon invalid at index ' + $index) }
    $key = $weapon + '|' + $id
    if ($seen.ContainsKey($key)) { throw ('Verdict predicate duplicated: ' + $key) }
    $seen[$key] = $true
    if (-not [bool](Get-Property $predicate 'pass')) { throw ('Verdict predicate failed: ' + $key) }
    $evidence = @(Get-Property $predicate 'evidenceImages')
    if ($evidence.Count -eq 0) { $evidence = @(Get-Property $predicate 'evidence') }
    if ($evidence.Count -eq 0) { throw ('Verdict predicate has no evidence images: ' + $key) }
    foreach ($image in $evidence) { $evidenceRecords.Add((Get-EvidenceImageRecord $image $captureSet (Split-Path -Parent $verdictFullPath))) | Out-Null }
}
foreach ($weapon in @('Rocket', 'Shotgun')) {
    foreach ($id in $expectedPredicateIds) { if (-not $seen.ContainsKey($weapon + '|' + $id)) { throw ('Verdict predicate set missing: ' + $weapon + '|' + $id) } }
}
$failures = @(Get-Property $verdict 'failures')
if ($failures.Count -ne 0) { throw 'Verdict failures must be empty when overallPass is true.' }
if (-not [bool](Get-Property $verdict 'overallPass')) { throw 'Verdict overallPass must be true after all predicates pass.' }
if ((Get-Hash $verdictFullPath) -ne $verdictBeforeHash) { throw 'Verdict changed during validation.' }

$resultPathValue = if ([string]::IsNullOrWhiteSpace($ResultPath)) { Join-Path (Split-Path -Parent $verdictFullPath) 'WeaponVisualVerdictValidation.json' } else { [IO.Path]::GetFullPath($ResultPath) }
$result = [ordered]@{
    schemaVersion = 1
    pass = $true
    verdictPath = $verdictFullPath
    verdictSha256 = $verdictBeforeHash
    expectedAgentId = $ExpectedAgentId
    sourceSha = $ExpectedSourceSha.ToLowerInvariant()
    referenceManifestPath = $reference.path
    referenceManifestSha256 = $reference.sha256
    captureManifestHashes = [ordered]@{ Rocket = $captureSet.Rocket.sha256; Shotgun = $captureSet.Shotgun.sha256 }
    acceptedPriorVerdictHash = if ([string]::IsNullOrWhiteSpace($ExpectedPriorVerdictHash)) { '' } else { $ExpectedPriorVerdictHash.ToLowerInvariant() }
    predicateCount = $predicates.Count
    evidenceImageCount = $evidenceRecords.Count
    overallPass = $true
}
Write-ImmutableJson $resultPathValue $result
Write-Output ('WEAPON_VISUAL_VERDICT_PASS ' + $resultPathValue)
Write-Output (($result | ConvertTo-Json -Depth 12 -Compress))
