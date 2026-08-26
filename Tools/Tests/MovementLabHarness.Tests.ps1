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
        'Start-Process', '-WindowStyle Hidden', '-Wait', '-PassThru', 'Wait-ProjectRelease', 'Acquire-ProjectLease', 'Release-ProjectLease',
        'New-ExclusiveWrapperDirectory', 'Get-CaptureSourceSnapshot', 'Get-IndexDigest', 'Get-ProductFileSnapshot', 'Get-ReferenceSnapshot',
        'wrapperDirectory', 'ExpectedSourceSha', 'sourceShaIsCleanHead', 'afterProductContentSha256', 'afterReferenceContentSha256', 'afterGeneratedManifestSha256',
        'WeaponVisualManifest.json', 'WEAPON_VISUAL_CAPTURE_PASS', 'Assert-WeaponManifest',
        'Assert-ExactJsonProperties', 'logicalId', 'copiedEvidencePath', 'byteLength', 'exactly three schema-1 references',
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
    $referenceParser = Get-HarnessFunctionAst $source 'Read-ReferenceManifest'
    foreach ($legacyField in @("'entries'", "'items'", "'id'", "'evidencePath'", "'bytes'")) {
        if ($referenceParser.Extent.Text.IndexOf($legacyField, [StringComparison]::Ordinal) -ge 0) {
            return New-HarnessFail ('reference parser retains legacy field alias: ' + $legacyField)
        }
    }

    # Static guards below keep the C# DTO and boundary wired to the canonical
    # schema. JsonUtility silently ignores unknown fields; executable negative
    # proof lives in the C# parser self-tests invoked by production capture.
    $dtoStart = $facade.IndexOf('private sealed class ReferenceManifestDto', [StringComparison]::Ordinal)
    $entryStart = $facade.IndexOf('private sealed class ReferenceEntry', $dtoStart + 1, [StringComparison]::Ordinal)
    $hashStart = $facade.IndexOf('private sealed class ReferenceHashDto', $entryStart + 1, [StringComparison]::Ordinal)
    if ($dtoStart -lt 0 -or $entryStart -le $dtoStart -or $hashStart -le $entryStart) {
        return New-HarnessFail 'C# reference DTO boundaries are missing or out of order'
    }
    $referenceDto = $facade.Substring($dtoStart, $entryStart - $dtoStart)
    $referenceEntry = $facade.Substring($entryStart, $hashStart - $entryStart)
    foreach ($field in @('public int schemaVersion;', 'public ReferenceEntry[] references;')) {
        if ($referenceDto.IndexOf($field, [StringComparison]::Ordinal) -lt 0) {
            return New-HarnessFail ('C# reference manifest DTO omitted canonical field: ' + $field)
        }
    }
    if (@([Regex]::Matches($referenceDto, '(?m)^\s*public\s+[^;]+;')).Count -ne 2) {
        return New-HarnessFail 'C# reference manifest DTO must expose exactly schemaVersion and references'
    }
    foreach ($field in @('public string logicalId;', 'public string originalPath;', 'public string copiedEvidencePath;', 'public long byteLength;', 'public string sha256;')) {
        if ($referenceEntry.IndexOf($field, [StringComparison]::Ordinal) -lt 0) {
            return New-HarnessFail ('C# reference entry DTO omitted canonical field: ' + $field)
        }
    }
    if (@([Regex]::Matches($referenceEntry, '(?m)^\s*public\s+[^;]+;')).Count -ne 5) {
        return New-HarnessFail 'C# reference entry DTO must expose exactly five canonical fields'
    }
    foreach ($legacyField in @(
        'public ReferenceEntry[] entries;', 'public ReferenceEntry[] items;',
        'public string id;', 'public string evidencePath;', 'public long bytes;'
    )) {
        if ($referenceDto.IndexOf($legacyField, [StringComparison]::Ordinal) -ge 0 -or
            $referenceEntry.IndexOf($legacyField, [StringComparison]::Ordinal) -ge 0) {
            return New-HarnessFail ('C# reference DTO retains legacy field alias: ' + $legacyField)
        }
    }
    $parserStart = $facade.IndexOf('private static ReferenceEvidence ReadAndValidateReferenceManifest', [StringComparison]::Ordinal)
    $resolverStart = $facade.IndexOf('private static string ResolveReferencePath', $parserStart + 1, [StringComparison]::Ordinal)
    if ($parserStart -lt 0 -or $resolverStart -le $parserStart) {
        return New-HarnessFail 'C# reference manifest parser boundary is missing or out of order'
    }
    $csharpParser = $facade.Substring($parserStart, $resolverStart - $parserStart)
    foreach ($field in @(
        'var entries = manifest.references;', 'entries.Length != ExpectedReferenceIds.Length',
        'ExpectedReferenceIds.Contains(entry.logicalId, StringComparer.Ordinal)', 'seen.Add(entry.logicalId)',
        'entry.byteLength <= 0', 'string.IsNullOrWhiteSpace(entry.originalPath)',
        'string.IsNullOrWhiteSpace(entry.copiedEvidencePath)', 'ResolveReferencePath(entry.copiedEvidencePath',
        'ValidateReferenceFile(copiedEvidencePath', 'new FileInfo(originalPath).Length != entry.byteLength'
    )) {
        if ($csharpParser.IndexOf($field, [StringComparison]::Ordinal) -lt 0) {
            return New-HarnessFail ('C# reference parser omitted canonical validation: ' + $field)
        }
    }
    foreach ($legacyField in @('manifest.entries', 'manifest.items', 'entry.id', 'entry.evidencePath', 'entry.bytes')) {
        if ($csharpParser.IndexOf($legacyField, [StringComparison]::Ordinal) -ge 0) {
            return New-HarnessFail ('C# reference parser retains legacy field alias: ' + $legacyField)
        }
    }
    if ($facade.IndexOf('referenceHashes = reference.Entries.Select(entry => new ReferenceHashDto { id = entry.logicalId, sha256 = entry.sha256 }).ToArray()', [StringComparison]::Ordinal) -lt 0) {
        return New-HarnessFail 'C# capture output no longer maps referenceHashes.id from logicalId'
    }
    foreach ($strictMarker in @(
        'private static void ValidateReferenceManifestStructure',
        'private static void RunReferenceManifestStructureSelfTests',
        'private static void AssertReferenceManifestStructureRejected',
        'new StrictReferenceManifestJsonReader(json).ReadManifest()',
        'mixed root alias', 'mixed entry alias', 'unknown property', 'duplicate property',
        'var prettyPrinted', 'ValidateReferenceManifestStructure(prettyPrinted)',
        'var reordered', 'ValidateReferenceManifestStructure(reordered)'
    )) {
        if ($facade.IndexOf($strictMarker, [StringComparison]::Ordinal) -lt 0) {
            return New-HarnessFail ('C# strict reference boundary omitted executable self-test marker: ' + $strictMarker)
        }
    }
    $readerStart = $facade.IndexOf('private sealed class StrictReferenceManifestJsonReader', [StringComparison]::Ordinal)
    if ($readerStart -lt 0) { return New-HarnessFail 'C# strict reference reader boundary is missing' }
    $reader = $facade.Substring($readerStart)
    foreach ($entryPoint in @(
        'private void ReadReferenceEntries()', 'private void ReadReferenceEntry()',
        'private void ReadValue()', 'private void ReadObjectValue()', 'private void ReadArrayValue()',
        'private string ReadString()', 'private void ReadNumber()', 'private void ReadLiteral(string literal)',
        'private void BeginContainer(char opening)'
    )) {
        $entryStart = $reader.IndexOf($entryPoint, [StringComparison]::Ordinal)
        if ($entryStart -lt 0) { return New-HarnessFail ('C# strict reference reader entry point is missing: ' + $entryPoint) }
        $nextMethod = $reader.IndexOf('private ', $entryStart + $entryPoint.Length, [StringComparison]::Ordinal)
        if ($nextMethod -lt 0) { $nextMethod = $reader.Length }
        $entryText = $reader.Substring($entryStart, $nextMethod - $entryStart)
        if ($entryText.IndexOf('SkipWhitespace();', [StringComparison]::Ordinal) -lt 0) {
            return New-HarnessFail ('C# strict reference reader entry point does not skip JSON whitespace: ' + $entryPoint)
        }
    }
    $strictCall = $facade.IndexOf('ValidateReferenceManifestStructure(manifestJson)', [StringComparison]::Ordinal)
    $jsonUtilityCall = $facade.IndexOf('JsonUtility.FromJson<ReferenceManifestDto>', [StringComparison]::Ordinal)
    if ($strictCall -lt 0 -or $jsonUtilityCall -lt 0 -or $strictCall -ge $jsonUtilityCall) {
        return New-HarnessFail 'C# strict reference structure validation must run before JsonUtility deserialization'
    }
    $workflowSource = [IO.File]::ReadAllText($State.WorkflowPath)
    $redWorkflowPath = Join-Path $State.ProjectRoot 'Tools/Tests/Fixtures/red-workflow.ps1.txt'
    $redWorkflowSource = [IO.File]::ReadAllText($redWorkflowPath)
    foreach ($leaseSource in @(
        [pscustomobject]@{ Name = 'capture'; Text = $source },
        [pscustomobject]@{ Name = 'workflow'; Text = $workflowSource }
    )) {
        $leaseAst = Get-HarnessFunctionAst $leaseSource.Text 'Acquire-ProjectLease'
        if ($leaseAst.Extent.Text -match '(?im)Remove-Item\s+-LiteralPath\s+\$script:LeasePath') {
            return New-HarnessFail ($leaseSource.Name + ' stale lease recovery still deletes the shared path')
        }
        if ($leaseAst.Extent.Text -notmatch '(?i)refusing automatic recovery') {
            return New-HarnessFail ($leaseSource.Name + ' stale lease recovery is not fail-closed')
        }
    }
    foreach ($redLease in @(
        [pscustomobject]@{ Name = 'capture'; Text = $red },
        [pscustomobject]@{ Name = 'workflow'; Text = $redWorkflowSource }
    )) {
        $redLeaseAst = Get-HarnessFunctionAst $redLease.Text 'Acquire-ProjectLease'
        if ($redLeaseAst.Extent.Text -notmatch '(?im)Remove-Item\s+-LiteralPath\s+\$script:LeasePath') {
            return New-HarnessFail ($redLease.Name + ' red lease fixture no longer retains unsafe stale deletion')
        }
    }
    return New-HarnessPass ('weapon CLI/render/pose/control/hash contract guarded; red fixture fails at ' + $missing[0])
}

