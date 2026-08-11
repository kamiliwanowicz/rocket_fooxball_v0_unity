Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
        $script:RequestedInventoryPaths = @('Assets/_Game/Generated/fixture.json')
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
        return New-HarnessPass 'digest discriminator'
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
            $null = Invoke-HarnessModuleFunction $module 'Get-ObjectPropertyText' @{ Object = $row; Name = [string]$key }
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
    $actual = @(Get-HarnessStringAssignment $State.CurrentSource 'productionBakeInputs')
    if (($actual -join "`n") -cne ($expected -join "`n")) {
        return New-HarnessFail ('production bake literal mismatch: observed ' + ($actual -join ', '))
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
            [string]$Node.Member.Extent.Text -eq 'Properties' -and
            [string]$Node.Expression.Extent.Text -match '(?i)\.PSObject$'
    }, $true))
    $allow = @{
        147 = 'Get-Item metadata adapter'
        839 = 'dictionary type-test fallback'
        840 = 'dictionary type-test fallback'
        891 = 'JSON ledger payload'
        894 = 'JSON ledger payload'
        895 = 'JSON ledger payload'
        1104 = 'JSON probe payload'
        1124 = 'JSON probe payload'
        1185 = 'JSON probe payload'
    }
    foreach ($member in $members) {
        $line = [int]$member.Extent.StartLineNumber
        if (-not $allow.ContainsKey($line)) { return New-HarnessFail ('G1 new PSObject.Properties site at line ' + $line + ': ' + $member.Extent.Text) }
    }
    if ($members.Count -ne $allow.Count) { return New-HarnessFail ('G1 site count mismatch: expected ' + $allow.Count + ', observed ' + $members.Count) }
    return New-HarnessPass ('G1 allowlist count=' + $members.Count)
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
        return New-HarnessPass 'scratch removal trips gopv-ordered and G3'
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

    $entries = @()
    if ($null -ne $settings.hooks -and $null -ne $settings.hooks.PostToolUse) { $entries = @($settings.hooks.PostToolUse) }
    $matching = @($entries | Where-Object { [string]$_.matcher -ceq 'Edit|Write' })
    if ($matching.Count -ne 1) { return New-HarnessFail ('expected one Edit|Write PostToolUse matcher, found ' + $matching.Count) }
    $commandHooks = @($matching[0].hooks | Where-Object {
        [string]$_.type -ceq 'command' -and [string]$_.command -match 'Tools[/\\]Tests[/\\]Invoke-HarnessTests\.ps1'
    })
    if ($commandHooks.Count -ne 1) { return New-HarnessFail 'PostToolUse matcher has no harness command hook' }
    $command = [string]$commandHooks[0].command
    $paths = @($matching[0].paths | ForEach-Object { [string]$_ })
    if ($paths -notcontains 'Tools/Validation/*.ps1' -or $paths -notcontains 'Assets/_Game/Editor/MovementLab/*.cs') {
        return New-HarnessFail 'PostToolUse matcher paths omit required validation/editor scopes'
    }
    if ($State.SkipHookCheck) { return New-HarnessPass 'hook command resolved (nested check skipped)' }

    $fileMatch = [regex]::Match($command, '(?i)-File\s+(?:"([^"]+)"|([^\s]+))')
    if (-not $fileMatch.Success) { return New-HarnessFail 'hook command has no -File target' }
    $fileValue = if ($fileMatch.Groups[1].Success) { $fileMatch.Groups[1].Value } else { $fileMatch.Groups[2].Value }
    $resolved = if ([System.IO.Path]::IsPathRooted($fileValue)) { [System.IO.Path]::GetFullPath($fileValue) } else { [System.IO.Path]::GetFullPath((Join-Path $State.ProjectRoot $fileValue)) }
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { return New-HarnessFail ('hook target missing: ' + $resolved) }
    $output = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $resolved -SkipHookCheck 2>&1)
    $exitCode = $LASTEXITCODE
    $State.HookExecution = [ordered]@{
        command = $command
        resolvedPath = $resolved
        exitCode = $exitCode
        outputTail = @($output | ForEach-Object { [string]$_ } | Select-Object -Last 3)
    }
    if ($exitCode -ne 0) { return New-HarnessFail ('resolved hook command exited ' + $exitCode) }
    return New-HarnessPass 'resolved hook command executed with exit 0'
}
