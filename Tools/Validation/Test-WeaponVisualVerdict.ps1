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
Import-Module -Name (Join-Path -Path $PSHOME -ChildPath 'Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
if ($null -eq (Get-Command -Name Get-FileHash -ErrorAction SilentlyContinue)) {
    throw 'Get-FileHash is unavailable after importing Microsoft.PowerShell.Utility.'
}

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

function Skip-ReferenceJsonWhitespace {
    param([Parameter(Mandatory = $true)][string]$Json, [Parameter(Mandatory = $true)][ref]$Index)
    while ($Index.Value -lt $Json.Length) {
        $character = $Json[$Index.Value]
        if ($character -cne ' ' -and $character -cne "`t" -and $character -cne "`r" -and $character -cne "`n") { break }
        $Index.Value = $Index.Value + 1
    }
}

function Read-ReferenceJsonString {
    param([Parameter(Mandatory = $true)][string]$Json, [Parameter(Mandatory = $true)][ref]$Index)
    if ($Index.Value -ge $Json.Length -or $Json[$Index.Value] -cne '"') { throw 'Reference manifest JSON string expected.' }
    $Index.Value = $Index.Value + 1
    $builder = New-Object Text.StringBuilder
    while ($Index.Value -lt $Json.Length) {
        $character = $Json[$Index.Value]
        if ($character -ceq '"') {
            $Index.Value = $Index.Value + 1
            return $builder.ToString()
        }
        if ([int][char]$character -lt 0x20) { throw 'Reference manifest JSON string contains an unescaped control character.' }
        if ($character -ceq '\') {
            $Index.Value = $Index.Value + 1
            if ($Index.Value -ge $Json.Length) { throw 'Reference manifest JSON escape is incomplete.' }
            $escape = $Json[$Index.Value]
            switch ([string]$escape) {
                '"' { [void]$builder.Append('"') }
                '\' { [void]$builder.Append('\') }
                '/' { [void]$builder.Append('/') }
                'b' { [void]$builder.Append([char]8) }
                'f' { [void]$builder.Append([char]12) }
                'n' { [void]$builder.Append([char]10) }
                'r' { [void]$builder.Append([char]13) }
                't' { [void]$builder.Append([char]9) }
                'u' {
                    if ($Index.Value + 4 -ge $Json.Length) { throw 'Reference manifest JSON unicode escape is incomplete.' }
                    $hex = $Json.Substring($Index.Value + 1, 4)
                    if ($hex -notmatch '^[0-9a-fA-F]{4}$') { throw 'Reference manifest JSON unicode escape is invalid.' }
                    [void]$builder.Append([char]([Convert]::ToInt32($hex, 16)))
                    $Index.Value = $Index.Value + 4
                }
                default { throw ('Reference manifest JSON escape is invalid: \' + [string]$escape) }
            }
        } else {
            [void]$builder.Append($character)
        }
        $Index.Value = $Index.Value + 1
    }
    throw 'Reference manifest JSON string is unterminated.'
}

function Read-ReferenceJsonNumber {
    param([Parameter(Mandatory = $true)][string]$Json, [Parameter(Mandatory = $true)][ref]$Index)
    $match = [regex]::Match($Json.Substring($Index.Value), '^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?')
    if (-not $match.Success) { throw 'Reference manifest JSON number is invalid.' }
    $Index.Value = $Index.Value + $match.Length
}

function Read-ReferenceJsonLiteral {
    param([Parameter(Mandatory = $true)][string]$Json, [Parameter(Mandatory = $true)][ref]$Index)
    foreach ($literal in @('true', 'false', 'null')) {
        if ($Index.Value + $literal.Length -le $Json.Length -and
            $Json.Substring($Index.Value, $literal.Length).Equals($literal, [StringComparison]::Ordinal)) {
            $Index.Value = $Index.Value + $literal.Length
            return
        }
    }
    throw 'Reference manifest JSON literal is invalid.'
}

function Read-ReferenceJsonValue {
    param([Parameter(Mandatory = $true)][string]$Json, [Parameter(Mandatory = $true)][ref]$Index)
    Skip-ReferenceJsonWhitespace $Json $Index
    if ($Index.Value -ge $Json.Length) { throw 'Reference manifest JSON value is missing.' }
    $character = $Json[$Index.Value]
    if ($character -ceq '"') { [void](Read-ReferenceJsonString $Json $Index); return }
    if ($character -ceq '{') { Read-ReferenceJsonObject $Json $Index; return }
    if ($character -ceq '[') { Read-ReferenceJsonArray $Json $Index; return }
    if ($character -ceq 't' -or $character -ceq 'f' -or $character -ceq 'n') { Read-ReferenceJsonLiteral $Json $Index; return }
    if ($character -ceq '-' -or ($character -ge '0' -and $character -le '9')) { Read-ReferenceJsonNumber $Json $Index; return }
    throw ('Reference manifest JSON value is invalid at offset ' + $Index.Value + '.')
}

function Read-ReferenceJsonArray {
    param([Parameter(Mandatory = $true)][string]$Json, [Parameter(Mandatory = $true)][ref]$Index)
    if ($Json[$Index.Value] -cne '[') { throw 'Reference manifest JSON array expected.' }
    $Index.Value = $Index.Value + 1
    Skip-ReferenceJsonWhitespace $Json $Index
    if ($Index.Value -lt $Json.Length -and $Json[$Index.Value] -ceq ']') { $Index.Value = $Index.Value + 1; return }
    while ($true) {
        Read-ReferenceJsonValue $Json $Index
        Skip-ReferenceJsonWhitespace $Json $Index
        if ($Index.Value -ge $Json.Length) { throw 'Reference manifest JSON array is unterminated.' }
        if ($Json[$Index.Value] -ceq ']') { $Index.Value = $Index.Value + 1; return }
        if ($Json[$Index.Value] -cne ',') { throw 'Reference manifest JSON array separator is missing.' }
        $Index.Value = $Index.Value + 1
        Skip-ReferenceJsonWhitespace $Json $Index
    }
}

function Read-ReferenceJsonObject {
    param([Parameter(Mandatory = $true)][string]$Json, [Parameter(Mandatory = $true)][ref]$Index)
    if ($Json[$Index.Value] -cne '{') { throw 'Reference manifest JSON object expected.' }
    $Index.Value = $Index.Value + 1
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    Skip-ReferenceJsonWhitespace $Json $Index
    if ($Index.Value -lt $Json.Length -and $Json[$Index.Value] -ceq '}') { $Index.Value = $Index.Value + 1; return }
    while ($true) {
        $name = Read-ReferenceJsonString $Json $Index
        if (-not $seen.Add($name)) { throw ('Reference manifest JSON object contains duplicate property: ' + $name) }
        Skip-ReferenceJsonWhitespace $Json $Index
        if ($Index.Value -ge $Json.Length -or $Json[$Index.Value] -cne ':') { throw 'Reference manifest JSON object separator is missing.' }
        $Index.Value = $Index.Value + 1
        Read-ReferenceJsonValue $Json $Index
        Skip-ReferenceJsonWhitespace $Json $Index
        if ($Index.Value -ge $Json.Length) { throw 'Reference manifest JSON object is unterminated.' }
        if ($Json[$Index.Value] -ceq '}') { $Index.Value = $Index.Value + 1; return }
        if ($Json[$Index.Value] -cne ',') { throw 'Reference manifest JSON object separator is missing.' }
        $Index.Value = $Index.Value + 1
        Skip-ReferenceJsonWhitespace $Json $Index
    }
}

function Assert-UniqueReferenceJsonProperties {
    param([Parameter(Mandatory = $true)][string]$Json)
    $index = 0
    Skip-ReferenceJsonWhitespace $Json ([ref]$index)
    if ($index -ge $Json.Length -or $Json[$index] -cne '{') { throw 'Reference manifest JSON root must be an object.' }
    Read-ReferenceJsonObject $Json ([ref]$index)
    Skip-ReferenceJsonWhitespace $Json ([ref]$index)
    if ($index -ne $Json.Length) { throw ('Reference manifest JSON has trailing data at offset ' + $index + '.') }
}

function Assert-ExactJsonProperties {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Label
    )
    if ($null -eq $Object -or $Object -is [string] -or $Object -is [ValueType]) { throw ($Label + ' must be a JSON object.') }
    $actual = @($Object.PSObject.Properties | ForEach-Object { [string]$_.Name })
    if ($actual.Count -ne $Expected.Count -or
        @($actual | Where-Object { $Expected -cnotcontains $_ }).Count -gt 0 -or
        @($Expected | Where-Object { $actual -cnotcontains $_ }).Count -gt 0) {
        throw ($Label + ' has unexpected fields; expected exactly: ' + ($Expected -join ', '))
    }
}

function Test-ReferenceJsonInteger {
    param([AllowNull()]$Value)
    return ($Value -is [byte] -or $Value -is [sbyte] -or $Value -is [int16] -or $Value -is [uint16] -or
        $Value -is [int32] -or $Value -is [uint32] -or $Value -is [int64] -or $Value -is [uint64])
}

function Read-ReferenceHashes {
    param([Parameter(Mandatory = $true)][string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw ('Reference manifest missing: ' + $full) }
    $manifestHash = Get-Hash $full
    $raw = [IO.File]::ReadAllText($full)
    try { Assert-UniqueReferenceJsonProperties $raw } catch { throw ('Reference manifest JSON structure is invalid: ' + $_.Exception.Message) }
    try { $manifest = $raw | ConvertFrom-Json -ErrorAction Stop } catch { throw ('Reference manifest JSON is invalid: ' + $_.Exception.Message) }
    Assert-ExactJsonProperties $manifest @('schemaVersion', 'references') 'Reference manifest'
    $schemaVersion = Get-Property $manifest 'schemaVersion'
    if (-not (Test-ReferenceJsonInteger $schemaVersion) -or [int64]$schemaVersion -ne 1) { throw 'Reference manifest schemaVersion must equal 1.' }
    $references = Get-Property $manifest 'references'
    if ($null -eq $references -or $references -isnot [Array]) { throw 'Reference manifest references must be an array.' }
    $entries = @($references)
    if ($entries.Count -ne 3) { throw 'Reference manifest must contain exactly three schema-1 references.' }
    $expectedIds = @('game-bright', 'game-dark', 'quake-hires')
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $result = [ordered]@{}
    foreach ($entry in $entries) {
        Assert-ExactJsonProperties $entry @('logicalId', 'originalPath', 'copiedEvidencePath', 'byteLength', 'sha256') 'Reference manifest reference'
        $idValue = Get-Property $entry 'logicalId'
        $id = if ($idValue -is [string]) { $idValue } else { [string]$idValue }
        if ($idValue -isnot [string] -or -not ($expectedIds -ccontains $id) -or -not $seen.Add($id)) { throw ('Reference manifest logicalId is invalid or duplicated: ' + $id) }
        $originalValue = Get-Property $entry 'originalPath'
        $evidenceValue = Get-Property $entry 'copiedEvidencePath'
        $bytesValue = Get-Property $entry 'byteLength'
        $shaValue = Get-Property $entry 'sha256'
        if ($originalValue -isnot [string] -or $evidenceValue -isnot [string] -or
            [string]::IsNullOrWhiteSpace($originalValue) -or [string]::IsNullOrWhiteSpace($evidenceValue) -or
            -not (Test-ReferenceJsonInteger $bytesValue) -or [int64]$bytesValue -le 0 -or
            $shaValue -isnot [string] -or $shaValue -notmatch '^[0-9a-fA-F]{64}$') {
            throw ('Reference manifest reference is incomplete: ' + $id)
        }
        $bytes = [int64]$bytesValue
        $sha = $shaValue.ToLowerInvariant()
        $original = $originalValue
        $evidence = $evidenceValue
        foreach ($file in @(
            [ordered]@{ label = 'originalPath'; path = Resolve-EvidencePath $original (Split-Path -Parent $full) },
            [ordered]@{ label = 'copiedEvidencePath'; path = Resolve-EvidencePath $evidence (Split-Path -Parent $full) }
        )) {
            if (-not (Test-Path -LiteralPath $file.path -PathType Leaf)) { throw ('Reference ' + $id + ' ' + $file.label + ' is missing.') }
            if ((Get-Item -LiteralPath $file.path).Length -ne $bytes -or (Get-Hash $file.path) -ne $sha) {
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
        $record = [pscustomobject]@{
            filename = $filename
            path = $imagePath
            sha256 = $actual
            captureWeapon = $ExpectedWeapon
            view = [string](Get-Property $item 'view')
            qualityLevel = [string](Get-Property $item 'qualityLevel')
        }
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
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)]$CaptureSet,
        [Parameter(Mandatory = $true)][string]$BaseDirectory,
        [Parameter(Mandatory = $true)][ValidateSet('Rocket', 'Shotgun')][string]$ExpectedWeapon,
        [string]$ExpectedView = '',
        [string]$ExpectedQuality = ''
    )
    $pathText = if ($Value -is [string]) { [string]$Value } else { [string](Get-Property $Value 'path') }
    if ([string]::IsNullOrWhiteSpace($pathText) -and $Value -isnot [string]) { $pathText = [string](Get-Property $Value 'imagePath') }
    if ([string]::IsNullOrWhiteSpace($pathText)) { throw 'Verdict evidenceImages item has no path.' }
    $path = Resolve-EvidencePath $pathText $BaseDirectory
    $declared = if ($Value -is [string]) { '' } else { [string](Get-Property $Value 'sha256') }
    if ([string]::IsNullOrWhiteSpace($declared) -and $Value -isnot [string]) { $declared = [string](Get-Property $Value 'hash') }
    $capture = Get-Property $CaptureSet $ExpectedWeapon
    if ($null -eq $capture) { throw ('Verdict capture set has no ' + $ExpectedWeapon + ' manifest.') }
    $record = $null
    if ($capture.images.Contains($path)) {
        $record = $capture.images[$path]
    } else {
        $name = [IO.Path]::GetFileName($path)
        # Filename-only references are supported, but a path carrying a
        # directory must resolve to that exact weapon capture path. This keeps
        # a Shotgun image from satisfying a Rocket predicate by basename.
        if ([string]::IsNullOrWhiteSpace([IO.Path]::GetDirectoryName($pathText)) -and $capture.images.Contains($name)) {
            $record = $capture.images[$name]
        }
    }
    if ($null -eq $record) { throw ('Verdict evidence image is not one of the current ' + $ExpectedWeapon + ' capture images: ' + $path) }
    if ([string](Get-Property $record 'captureWeapon') -ne '' -and [string](Get-Property $record 'captureWeapon') -cne $ExpectedWeapon) {
        throw ('Verdict evidence image weapon mismatch: expected ' + $ExpectedWeapon + ': ' + $path)
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedView) -and [string](Get-Property $record 'view') -cne $ExpectedView) {
        throw ('Verdict evidence image orientation mismatch: expected ' + $ExpectedView + ' for ' + $ExpectedWeapon + ': ' + $path)
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedQuality) -and [string](Get-Property $record 'qualityLevel') -cne $ExpectedQuality) {
        throw ('Verdict evidence image quality mismatch: expected ' + $ExpectedQuality + ' for ' + $ExpectedWeapon + ': ' + $path)
    }
    $actual = Get-Hash $record.path
    if (-not [string]::IsNullOrWhiteSpace($declared) -and $declared.ToLowerInvariant() -ne $actual) { throw ('Verdict evidence image hash mismatch: ' + $path) }
    return [ordered]@{ path = $record.path; sha256 = $actual }
}

function Get-PredicateImageRequirement {
    param([Parameter(Mandatory = $true)][string]$Id)
    switch ($Id) {
        'high.sunward.readable' { return [pscustomobject]@{ view = 'sunward'; qualityLevel = 'High' } }
        'high.crosslight.readable' { return [pscustomobject]@{ view = 'crosslight'; qualityLevel = 'High' } }
        'high.awaylight.readable' { return [pscustomobject]@{ view = 'awaylight'; qualityLevel = 'High' } }
        'low-material-hierarchy' { return [pscustomobject]@{ view = ''; qualityLevel = 'Low' } }
        default { return [pscustomobject]@{ view = ''; qualityLevel = '' } }
    }
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
    $requirement = Get-PredicateImageRequirement $id
    foreach ($image in $evidence) {
        $evidenceRecords.Add((Get-EvidenceImageRecord $image $captureSet (Split-Path -Parent $verdictFullPath) $weapon $requirement.view $requirement.qualityLevel)) | Out-Null
    }
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
