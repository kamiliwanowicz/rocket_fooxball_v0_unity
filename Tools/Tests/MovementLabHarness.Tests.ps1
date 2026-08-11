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
        $functionText = [string]$function.Extent.Text
        switch ($key) {
            'Get-CanonicalPath|item' {
                if ($functionText -notmatch '(?im)\$item\s*=\s*Get-Item\b') { return New-HarnessFail 'G1 item provenance is not Get-Item metadata' }
            }
            'Get-ObjectPropertyValue|Object|name' {
                if ($functionText -notmatch '(?i)-is\s+\[System\.Collections\.IDictionary\]') { return New-HarnessFail 'G1 Object fallback lacks IDictionary guard' }
            }
            'Get-ObjectPropertyValue|Object|index:Name' {
                if ($functionText -notmatch '(?i)-is\s+\[System\.Collections\.IDictionary\]') { return New-HarnessFail 'G1 Object fallback lacks IDictionary guard' }
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
    $entries = if ($null -ne $settings.hooks.PostToolUse) { @($settings.hooks.PostToolUse) } else { @() }
    $matching = @($entries | Where-Object { [string]$_.matcher -ceq 'Edit|Write' })
    if ($matching.Count -ne 1) { return New-HarnessFail ('expected one Edit|Write PostToolUse matcher, found ' + $matching.Count) }
    if ($matching[0].PSObject.Properties.Name -contains 'paths') { return New-HarnessFail 'PostToolUse matcher uses unsupported paths field' }
    $commandHooks = @($matching[0].hooks | Where-Object {
        [string]$_.type -ceq 'command' -and [string]$_.command -match 'Tools[/\\]Tests[/\\]Invoke-HarnessTests\.ps1'
    })
    if ($commandHooks.Count -ne 1) { return New-HarnessFail 'PostToolUse matcher has no harness command hook' }
    $command = [string]$commandHooks[0].command
    if ($command -notmatch '(?i)-HookMode\s+PostToolUse') { return New-HarnessFail 'PostToolUse command omits -HookMode PostToolUse' }

    $preEntries = if ($null -ne $settings.hooks.PreToolUse) { @($settings.hooks.PreToolUse) } else { @() }
    $preMatching = @($preEntries | Where-Object { [string]$_.matcher -ceq 'Bash' })
    if ($preMatching.Count -ne 1) { return New-HarnessFail ('expected one Bash PreToolUse matcher, found ' + $preMatching.Count) }
    $preCommandHooks = @($preMatching[0].hooks | Where-Object {
        [string]$_.type -ceq 'command' -and [string]$_.command -match 'Tools[/\\]Tests[/\\]Invoke-HarnessTests\.ps1'
    })
    if ($preCommandHooks.Count -ne 1) { return New-HarnessFail 'PreToolUse matcher has no harness command hook' }
    $preCommand = [string]$preCommandHooks[0].command
    if ($preCommand -notmatch '(?i)-HookMode\s+PreToolUse') { return New-HarnessFail 'PreToolUse command omits -HookMode PreToolUse' }
    if ($State.SkipHookCheck) { return New-HarnessPass 'hook command resolved (nested check skipped)' }

    function Invoke-HookFixture {
        param(
            [Parameter(Mandatory = $true)][string]$CommandText,
            [Parameter(Mandatory = $true)][string]$EventJson,
            [switch]$ForceFailure
        )
        $fileMatch = [regex]::Match($CommandText, '(?i)-File\s+(?:"([^"]+)"|([^\s]+))')
        if (-not $fileMatch.Success) { throw 'hook command has no -File target' }
        $fileValue = if ($fileMatch.Groups[1].Success) { $fileMatch.Groups[1].Value } else { $fileMatch.Groups[2].Value }
        $resolved = if ([System.IO.Path]::IsPathRooted($fileValue)) { [System.IO.Path]::GetFullPath($fileValue) } else { [System.IO.Path]::GetFullPath((Join-Path $State.ProjectRoot $fileValue)) }
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw ('hook target missing: ' + $resolved) }
        $modeMatch = [regex]::Match($CommandText, '(?i)-HookMode\s+(PostToolUse|PreToolUse)')
        if (-not $modeMatch.Success) { throw 'hook command has no supported -HookMode' }
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $resolved, '-HookMode', $modeMatch.Groups[1].Value)
        if ($ForceFailure) { $arguments += '-HookTestForceFailure' }
        $output = @($EventJson | & powershell.exe @arguments 2>&1)
        return [pscustomobject]@{
            exitCode = [int]$LASTEXITCODE
            output = @($output | ForEach-Object { [string]$_ })
            mode = $modeMatch.Groups[1].Value
        }
    }

    $postTarget = '{"tool_name":"Edit","tool_input":{"file_path":"Tools/Tests/MovementLabHarness.Tests.ps1"}}'
    $postUnrelated = '{"tool_name":"Edit","tool_input":{"file_path":"README.md"}}'
    $preTarget = '{"tool_name":"Bash","tool_input":{"command":"powershell -File Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode Fast -PlanOnly; C:\\Unity\\Editor\\Unity.exe -batchmode -quit"}}'
    $preUnrelated = '{"tool_name":"Bash","tool_input":{"command":"Get-Date"}}'
    try {
        $postTargetResult = Invoke-HookFixture $command $postTarget
        $postUnrelatedResult = Invoke-HookFixture $command $postUnrelated
        $preTargetResult = Invoke-HookFixture $preCommand $preTarget
        $preUnrelatedResult = Invoke-HookFixture $preCommand $preUnrelated
        $preForcedResult = Invoke-HookFixture $preCommand $preTarget -ForceFailure
    } catch {
        return New-HarnessFail $_.Exception.Message
    }
    $State.HookExecution = [ordered]@{
        postTargetExit = $postTargetResult.exitCode
        postUnrelatedExit = $postUnrelatedResult.exitCode
        preTargetExit = $preTargetResult.exitCode
        preUnrelatedExit = $preUnrelatedResult.exitCode
        preForcedExit = $preForcedResult.exitCode
        postTargetTail = @($postTargetResult.output | Select-Object -Last 2)
        preForcedTail = @($preForcedResult.output | Select-Object -Last 2)
    }
    if ($postTargetResult.exitCode -ne 0) { return New-HarnessFail ('target PostToolUse hook exited ' + $postTargetResult.exitCode) }
    if ($postUnrelatedResult.exitCode -ne 0) { return New-HarnessFail ('unrelated PostToolUse hook exited ' + $postUnrelatedResult.exitCode) }
    if ($preTargetResult.exitCode -ne 0) { return New-HarnessFail ('target workflow PreToolUse hook exited ' + $preTargetResult.exitCode) }
    if ($preUnrelatedResult.exitCode -ne 0) { return New-HarnessFail ('unrelated PreToolUse hook exited ' + $preUnrelatedResult.exitCode) }
    if ($preForcedResult.exitCode -eq 0) { return New-HarnessFail 'forced failing PreToolUse hook unexpectedly exited 0' }
    if (@($postUnrelatedResult.output | Where-Object { $_ -match '^CASE ' }).Count -gt 0) { return New-HarnessFail 'unrelated PostToolUse event ran harness suite' }
    if (@($preUnrelatedResult.output | Where-Object { $_ -match '^CASE ' }).Count -gt 0) { return New-HarnessFail 'unrelated PreToolUse event ran harness suite' }
    if (@($preForcedResult.output | Where-Object { $_ -match 'HOOK TEST FORCED FAILURE' }).Count -ne 1) { return New-HarnessFail 'forced PreToolUse fixture lacked failure marker' }
    return New-HarnessPass 'target/unrelated PostToolUse + PreToolUse fixtures; forced PreToolUse failure blocks'
}