function Write-HarnessJson {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)]$Value)
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 32) + [Environment]::NewLine), (New-Object Text.UTF8Encoding($false)))
}

function New-HarnessPngHeader {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][int]$Marker)
    $bytes = New-Object byte[] 24
    $bytes[0] = [byte]137; $bytes[1] = [byte]80; $bytes[2] = [byte]78; $bytes[3] = [byte]71
    $bytes[16] = [byte]0; $bytes[17] = [byte]0; $bytes[18] = [byte]7; $bytes[19] = [byte]128
    $bytes[20] = [byte]0; $bytes[21] = [byte]0; $bytes[22] = [byte]4; $bytes[23] = [byte]56
    $bytes[4] = [byte]($Marker -band 0xff)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function Get-HarnessStringSha256 {
    param([Parameter(Mandatory = $true)][string]$Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value)))).Replace('-', '').ToLowerInvariant()
    } finally { $sha.Dispose() }
}

function Test-ProjectLeaseContentionBehavior {
    param([Parameter(Mandatory = $true)]$State)
    $root = Join-Path $script:HarnessScratchRoot ('project-lease-contention-' + [Guid]::NewGuid().ToString('N'))
    $shimPath = Join-Path $State.ProjectRoot 'Tools\Tests\HarnessShim.psm1'
    $probeScript = Join-Path $root 'lease-contender.ps1'
    $probeSource = @'
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$ShimPath,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$GitCommonPath,
    [Parameter(Mandatory = $true)][ValidateSet('Capture', 'Workflow')][string]$LeaseOwner
)

