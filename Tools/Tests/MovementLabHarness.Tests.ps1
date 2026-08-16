Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:HarnessScratchRoot = 'C:\wt'

function New-HarnessPass {
    param([AllowEmptyString()][string]$Message = 'ok')
    return [pscustomobject]@{ pass = $true; message = $Message }
}

function New-HarnessFail {
    param([Parameter(Mandatory = $true)][string]$Message)
    return [pscustomobject]@{ pass = $false; message = $Message }
}

function Get-HarnessField {
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

function Invoke-HarnessModuleFunction {
    param(
        [Parameter(Mandatory = $true)]$Module,
        [Parameter(Mandatory = $true)][string]$Name,
        [hashtable]$Arguments = @{}
    )

    $invoke = {
        param([string]$FunctionName, [hashtable]$FunctionArguments)
        if ($null -eq $FunctionArguments) { return (& $FunctionName) }
        return (& $FunctionName @FunctionArguments)
    }
    $values = @(& $Module $invoke $Name $Arguments)
    if ($values.Count -eq 0) { return $null }
    if ($values.Count -eq 1) { return $values[0] }
    return ,$values
}

function Set-HarnessRowStubs {
    param([Parameter(Mandatory = $true)]$Module)

    $setup = {
        function script:Get-AuthoritativeGeneratedInventory { return @('Assets/_Game/Generated/fixture.json') }
        function script:Get-GeneratedHashes { return [ordered]@{ 'fixture.json' = 'hash' } }
        function script:Get-GeneratedHashDigest { param($Value) return 'digest' }
        function script:Get-InputDigest { param([string[]]$Paths) return 'input-digest' }
        function script:Get-EnvironmentFingerprint { return 'environment' }
        function script:Get-WorkingTreeDigest { return 'working-tree' }
        $script:InvocationId = 'fixture-invocation'
    }
    & $Module $setup | Out-Null
}

function New-HarnessRowModule {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)]$ShimCommand
    )

    $names = @(
        'New-LedgerRow', 'Get-AuthoritativeGeneratedInventory', 'Get-GeneratedHashes',
        'Get-GeneratedHashDigest', 'Get-InputDigest', 'Get-EnvironmentFingerprint',
        'Get-WorkingTreeDigest', 'Get-ObjectPropertyValue', 'Get-ObjectPropertyText'
    )
    $module = & $ShimCommand $Source $names @()
    Set-HarnessRowStubs $module
    return $module
}

function Get-HarnessAst {
    param([Parameter(Mandatory = $true)][string]$Source)
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput($Source, [ref]$tokens, [ref]$errors)
    if ($null -ne $errors -and @($errors).Count -gt 0) {
        throw ('Source parse failed: ' + (@($errors | ForEach-Object { [string]$_.Message }) -join ' | '))
    }
    return $ast
}

function Get-HarnessFunctionAst {
    param([Parameter(Mandatory = $true)][string]$Source, [Parameter(Mandatory = $true)][string]$Name)
    $ast = Get-HarnessAst $Source
    $found = @($ast.FindAll({
        param($Node)
        $Node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            [string]::Equals([string]$Node.Name, $Name, [StringComparison]::OrdinalIgnoreCase)
    }, $true))
    if ($found.Count -ne 1) { throw ('Expected one function AST for ' + $Name + ', found ' + $found.Count) }
    return $found[0]
}

function Get-HarnessStringAssignment {
    param([Parameter(Mandatory = $true)][string]$Source, [Parameter(Mandatory = $true)][string]$Name)
    $ast = Get-HarnessAst $Source
    $found = @($ast.FindAll({
        param($Node)
        if ($Node -isnot [System.Management.Automation.Language.AssignmentStatementAst]) { return $false }
        $left = $Node.Left
        if ($left -isnot [System.Management.Automation.Language.VariableExpressionAst]) { return $false }
        [string]::Equals((([string]$left.VariablePath.UserPath).TrimStart('$')), $Name, [StringComparison]::OrdinalIgnoreCase)
    }, $true))
    if ($found.Count -ne 1) { throw ('Expected one assignment for ' + $Name + ', found ' + $found.Count) }
    $strings = @($found[0].Right.FindAll({
        param($Node)
        $Node -is [System.Management.Automation.Language.StringConstantExpressionAst]
    }, $true) | ForEach-Object { [string]$_.Value })
    return $strings
}

function Test-ShimExportSet {
    param([Parameter(Mandatory = $true)]$State)
    $names = @('Get-ObjectPropertyValue', 'Get-ObjectPropertyText')
    $module = $null
    try {
        $module = & $State.ShimCommand $State.CurrentSource $names @()
        $exported = @($module.ExportedCommands.Keys | ForEach-Object { [string]$_ } | Sort-Object)
        $expected = @($names | Sort-Object)
        if (($exported -join '|') -cne ($expected -join '|')) {
            return New-HarnessFail ('exported set mismatch: expected ' + ($expected -join ',') + '; observed ' + ($exported -join ','))
        }
        return New-HarnessPass 'exact export set'
    } finally {
        if ($null -ne $module) { Remove-Module -ModuleInfo $module -Force -ErrorAction SilentlyContinue }
    }
}

function Test-GopvOrdered {
    param([Parameter(Mandatory = $true)]$State)
    $current = $null
    $red = $null
    try {
        $current = & $State.ShimCommand $State.CurrentSource @('Get-ObjectPropertyValue') @()
        $currentValue = Invoke-HarnessModuleFunction $current 'Get-ObjectPropertyValue' @{ Object = [ordered]@{ marker = 'ordered-value' }; Name = 'marker' }
        if ([string]$currentValue -cne 'ordered-value') { return New-HarnessFail 'HEAD ordered dictionary lookup failed' }

        $red = & $State.ShimCommand $State.RedSource @('Get-ObjectPropertyValue') @()
        $redValue = Invoke-HarnessModuleFunction $red 'Get-ObjectPropertyValue' @{ Object = [ordered]@{ marker = 'ordered-value' }; Name = 'marker' }
        if ([string]$redValue -ceq 'ordered-value') { return New-HarnessFail 'RedAtSha unexpectedly passed ordered lookup' }
        return New-HarnessPass 'HEAD pass; RedAtSha fail'
    } finally {
        if ($null -ne $current) { Remove-Module -ModuleInfo $current -Force -ErrorAction SilentlyContinue }
        if ($null -ne $red) { Remove-Module -ModuleInfo $red -Force -ErrorAction SilentlyContinue }
    }
}

function Test-GopvJson {
    param([Parameter(Mandatory = $true)]$State)
    $module = $null
    try {
        $module = & $State.ShimCommand $State.CurrentSource @('Get-ObjectPropertyValue') @()
        $json = '{"marker":"json-value"}' | ConvertFrom-Json
        $value = Invoke-HarnessModuleFunction $module 'Get-ObjectPropertyValue' @{ Object = $json; Name = 'marker' }
        if ([string]$value -cne 'json-value') { return New-HarnessFail 'JSON property lookup failed' }
        return New-HarnessPass 'JSON lookup'
    } finally {
        if ($null -ne $module) { Remove-Module -ModuleInfo $module -Force -ErrorAction SilentlyContinue }
    }
}

function Test-GopvAbsent {
    param([Parameter(Mandatory = $true)]$State)
    $module = $null
    try {
        $module = & $State.ShimCommand $State.CurrentSource @('Get-ObjectPropertyValue') @()
        $ordered = Invoke-HarnessModuleFunction $module 'Get-ObjectPropertyValue' @{ Object = [ordered]@{ marker = 'value' }; Name = 'missing' }
        $json = '{"marker":"value"}' | ConvertFrom-Json
        $jsonValue = Invoke-HarnessModuleFunction $module 'Get-ObjectPropertyValue' @{ Object = $json; Name = 'missing' }
        if ($null -ne $ordered -or $null -ne $jsonValue) { return New-HarnessFail 'missing property returned a value' }
        return New-HarnessPass 'missing property is null for ordered and JSON objects'
    } finally {
        if ($null -ne $module) { Remove-Module -ModuleInfo $module -Force -ErrorAction SilentlyContinue }
    }
}

