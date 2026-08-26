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

function New-HarnessRowModule {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)]$ShimCommand
    )

    $names = @(
        'New-LedgerRow', 'Get-AuthoritativeGeneratedInventory', 'Get-GeneratedHashes',
        'Get-GeneratedHashDigest', 'Get-EnvironmentFingerprint', 'Get-WorkingTreeDigest'
    )
    $module = & $ShimCommand $Source $names @()
    & $module {
        function script:Get-AuthoritativeGeneratedInventory { return @('Assets/_Game/Generated/fixture.json') }
        function script:Get-GeneratedHashes { return [ordered]@{ 'fixture.json' = 'hash' } }
        function script:Get-GeneratedHashDigest { param($Value) return 'digest' }
        function script:Get-EnvironmentFingerprint { return 'environment' }
        function script:Get-WorkingTreeDigest { return 'working-tree' }
        $script:InvocationId = 'fixture-invocation'
    } | Out-Null
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
        $legacyInventoryVariables = @($ast.FindAll({
            param($Node)
            $Node -is [System.Management.Automation.Language.VariableExpressionAst] -and
            [string]$Node.VariablePath.UserPath -in @('GeneratedRoots', 'AuthoritativeInventory', 'RequestedInventoryPaths', 'script:GeneratedRoots', 'script:AuthoritativeInventory', 'script:RequestedInventoryPaths')
        }, $true))
        $requestedInventoryFields = @($ast.FindAll({
            param($Node)
            $Node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
            [string]$Node.Value -ceq 'requested_inventory'
        }, $true))
        return $legacyInventoryVariables.Count + $requestedInventoryFields.Count
    }

    $headCount = [int](& $findLegacySurface $State.CurrentSource)
    if ($headCount -ne 0) { return New-HarnessFail ('legacy generated-path surface remains at ' + $headCount + ' site(s)') }
    $redSource = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $State.RedSource -ErrorAction Stop).Path)
    $redCount = [int](& $findLegacySurface $redSource)
    if ($redCount -eq 0) { return New-HarnessFail 'red baseline does not retain the legacy generated-path surface' }

    $teamRedTrailPaths = @(
        'Assets/_Game/Materials/TeamRedTrail.mat',
        'Assets/_Game/Materials/TeamRedTrail.mat.meta'
    )
    $shotgunAtlasMetaPaths = @(
        'Assets/_Game/Textures/Shotgun_BaseColor.png.meta',
        'Assets/_Game/Textures/Shotgun_Normal.png.meta',
        'Assets/_Game/Textures/Shotgun_MetallicSmoothness.png.meta',
        'Assets/_Game/Textures/Shotgun_Occlusion.png.meta',
        'Assets/_Game/Textures/Shotgun_Emission.png.meta'
    )
    $currentAppendixValues = @(Get-HarnessStringAssignment $State.CurrentSource 'script:AppendixAPaths')
    $currentAst = Get-HarnessAst $State.CurrentSource
    $currentSourceValues = @($currentAst.FindAll({
        param($Node)
        $Node -is [System.Management.Automation.Language.StringConstantExpressionAst]
    }, $true) | ForEach-Object { [string]$_.Value })
    $redBuilderValues = @(Get-HarnessStringAssignment $redSource 'script:BuilderOutputContract')
    $builderAlias = @($currentAst.FindAll({
        param($Node)
        $Node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            [string]$Node.Left.Extent.Text -ceq '$script:BuilderOutputContract' -and
            [string]$Node.Right.Extent.Text.Trim() -ceq '$script:AppendixAPaths'
    }, $true))
    if ($builderAlias.Count -ne 1) { return New-HarnessFail ('BuilderOutputContract must derive from AppendixAPaths exactly once; observed ' + $builderAlias.Count) }
    foreach ($path in $teamRedTrailPaths) {
        $appendixCount = @($currentAppendixValues | Where-Object { [string]$_ -ceq $path }).Count
        if ($appendixCount -ne 1) { return New-HarnessFail ('Appendix-A inventory must contain exactly one TeamRedTrail entry: ' + $path + ' (observed ' + $appendixCount + ')') }
        $sourceCount = @($currentSourceValues | Where-Object { [string]$_ -ceq $path }).Count
        if ($sourceCount -ne 1) { return New-HarnessFail ('TeamRedTrail entry must occur exactly once in workflow source: ' + $path + ' (observed ' + $sourceCount + ')') }
        $redCountForPath = @($redBuilderValues | Where-Object { [string]$_ -ceq $path }).Count
        if ($redCountForPath -ne 0) { return New-HarnessFail ('historical red workflow must remain missing TeamRedTrail entry: ' + $path) }
    }
    foreach ($path in $shotgunAtlasMetaPaths) {
        $appendixCount = @($currentAppendixValues | Where-Object { [string]$_ -ceq $path }).Count
        if ($appendixCount -ne 1) { return New-HarnessFail ('Appendix-A inventory must contain exactly one shotgun atlas metadata entry: ' + $path + ' (observed ' + $appendixCount + ')') }
        $sourceCount = @($currentSourceValues | Where-Object { [string]$_ -ceq $path }).Count
        if ($sourceCount -ne 1) { return New-HarnessFail ('Shotgun atlas metadata entry must occur exactly once in workflow source: ' + $path + ' (observed ' + $sourceCount + ')') }
        $redCountForPath = @($redBuilderValues | Where-Object { [string]$_ -ceq $path }).Count
        if ($redCountForPath -ne 0) { return New-HarnessFail ('historical red workflow must remain missing shotgun atlas metadata entry: ' + $path) }
    }
    return New-HarnessPass ('HEAD removed legacy surface; RedAtSha retains ' + $redCount + ' site(s); TeamRedTrail and five shotgun atlas metadata entries are closed')
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
    $pipeDrainResult = Test-GeneratedYamlComparatorAsyncPipeDrain $State
    if (-not [bool](Get-HarnessField $pipeDrainResult 'pass')) {
        return New-HarnessFail ('async pipe drain contract failed: ' + [string](Get-HarnessField $pipeDrainResult 'message'))
    }
    return New-HarnessPass 'controller/cue paths covered; uncovered and non-authoritative requests rejected; async pipe drain guarded'
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
        $materialPath = Join-Path $materialsDirectory 'Floor.mat'
        $metaPath = $materialPath + '.meta'
        $physicMaterialPath = Join-Path $materialsDirectory 'BallSurface.physicMaterial'
        $physicMaterialMetaPath = $physicMaterialPath + '.meta'
        $churnPath = Join-Path $materialsDirectory 'Wall.mat'
        $churnMetaPath = $churnPath + '.meta'
        $generatedPath = Join-Path $generatedDirectory 'BlueCircleCueMesh.asset'
        $generatedMetaPath = $generatedPath + '.meta'
        $projectSettingsPath = Join-Path $projectSettingsDirectory 'QualitySettings.asset'
        $binaryPaths = @(
            (Join-Path $movementLabDirectory 'Lightmap-0_comp_dir.png'),
            (Join-Path $movementLabDirectory 'Lightmap-0_comp_light.exr'),
            (Join-Path $movementLabDirectory 'LightingData.asset')
        )
        [IO.File]::WriteAllText($materialPath, "%YAML 1.1`n--- !u!21 &1`nMaterial:`n  m_Name: Fixture`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($metaPath, "fileFormatVersion: 2`nguid: 11111111111111111111111111111111`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($physicMaterialPath, "%YAML 1.1`n--- !u!134 &4`nPhysicMaterial:`n  m_Name: BallSurface`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($physicMaterialMetaPath, "fileFormatVersion: 2`nguid: 55555555555555555555555555555555`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($churnPath, "%YAML 1.1`n--- !u!21 &2`nMaterial:`n  m_Name: Churn`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($churnMetaPath, "fileFormatVersion: 2`nguid: 22222222222222222222222222222222`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($generatedPath, "%YAML 1.1`n--- !u!114 &3`nMonoBehaviour:`n  m_Name: Cue`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($generatedMetaPath, "fileFormatVersion: 2`nguid: 33333333333333333333333333333333`n", (New-Object Text.UTF8Encoding($false)))
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
        [IO.File]::AppendAllText($physicMaterialPath, "  dynamicFriction: 0.5`n", (New-Object Text.UTF8Encoding($false)))
        foreach ($binaryPath in $binaryPaths) { [IO.File]::AppendAllText($binaryPath, "`nhead binary drift", (New-Object Text.UTF8Encoding($false))) }
        [IO.File]::AppendAllText($generatedPath, "  changed: true`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::AppendAllText($projectSettingsPath, "setting: head`n", (New-Object Text.UTF8Encoding($false)))
        [IO.File]::WriteAllText($churnMetaPath, "fileFormatVersion: 2`nguid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa`n", (New-Object Text.UTF8Encoding($false)))
        Remove-Item -LiteralPath $churnPath -Force

        $fixtureComparator = Join-Path $validationDirectory 'Compare-GeneratedYaml.ps1'
        $paths = @(
            'Assets/_Game/Materials/Floor.mat', 'Assets/_Game/Materials/Floor.mat.meta',
            'Assets/_Game/Materials/BallSurface.physicMaterial', 'Assets/_Game/Materials/BallSurface.physicMaterial.meta',
            'Assets/_Game/Materials/Wall.mat', 'Assets/_Game/Materials/Wall.mat.meta',
            'Assets/_Game/Generated/BlueCircleCueMesh.asset', 'Assets/_Game/Generated/BlueCircleCueMesh.asset.meta',
            'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_dir.png', 'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_dir.png.meta',
            'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_light.exr', 'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_light.exr.meta',
            'Assets/_Game/Scenes/MovementLab/LightingData.asset', 'Assets/_Game/Scenes/MovementLab/LightingData.asset.meta',
            'ProjectSettings/QualitySettings.asset'
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
        if ($text -notmatch '(?s)== Assets/_Game/Materials/Floor\.mat\.meta.*?kind\s+metadata.*?guid\s+stable\s+11111111111111111111111111111111') {
            return New-HarnessFail 'stable GUID metadata was not reported'
        }
        if ($text -notmatch '(?s)== Assets/_Game/Materials/Wall\.mat\.meta.*?guid\s+churn\s+22222222222222222222222222222222\s+->\s+aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa') {
            return New-HarnessFail 'GUID churn was not reported'
        }
        if ($text -notmatch '(?m)^  pair\s+broken') {
            return New-HarnessFail 'broken asset/meta pair was not reported'
        }
        $projectSettingsSection = [regex]::Match($text, '(?s)== ProjectSettings/QualitySettings\.asset.*?(?=\r?\n== |\r?\nCOVERAGE:)')
        if (-not $projectSettingsSection.Success -or $projectSettingsSection.Value -notmatch '(?m)^  pair\s+not-applicable') {
            return New-HarnessFail 'ProjectSettings asset was not reported as pair not-applicable'
        }
        if ($projectSettingsSection.Value -match '(?m)^  pair\s+broken|(?m)^  guid\s+') {
            return New-HarnessFail 'ProjectSettings asset incorrectly received pair/GUID analysis'
        }
        foreach ($path in @(
            'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_dir.png',
            'Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_light.exr',
            'Assets/_Game/Scenes/MovementLab/LightingData.asset'
        )) {
            if ($text -notmatch ('(?s)== ' + [regex]::Escape($path) + '.*?kind\s+binary provenance.*?bytes\s+.*?blob\s+.*?provenance')) {
                return New-HarnessFail ('binary provenance was not reported for ' + $path)
            }
        }
        foreach ($header in @('COVERAGE:', 'SEMANTIC:', 'DANGLING:', 'GUID:', 'PAIRS:', 'UNSUPPORTED:')) {
            if ($text -notmatch ('(?m)^' + [regex]::Escape($header))) { return New-HarnessFail ('comparator omitted exact summary header ' + $header) }
        }
        if ($text -notmatch '(?m)^COVERAGE: 15/15 authoritative changed paths reported; semantic checked 5; NOT CHECKED 10$') {
            return New-HarnessFail ('default comparator coverage summary did not account for every changed path: ' + $text)
        }
        if ($text -notmatch '(?m)^GUID: stable 6; churn 1; added 0; removed 0; invalid 0$' -or
            $text -notmatch '(?m)^PAIRS: intact 6; repaired 0; broken 1$' -or
            $text -notmatch '(?m)^UNSUPPORTED: 0$') {
            return New-HarnessFail ('GUID/pair/unsupported summaries were incorrect: ' + $text)
        }

        # A raw asset committed before its .meta is added is a repaired pair once the
        # working tree contains both files. The existing Wall fixture remains head-broken.
        $repairedRoot = Join-Path $script:HarnessScratchRoot ('generated-yaml-comparator-repaired-' + [Guid]::NewGuid().ToString('N'))
        try {
            $repairedValidationDirectory = Join-Path $repairedRoot 'Tools/Validation'
            $repairedMaterialsDirectory = Join-Path $repairedRoot 'Assets/_Game/Materials'
            [IO.Directory]::CreateDirectory($repairedValidationDirectory) | Out-Null
            [IO.Directory]::CreateDirectory($repairedMaterialsDirectory) | Out-Null
            Copy-Item -LiteralPath $State.ComparatorPath -Destination (Join-Path $repairedValidationDirectory 'Compare-GeneratedYaml.ps1') -Force
            Copy-Item -LiteralPath $State.WorkflowPath -Destination (Join-Path $repairedValidationDirectory 'Invoke-MovementLabWorkflow.ps1') -Force
            # Floor.mat is already in the workflow's exact Appendix-A inventory.
            $repairedAssetPath = Join-Path $repairedMaterialsDirectory 'Floor.mat'
            $repairedAssetMetaPath = $repairedAssetPath + '.meta'
            [IO.File]::WriteAllText($repairedAssetPath, "%YAML 1.1`n--- !u!21 &1`nMaterial:`n  m_Name: Repaired`n", (New-Object Text.UTF8Encoding($false)))
            & git -C $repairedRoot init --quiet 2>$null
            if ($LASTEXITCODE -ne 0) { return New-HarnessFail 'repaired-pair fixture Git initialization failed' }
            & git -C $repairedRoot config core.autocrlf false 2>$null
            & git -C $repairedRoot config user.email 'harness@example.invalid' 2>$null
            & git -C $repairedRoot config user.name 'Harness' 2>$null
            & git -C $repairedRoot add . 2>$null
            & git -C $repairedRoot commit --quiet -m 'repaired pair baseline' 2>$null
            if ($LASTEXITCODE -ne 0) { return New-HarnessFail 'repaired-pair fixture Git baseline commit failed' }
            [IO.File]::WriteAllText($repairedAssetMetaPath, "fileFormatVersion: 2`nguid: 66666666666666666666666666666666`n", (New-Object Text.UTF8Encoding($false)))
            $repairedComparatorPath = Join-Path $repairedValidationDirectory 'Compare-GeneratedYaml.ps1'
            $repairedPreviousErrorAction = $ErrorActionPreference
            try {
                $ErrorActionPreference = 'Continue'
                $repairedOutput = @(& powershell -NoProfile -ExecutionPolicy Bypass -File $repairedComparatorPath -Base 'HEAD' -Head 'WORKTREE' -Path 'Assets/_Game/Materials/Floor.mat.meta' 2>&1)
                $repairedExitCode = $LASTEXITCODE
            } finally {
                $ErrorActionPreference = $repairedPreviousErrorAction
            }
            if ($repairedExitCode -ne 0) {
                $repairedDiagnostics = @($repairedOutput | ForEach-Object { '[' + $_.GetType().FullName + '] ' + ($_ | Out-String).Trim() }) -join ' | '
                return New-HarnessFail ('repaired-pair comparator failed exit=' + $repairedExitCode + ': ' + $repairedDiagnostics)
            }
            $repairedText = $repairedOutput -join "`n"
            if ($repairedText -notmatch '(?m)^  pair\s+repaired \(one-sided -> both-present\)$') {
                return New-HarnessFail ('base one-sided/head complete pair was not reported as repaired: ' + $repairedText)
            }
            if ($repairedText -notmatch '(?m)^PAIRS: intact 0; repaired 1; broken 0$') {
                return New-HarnessFail ('repaired-pair summary was incorrect: ' + $repairedText)
            }
            if ($text -notmatch '(?s)== Assets/_Game/Materials/Wall\.mat\.meta.*?pair\s+broken \(both-present -> one-sided\)') {
                return New-HarnessFail 'existing head-broken pair was not reported with broken transition'
            }
        } finally {
            Remove-Item -LiteralPath $repairedRoot -Recurse -Force -ErrorAction SilentlyContinue
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
    return New-HarnessPass 'semantic change exit-0; stable/churn GUIDs; binary provenance; intact/repaired/broken pairs; unsupported type; exact summaries; red fixture fail-closed'
}

function Test-RowFieldSweep {
    param([Parameter(Mandatory = $true)]$State)
    $module = $null
    try {
        $module = New-HarnessRowModule $State.CurrentSource $State.ShimCommand
        $row = Invoke-HarnessModuleFunction $module 'New-LedgerRow' @{
            CheckId = 'fixture'; Tier = 'fast'; MutatesProject = $false; RunPoint = 'coding'
        }
        if ($null -eq $row -or $row -isnot [System.Collections.IDictionary]) { return New-HarnessFail 'New-LedgerRow did not return dictionary row' }
        foreach ($key in @($row.Keys)) {
            $name = [string]$key
            $directValue = $row[$key]
            $expected = if ($null -eq $directValue) { '' } else { [string]$directValue }
            $actual = [string]$row[$key]
            if ($actual -cne $expected) {
                return New-HarnessFail ('row field text mismatch for ' + $name + ': expected [' + $expected + '], observed [' + $actual + ']')
            }
        }
        return New-HarnessPass ('readable keys=' + $row.Keys.Count)
    } finally {
        if ($null -ne $module) { Remove-Module -ModuleInfo $module -Force -ErrorAction SilentlyContinue }
    }
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
    $outcomeResult = Test-ProductionBakeOutcomePreserved $State
    if (-not [bool](Get-HarnessField $outcomeResult 'pass')) {
        return New-HarnessFail ('production bake outcome contract failed: ' + [string](Get-HarnessField $outcomeResult 'message'))
    }
    $workflowResult = Test-FastPersistedValidatorContract $State
    if (-not [bool](Get-HarnessField $workflowResult 'pass')) {
        return New-HarnessFail ('fast persisted validator contract failed: ' + [string](Get-HarnessField $workflowResult 'message'))
    }
    $captureResult = Test-WeaponCaptureContract $State
    if (-not [bool](Get-HarnessField $captureResult 'pass')) {
        return New-HarnessFail ('weapon capture contract failed: ' + [string](Get-HarnessField $captureResult 'message'))
    }
    $verdictResult = Test-WeaponVisualVerdictContract $State
    if (-not [bool](Get-HarnessField $verdictResult 'pass')) {
        return New-HarnessFail ('weapon verdict contract failed: ' + [string](Get-HarnessField $verdictResult 'message'))
    }
    return New-HarnessPass 'PlanOnly pending assignment is production-bake-only; production bake outcome preserved'
}

function Test-FastPersistedValidatorContract {
    param([Parameter(Mandatory = $true)]$State)
    $source = [IO.File]::ReadAllText($State.WorkflowPath)
    $redPath = Join-Path $State.ProjectRoot 'Tools/Tests/Fixtures/red-workflow.ps1.txt'
    if (-not (Test-Path -LiteralPath $redPath -PathType Leaf)) { return New-HarnessFail 'missing workflow red fixture' }
    $red = [IO.File]::ReadAllText($redPath)
    $required = @(
        "fast-persisted-validator",
        "FastPersistedValidator",
        "RocketFooxball.Editor.MovementLabBuilder.ValidateMovementLabFastPersisted",
        "Invoke-UnityStep 'FastPersistedValidator'",
        "Mark-CheckExecuted 'fast-persisted-validator'"
    )
    foreach ($needle in $required) {
        if ($source.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { return New-HarnessFail ('workflow omitted Fast persisted validator contract: ' + $needle) }
        if ($red.IndexOf($needle, [StringComparison]::Ordinal) -ge 0) { return New-HarnessFail ('workflow red fixture unexpectedly contains new contract: ' + $needle) }
    }
    $buildIndex = $source.IndexOf("Invoke-UnityStep 'BuildFast'", [StringComparison]::Ordinal)
    $persistIndex = $source.IndexOf("Invoke-UnityStep 'FastPersistedValidator'", [StringComparison]::Ordinal)
    if ($buildIndex -lt 0 -or $persistIndex -lt 0 -or $persistIndex -le $buildIndex) {
        return New-HarnessFail 'Fast workflow does not retain early BuildFast plus later persisted validator process.'
    }
    return New-HarnessPass 'Fast workflow has early BuildFast and distinct fast-persisted-validator process; red fixture rejects both'
}

function Test-WeaponCaptureContract {
    param([Parameter(Mandatory = $true)]$State)
    $path = Join-Path $State.ProjectRoot 'Tools/Validation/Capture-WeaponVisuals.ps1'
    $facadePath = Join-Path $State.ProjectRoot 'Assets/_Game/Editor/WeaponVisualCapture.cs'
    $redPath = Join-Path $State.ProjectRoot 'Tools/Tests/Fixtures/red-weapon-visual-capture.ps1.txt'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or -not (Test-Path -LiteralPath $facadePath -PathType Leaf) -or -not (Test-Path -LiteralPath $redPath -PathType Leaf)) { return New-HarnessFail 'weapon capture source/facade or red fixture missing' }
    $source = [IO.File]::ReadAllText($path)
    $facade = [IO.File]::ReadAllText($facadePath)
    $combined = $source + [Environment]::NewLine + $facade
    $red = [IO.File]::ReadAllText($redPath)
    $required = @(
        '-weaponCaptureEvidenceRoot', '-weaponCaptureAttemptId', '-weaponCaptureWeapon', '-weaponCaptureMode', '-weaponCaptureReferenceManifest',
        'Start-Process', '-WindowStyle Hidden', '-Wait', '-PassThru', 'Wait-ProjectRelease',
        'WeaponVisualManifest.json', 'WEAPON_VISUAL_CAPTURE_PASS', 'Assert-WeaponManifest',
        'SetLocalMode(true)', 'SetAlive(true)', 'SetShotgunOwned', 'MovementLabFastModeSession.EnterForCapture', 'MovementLabFastModeSession.RestoreIfActive',
        'RenderSettings.sun', 'Vector3.SignedAngle', 'Mathf.DeltaAngle', 'maskRowOrigin', 'differencePixelCount', 'differenceMeanAbsRgb',
        'DifferencePixelFloor', 'DifferenceChannelFloor', '1920', '1080', 'Git status changed during non-mutating weapon capture'
    )
    foreach ($needle in $required) {
        if ($combined.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { return New-HarnessFail ('weapon capture omitted contract: ' + $needle) }
        }
    $missing = @($required | Where-Object { $red.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
    if ($missing.Count -eq 0) { return New-HarnessFail 'weapon capture red fixture unexpectedly contains every guarded contract marker' }
    if ($combined.IndexOf('BrightArenaVisualCapture', [StringComparison]::Ordinal) -ge 0) { return New-HarnessFail 'weapon capture must remain independent of BrightArena capture' }
    return New-HarnessPass ('weapon CLI/render/pose/control/hash contract guarded; red fixture fails at ' + $missing[0])
}

function Test-WeaponVisualVerdictContract {
    param([Parameter(Mandatory = $true)]$State)
    $path = Join-Path $State.ProjectRoot 'Tools/Validation/Test-WeaponVisualVerdict.ps1'
    $redPath = Join-Path $State.ProjectRoot 'Tools/Tests/Fixtures/red-weapon-visual-verdict.ps1.txt'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or -not (Test-Path -LiteralPath $redPath -PathType Leaf)) { return New-HarnessFail 'weapon verdict source or red fixture missing' }
    $source = [IO.File]::ReadAllText($path)
    $red = [IO.File]::ReadAllText($redPath)
    $required = @(
        'ExpectedAgentId', 'ExpectedSourceSha', 'ReferenceManifestPath', 'captureManifestHashes', 'referenceHashes',
        'acceptedPriorVerdictHash', 'evidenceImages', 'overallPass', 'schemaVersion', 'sol_high', 'weapon-visual-verifier',
        'high.sunward.readable', 'high.crosslight.readable', 'high.awaylight.readable', 'surface-marks-fixed',
        'palette-warm-no-blue', 'scratches-physical', 'framing-silhouette', 'low-material-hierarchy',
        'predicates must contain exactly 16', 'Get-Hash', 'Write-ImmutableJson'
    )
    foreach ($needle in $required) {
        if ($source.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { return New-HarnessFail ('weapon verdict omitted contract: ' + $needle) }
    }
    $missing = @($required | Where-Object { $red.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
    if ($missing.Count -eq 0) { return New-HarnessFail 'weapon verdict red fixture unexpectedly contains every guarded contract marker' }
    return New-HarnessPass ('weapon verdict hash/predicate/reduction contract guarded; red fixture fails at ' + $missing[0])
}

function Test-ProductionBakeOutcomePreserved {
    param([Parameter(Mandatory = $true)]$State)

    $assertPreserved = {
        param([string]$Source)
        $ast = Get-HarnessAst $Source
        $outcomeAssignments = @($ast.FindAll({
            param($Node)
            $Node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                [string]$Node.Extent.Text -match '(?im)^\s*\$productionBakeRow\.status\s*=\s*\[string\]\$productionBakeOutcome\.status\s*$'
        }, $true))
        if ($outcomeAssignments.Count -ne 1) { return 'production bake outcome must be assigned exactly once' }

        $executedAssignments = @($ast.FindAll({
            param($Node)
            $Node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                [string]$Node.Extent.Text -match '(?im)^\s*\$row\.status\s*=\s*''executed''\s*$'
        }, $true))
        if ($executedAssignments.Count -ne 1) { return 'generic executed status assignment must occur exactly once' }

        $ancestor = $executedAssignments[0].Parent
        while ($null -ne $ancestor) {
            if ($ancestor -is [System.Management.Automation.Language.IfStatementAst] -and
                [string]$ancestor.Extent.Text -match '(?im)^\s*if\s*\(\s*\[string\]\$row\.check_id\s+-ne\s+''production-bake''\s*\)') {
                return $null
            }
            $ancestor = $ancestor.Parent
        }
        return 'generic executed status assignment does not exclude production-bake'
    }

    $failure = & $assertPreserved $State.CurrentSource
    if ($null -ne $failure) { return New-HarnessFail $failure }
    $expected = "if ([string]`$row.check_id -ne 'production-bake') { `$row.status = 'executed' }"
    $scratch = $State.CurrentSource.Replace($expected, "if (`$true) { `$row.status = 'executed' }")
    $scratchFailure = & $assertPreserved $scratch
    if ($null -eq $scratchFailure) { return New-HarnessFail 'production-bake overwrite scratch unexpectedly passed' }
    return New-HarnessPass 'production-bake reused/executed outcome survives generic ledger finalization'
}

function Test-GeneratedYamlComparatorAsyncPipeDrain {
    param([Parameter(Mandatory = $true)]$State)

    $assertAsyncDrain = {
        param([string]$Source)
        $function = Get-HarnessFunctionAst $Source 'Invoke-GitNullDelimitedCapture'
        $text = [string]$function.Extent.Text
        $required = @(
            '$outputTask = $process.StandardOutput.ReadToEndAsync()',
            '$errorTask = $process.StandardError.ReadToEndAsync()',
            '$timedOut = -not $process.WaitForExit(120000)',
            '$process.WaitForExit()',
            '$output = $outputTask.Result',
            '$errorOutput = $errorTask.Result'
        )
        foreach ($needle in $required) {
            if ($text.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { return 'missing required pipe-drain operation: ' + $needle }
        }
        $killIndex = $text.IndexOf('$process.Kill()', [StringComparison]::Ordinal)
        $waitIndex = $text.IndexOf('$process.WaitForExit()', [StringComparison]::Ordinal)
        $outputIndex = $text.IndexOf('$output = $outputTask.Result', [StringComparison]::Ordinal)
        $errorIndex = $text.IndexOf('$errorOutput = $errorTask.Result', [StringComparison]::Ordinal)
        if ($killIndex -lt 0 -or $killIndex -gt $waitIndex -or $waitIndex -gt $outputIndex -or $outputIndex -gt $errorIndex) {
            return 'timeout cleanup must kill, wait, then consume both asynchronous stream tasks'
        }
        return $null
    }

    $source = [IO.File]::ReadAllText($State.ComparatorPath)
    $failure = & $assertAsyncDrain $source
    if ($null -ne $failure) { return New-HarnessFail $failure }
    $scratch = $source.Replace('StandardOutput.ReadToEndAsync()', 'StandardOutput.ReadToEnd()')
    $scratchFailure = & $assertAsyncDrain $scratch
    if ($null -eq $scratchFailure) { return New-HarnessFail 'sequential stdout drain scratch unexpectedly passed' }
    return New-HarnessPass 'stdout/stderr drain asynchronously; timeout kills, waits, then consumes both tasks'
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
            'Read-ProbeContract|probe|name' {
                if (@($function.FindAll({ param($Node) $Node -is [System.Management.Automation.Language.AssignmentStatementAst] -and [string]$Node.Left.Extent.Text -ceq '$probe' -and $Node.Right.Extent.Text -match '(?i)ConvertFrom-Json' }, $true)).Count -eq 0) { return New-HarnessFail 'G1 probe provenance is not ConvertFrom-Json' }
            }
        }
    }
    if ($members.Count -ne $allow.Count -or $seen.Count -ne $allow.Count) { return New-HarnessFail ('G1 allowlist mismatch: expected ' + $allow.Count + ' exact sites, observed ' + $members.Count) }
    return New-HarnessPass ('G1 exact symbol/provenance sites=' + $members.Count)
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
    return New-HarnessPass 'alias/substitution trip G1'
}

function Test-HookSettings {
    param([Parameter(Mandatory = $true)]$State)
    if (-not (Test-Path -LiteralPath $State.HookSettingsPath -PathType Leaf)) {
        return New-HarnessFail 'missing .claude/settings.json'
    }
    try { $settings = Get-Content -Raw -LiteralPath $State.HookSettingsPath | ConvertFrom-Json }
    catch { return New-HarnessFail ('invalid hook JSON: ' + $_.Exception.Message) }
    if ($null -eq $settings.hooks) { return New-HarnessFail 'hook JSON has no hooks object' }

    # Presence check only. This case exists to catch the Unity/workflow gate being deleted, not
    # to pin the exact spelling of .claude/settings.json. Codex runs this same suite as its
    # manual pre-gate (AGENTS.md), so cosmetic hook-config edits must not block a non-Claude run.
    function Get-HookSettingsProperty {
        param([AllowNull()]$Object, [Parameter(Mandatory = $true)][string]$Name)
        if ($null -eq $Object) { return $null }
        $property = $Object.PSObject.Properties[$Name]
        if ($null -eq $property) { return $null }
        return $property.Value
    }

    function Get-HookInvocationText {
        param([Parameter(Mandatory = $true)]$Hook)
        $parts = New-Object System.Collections.Generic.List[string]
        foreach ($name in @('command', 'args')) {
            $value = Get-HookSettingsProperty $Hook $name
            if ($null -eq $value) { continue }
            if ($value -is [string]) { $parts.Add($value) | Out-Null }
            elseif ($value -is [System.Collections.IEnumerable]) {
                foreach ($item in $value) { if ($null -ne $item) { $parts.Add([string]$item) | Out-Null } }
            } else { $parts.Add([string]$value) | Out-Null }
        }
        return ($parts -join ' ')
    }

    $entries = @(Get-HookSettingsProperty $settings.hooks 'PreToolUse' | Where-Object { $null -ne $_ })
    if ($entries.Count -eq 0) { return New-HarnessFail 'no PreToolUse hook configured; Unity/workflow pre-gate is not wired up' }

    $preGatePath = Join-Path $State.ProjectRoot 'Tools/Tests/harness-pregate.mjs'
    if (-not (Test-Path -LiteralPath $preGatePath -PathType Leaf)) {
        return New-HarnessFail 'missing Tools/Tests/harness-pregate.mjs PreToolUse dispatcher'
    }
    $preGateSource = Get-Content -Raw -LiteralPath $preGatePath
    if ($preGateSource -notmatch '(?i)Invoke-HarnessTests\.ps1' -or $preGateSource -notmatch '(?i)-HookMode\s+PreToolUse') {
        return New-HarnessFail 'harness-pregate.mjs does not invoke Invoke-HarnessTests.ps1 -HookMode PreToolUse'
    }

    $covered = $false
    foreach ($entry in $entries) {
        if ($null -eq $entry) { continue }
        # An absent or empty matcher means "every tool" in Claude Code, which still covers the gate.
        $matcher = [string](Get-HookSettingsProperty $entry 'matcher')
        if (-not [string]::IsNullOrWhiteSpace($matcher)) {
            $coversTools = $false
            try { $coversTools = ('Bash' -match $matcher) -and ('PowerShell' -match $matcher) } catch { $coversTools = $false }
            if (-not $coversTools) { continue }
        }
        foreach ($hook in @(Get-HookSettingsProperty $entry 'hooks')) {
            if ($null -eq $hook) { continue }
            $invocation = Get-HookInvocationText $hook
            if ($invocation -notmatch '(?i)harness-pregate\.mjs') { continue }
            $covered = $true
            break
        }
        if ($covered) { break }
    }
    if (-not $covered) {
        return New-HarnessFail 'no PreToolUse hook covering Bash/PowerShell invokes harness-pregate.mjs which invokes Invoke-HarnessTests.ps1 -HookMode PreToolUse'
    }
    return New-HarnessPass 'PreToolUse harness gate present for Bash/PowerShell'
}