$ErrorActionPreference = 'Stop'
Import-Module -Name $ShimPath -Force
$functionNames = @('Acquire-ProjectLease', 'Read-LeaseRecord', 'Get-CanonicalPath', 'Get-FullPath', 'Get-StringSha256', 'Get-CurrentProcessStartUtc')
if ($LeaseOwner -eq 'Capture') { $functionNames += 'Assert-LeasePath' }
$module = Import-HarnessFunctions -Source $Source -FunctionNames $functionNames
& $module {
    param([string]$Project, [string]$GitCommon)
    $script:ProjectRoot = $Project
    $script:GitCommonRoot = $GitCommon
    try {
        Acquire-ProjectLease | Out-Null
        Write-Output 'ACQUIRED'
        $script:ProbeExitCode = 11
    } catch {
        Write-Output ('REJECTED ' + $_.Exception.Message)
        $script:ProbeExitCode = 0
    }
} $ProjectPath $GitCommonPath
exit [int](& $module { return $script:ProbeExitCode })
'@
    try {
        New-Item -ItemType Directory -Force -Path $root | Out-Null
        [IO.File]::WriteAllText($probeScript, $probeSource, (New-Object Text.UTF8Encoding($false)))
        $processStartUtc = (Get-Process -Id $PID -ErrorAction Stop).StartTime.ToUniversalTime().ToString('O')
        foreach ($sourceInfo in @(
            [pscustomobject]@{ Name = 'Capture'; Path = $State.CapturePath },
            [pscustomobject]@{ Name = 'Workflow'; Path = $State.WorkflowPath }
        )) {
            $caseRoot = Join-Path $root $sourceInfo.Name
            $leaseRoot = Join-Path $caseRoot 'common\movement-lab-proof\leases'
            New-Item -ItemType Directory -Force -Path $leaseRoot | Out-Null
            $canonicalProject = [IO.Path]::GetFullPath((Join-Path $caseRoot 'project')).TrimEnd('\')
            New-Item -ItemType Directory -Force -Path $canonicalProject | Out-Null
            $leaseName = Get-HarnessStringSha256 $canonicalProject
            $leasePath = Join-Path $leaseRoot ($leaseName + '.lease')
            $staleRecord = [ordered]@{
                schemaVersion = 1
                leaseToken = 'a' * 32
                canonicalProjectRoot = $canonicalProject
                ownerPid = [int]$PID
                ownerProcessStartUtc = '2000-01-01T00:00:00.0000000Z'
                acquiredUtc = '2000-01-01T00:00:00.0000000Z'
            }
            $staleJson = (($staleRecord | ConvertTo-Json -Depth 8) + "`n")
            [IO.File]::WriteAllText($leasePath, $staleJson, (New-Object Text.UTF8Encoding($false)))
            $staleHash = (Get-FileHash -LiteralPath $leasePath -Algorithm SHA256).Hash.ToLowerInvariant()

            $contenders = New-Object System.Collections.Generic.List[object]
            for ($index = 0; $index -lt 2; $index++) {
                $stdout = Join-Path $caseRoot ('stale-' + $index + '.stdout.log')
                $stderr = Join-Path $caseRoot ('stale-' + $index + '.stderr.log')
                $args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $probeScript, '-Source', $sourceInfo.Path, '-ShimPath', $shimPath, '-ProjectPath', $canonicalProject, '-GitCommonPath', (Join-Path $caseRoot 'common'), '-LeaseOwner', $sourceInfo.Name)
                $contenders.Add((Start-Process -FilePath 'powershell.exe' -ArgumentList $args -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru)) | Out-Null
            }
            foreach ($contender in @($contenders.ToArray())) { $contender.WaitForExit(); $contender.Refresh() }
            $staleOutput = New-Object System.Collections.Generic.List[string]
            for ($index = 0; $index -lt 2; $index++) {
                $stdoutPath = Join-Path $caseRoot ('stale-' + $index + '.stdout.log')
                $stderrPath = Join-Path $caseRoot ('stale-' + $index + '.stderr.log')
                if (Test-Path -LiteralPath $stdoutPath) { $staleOutput.Add((Get-Content -Raw -LiteralPath $stdoutPath)) | Out-Null }
                if (Test-Path -LiteralPath $stderrPath) { $staleOutput.Add((Get-Content -Raw -LiteralPath $stderrPath)) | Out-Null }
            }
            $staleOutputText = $staleOutput -join "`n"
            if ($staleOutputText -notmatch '(?i)REJECTED .*stale.*refusing automatic recovery') {
                return New-HarnessFail ($sourceInfo.Name + ' stale contenders did not fail closed: ' + $staleOutputText)
            }
            if (-not (Test-Path -LiteralPath $leasePath -PathType Leaf) -or (Get-FileHash -LiteralPath $leasePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $staleHash) {
                return New-HarnessFail ($sourceInfo.Name + ' stale contender changed the lease record')
            }

            $liveRecord = [ordered]@{
                schemaVersion = 1
                leaseToken = 'b' * 32
                canonicalProjectRoot = $canonicalProject
                ownerPid = [int]$PID
                ownerProcessStartUtc = $processStartUtc
                acquiredUtc = [DateTime]::UtcNow.ToString('O')
            }
            [IO.File]::WriteAllText($leasePath, (($liveRecord | ConvertTo-Json -Depth 8) + "`n"), (New-Object Text.UTF8Encoding($false)))
            $liveHash = (Get-FileHash -LiteralPath $leasePath -Algorithm SHA256).Hash.ToLowerInvariant()
            $liveStdout = Join-Path $caseRoot 'live.stdout.log'
            $liveStderr = Join-Path $caseRoot 'live.stderr.log'
            $liveArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $probeScript, '-Source', $sourceInfo.Path, '-ShimPath', $shimPath, '-ProjectPath', $canonicalProject, '-GitCommonPath', (Join-Path $caseRoot 'common'), '-LeaseOwner', $sourceInfo.Name)
            $live = Start-Process -FilePath 'powershell.exe' -ArgumentList $liveArgs -WindowStyle Hidden -RedirectStandardOutput $liveStdout -RedirectStandardError $liveStderr -Wait -PassThru
            $liveOutput = if (Test-Path -LiteralPath $liveStdout) { Get-Content -Raw -LiteralPath $liveStdout } else { '' }
            if ($liveOutput -notmatch '(?i)REJECTED .*live PID') {
                $liveError = if (Test-Path -LiteralPath $liveStderr) { Get-Content -Raw -LiteralPath $liveStderr } else { '' }
                return New-HarnessFail ($sourceInfo.Name + ' live contender did not remain token-bound: ' + $liveOutput + ' ' + $liveError)
            }
            if (-not (Test-Path -LiteralPath $leasePath -PathType Leaf) -or (Get-FileHash -LiteralPath $leasePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $liveHash) {
                return New-HarnessFail ($sourceInfo.Name + ' live contender changed another owner lease')
            }
        }
        return New-HarnessPass 'Capture and workflow stale contenders fail closed; live lease remains owner-bound'
    } catch {
        return New-HarnessFail ('project lease contention behavior failed: ' + $_.Exception.Message)
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-WeaponCaptureBehavior {
    param([Parameter(Mandatory = $true)]$State)
    $root = Join-Path $script:HarnessScratchRoot ('weapon-capture-behavior-' + [Guid]::NewGuid().ToString('N'))
    $module = $null
    try {
        $captureDirectory = Join-Path $root 'capture'
        New-Item -ItemType Directory -Force -Path $captureDirectory | Out-Null
        $sourceSha = 'a' * 40
        $generatedSha = 'b' * 64
        $referencePath = Join-Path $root 'reference.json'
        $referenceEntries = New-Object System.Collections.Generic.List[object]
        foreach ($logicalId in @('game-bright', 'game-dark', 'quake-hires')) {
            $originalPath = Join-Path $root ($logicalId + '-original.bin')
            $copiedEvidencePath = Join-Path $root ($logicalId + '-evidence.bin')
            [IO.File]::WriteAllBytes($originalPath, [byte[]](1, 2, 3, 4))
            [IO.File]::WriteAllBytes($copiedEvidencePath, [byte[]](1, 2, 3, 4))
            $hash = (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash.ToLowerInvariant()
            $referenceEntries.Add([ordered]@{ logicalId = $logicalId; originalPath = $originalPath; copiedEvidencePath = $copiedEvidencePath; byteLength = 4; sha256 = $hash }) | Out-Null
        }
        Write-HarnessJson $referencePath ([ordered]@{ schemaVersion = 1; references = @($referenceEntries.ToArray()) })

        $module = & $State.ShimCommand $State.CapturePath @(
            'Assert-WeaponManifest', 'Read-PngDimensions', 'Assert-NumericClose', 'Assert-Vector', 'Get-Property', 'Get-Hash',
            'Read-ReferenceManifest', 'Assert-ExactJsonProperties', 'Resolve-ReferencePath', 'Get-FullPath'
        ) @()
        & $module { param($project) $script:ProjectRoot = $project } $root | Out-Null
        $parsedReference = Invoke-HarnessModuleFunction $module 'Read-ReferenceManifest' @{ Path = $referencePath }
        if ($parsedReference.entries.Count -ne 3) { return New-HarnessFail ('canonical reference manifest returned ' + $parsedReference.entries.Count + ' entries') }
        foreach ($entry in @($parsedReference.entries)) {
            foreach ($field in @('logicalId', 'originalPath', 'copiedEvidencePath', 'byteLength', 'sha256')) {
                if ($null -eq (Get-HarnessField $entry $field)) { return New-HarnessFail ('canonical reference record omitted ' + $field) }
            }
            foreach ($legacyField in @('id', 'evidencePath', 'bytes')) {
                if ($null -ne (Get-HarnessField $entry $legacyField)) { return New-HarnessFail ('canonical reference record retained ' + $legacyField) }
            }
        }
        $reference = $parsedReference

        $legacyEntries = New-Object System.Collections.Generic.List[object]
        foreach ($entry in @($referenceEntries.ToArray())) {
            $legacyEntries.Add([ordered]@{
                    id = [string]$entry.logicalId
                    originalPath = [string]$entry.originalPath
                    evidencePath = [string]$entry.copiedEvidencePath
                    bytes = [int64]$entry.byteLength
                    sha256 = [string]$entry.sha256
                }) | Out-Null
        }
        $legacyPath = Join-Path $root 'reference-legacy.json'
        Write-HarnessJson $legacyPath ([ordered]@{ schemaVersion = 1; entries = @($legacyEntries.ToArray()) })
        $legacyRejected = $false
        try { Invoke-HarnessModuleFunction $module 'Read-ReferenceManifest' @{ Path = $legacyPath } | Out-Null } catch { $legacyRejected = $true }
        if (-not $legacyRejected) { return New-HarnessFail 'legacy reference manifest fields unexpectedly passed canonical parser' }

        $wrongFieldEntries = New-Object System.Collections.Generic.List[object]
        foreach ($entry in @($referenceEntries.ToArray())) {
            $wrongFieldEntries.Add([ordered]@{
                    logicalId = [string]$entry.logicalId
                    originalPath = [string]$entry.originalPath
                    evidencePath = [string]$entry.copiedEvidencePath
                    byteLength = [int64]$entry.byteLength
                    sha256 = [string]$entry.sha256
                }) | Out-Null
        }
        $wrongFieldPath = Join-Path $root 'reference-wrong-field.json'
        Write-HarnessJson $wrongFieldPath ([ordered]@{ schemaVersion = 1; references = @($wrongFieldEntries.ToArray()) })
        $wrongFieldRejected = $false
        try { Invoke-HarnessModuleFunction $module 'Read-ReferenceManifest' @{ Path = $wrongFieldPath } | Out-Null } catch { $wrongFieldRejected = $true }
        if (-not $wrongFieldRejected) { return New-HarnessFail 'wrong reference field alias unexpectedly passed canonical parser' }

        $wrongLengthEntries = New-Object System.Collections.Generic.List[object]
        foreach ($entry in @($referenceEntries.ToArray())) {
            $length = if ([string]$entry.logicalId -ceq 'game-bright') { 5 } else { [int64]$entry.byteLength }
            $wrongLengthEntries.Add([ordered]@{
                    logicalId = [string]$entry.logicalId
                    originalPath = [string]$entry.originalPath
                    copiedEvidencePath = [string]$entry.copiedEvidencePath
                    byteLength = $length
                    sha256 = [string]$entry.sha256
                }) | Out-Null
        }
        $wrongLengthPath = Join-Path $root 'reference-wrong-length.json'
        Write-HarnessJson $wrongLengthPath ([ordered]@{ schemaVersion = 1; references = @($wrongLengthEntries.ToArray()) })
        $wrongLengthRejected = $false
        try { Invoke-HarnessModuleFunction $module 'Read-ReferenceManifest' @{ Path = $wrongLengthPath } | Out-Null } catch { $wrongLengthRejected = $true }
        if (-not $wrongLengthRejected) { return New-HarnessFail 'reference byteLength mismatch unexpectedly passed canonical parser' }

        $names = @('rocket-sunward-high.png', 'rocket-sunward-low.png', 'rocket-crosslight-high.png', 'rocket-crosslight-low.png', 'rocket-awaylight-high.png', 'rocket-awaylight-low.png')
        $images = New-Object System.Collections.Generic.List[object]
        for ($index = 0; $index -lt $names.Count; $index++) {
            $imagePath = Join-Path $captureDirectory $names[$index]
            New-HarnessPngHeader $imagePath ($index + 1)
            $quality = if (($index % 2) -eq 0) { 'High' } else { 'Low' }
            $angle = if ($index -lt 2) { 0 } elseif ($index -lt 4) { 90 } else { 180 }
            $imageHash = (Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash.ToLowerInvariant()
            $images.Add([ordered]@{
                    filename = $names[$index]; path = $imagePath; sha256 = $imageHash; width = 1920; height = 1080; visualMode = 'Fast'; qualityLevel = $quality; fastSessionApplied = $true
                    targetAngle = $angle; angleDelta = 0; fieldOfView = 75; playerPosition = [ordered]@{ x = 0; y = 0; z = 0 }; cameraLocalEulerAngles = [ordered]@{ x = 8; y = 0; z = 0 }
                    maskOrigin = 'top-left'; maskRowOrigin = 'bottom-left'; maskXMin = 64; maskXMax = 1855; maskYMin = 540; maskYMax = 1079; maskRowMin = 0; maskRowMax = 539
                    differencePixelCount = 10001; differenceMeanAbsRgb = 0.02; differencePixelThreshold = 10000; differenceChannelThreshold = 8; controlRendered = $true; pass = $true
                }) | Out-Null
        }
        $manifestPath = Join-Path $captureDirectory 'WeaponVisualManifest.json'
        $manifest = [ordered]@{
            schemaVersion = 1; attemptId = 'behavior-attempt'; weaponCaptureAttemptId = 'behavior-attempt'; weapon = 'Rocket'; weaponCaptureWeapon = 'Rocket'; weaponCaptureMode = 'Fast'
            sourceSha = $sourceSha; generatedManifestSha256 = $generatedSha; referenceManifestPath = $reference.path; referenceManifestSha256 = $reference.sha256
            referenceHashes = @($reference.entries | ForEach-Object { [ordered]@{ id = $_.logicalId; sha256 = $_.sha256 } }); images = @($images.ToArray()); pass = $true
        }
        Write-HarnessJson $manifestPath $manifest

        & $module {
            param($attempt, $weapon, $mode, $directory)
            $script:AttemptId = $attempt; $script:Weapon = $weapon; $script:Mode = $mode; $script:EvidenceDirectory = $directory
        } 'behavior-attempt' 'Rocket' 'Fast' $captureDirectory | Out-Null
        $records = Invoke-HarnessModuleFunction $module 'Assert-WeaponManifest' @{ Path = $manifestPath; Reference = $reference; ExpectedSha = $sourceSha; ExpectedGeneratedManifestSha = $generatedSha }
        if ($records.Count -ne 6) { return New-HarnessFail ('green capture validator returned ' + $records.Count + ' records') }

        $invalid = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
        $invalid.images[0].differencePixelCount = 0
        $invalidPath = Join-Path $captureDirectory 'WeaponVisualManifest.invalid.json'
        Write-HarnessJson $invalidPath $invalid
        $rejected = $false
        try { Invoke-HarnessModuleFunction $module 'Assert-WeaponManifest' @{ Path = $invalidPath; Reference = $reference; ExpectedSha = $sourceSha; ExpectedGeneratedManifestSha = $generatedSha } | Out-Null } catch { $rejected = $true }
        if (-not $rejected) { return New-HarnessFail 'invalid capture manifest unexpectedly passed behavioral validator' }
        $leaseResult = Test-ProjectLeaseContentionBehavior $State
        if (-not [bool](Get-HarnessField $leaseResult 'pass')) {
            return New-HarnessFail ('project lease contention contract failed: ' + [string](Get-HarnessField $leaseResult 'message'))
        }
        return New-HarnessPass 'green capture manifest accepted; invalid visibility threshold rejected; lease contention fails closed'
    } catch {
        return New-HarnessFail ('capture behavioral validation failed: ' + $_.Exception.Message)
    } finally {
        if ($null -ne $module) { Remove-Module -ModuleInfo $module -Force -ErrorAction SilentlyContinue }
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
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
        'predicates must contain exactly 16', 'Get-Hash', 'Write-ImmutableJson',
        'ExpectedWeapon', 'ExpectedView', 'ExpectedQuality', 'Get-PredicateImageRequirement',
        'qualityLevel', 'view', ' capture images', 'weapon mismatch'
    )
    foreach ($needle in $required) {
        if ($source.IndexOf($needle, [StringComparison]::Ordinal) -lt 0) { return New-HarnessFail ('weapon verdict omitted contract: ' + $needle) }
    }
    $missing = @($required | Where-Object { $red.IndexOf($_, [StringComparison]::Ordinal) -lt 0 })
    if ($missing.Count -eq 0) { return New-HarnessFail 'weapon verdict red fixture unexpectedly contains every guarded contract marker' }
    $resolver = Get-HarnessFunctionAst $source 'Get-EvidenceImageRecord'
    if ($resolver.Extent.Text -match '(?im)foreach\s*\(\s*\$capture\s+in\s+@\(\s*\$CaptureSet\.Rocket\s*,\s*\$CaptureSet\.Shotgun\s*\)\s*\)') {
        return New-HarnessFail 'weapon verdict resolver still searches both capture sets'
    }
    $redResolver = Get-HarnessFunctionAst $red 'Get-EvidenceImageRecord'
    if ($redResolver.Extent.Text -notmatch '(?im)foreach\s*\(\s*\$capture\s+in\s+@\(\s*\$CaptureSet\.Rocket\s*,\s*\$CaptureSet\.Shotgun\s*\)\s*\)') {
        return New-HarnessFail 'weapon verdict red fixture no longer retains cross-weapon resolver'
    }
    return New-HarnessPass ('weapon verdict hash/predicate/reduction contract guarded; red fixture fails at ' + $missing[0])
}

function Invoke-HarnessPowerShell {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& powershell -NoProfile -ExecutionPolicy Bypass @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    return [pscustomobject]@{ exitCode = $exitCode; output = @($output) }
}

function Invoke-HarnessFixture {
    param(
        [Parameter(Mandatory = $true)][string]$FixturePath,
        [Parameter(Mandatory = $true)][string]$ScratchRoot,
        [string[]]$Arguments = @()
    )
    if (-not (Test-Path -LiteralPath $FixturePath -PathType Leaf)) { throw ('Fixture missing: ' + $FixturePath) }
    $temporaryPath = Join-Path $ScratchRoot ('fixture-' + [Guid]::NewGuid().ToString('N') + '.ps1')
    try {
        # Checked-in red fixtures use .ps1.txt so they cannot be mistaken for
        # runnable product scripts. Execute an exact copied source with a
        # temporary .ps1 extension so PowerShell runs the fixture body.
        Copy-Item -LiteralPath $FixturePath -Destination $temporaryPath -Force
        return Invoke-HarnessPowerShell (@('-File', $temporaryPath) + @($Arguments))
    } finally {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

function Test-WeaponVisualVerdictBehavior {
    param([Parameter(Mandatory = $true)]$State)
    $root = Join-Path $script:HarnessScratchRoot ('weapon-verdict-behavior-' + [Guid]::NewGuid().ToString('N'))
    try {
        $sourceSha = 'a' * 40
        $referenceEntries = New-Object System.Collections.Generic.List[object]
        foreach ($id in @('game-bright', 'game-dark', 'quake-hires')) {
            $originalPath = Join-Path $root ($id + '-original.bin')
            $evidencePath = Join-Path $root ($id + '-evidence.bin')
            New-Item -ItemType Directory -Force -Path $root | Out-Null
            [IO.File]::WriteAllBytes($originalPath, [byte[]](5, 6, 7, 8))
            [IO.File]::WriteAllBytes($evidencePath, [byte[]](5, 6, 7, 8))
            $hash = (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash.ToLowerInvariant()
            $referenceEntries.Add([ordered]@{ id = $id; originalPath = $originalPath; evidencePath = $evidencePath; bytes = 4; sha256 = $hash }) | Out-Null
        }
        $referencePath = Join-Path $root 'ReferenceManifest.json'
        Write-HarnessJson $referencePath ([ordered]@{ schemaVersion = 1; entries = @($referenceEntries.ToArray()) })
        $captureManifestPaths = New-Object System.Collections.Generic.List[string]
        $captureManifestHashes = [ordered]@{}
        $captureImageRecords = [ordered]@{}
        foreach ($weapon in @('Rocket', 'Shotgun')) {
            $captureDirectory = Join-Path $root $weapon
            New-Item -ItemType Directory -Force -Path $captureDirectory | Out-Null
            $items = New-Object System.Collections.Generic.List[object]
            for ($index = 0; $index -lt 6; $index++) {
                $filename = $weapon.ToLowerInvariant() + '-' + $index + '.png'
                $imagePath = Join-Path $captureDirectory $filename
                [IO.File]::WriteAllBytes($imagePath, [byte[]]([int](10 + $index), [int](20 + $index), [int](30 + $index)))
                $hash = (Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash.ToLowerInvariant()
                $view = if ($index -lt 2) { 'sunward' } elseif ($index -lt 4) { 'crosslight' } else { 'awaylight' }
                $qualityLevel = if (($index % 2) -eq 0) { 'High' } else { 'Low' }
                $items.Add([ordered]@{ filename = $filename; path = $imagePath; sha256 = $hash; view = $view; qualityLevel = $qualityLevel }) | Out-Null
            }
            $captureImageRecords[$weapon] = [pscustomobject]@{ images = @($items.ToArray()) }
            $manifestPath = Join-Path $captureDirectory 'WeaponVisualManifest.json'
            Write-HarnessJson $manifestPath ([ordered]@{ schemaVersion = 1; weaponCaptureWeapon = $weapon; sourceSha = $sourceSha; images = @($items.ToArray()) })
            $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
            $captureManifestPaths.Add($manifestPath) | Out-Null
            $captureManifestHashes[$weapon] = $manifestHash
        }
        $predicates = New-Object System.Collections.Generic.List[object]
        foreach ($weapon in @('Rocket', 'Shotgun')) {
            foreach ($id in @('high.sunward.readable', 'high.crosslight.readable', 'high.awaylight.readable', 'surface-marks-fixed', 'palette-warm-no-blue', 'scratches-physical', 'framing-silhouette', 'low-material-hierarchy')) {
                $imageIndex = switch ($id) {
                    'high.sunward.readable' { 0 }
                    'high.crosslight.readable' { 2 }
                    'high.awaylight.readable' { 4 }
                    'low-material-hierarchy' { 1 }
                    default { 0 }
                }
                $imagePath = [string]$captureImageRecords[$weapon].images[$imageIndex].path
                $imageHash = (Get-FileHash -LiteralPath $imagePath -Algorithm SHA256).Hash.ToLowerInvariant()
                $predicates.Add([ordered]@{ weapon = $weapon; id = $id; pass = $true; evidenceImages = @([ordered]@{ path = $imagePath; sha256 = $imageHash }) }) | Out-Null
            }
        }
        $referenceHashRecords = @($referenceEntries.ToArray() | ForEach-Object { [ordered]@{ id = $_.id; sha256 = $_.sha256 } })
        $verdictPath = Join-Path $root 'WeaponVisualVerdict.json'
        $verdict = [ordered]@{
            schemaVersion = 1; agentId = 'behavior-agent'; profile = 'sol_high'; role = 'weapon-visual-verifier'; sourceSha = $sourceSha
            referenceHashes = $referenceHashRecords; captureManifestHashes = @([ordered]@{ weapon = 'Rocket'; sha256 = $captureManifestHashes['Rocket'] }, [ordered]@{ weapon = 'Shotgun'; sha256 = $captureManifestHashes['Shotgun'] })
            acceptedPriorVerdictHash = ''; predicates = @($predicates.ToArray()); failures = @(); overallPass = $true
        }
        Write-HarnessJson $verdictPath $verdict
        $validator = Join-Path $State.ProjectRoot 'Tools/Validation/Test-WeaponVisualVerdict.ps1'
        $greenResultPath = Join-Path $root 'green-result.json'
        $green = Invoke-HarnessPowerShell @('-File', $validator, '-VerdictPath', $verdictPath, '-ExpectedAgentId', 'behavior-agent', '-ExpectedSourceSha', $sourceSha, '-ReferenceManifestPath', $referencePath, '-CaptureManifestPaths', $captureManifestPaths[0], $captureManifestPaths[1], '-ResultPath', $greenResultPath)
        if ($green.exitCode -ne 0) { return New-HarnessFail ('green verdict validator exited ' + $green.exitCode + ': ' + (($green.output | ForEach-Object { [string]$_ }) -join ' | ')) }
        if (-not (Test-Path -LiteralPath $greenResultPath -PathType Leaf)) { return New-HarnessFail 'green verdict validator did not write result evidence' }

        $crossWeaponVerdict = Get-Content -Raw -LiteralPath $verdictPath | ConvertFrom-Json
        $crossWeaponPredicate = @($crossWeaponVerdict.predicates | Where-Object { $_.weapon -eq 'Rocket' -and $_.id -eq 'high.sunward.readable' })[0]
        $crossWeaponImagePath = [string]$captureImageRecords['Shotgun'].images[0].path
        $crossWeaponPredicate.evidenceImages[0].path = $crossWeaponImagePath
        $crossWeaponPredicate.evidenceImages[0].sha256 = (Get-FileHash -LiteralPath $crossWeaponImagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $crossWeaponPath = Join-Path $root 'WeaponVisualVerdict.cross-weapon.json'
        Write-HarnessJson $crossWeaponPath $crossWeaponVerdict
        $crossWeapon = Invoke-HarnessPowerShell @('-File', $validator, '-VerdictPath', $crossWeaponPath, '-ExpectedAgentId', 'behavior-agent', '-ExpectedSourceSha', $sourceSha, '-ReferenceManifestPath', $referencePath, '-CaptureManifestPaths', $captureManifestPaths[0], $captureManifestPaths[1], '-ResultPath', (Join-Path $root 'cross-weapon-result.json'))
        if ($crossWeapon.exitCode -eq 0) { return New-HarnessFail 'cross-weapon evidence unexpectedly passed behavioral validator' }

        $wrongOrientationVerdict = Get-Content -Raw -LiteralPath $verdictPath | ConvertFrom-Json
        $wrongOrientationPredicate = @($wrongOrientationVerdict.predicates | Where-Object { $_.weapon -eq 'Rocket' -and $_.id -eq 'high.crosslight.readable' })[0]
        $wrongOrientationImagePath = [string]$captureImageRecords['Rocket'].images[0].path
        $wrongOrientationPredicate.evidenceImages[0].path = $wrongOrientationImagePath
        $wrongOrientationPredicate.evidenceImages[0].sha256 = (Get-FileHash -LiteralPath $wrongOrientationImagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $wrongOrientationPath = Join-Path $root 'WeaponVisualVerdict.wrong-orientation.json'
        Write-HarnessJson $wrongOrientationPath $wrongOrientationVerdict
        $wrongOrientation = Invoke-HarnessPowerShell @('-File', $validator, '-VerdictPath', $wrongOrientationPath, '-ExpectedAgentId', 'behavior-agent', '-ExpectedSourceSha', $sourceSha, '-ReferenceManifestPath', $referencePath, '-CaptureManifestPaths', $captureManifestPaths[0], $captureManifestPaths[1], '-ResultPath', (Join-Path $root 'wrong-orientation-result.json'))
        if ($wrongOrientation.exitCode -eq 0) { return New-HarnessFail 'wrong-orientation evidence unexpectedly passed behavioral validator' }

        $wrongQualityVerdict = Get-Content -Raw -LiteralPath $verdictPath | ConvertFrom-Json
        $wrongQualityPredicate = @($wrongQualityVerdict.predicates | Where-Object { $_.weapon -eq 'Rocket' -and $_.id -eq 'low-material-hierarchy' })[0]
        $wrongQualityImagePath = [string]$captureImageRecords['Rocket'].images[0].path
        $wrongQualityPredicate.evidenceImages[0].path = $wrongQualityImagePath
        $wrongQualityPredicate.evidenceImages[0].sha256 = (Get-FileHash -LiteralPath $wrongQualityImagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $wrongQualityPath = Join-Path $root 'WeaponVisualVerdict.wrong-quality.json'
        Write-HarnessJson $wrongQualityPath $wrongQualityVerdict
        $wrongQuality = Invoke-HarnessPowerShell @('-File', $validator, '-VerdictPath', $wrongQualityPath, '-ExpectedAgentId', 'behavior-agent', '-ExpectedSourceSha', $sourceSha, '-ReferenceManifestPath', $referencePath, '-CaptureManifestPaths', $captureManifestPaths[0], $captureManifestPaths[1], '-ResultPath', (Join-Path $root 'wrong-quality-result.json'))
        if ($wrongQuality.exitCode -eq 0) { return New-HarnessFail 'wrong-quality evidence unexpectedly passed behavioral validator' }

        $invalidVerdict = Get-Content -Raw -LiteralPath $verdictPath | ConvertFrom-Json
        $invalidVerdict.overallPass = $false
        $invalidPath = Join-Path $root 'WeaponVisualVerdict.invalid.json'
        Write-HarnessJson $invalidPath $invalidVerdict
        $red = Invoke-HarnessPowerShell @('-File', $validator, '-VerdictPath', $invalidPath, '-ExpectedAgentId', 'behavior-agent', '-ExpectedSourceSha', $sourceSha, '-ReferenceManifestPath', $referencePath, '-CaptureManifestPaths', $captureManifestPaths[0], $captureManifestPaths[1], '-ResultPath', (Join-Path $root 'invalid-result.json'))
        if ($red.exitCode -eq 0) { return New-HarnessFail 'invalid verdict unexpectedly passed behavioral validator' }
        return New-HarnessPass 'green verdict accepted and invalid overallPass was rejected'
    } catch {
        return New-HarnessFail ('verdict behavioral validation failed: ' + $_.Exception.Message)
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Test-RedFixturesBehavior {
    param([Parameter(Mandatory = $true)]$State)
    $root = Join-Path $script:HarnessScratchRoot ('red-fixture-behavior-' + [Guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Force -Path $root | Out-Null
        $workflow = Invoke-HarnessFixture $State.RedSource $root
        if ($workflow.exitCode -eq 0) { return New-HarnessFail 'red workflow fixture unexpectedly succeeded when executed' }
        $capturePath = Join-Path $State.ProjectRoot 'Tools/Tests/Fixtures/red-weapon-visual-capture.ps1.txt'
        $capture = Invoke-HarnessFixture $capturePath $root @('-EvidenceRoot', $root, '-AttemptId', 'red')
        if ($capture.exitCode -eq 0) { return New-HarnessFail 'red capture fixture unexpectedly succeeded when executed' }
        $invalidVerdictPath = Join-Path $root 'invalid-verdict.json'
        Write-HarnessJson $invalidVerdictPath ([ordered]@{ overallPass = $false })
        $verdictPath = Join-Path $State.ProjectRoot 'Tools/Tests/Fixtures/red-weapon-visual-verdict.ps1.txt'
        $verdict = Invoke-HarnessFixture $verdictPath $root @('-VerdictPath', $invalidVerdictPath)
        if ($verdict.exitCode -eq 0) { return New-HarnessFail 'red verdict fixture unexpectedly succeeded when executed' }
        return New-HarnessPass 'workflow, capture, and verdict red fixtures all fail when executed'
    } catch {
        return New-HarnessFail ('red fixture behavioral check failed: ' + $_.Exception.Message)
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
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