function Test-LedgerRowOrderedLiteral {
    param([Parameter(Mandatory = $true)]$State)
    $ast = Get-HarnessFunctionAst $State.CurrentSource 'New-LedgerRow'
    if ($ast.Extent.Text -notmatch '(?im)\[ordered\]\s*@\{') { return New-HarnessFail 'New-LedgerRow is not backed by [ordered] literal' }
    return New-HarnessPass '[ordered] row literal'
}

function Test-GeneratedPathSurfaceRemoved {
    param([Parameter(Mandatory = $true)]$State)

    $findLegacySurface = {
        param([string]$Source)
        $ast = Get-HarnessAst $Source
        $generatedPathVariables = @($ast.FindAll({
            param($Node)
            $Node -is [System.Management.Automation.Language.VariableExpressionAst] -and
            [string]$Node.VariablePath.UserPath -ceq 'GeneratedPath'
        }, $true))
        $requestedInventoryVariables = @($ast.FindAll({
            param($Node)
            $Node -is [System.Management.Automation.Language.VariableExpressionAst] -and
            [string]$Node.VariablePath.UserPath -ceq 'RequestedInventoryPaths'
        }, $true))
        $requestedInventoryFields = @($ast.FindAll({
            param($Node)
            $Node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
            [string]$Node.Value -ceq 'requested_inventory'
        }, $true))
        return $generatedPathVariables.Count + $requestedInventoryVariables.Count + $requestedInventoryFields.Count
    }

    $headCount = [int](& $findLegacySurface $State.CurrentSource)
    if ($headCount -ne 0) { return New-HarnessFail ('legacy generated-path surface remains at ' + $headCount + ' site(s)') }
    $redSource = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $State.RedSource -ErrorAction Stop).Path)
    $redCount = [int](& $findLegacySurface $redSource)
    if ($redCount -eq 0) { return New-HarnessFail 'red baseline does not retain the legacy generated-path surface' }
    return New-HarnessPass ('HEAD removed legacy surface; RedAtSha retains ' + $redCount + ' site(s)')
}

function Test-GeneratedYamlComparatorCoverage {
    param([Parameter(Mandatory = $true)]$State)

    $paths = @(
        'Assets/_Game/Animations/FpsKick.controller',
        'Assets/_Game/Generated/BlueCircleCueMesh.asset'
    )
    $quotedPaths = @($paths | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ', '
    $command = "& '" + $State.ComparatorPath.Replace("'", "''") + "' -Base 'HEAD' -Head 'WORKTREE' -Path @(" + $quotedPaths + ')'
    $output = @(& powershell -NoProfile -ExecutionPolicy Bypass -Command $command 2>&1)
    if ($LASTEXITCODE -ne 0) { return New-HarnessFail ('comparator rejected supported controller/cue paths: ' + ($output -join ' | ')) }
    foreach ($path in $paths) {
        if (($output -join "`n") -notmatch [regex]::Escape('== ' + $path)) {
            return New-HarnessFail ('comparator omitted supported generated path: ' + $path)
        }
        if (($output -join "`n") -notmatch '(?m)^SEMANTIC: ' -or ($output -join "`n") -notmatch '(?m)^DANGLING: ') {
            return New-HarnessFail 'comparator output contract omitted SEMANTIC or DANGLING.'
        }
    }

    $previousErrorAction = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $uncovered = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $State.ComparatorPath -Base 'HEAD' -Head 'WORKTREE' -Path 'Assets/_Game/Generated/NotAuthoritative.asset' 2>&1)
        $uncoveredExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorAction
    }
    if ($uncoveredExitCode -eq 0 -or ($uncovered -join "`n") -notmatch 'Requested path is uncovered') {
        return New-HarnessFail ('uncovered generated-path request did not fail closed: ' + ($uncovered -join ' | '))
    }
    try {
        $ErrorActionPreference = 'Continue'
        $outsideInventory = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $State.ComparatorPath -Base 'HEAD' -Head 'WORKTREE' -Path 'Assets/InputSystem_Actions.inputactions' 2>&1)
        $outsideInventoryExitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousErrorAction
    }
    if ($outsideInventoryExitCode -eq 0 -or ($outsideInventory -join "`n") -notmatch 'outside the authoritative generated inventory') {
        return New-HarnessFail ('non-authoritative requested path did not fail closed: ' + ($outsideInventory -join ' | '))
    }
    return New-HarnessPass 'controller/cue paths covered; uncovered and non-authoritative requests rejected'
}

