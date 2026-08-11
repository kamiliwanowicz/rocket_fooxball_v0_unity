Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-HarnessParsedSource {
    param(
        [Parameter(Mandatory = $true)][string]$Source
    )

    if ([string]::IsNullOrWhiteSpace($Source)) {
        throw 'Source must contain a script path or script text.'
    }

    $tokens = $null
    $errors = $null
    if (Test-Path -LiteralPath $Source -PathType Leaf) {
        $resolved = (Resolve-Path -LiteralPath $Source -ErrorAction Stop).Path
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($resolved, [ref]$tokens, [ref]$errors)
        $sourceText = [System.IO.File]::ReadAllText($resolved)
    } else {
        $sourceText = $Source
        $ast = [System.Management.Automation.Language.Parser]::ParseInput($sourceText, [ref]$tokens, [ref]$errors)
    }

    if ($null -ne $errors -and @($errors).Count -gt 0) {
        $messages = @($errors | ForEach-Object { [string]$_.Message })
        throw ('Harness source parse failed: ' + ($messages -join ' | '))
    }

    return [pscustomobject]@{ Ast = $ast; Text = $sourceText }
}

function Get-HarnessVariablePath {
    param(
        [Parameter(Mandatory = $true)]$Assignment
    )

    $left = $Assignment.Left
    if ($left -isnot [System.Management.Automation.Language.VariableExpressionAst]) {
        return $null
    }
    return [string]$left.VariablePath.UserPath
}

function Normalize-HarnessName {
    param([AllowEmptyString()][string]$Name)
    if ($null -eq $Name) { return '' }
    $value = $Name.Trim()
    if ($value.StartsWith('$', [StringComparison]::Ordinal)) { $value = $value.Substring(1) }
    return $value
}

function Import-HarnessFunctions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$FunctionNames,
        [AllowEmptyCollection()][string[]]$VariableNames = @()
    )

    $parsed = Get-HarnessParsedSource $Source
    $ast = $parsed.Ast
    $requestedFunctions = @($FunctionNames | ForEach-Object { Normalize-HarnessName ([string]$_) } | Where-Object { $_ } | Select-Object -Unique)
    $requestedVariables = @($VariableNames | ForEach-Object { Normalize-HarnessName ([string]$_) } | Where-Object { $_ } | Select-Object -Unique)

    $rootStatements = @()
    if ($null -ne $ast.EndBlock) { $rootStatements += @($ast.EndBlock.Statements) }
    if ($null -ne $ast.BeginBlock) { $rootStatements += @($ast.BeginBlock.Statements) }
    if ($ast.PSObject.Properties.Name -contains 'CleanBlock' -and $null -ne $ast.CleanBlock) { $rootStatements += @($ast.CleanBlock.Statements) }
    $rootStatements = @($rootStatements | Sort-Object Extent.StartOffset)

    $functionSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $requestedFunctions) { [void]$functionSet.Add($name) }
    $variableSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $requestedVariables) { [void]$variableSet.Add($name) }

    $pieces = New-Object System.Collections.Generic.List[object]
    foreach ($statement in $rootStatements) {
        if ($statement -is [System.Management.Automation.Language.FunctionDefinitionAst]) {
            if ($functionSet.Contains([string]$statement.Name)) {
                $pieces.Add([pscustomobject]@{ Offset = [int]$statement.Extent.StartOffset; Text = [string]$statement.Extent.Text })
            }
            continue
        }
        if ($statement -is [System.Management.Automation.Language.AssignmentStatementAst]) {
            $path = Get-HarnessVariablePath $statement
            if ($null -ne $path -and $variableSet.Contains((Normalize-HarnessName $path))) {
                $pieces.Add([pscustomobject]@{ Offset = [int]$statement.Extent.StartOffset; Text = [string]$statement.Extent.Text })
            }
        }
    }

    $missingFunctions = @($requestedFunctions | Where-Object {
        $target = $_
        @($pieces | Where-Object { $_.Text -match ('(?im)^function\s+' + [regex]::Escape($target) + '\b') }).Count -eq 0
    })
    if ($missingFunctions.Count -gt 0) {
        throw ('Harness source did not contain requested top-level function(s): ' + ($missingFunctions -join ', '))
    }

    $moduleName = 'MovementLabHarness_' + [Guid]::NewGuid().ToString('N')
    $body = @($pieces | Sort-Object Offset | ForEach-Object { $_.Text }) -join [Environment]::NewLine
    if ($requestedFunctions.Count -gt 0) {
        $quoted = @($requestedFunctions | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ', '
        $body += [Environment]::NewLine + 'Export-ModuleMember -Function @(' + $quoted + ')'
    } else {
        $body += [Environment]::NewLine + 'Export-ModuleMember -Function @()'
    }

    $module = New-Module -Name $moduleName -ScriptBlock ([scriptblock]::Create($body))
    return $module
}

Export-ModuleMember -Function Import-HarnessFunctions