function Test-GeneratedYamlComparatorDefaultMetaCoverage {
    param([Parameter(Mandatory = $true)]$State)

    $fixtureRoot = Join-Path $script:HarnessScratchRoot ('generated-yaml-comparator-' + [Guid]::NewGuid().ToString('N'))
    try {
        $validationDirectory = Join-Path $fixtureRoot 'Tools/Validation'
        $materialsDirectory = Join-Path $fixtureRoot 'Assets/_Game/Materials'
        $generatedDirectory = Join-Path $fixtureRoot 'Assets/_Game/Generated'
        $movementLabDirectory = Join-Path $fixtureRoot 'Assets/_Game/Scenes/MovementLab'
        $projectSettingsDirectory = Join-Path $fixtureRoot 'ProjectSettings'
        [IO.Directory]::CreateDirectory($validationDirectory) | Out-Null
        [IO.Directory]::CreateDirectory($materialsDirectory) | Out-Null
        [IO.Directory]::CreateDirectory($generatedDirectory) | Out-Null
        [IO.Directory]::CreateDirectory($movementLabDirectory) | Out-Null
        [IO.Directory]::CreateDirectory($projectSettingsDirectory) | Out-Null
        Copy-Item -LiteralPath $State.ComparatorPath -Destination (Join-Path $validationDirectory 'Compare-GeneratedYaml.ps1') -Force
        Copy-Item -LiteralPath $State.WorkflowPath -Destination (Join-Path $validationDirectory 'Invoke-MovementLabWorkflow.ps1') -Force
        $materialPath = Join-Path $materialsDirectory 'Fixture.mat'
        $metaPath = $materialPath + '.meta'
        $churnPath = Join-Path $materialsDirectory 'Churn.mat'
        $churnMetaPath = $churnPath + '.meta'
        $unknownPath = Join-Path $generatedDirectory 'Fixture.unknown'
        $unknownMetaPath = $unknownPath + '.meta'
        $projectSettingsPath = Join-Path $projectSettingsDirectory 'FixtureSettings.asset'
        $binaryPaths = @(
            (Join-Path $movementLabDirectory 'Fixture.png'),
            (Join-Path $movementLabDirectory 'Fixture.exr'),
            (Join-Path $movementLabDirectory 'LightingData.asset')
        )
        [IO.File]::WriteAllText($materialPath, "%YAML 1.1`n--- !u!21 &1`nMaterial:`n  m_Name: Fixture`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($metaPath, "fileFormatVersion: 2`nguid: 11111111111111111111111111111111`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($churnPath, "%YAML 1.1`n--- !u!21 &2`nMaterial:`n  m_Name: Churn`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($churnMetaPath, "fileFormatVersion: 2`nguid: 22222222222222222222222222222222`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllBytes($unknownPath, [Text.Encoding]::ASCII.GetBytes('unknown baseline'))
        [IO.File]::WriteAllText($unknownMetaPath, "fileFormatVersion: 2`nguid: 33333333333333333333333333333333`n", (New-Object Text.UTF8Encoding($false)))
        foreach ($binaryPath in $binaryPaths) {
            [IO.File]::WriteAllBytes($binaryPath, [Text.Encoding]::ASCII.GetBytes('binary baseline ' + $binaryPath))
            [IO.File]::WriteAllText(($binaryPath + '.meta'), "fileFormatVersion: 2`nguid: 44444444444444444444444444444444`n", (New-Object Text.UTF8Encoding($false)))
        }
        [IO.File]::WriteAllText($projectSettingsPath, "setting: baseline`n", (New-Object Text.UTF8Encoding($false)))
        & git -C $fixtureRoot init --quiet 2>$null
        if ($LASTEXITCODE -ne 0) { return New-HarnessFail 'fixture Git initialization failed' }
        & git -C $fixtureRoot config core.autocrlf false 2>$null
        & git -C $fixtureRoot config user.email 'harness@example.invalid' 2>$null
        & git -C $fixtureRoot config user.name 'Harness' 2>$null
        & git -C $fixtureRoot add . 2>$null
        & git -C $fixtureRoot commit --quiet -m 'fixture baseline' 2>$null
        if ($LASTEXITCODE -ne 0) { return New-HarnessFail 'fixture Git baseline commit failed' }
        [IO.File]::AppendAllText($materialPath, "  m_ShaderKeywords: CHANGED`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::AppendAllText($metaPath, "timeCreated: 1`n", (New-Object Text.UTF8Encoding($false)))
        foreach ($binaryPath in $binaryPaths) { [IO.File]::AppendAllText($binaryPath, "`nhead binary drift", (New-Object Text.UTF8Encoding($false))) }
        [IO.File]::AppendAllText($unknownPath, "`nhead unknown drift", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::AppendAllText($projectSettingsPath, "setting: head`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($churnMetaPath, "fileFormatVersion: 2`nguid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa`n", (New-Object Text.UTF8Encoding($false)))
        Remove-Item -LiteralPath $churnPath -Force

        $fixtureComparator = Join-Path $validationDirectory 'Compare-GeneratedYaml.ps1'
        $paths = @(
            'Assets/_Game/Materials/Fixture.mat', 'Assets/_Game/Materials/Fixture.mat.meta',
            'Assets/_Game/Materials/Churn.mat', 'Assets/_Game/Materials/Churn.mat.meta',
            'Assets/_Game/Generated/Fixture.unknown', 'Assets/_Game/Generated/Fixture.unknown.meta',
            'Assets/_Game/Scenes/MovementLab/Fixture.png', 'Assets/_Game/Scenes/MovementLab/Fixture.png.meta',
            'Assets/_Game/Scenes/MovementLab/Fixture.exr', 'Assets/_Game/Scenes/MovementLab/Fixture.exr.meta',
            'Assets/_Game/Scenes/MovementLab/LightingData.asset', 'Assets/_Game/Scenes/MovementLab/LightingData.asset.meta',
            'ProjectSettings/FixtureSettings.asset'
        )
        $quotedPaths = @($paths | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ', '
        $command = "& '" + $fixtureComparator.Replace("'", "''") + "' -Base 'HEAD' -Head 'WORKTREE' -Path @(" + $quotedPaths + ')'
        $output = @(& powershell -NoProfile -ExecutionPolicy Bypass -Command $command 2>&1)
        if ($LASTEXITCODE -ne 0) { return New-HarnessFail ('default comparator fixture failed: ' + ($output -join ' | ')) }
        $text = $output -join "`n"
        foreach ($path in $paths) {
            if ($text -notmatch [regex]::Escape('== ' + $path)) { return New-HarnessFail ('default comparator omitted changed authoritative path: ' + $path) }
        }
        if ($text -notmatch '(?m)^SEMANTIC: changed$') {
            return New-HarnessFail 'intentional YAML/text semantic change was not reported'
        }
        if ($text -notmatch '(?s)== Assets/_Game/Materials/Fixture\.mat\.meta.*?kind\s+metadata.*?guid\s+stable\s+11111111111111111111111111111111') {
            return New-HarnessFail 'stable GUID metadata was not reported'
        }
        if ($text -notmatch '(?s)== Assets/_Game/Materials/Churn\.mat\.meta.*?guid\s+churn\s+22222222222222222222222222222222\s+->\s+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa') {
            return New-HarnessFail 'GUID churn was not reported'
        }
        if ($text -notmatch '(?m)^  pair\s+broken') {
            return New-HarnessFail 'broken asset/meta pair was not reported'
        }
        $projectSettingsSection = [regex]::Match($text, '(?s)== ProjectSettings/FixtureSettings\.asset.*?(?=\r?\n== |\r?\nCOVERAGE:)')
        if (-not $projectSettingsSection.Success -or $projectSettingsSection.Value -notmatch '(?m)^  pair\s+not-applicable') {
            return New-HarnessFail 'ProjectSettings asset was not reported as pair not-applicable'
        }
        if ($projectSettingsSection.Value -match '(?m)^  pair\s+broken|(?m)^  guid\s+') {
            return New-HarnessFail 'ProjectSettings asset incorrectly received pair/GUID analysis'
        }
        foreach ($path in @(
            'Assets/_Game/Scenes/MovementLab/Fixture.png',
            'Assets/_Game/Scenes/MovementLab/Fixture.exr',
            'Assets/_Game/Scenes/MovementLab/LightingData.asset'
        )) {
            if ($text -notmatch ('(?s)== ' + [regex]::Escape($path) + '.*?kind\s+binary provenance.*?bytes\s+.*?blob\s+.*?provenance')) {
                return New-HarnessFail ('binary provenance was not reported for ' + $path)
            }
        }
        if ($text -notmatch '(?s)== Assets/_Game/Generated/Fixture\.unknown\r?\n.*?kind\s+unsupported') {
            return New-HarnessFail 'unknown generated type was not reported unsupported'
        }
        foreach ($header in @('COVERAGE:', 'SEMANTIC:', 'DANGLING:', 'GUID:', 'PAIRS:', 'UNSUPPORTED:')) {
            if ($text -notmatch ('(?m)^' + [regex]::Escape($header))) { return New-HarnessFail ('comparator omitted exact summary header ' + $header) }
        }
        if ($text -notmatch '(?m)^COVERAGE: 13/13 authoritative changed paths reported; semantic checked 3; NOT CHECKED 10$') {
            return New-HarnessFail ('default comparator coverage summary did not account for every changed path: ' + $text)
        }
        if ($text -notmatch '(?m)^GUID: stable 5; churn 1; added 0; removed 0; invalid 0$' -or
            $text -notmatch '(?m)^PAIRS: intact 5; broken 1$' -or
            $text -notmatch '(?m)^UNSUPPORTED: 1$') {
            return New-HarnessFail ('GUID/pair/unsupported summaries were incorrect: ' + $text)
        }
    } finally {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    $currentSource = [IO.File]::ReadAllText($State.ComparatorPath)
    $redSource = [IO.File]::ReadAllText($State.ComparatorRedFixturePath)
    $legacyGate = 'if ($explicitPathRequest -or $basePresent -ne $headPresent)'
    if ($currentSource.IndexOf($legacyGate, [StringComparison]::Ordinal) -ge 0) { return New-HarnessFail 'current comparator retains the legacy default .meta omission gate' }
    if ($redSource.IndexOf($legacyGate, [StringComparison]::Ordinal) -lt 0) { return New-HarnessFail 'red comparator fixture does not retain the legacy default .meta omission gate' }
    foreach ($marker in @('binary provenance', 'hash-object', 'Get-MetadataAnalysis', 'GUID:', 'PAIRS:', 'UNSUPPORTED:')) {
        if ($currentSource.IndexOf($marker, [StringComparison]::Ordinal) -lt 0) { return New-HarnessFail ('current comparator omitted coverage marker: ' + $marker) }
        if ($redSource.IndexOf($marker, [StringComparison]::Ordinal) -ge 0) { return New-HarnessFail ('red comparator fixture unexpectedly contains coverage marker: ' + $marker) }
    }
    if ($currentSource -notmatch '(?m)^Write-Output \(''COVERAGE: '' \+ \$reportedPathCount \+ ''/'' \+ \$selected.Count') {
        return New-HarnessFail 'coverage summary does not account for every selected authoritative changed path'
    }
    return New-HarnessPass 'semantic change exit-0; stable/churn GUIDs; binary provenance; intact/broken pairs; unsupported type; exact summaries; red fixture fail-closed'
}

function Test-RowReuseEqual {
    param([Parameter(Mandatory = $true)]$State)
    $current = $null
    $red = $null
    try {
        $functions = @('Get-ObjectPropertyValue', 'Get-ObjectPropertyText', 'Test-RowReuseProof')
        $current = & $State.ShimCommand $State.CurrentSource $functions @()
        $red = & $State.ShimCommand $State.RedSource $functions @()
        $prior = '{"input_digest":"same","environment_fingerprint":"same-env"}' | ConvertFrom-Json
        $row = [ordered]@{ input_digest = 'same'; environment_fingerprint = 'same-env' }
        $currentProof = Invoke-HarnessModuleFunction $current 'Test-RowReuseProof' @{ Prior = $prior; Row = $row }
        $redProof = Invoke-HarnessModuleFunction $red 'Test-RowReuseProof' @{ Prior = $prior; Row = $row }
        if (-not [bool](Get-HarnessField $currentProof 'valid')) { return New-HarnessFail 'HEAD JSON-prior/ordered-current proof rejected equal row' }
        if ([bool](Get-HarnessField $redProof 'valid')) { return New-HarnessFail 'RedAtSha unexpectedly accepted JSON-prior/ordered-current proof' }
        return New-HarnessPass 'HEAD pass; RedAtSha fail'
    } finally {
        if ($null -ne $current) { Remove-Module -ModuleInfo $current -Force -ErrorAction SilentlyContinue }
        if ($null -ne $red) { Remove-Module -ModuleInfo $red -Force -ErrorAction SilentlyContinue }
    }
}

function Test-RowReuseDiff {
    param([Parameter(Mandatory = $true)]$State)
    $module = $null
    try {
        $module = & $State.ShimCommand $State.CurrentSource @('Get-ObjectPropertyValue', 'Get-ObjectPropertyText', 'Test-RowReuseProof') @()
        $prior = '{"input_digest":"old","environment_fingerprint":"same-env"}' | ConvertFrom-Json
        $row = [ordered]@{ input_digest = 'new'; environment_fingerprint = 'same-env' }
        $proof = Invoke-HarnessModuleFunction $module 'Test-RowReuseProof' @{ Prior = $prior; Row = $row }
        if ([bool](Get-HarnessField $proof 'valid')) { return New-HarnessFail 'different digest accepted as reusable' }
        if ([string](Get-HarnessField $proof 'reason') -cne 'input digest changed') { return New-HarnessFail 'wrong discriminator reason' }
        $environmentPrior = [ordered]@{ input_digest = 'same'; environment_fingerprint = 'old-env' }
        $environmentRow = [ordered]@{ input_digest = 'same'; environment_fingerprint = 'new-env' }
        $environmentProof = Invoke-HarnessModuleFunction $module 'Test-RowReuseProof' @{ Prior = $environmentPrior; Row = $environmentRow }
        if ([bool](Get-HarnessField $environmentProof 'valid')) { return New-HarnessFail 'different environment fingerprint accepted as reusable' }
        if ([string](Get-HarnessField $environmentProof 'reason') -cne 'environment fingerprint changed') { return New-HarnessFail 'wrong environment discriminator reason' }
        foreach ($fixture in @(
            [pscustomobject]@{ Object = $environmentPrior; Expected = 'old-env' },
            [pscustomobject]@{ Object = $environmentRow; Expected = 'new-env' }
        )) {
            $observed = [string](Invoke-HarnessModuleFunction $module 'Get-ObjectPropertyText' @{ Object = $fixture.Object; Name = 'environment_fingerprint' })
            if ($observed -cne $fixture.Expected) { return New-HarnessFail ('environment_fingerprint getter mismatch: expected ' + $fixture.Expected + ', observed ' + $observed) }
        }
        return New-HarnessPass 'digest and environment discriminators'
    } finally {
        if ($null -ne $module) { Remove-Module -ModuleInfo $module -Force -ErrorAction SilentlyContinue }
    }
}

function Test-RowFieldSweep {
    param([Parameter(Mandatory = $true)]$State)
    $module = $null
    try {
        $module = New-HarnessRowModule $State.CurrentSource $State.ShimCommand
        $row = Invoke-HarnessModuleFunction $module 'New-LedgerRow' @{
            CheckId = 'fixture'; Tier = 'fast'; MutatesProject = $false
            InputPaths = @('Tools/Validation'); InvalidationPaths = @('Tools/Validation')
            Subsumes = @(); RunPoint = 'coding'
        }
        if ($null -eq $row -or $row -isnot [System.Collections.IDictionary]) { return New-HarnessFail 'New-LedgerRow did not return dictionary row' }
        foreach ($key in @($row.Keys)) {
            $name = [string]$key
            $directValue = $row[$key]
            $expected = if ($null -eq $directValue) { '' } else { [string]$directValue }
            $actual = [string](Invoke-HarnessModuleFunction $module 'Get-ObjectPropertyText' @{ Object = $row; Name = $name })
            if ($actual -cne $expected) {
                return New-HarnessFail ('row field text mismatch for ' + $name + ': expected [' + $expected + '], observed [' + $actual + ']')
            }
        }
        return New-HarnessPass ('readable keys=' + $row.Keys.Count)
    } finally {
        if ($null -ne $module) { Remove-Module -ModuleInfo $module -Force -ErrorAction SilentlyContinue }
    }
}

function Test-BakeInputsLiteral {
    param([Parameter(Mandatory = $true)]$State)
    $expected = @(
        'Assets/_Game/Lighting',
        'Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs',
        'Assets/_Game/Editor/MovementLab/MovementLabLightingProfiles.cs',
        'Assets/_Game/Lighting/MovementLabLightingSettings.asset',
        'Assets/_Game/Lighting/MovementLabLightingSettings.asset.meta',
        'Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset',
        'Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset.meta',
        'Assets/_Game/Lighting/MovementLabVolumeProfile.asset',
        'Assets/_Game/Lighting/MovementLabVolumeProfile.asset.meta',
        'Assets/_Game/Lighting/MovementLabLightingManifest.json',
        'Assets/_Game/Lighting/MovementLabLightingManifest.json.meta'
    )
    $ledger = Get-HarnessFunctionAst $State.CurrentSource 'New-CheckLedger'
    $switches = @($ledger.FindAll({
        param($Node)
        $Node -is [System.Management.Automation.Language.SwitchStatementAst]
    }, $true))
    if ($switches.Count -ne 1) { return New-HarnessFail ('expected one New-CheckLedger switch AST, found ' + $switches.Count) }
    $clauses = @($switches[0].Clauses | Where-Object { [string]$_.Item1.Value -ceq 'ProductionPrepare' })
    if ($clauses.Count -ne 1) { return New-HarnessFail ('expected one ProductionPrepare switch branch, found ' + $clauses.Count) }
    $branch = $clauses[0].Item2
    $assignments = @($branch.FindAll({
        param($Node)
        if ($Node -isnot [System.Management.Automation.Language.AssignmentStatementAst]) { return $false }
        $left = $Node.Left
        $left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            [string]$left.VariablePath.UserPath -ceq 'productionBakeInputs'
    }, $true))
    if ($assignments.Count -ne 1) { return New-HarnessFail ('expected one productionBakeInputs assignment inside ProductionPrepare, found ' + $assignments.Count) }
    $actual = @($assignments[0].Right.FindAll({
        param($Node)
        $Node -is [System.Management.Automation.Language.StringConstantExpressionAst]
    }, $true) | ForEach-Object { [string]$_.Value })
    if (($actual -join "`n") -cne ($expected -join "`n")) {
        return New-HarnessFail ('production bake literal mismatch: observed ' + ($actual -join ', '))
    }

    $rowCommands = @($branch.FindAll({
        param($Node)
        $Node -is [System.Management.Automation.Language.CommandAst] -and
            @($Node.CommandElements | Where-Object { $_.Extent.Text -ceq 'New-LedgerRow' }).Count -gt 0
    }, $true) | Where-Object {
        [string]$_.Extent.Text -match "(?i)-CheckId\s+'production-bake'"
    })
    if ($rowCommands.Count -ne 1) { return New-HarnessFail ('expected one production-bake New-LedgerRow call inside ProductionPrepare, found ' + $rowCommands.Count) }
    $arguments = @{}
    $elements = @($rowCommands[0].CommandElements)
    for ($index = 0; $index -lt $elements.Count; $index++) {
        $element = $elements[$index]
        if ($element -is [System.Management.Automation.Language.CommandParameterAst] -and $element.ParameterName -in @('InputPaths', 'InvalidationPaths')) {
            if ($index + 1 -ge $elements.Count) { return New-HarnessFail ('production-bake row parameter has no argument: ' + $element.ParameterName) }
            $arguments[$element.ParameterName] = $elements[$index + 1]
        }
    }
    foreach ($name in @('InputPaths', 'InvalidationPaths')) {
        if (-not $arguments.ContainsKey($name)) { return New-HarnessFail ('production-bake row missing -' + $name) }
        $argument = $arguments[$name]
        if ($argument -isnot [System.Management.Automation.Language.VariableExpressionAst] -or
            [string]$argument.VariablePath.UserPath -cne 'productionBakeInputs') {
            return New-HarnessFail ('production-bake -' + $name + ' is not $productionBakeInputs: ' + $argument.Extent.Text)
        }
    }
    return New-HarnessPass 'exact production bake input literal'
}

function Test-BakeInputsAsymmetry {
    param([Parameter(Mandatory = $true)]$State)
    $common = @(Get-HarnessStringAssignment $State.CurrentSource 'commonInputs')
    $validator = @(Get-HarnessStringAssignment $State.CurrentSource 'productionValidatorInputs')
    $bake = @(Get-HarnessStringAssignment $State.CurrentSource 'productionBakeInputs')
    if ($common -notcontains 'Tools/Validation') { return New-HarnessFail 'compile common inputs omit Tools/Validation' }
    if ($validator -notcontains 'Tools/Validation') { return New-HarnessFail 'production-validator inputs omit Tools/Validation' }
    if ($bake -contains 'Tools/Validation') { return New-HarnessFail 'production-bake inputs include Tools/Validation' }
    return New-HarnessPass 'compile+validator include; bake excludes Tools/Validation'
}

function Test-BakeCountProductionMethod {
    param([Parameter(Mandatory = $true)]$State)
    $function = Get-HarnessFunctionAst $State.CurrentSource 'Invoke-UnityStep'
    $method = 'RocketFooxball.Editor.MovementLabBuilder.BakeMovementLabLighting'
    $conditions = @($function.FindAll({
        param($Node)
        if ($Node -isnot [System.Management.Automation.Language.IfStatementAst]) { return $false }
        $Node.Clauses.Count -eq 1 -and
            [string]$Node.Clauses[0].Item1.Extent.Text -match ('^\s*\$Method\s+-ceq\s+''' + [regex]::Escape($method) + '''\s*$') -and
            [string]$Node.Clauses[0].Item2.Extent.Text -match '\$script:BakeCount\+\+'
    }, $true))
    if ($conditions.Count -ne 1) {
        return New-HarnessFail 'Invoke-UnityStep must increment BakeCount only for exact production BakeMovementLabLighting method'
    }

    $counter = [scriptblock]::Create(
        'param([string]$Method) $value = 0; if (' + [string]$conditions[0].Clauses[0].Item1.Extent.Text + ') { $value++ }; return $value')
    $development = @(& $counter 'RocketFooxball.Editor.MovementLabBuilder.BakeMovementLabLightingDevelopment')
    $production = @(& $counter $method)
    if ($development.Count -ne 1 -or [int]$development[0] -ne 0) {
        return New-HarnessFail 'development bake method incremented BakeCount'
    }
    if ($production.Count -ne 1 -or [int]$production[0] -ne 1) {
        return New-HarnessFail 'production bake method did not increment BakeCount'
    }
    return New-HarnessPass 'development method=0; production method=1'
}

function Test-PathIntersects {
    param([Parameter(Mandatory = $true)]$State)
    $module = $null
    try {
        $module = & $State.ShimCommand $State.CurrentSource @('Test-PathIntersects') @()
        $cases = @(
            @{ ChangedPaths = @('Assets/_Game/Editor'); InvalidationPaths = @('Assets/_Game/Editor'); Expected = $true },
            @{ ChangedPaths = @('Assets/_Game/Editor/MovementLab/A.cs'); InvalidationPaths = @('Assets/_Game/Editor'); Expected = $true },
            @{ ChangedPaths = @('Assets/_Game/Editor'); InvalidationPaths = @('Assets/_Game/Editor/MovementLab'); Expected = $true },
            @{ ChangedPaths = @('Assets/_Game/EditorX/A.cs'); InvalidationPaths = @('Assets/_Game/Editor'); Expected = $false }
        )
        foreach ($case in $cases) {
            $value = Invoke-HarnessModuleFunction $module 'Test-PathIntersects' @{ ChangedPaths = $case.ChangedPaths; InvalidationPaths = $case.InvalidationPaths }
            if ([bool]$value -ne [bool]$case.Expected) { return New-HarnessFail ('path intersection mismatch for ' + ($case.ChangedPaths -join ',')) }
        }
        return New-HarnessPass 'exact/descendant/ancestor and sibling-prefix cases'
    } finally {
        if ($null -ne $module) { Remove-Module -ModuleInfo $module -Force -ErrorAction SilentlyContinue }
    }
}

function Test-EvidencePathBudget {
    param([Parameter(Mandatory = $true)]$State)
    $module = $null
    $root = Join-Path $script:HarnessScratchRoot ('harness-evidence-' + [Guid]::NewGuid().ToString('N'))
    try {
        $module = & $State.ShimCommand $State.CurrentSource @('Assert-EvidencePathBudget') @()
        $deepest = [string](Invoke-HarnessModuleFunction $module 'Assert-EvidencePathBudget' @{ EvidenceDirectory = $root })
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { return New-HarnessFail 'short evidence directory not provisioned' }
        if (Test-Path -LiteralPath $deepest) { return New-HarnessFail ('probe file left behind: ' + $deepest) }
        $long = Join-Path $root ('x' * [Math]::Max(1, 260 - $root.Length))
        $threw = $false
        try { Invoke-HarnessModuleFunction $module 'Assert-EvidencePathBudget' @{ EvidenceDirectory = $long } | Out-Null } catch { $threw = $true }
        if (-not $threw) { return New-HarnessFail 'over-long evidence directory accepted' }
        if (Test-Path -LiteralPath $long) { return New-HarnessFail 'over-long evidence directory was created before the budget check' }
        return New-HarnessPass 'deepest-path budget rejected before mkdir; short root probed and cleaned'
    } finally {
        if ($null -ne $module) { Remove-Module -ModuleInfo $module -Force -ErrorAction SilentlyContinue }
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-ShortWorkspacePath {
    param([Parameter(Mandatory = $true)]$State)
    $currentModule = $null
    $redModule = $null
    try {
        $currentModule = & $State.ShimCommand $State.CurrentSource @('Get-FullPath', 'Assert-ShortWorkspacePath') @()
        $accepted = [string](Invoke-HarnessModuleFunction $currentModule 'Assert-ShortWorkspacePath' @{ Path = 'C:\wt\fixture'; Label = 'Fixture' })
        if ($accepted -cne 'C:\wt\fixture') { return New-HarnessFail ('C:\wt child path changed: ' + $accepted) }
        $currentRejected = $false
        try { Invoke-HarnessModuleFunction $currentModule 'Assert-ShortWorkspacePath' @{ Path = 'C:\evidence'; Label = 'EvidenceRoot' } | Out-Null } catch { $currentRejected = $true }
        if (-not $currentRejected) { return New-HarnessFail 'root-level C:\evidence path accepted' }

        $redModule = & $State.ShimCommand $State.RedSource @('Assert-ShortWorkspacePath') @()
        $redRejected = $false
        try { Invoke-HarnessModuleFunction $redModule 'Assert-ShortWorkspacePath' @{ Path = 'C:\evidence'; Label = 'EvidenceRoot' } | Out-Null } catch { $redRejected = $true }
        if ($redRejected) { return New-HarnessFail 'red baseline no longer accepts root-level evidence path' }
        return New-HarnessPass 'C:\wt child accepted; C:\ root sibling rejected; red baseline remains permissive'
    } finally {
        if ($null -ne $currentModule) { Remove-Module -ModuleInfo $currentModule -Force -ErrorAction SilentlyContinue }
        if ($null -ne $redModule) { Remove-Module -ModuleInfo $redModule -Force -ErrorAction SilentlyContinue }
    }
}

function Test-StringSetNull {
    param([Parameter(Mandatory = $true)]$State)
    $module = $null
    try {
        $module = & $State.ShimCommand $State.CurrentSource @('Test-StringSetEqual') @()
        $value = Invoke-HarnessModuleFunction $module 'Test-StringSetEqual' @{ Left = @('path'); Right = @($null) }
        if ([bool]$value) { return New-HarnessFail 'list versus @($null) considered equal' }
        return New-HarnessPass 'null discriminator'
    } finally {
        if ($null -ne $module) { Remove-Module -ModuleInfo $module -Force -ErrorAction SilentlyContinue }
    }
}

function Test-PlanOnlyPendingOnly {
    param([Parameter(Mandatory = $true)]$State)
    $ast = Get-HarnessAst $State.CurrentSource
    $pending = @($ast.FindAll({
        param($Node)
        $Node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $Node.Extent.Text -match '(?im)\$productionBakeRow\.status\s*=\s*''pending'''
    }, $true))
    if ($pending.Count -ne 1) { return New-HarnessFail ('expected one production-bake PlanOnly pending assignment, found ' + $pending.Count) }
    $condition = @($ast.FindAll({
        param($Node)
            $Node -is [System.Management.Automation.Language.IfStatementAst] -and
            $Node.Extent.Text -match '(?im)if\s*\(\s*\$PlanOnly\s*\)' -and
            $Node.Extent.Text -match '(?im)\$productionBakeRow\.status\s*=\s*''pending'''
    }, $true))
    if ($condition.Count -ne 1) { return New-HarnessFail 'pending assignment is not guarded by exact if ($PlanOnly)' }
    if ($condition[0].Extent.Text -match '(?im)\$row\.status\s*=\s*''pending''') { return New-HarnessFail 'PlanOnly branch assigns pending to arbitrary rows' }
    return New-HarnessPass 'PlanOnly pending assignment is production-bake-only'
}

function Test-GuardG1 {
    param([Parameter(Mandatory = $true)]$State)
    $ast = Get-HarnessAst $State.CurrentSource
    $members = @($ast.FindAll({
        param($Node)
        $Node -is [System.Management.Automation.Language.MemberExpressionAst] -and
            [string]$Node.Member.Extent.Text -ceq 'Properties'
    }, $true))
    $allow = @{
        'Get-CanonicalPath|item|index:Target' = 'Get-Item metadata adapter'
        'Get-ObjectPropertyValue|Object|name' = 'IDictionary fallback'
        'Get-ObjectPropertyValue|Object|index:Name' = 'IDictionary fallback'
        'Merge-ExistingLedger|parsed|contains:history' = 'ConvertFrom-Json ledger payload'
        'Merge-ExistingLedger|parsed|contains:checkLedger' = 'ConvertFrom-Json ledger payload'
        'Merge-ExistingLedger|parsed|contains:rows' = 'ConvertFrom-Json ledger payload'
        'Get-ProbeStringValue|Probe|index:Field' = 'validated probe payload'
        'Get-ProbeStringArrayValue|Probe|index:Field' = 'validated probe payload'
        'Read-ProbeContract|probe|name' = 'ConvertFrom-Json probe payload'
    }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($member in $members) {
        $expression = $member.Expression
        if ($expression -isnot [System.Management.Automation.Language.MemberExpressionAst] -or
            [string]$expression.Member.Extent.Text -cne 'PSObject' -or
            $expression.Expression -isnot [System.Management.Automation.Language.VariableExpressionAst]) {
            return New-HarnessFail ('G1 PSObject.Properties operand provenance invalid: ' + $member.Extent.Text)
        }
        $operand = [string]$expression.Expression.VariablePath.UserPath
        $function = $member.Parent
        while ($null -ne $function -and $function -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) { $function = $function.Parent }
        if ($null -eq $function) { return New-HarnessFail ('G1 site has no enclosing function: ' + $member.Extent.Text) }
        $usage = 'name'
        $parent = $member.Parent
        if ($parent -is [System.Management.Automation.Language.IndexExpressionAst]) {
            $indexText = [string]$parent.Extent.Text
            $indexMatch = [regex]::Match($indexText, '(?i)\[[''\"]([^''\"]+)[''\"]\]')
            if ($indexMatch.Success) { $usage = 'index:' + $indexMatch.Groups[1].Value }
            elseif ($indexText -match '(?i)\[\$Name\]') { $usage = 'index:Name' }
            elseif ($indexText -match '(?i)\[\$Field\]') { $usage = 'index:Field' }
            else { $usage = 'index' }
        } else {
            $context = $parent
            while ($null -ne $context -and $context -isnot [System.Management.Automation.Language.FunctionDefinitionAst]) {
                if ($context -is [System.Management.Automation.Language.BinaryExpressionAst] -and [string]$context.Extent.Text -match '(?i)-contains\s*[\''\"]([^\''\"]+)[\''\"]') {
                    $usage = 'contains:' + ([regex]::Match([string]$context.Extent.Text, '(?i)-contains\s*[\''\"]([^\''\"]+)[\''\"]')).Groups[1].Value
                    break
                }
                $context = $context.Parent
            }
        }
        $key = [string]$function.Name + '|' + $operand + '|' + $usage
        if (-not $allow.ContainsKey($key)) { return New-HarnessFail ('G1 unapproved symbol/provenance: ' + $key + ' at ' + $member.Extent.Text) }
        if (-not $seen.Add($key)) { return New-HarnessFail ('G1 duplicate allowlisted symbol/provenance: ' + $key) }
        if ([string]$member.Extent.Text -cne ('$' + $operand + '.PSObject.Properties')) {
            return New-HarnessFail ('G1 operand chain is not normalized: ' + $member.Extent.Text)
        }
        switch ($key) {
            'Get-CanonicalPath|item|index:Target' {
                $itemAssignments = @($function.FindAll({
                    param($Node)
                    if ($Node -isnot [System.Management.Automation.Language.AssignmentStatementAst]) { return $false }
                    $left = $Node.Left
                    if ($left -isnot [System.Management.Automation.Language.VariableExpressionAst] -or
                        [string]$left.VariablePath.UserPath -cne 'item') { return $false }
                    $right = $Node.Right
                    if ($right -isnot [System.Management.Automation.Language.PipelineAst]) { return $false }
                    $elements = @($right.PipelineElements)
                    return $elements.Count -eq 1 -and
                        $elements[0] -is [System.Management.Automation.Language.CommandAst] -and
                        [string]$elements[0].GetCommandName() -ceq 'Get-Item'
                }, $true))
                if ($itemAssignments.Count -ne 1) { return New-HarnessFail 'G1 item provenance requires one AST assignment with RHS command Get-Item' }
            }
            'Get-ObjectPropertyValue|Object|name' {
                if ([string]$function.Extent.Text -notmatch '(?i)-is\s+\[System\.Collections\.IDictionary\]') { return New-HarnessFail 'G1 Object fallback lacks IDictionary guard' }
            }
            'Get-ObjectPropertyValue|Object|index:Name' {
                if ([string]$function.Extent.Text -notmatch '(?i)-is\s+\[System\.Collections\.IDictionary\]') { return New-HarnessFail 'G1 Object fallback lacks IDictionary guard' }
            }
            'Merge-ExistingLedger|parsed|contains:history' {
                if (@($function.FindAll({ param($Node) $Node -is [System.Management.Automation.Language.AssignmentStatementAst] -and [string]$Node.Left.Extent.Text -ceq '$parsed' -and $Node.Right.Extent.Text -match '(?i)ConvertFrom-Json' }, $true)).Count -eq 0) { return New-HarnessFail 'G1 parsed provenance is not ConvertFrom-Json' }
            }
            'Merge-ExistingLedger|parsed|contains:checkLedger' {
                if (@($function.FindAll({ param($Node) $Node -is [System.Management.Automation.Language.AssignmentStatementAst] -and [string]$Node.Left.Extent.Text -ceq '$parsed' -and $Node.Right.Extent.Text -match '(?i)ConvertFrom-Json' }, $true)).Count -eq 0) { return New-HarnessFail 'G1 parsed provenance is not ConvertFrom-Json' }
            }
            'Merge-ExistingLedger|parsed|contains:rows' {
                if (@($function.FindAll({ param($Node) $Node -is [System.Management.Automation.Language.AssignmentStatementAst] -and [string]$Node.Left.Extent.Text -ceq '$parsed' -and $Node.Right.Extent.Text -match '(?i)ConvertFrom-Json' }, $true)).Count -eq 0) { return New-HarnessFail 'G1 parsed provenance is not ConvertFrom-Json' }
            }
            'Read-ProbeContract|probe|name' {
                if (@($function.FindAll({ param($Node) $Node -is [System.Management.Automation.Language.AssignmentStatementAst] -and [string]$Node.Left.Extent.Text -ceq '$probe' -and $Node.Right.Extent.Text -match '(?i)ConvertFrom-Json' }, $true)).Count -eq 0) { return New-HarnessFail 'G1 probe provenance is not ConvertFrom-Json' }
            }
        }
    }
    if ($members.Count -ne $allow.Count -or $seen.Count -ne $allow.Count) { return New-HarnessFail ('G1 allowlist mismatch: expected ' + $allow.Count + ' exact sites, observed ' + $members.Count) }
    return New-HarnessPass ('G1 exact symbol/provenance sites=' + $members.Count)
}

function Test-GuardG3 {
    param([Parameter(Mandatory = $true)]$State)
    $ast = Get-HarnessFunctionAst $State.CurrentSource 'Get-ObjectPropertyValue'
    if ($ast.Extent.Text -notmatch '(?i)-is\s+\[System\.Collections\.IDictionary\]') {
        return New-HarnessFail 'Get-ObjectPropertyValue lacks IDictionary type-test'
    }
    return New-HarnessPass 'IDictionary type-test present'
}

function Test-GuardG4 {
    param([Parameter(Mandatory = $true)]$State)
    $editorRoot = Join-Path $State.ProjectRoot 'Assets/_Game/Editor'
    foreach ($file in @(Get-ChildItem -LiteralPath $editorRoot -Filter '*.cs' -File -Recurse)) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        if ($text -notmatch '(?-i)\bFile\.Replace\s*\(') { continue }
        if (-not $file.Name.Equals('MovementLabAtomicFile.cs', [StringComparison]::OrdinalIgnoreCase)) {
            return New-HarnessFail ('G4 File.Replace outside MovementLabAtomicFile: ' + $file.FullName)
        }
    }
    return New-HarnessPass 'File.Replace confined to MovementLabAtomicFile'
}

function Test-GuardG5 {
    param([Parameter(Mandatory = $true)]$State)
    $validationRoot = Join-Path $State.ProjectRoot 'Tools/Validation'
    foreach ($file in @(Get-ChildItem -LiteralPath $validationRoot -Filter '*.ps1' -File)) {
        $tokens = $null
        $errors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors)
        if ($null -ne $errors -and @($errors).Count -gt 0) {
            return New-HarnessFail ('G5 parser errors in ' + $file.FullName + ': ' + (@($errors | ForEach-Object { [string]$_.Message }) -join ' | '))
        }
    }
    return New-HarnessPass 'all Tools/Validation PowerShell files parse cleanly'
}

function Test-ScratchDrill {
    param([Parameter(Mandatory = $true)]$State)
    $scratch = $State.CurrentSource -replace '(?ms)\s*if \(\$Object -is \[System\.Collections\.IDictionary\]\) \{.*?\n\s*\}\r?\n(?=\s*if \(\$null -eq \$Object)', "`r`n"
    $module = $null
    try {
        $module = & $State.ShimCommand $scratch @('Get-ObjectPropertyValue') @()
        $value = Invoke-HarnessModuleFunction $module 'Get-ObjectPropertyValue' @{ Object = [ordered]@{ marker = 'ordered-value' }; Name = 'marker' }
        $gopvFails = [string]$value -cne 'ordered-value'
        $scratchAst = Get-HarnessFunctionAst $scratch 'Get-ObjectPropertyValue'
        $g3Fails = $scratchAst.Extent.Text -notmatch '(?i)-is\s+\[System\.Collections\.IDictionary\]'
        if (-not $gopvFails -or -not $g3Fails) { return New-HarnessFail ('scratch drill did not trip both guards: gopv=' + $gopvFails + '; G3=' + $g3Fails) }

        $itemAssignmentScratch = $State.CurrentSource -replace '(?m)^[ \t]*\$item[ \t]*=[ \t]*Get-Item\b[^\r\n]*(?:\r?$)', '    $item = [ordered]@{ Target = $full }'
        $itemAssignmentResult = Test-GuardG1 ([pscustomobject]@{ CurrentSource = $itemAssignmentScratch })
        if ([bool](Get-HarnessField $itemAssignmentResult 'pass')) { return New-HarnessFail 'G1 dictionary-like item assignment scratch unexpectedly passed' }

        $aliasScratch = $State.CurrentSource.Replace(
            '$targetProperty = $item.PSObject.Properties[''Target'']',
            ('$adapter = $item.PSObject' + [Environment]::NewLine + '        $targetProperty = $adapter.Properties[''Target'']'))
        $aliasResult = Test-GuardG1 ([pscustomobject]@{ CurrentSource = $aliasScratch })
        if ([bool](Get-HarnessField $aliasResult 'pass')) { return New-HarnessFail 'G1 alias chain scratch unexpectedly passed' }

        $dictionaryOperandScratch = $State.CurrentSource.Replace(
            '$item.PSObject.Properties[''Target'']',
            '$row.PSObject.Properties[''Target'']')
        $dictionaryResult = Test-GuardG1 ([pscustomobject]@{ CurrentSource = $dictionaryOperandScratch })
        if ([bool](Get-HarnessField $dictionaryResult 'pass')) { return New-HarnessFail 'G1 dictionary-like operand substitution unexpectedly passed' }
        return New-HarnessPass 'scratch removal trips G3; alias/substitution trip G1'
    } finally {
        if ($null -ne $module) { Remove-Module -ModuleInfo $module -Force -ErrorAction SilentlyContinue }
    }
}

function Test-HookSettings {
    param([Parameter(Mandatory = $true)]$State)
    if (-not (Test-Path -LiteralPath $State.HookSettingsPath -PathType Leaf)) {
        return New-HarnessFail 'missing .claude/settings.json'
    }
    try { $settings = Get-Content -Raw -LiteralPath $State.HookSettingsPath | ConvertFrom-Json }
    catch { return New-HarnessFail ('invalid hook JSON: ' + $_.Exception.Message) }

    if ($null -eq $settings.hooks) { return New-HarnessFail 'hook JSON has no hooks object' }
    function Get-ConfiguredHook {
        param(
            [Parameter(Mandatory = $true)][string]$HookName,
            [Parameter(Mandatory = $true)][string]$Matcher,
            [Parameter(Mandatory = $true)][string]$Mode
        )
        $entries = if ($null -ne $settings.hooks.$HookName) { @($settings.hooks.$HookName) } else { @() }
        $matching = @($entries | Where-Object { [string]$_.matcher -ceq $Matcher })
        if ($matching.Count -ne 1) { throw ('expected one ' + $Matcher + ' ' + $HookName + ' matcher, found ' + $matching.Count) }
        if ($matching[0].PSObject.Properties.Name -contains 'paths') { throw ($HookName + ' matcher uses unsupported paths field') }
        $commandHooks = @($matching[0].hooks | Where-Object { [string]$_.type -ceq 'command' })
        if ($commandHooks.Count -ne 1) { throw ($HookName + ' matcher must have one command hook') }
        $hook = $commandHooks[0]
        $properties = @($hook.PSObject.Properties.Name | Sort-Object)
        if (($properties -join '|') -cne 'args|command|type') { throw ($HookName + ' command hook schema must contain exactly type, command, args') }
        if ([string]$hook.command -cne 'powershell.exe') { throw ($HookName + ' command must be powershell.exe') }
        if ($hook.args -is [string] -or $null -eq $hook.args) { throw ($HookName + ' command args must be an array') }
        $actualArgs = @($hook.args | ForEach-Object { [string]$_ })
        $expectedArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', '${CLAUDE_PROJECT_DIR}/Tools/Tests/Invoke-HarnessTests.ps1', '-HookMode', $Mode)
        if (($actualArgs -join '|') -cne ($expectedArgs -join '|')) { throw ($HookName + ' command args mismatch: ' + ($actualArgs -join ' ')) }
        return $hook
    }

    $preHook = $null
    try {
        $preHook = Get-ConfiguredHook 'PreToolUse' 'Bash|PowerShell' 'PreToolUse'
        if ($settings.hooks.PSObject.Properties.Name -contains 'PostToolUse') { throw 'PostToolUse harness hook must be absent' }
    } catch {
        return New-HarnessFail $_.Exception.Message
    }
    if ($State.SkipHookCheck) { return New-HarnessPass 'hook command resolved (nested check skipped)' }

    function Start-HookFixture {
        param(
            [Parameter(Mandatory = $true)]$Hook,
            [Parameter(Mandatory = $true)][string]$EventJson,
            [switch]$ForceFailure
        )
        $arguments = @($Hook.args | ForEach-Object {
            $value = [string]$_
            if ($value.Contains('${CLAUDE_PROJECT_DIR}')) { $value.Replace('${CLAUDE_PROJECT_DIR}', $State.ProjectRoot) } else { $value }
        })
        if ($ForceFailure) { $arguments += '-HookTestForceFailure' }
        $fixtureRoot = Join-Path $script:HarnessScratchRoot ('RocketFooxballHook-' + [guid]::NewGuid().ToString('N'))
        [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
        $process = $null
        $quoteArgument = {
            param([string]$Value)
            return ('"' + $Value.Replace('"', '\"') + '"')
        }
        try {
            $startInfo = New-Object System.Diagnostics.ProcessStartInfo
            $startInfo.FileName = [string]$Hook.command
            $startInfo.Arguments = (($arguments | ForEach-Object { & $quoteArgument ([string]$_) }) -join ' ')
            $startInfo.WorkingDirectory = $fixtureRoot
            $startInfo.UseShellExecute = $false
            $startInfo.CreateNoWindow = $true
            $startInfo.RedirectStandardInput = $true
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $process = New-Object System.Diagnostics.Process
            $process.StartInfo = $startInfo
            if (-not $process.Start()) { throw ('unable to launch configured ' + [string]$Hook.args[-1] + ' hook') }
            $process.StandardInput.Write($EventJson)
            $process.StandardInput.Close()
            return [pscustomobject]@{ Process = $process; FixtureRoot = $fixtureRoot; Mode = [string]$Hook.args[-1] }
        } catch {
            if ($null -ne $process) { $process.Dispose() }
            Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
            throw
        }
    }

    function Complete-HookFixture {
        param([Parameter(Mandatory = $true)]$Pending)
        $process = $Pending.Process
        try {
            $stdoutText = $process.StandardOutput.ReadToEnd()
            $stderrText = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            $exitCode = [int]$process.ExitCode
        } finally {
            $process.Dispose()
        }
        Remove-Item -LiteralPath $Pending.FixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
        $stdoutLines = @()
        if (-not [string]::IsNullOrEmpty($stdoutText)) {
            $stdoutLines = @($stdoutText -split "`r?`n" | Where-Object { $_ -ne '' })
        }
        return [pscustomobject]@{
            exitCode = $exitCode
            stdout = $stdoutLines
            stderr = [string]$stderrText
            mode = [string]$Pending.Mode
        }
    }

    # Nested hook failures otherwise surface as a bare exit code; the real child error is only
    # reachable from -EvidenceRoot JSON. Fold the child's own output into the fail message.
    function Get-HookFailureDetail {
        param([Parameter(Mandatory = $true)]$Result)
        $label = 'stderr'
        $detail = [string]$Result.stderr
        if ([string]::IsNullOrWhiteSpace($detail)) {
            $label = 'stdout'
            $detail = (@($Result.stdout) -join ' ')
        }
        $detail = (($detail -replace '\s+', ' ')).Trim()
        if ([string]::IsNullOrWhiteSpace($detail)) { return '; child output empty' }
        if ($detail.Length -gt 200) { $detail = $detail.Substring(0, 197) + '...' }
        return ('; ' + $label + '=' + $detail)
    }

    $preWorkflowTarget = '{"tool_name":"Bash","tool_input":{"command":"powershell -File Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode Fast -PlanOnly"}}'
    $preUnityTarget = '{"tool_name":"PowerShell","tool_input":{"command":"C:\\Unity\\Editor\\Unity.exe -batchmode -quit"}}'
    $preUnrelated = '{"tool_name":"PowerShell","tool_input":{"command":"Get-Date"}}'
    # Process.StandardInput inherits [Console]::InputEncoding and flushes that encoding's preamble
    # when Start() sets AutoFlush. Under an active UTF-8 console (chcp 65001) the preamble is a
    # 3-byte BOM that lands ahead of the event JSON and makes every nested hook reject stdin.
    # Swapping in a preamble-free UTF-8 keeps code page 65001 unchanged; other code pages already
    # report an empty preamble and are left alone.
    $previousInputEncoding = $null
    if ([Console]::InputEncoding.GetPreamble().Length -gt 0) {
        $previousInputEncoding = [Console]::InputEncoding
        [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
    }
    try {
        $preWorkflowPending = Start-HookFixture $preHook $preWorkflowTarget
        $preUnityPending = Start-HookFixture $preHook $preUnityTarget
        $preUnrelatedPending = Start-HookFixture $preHook $preUnrelated
        $preForcedPending = Start-HookFixture $preHook $preWorkflowTarget -ForceFailure
        $preWorkflowResult = Complete-HookFixture $preWorkflowPending
        $preUnityResult = Complete-HookFixture $preUnityPending
        $preUnrelatedResult = Complete-HookFixture $preUnrelatedPending
        $preForcedResult = Complete-HookFixture $preForcedPending
    } catch {
        return New-HarnessFail $_.Exception.Message
    } finally {
        if ($null -ne $previousInputEncoding) { [Console]::InputEncoding = $previousInputEncoding }
    }
    $State.HookExecution = [ordered]@{
        preWorkflowExit = $preWorkflowResult.exitCode
        preUnityExit = $preUnityResult.exitCode
        preUnrelatedExit = $preUnrelatedResult.exitCode
        preForcedExit = $preForcedResult.exitCode
        preForcedStderr = $preForcedResult.stderr.Trim()
    }
    foreach ($fixture in @(
        [pscustomobject]@{ Name = 'workflow PreToolUse'; Result = $preWorkflowResult },
        [pscustomobject]@{ Name = 'Unity PreToolUse'; Result = $preUnityResult }
    )) {
        if ($fixture.Result.exitCode -ne 0) { return New-HarnessFail ($fixture.Name + ' target hook exited ' + $fixture.Result.exitCode + (Get-HookFailureDetail $fixture.Result)) }
        if ($fixture.Result.stdout.Count -ne 0) { return New-HarnessFail ($fixture.Name + ' target emitted stdout' + (Get-HookFailureDetail $fixture.Result)) }
        if ($fixture.Result.stderr -notmatch '(?i)HOOK .* PASS: harness cases=') { return New-HarnessFail ($fixture.Name + ' target did not run harness' + (Get-HookFailureDetail $fixture.Result)) }
    }
    if ($preUnrelatedResult.exitCode -ne 0) { return New-HarnessFail ('unrelated PreToolUse hook exited ' + $preUnrelatedResult.exitCode + (Get-HookFailureDetail $preUnrelatedResult)) }
    if ($preUnrelatedResult.stdout.Count -ne 0 -or $preUnrelatedResult.stderr -notmatch '(?i)SKIP unrelated event') { return New-HarnessFail ('unrelated PreToolUse event did not skip cleanly' + (Get-HookFailureDetail $preUnrelatedResult)) }
    if ($preForcedResult.exitCode -ne 2) { return New-HarnessFail ('forced failing PreToolUse hook exited ' + $preForcedResult.exitCode + ', expected 2' + (Get-HookFailureDetail $preForcedResult)) }
    if ($preForcedResult.stdout.Count -ne 0 -or $preForcedResult.stderr -notmatch '(?i)forced harness failure') { return New-HarnessFail ('forced PreToolUse fixture lacked concise stderr failure' + (Get-HookFailureDetail $preForcedResult)) }
    return New-HarnessPass 'one PreToolUse hook; workflow/Unity targets; nested hook check skipped; unrelated skip and exit-2 failure'
}
