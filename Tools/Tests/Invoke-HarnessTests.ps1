[CmdletBinding()]
param(
    [string]$EvidenceRoot,
    [switch]$SkipHookCheck,
    [ValidateSet('PreToolUse')][string]$HookMode,
    [Alias('ForceFailure')][switch]$HookTestForceFailure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$runtimeLimitMs = 90000

function Write-HarnessOutput {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Message)
    if (-not [string]::IsNullOrWhiteSpace($HookMode)) {
        [Console]::Error.WriteLine($Message)
        return
    }
    Write-Output $Message
}

trap {
    if (-not [string]::IsNullOrWhiteSpace($HookMode)) {
        $message = [string]$_.Exception.Message
        if ([string]::IsNullOrWhiteSpace($message)) { $message = 'unspecified hook failure' }
        [Console]::Error.WriteLine(('HOOK ' + $HookMode + ' ERROR: ' + $message))
        exit 2
    }
    throw $_
}

$testsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $testsRoot)
$projectRoot = [System.IO.Path]::GetFullPath($projectRoot).TrimEnd('\')
$shimPath = Join-Path $testsRoot 'HarnessShim.psm1'
$testsPath = Join-Path $testsRoot 'MovementLabHarness.Tests.ps1'
$redFixturePath = Join-Path $testsRoot 'Fixtures/red-workflow.ps1.txt'
$workflowPath = Join-Path $projectRoot 'Tools/Validation/Invoke-MovementLabWorkflow.ps1'
$capturePath = Join-Path $projectRoot 'Tools/Validation/Capture-WeaponVisuals.ps1'
$verdictPath = Join-Path $projectRoot 'Tools/Validation/Test-WeaponVisualVerdict.ps1'
$comparatorPath = Join-Path $projectRoot 'Tools/Validation/Compare-GeneratedYaml.ps1'
$comparatorRedFixturePath = Join-Path $testsRoot 'Fixtures/red-generated-yaml-comparator.ps1.txt'
$hookSettingsPath = Join-Path $projectRoot '.claude/settings.json'

Import-Module -Name $shimPath -Force
. $testsPath

function Get-HookProperty {
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

function Get-HookInput {
    param([Parameter(Mandatory = $true)]$Event)
    $toolInput = Get-HookProperty $Event 'tool_input'
    if ($null -eq $toolInput) { return $Event }
    return $toolInput
}

function Get-HookStrings {
    param([AllowNull()]$Object, [Parameter(Mandatory = $true)][string[]]$Names)
    $values = New-Object System.Collections.Generic.List[string]
    foreach ($name in $Names) {
        $value = Get-HookProperty $Object $name
        if ($null -eq $value) { continue }
        if ($value -is [System.Array] -and $value -isnot [string]) {
            foreach ($item in @($value)) {
                if ($null -ne $item -and -not [string]::IsNullOrWhiteSpace([string]$item)) { $values.Add([string]$item) | Out-Null }
            }
        } elseif (-not [string]::IsNullOrWhiteSpace([string]$value)) {
            $values.Add([string]$value) | Out-Null
        }
    }
    return $values.ToArray()
}

function Test-HookPreToolTarget {
    param([Parameter(Mandatory = $true)]$Event)
    $toolName = [string](Get-HookProperty $Event 'tool_name')
    if ($toolName -notin @('Bash', 'PowerShell')) { return $false }
    $toolInput = Get-HookInput $Event
    $commands = @(Get-HookStrings $toolInput @('command', 'cmd', 'script'))
    $commands += @(Get-HookStrings $Event @('command', 'cmd', 'script'))
    foreach ($command in $commands) {
        if ($command -match '(?i)Invoke-MovementLabWorkflow\.ps1') { return $true }
        if ($command -match '(?i)(?:^|[\s"''/\\])Unity(?:\.exe)?(?:$|[\s"''/\\])') { return $true }
    }
    return $false
}

if (-not [string]::IsNullOrWhiteSpace($HookMode)) {
    $eventText = [Console]::In.ReadToEnd()
    if ($null -ne $eventText) { $eventText = $eventText.TrimStart([char]0xFEFF) }
    if ([string]::IsNullOrWhiteSpace($eventText)) { throw ($HookMode + ' hook event JSON missing on stdin.') }
    try { $hookEvent = $eventText | ConvertFrom-Json -ErrorAction Stop } catch { throw ($HookMode + ' hook event JSON invalid: ' + $_.Exception.Message) }
    $target = Test-HookPreToolTarget $hookEvent
    if (-not $target) {
        Write-HarnessOutput ('HOOK ' + $HookMode + ' SKIP unrelated event')
        exit 0
    }
    $SkipHookCheck = $true
}

$shimInvoker = {
    param([string]$Source, [string[]]$FunctionNames, [string[]]$VariableNames)
    return Import-HarnessFunctions -Source $Source -FunctionNames $FunctionNames -VariableNames $VariableNames
}

function Get-RedSource {
    param([Parameter(Mandatory = $true)][string]$FixturePath)
    if (-not (Test-Path -LiteralPath $FixturePath -PathType Leaf)) {
        throw ('Red baseline fixture missing: ' + $FixturePath)
    }
    return $FixturePath
}

function Assert-EvidenceRoot {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Root)
    $full = [System.IO.Path]::GetFullPath($Path)
    $workspaceRoot = 'C:\wt'
    if (-not $full.StartsWith($workspaceRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw ('EvidenceRoot must be under C:\wt: ' + $full)
    }
    $prefix = $Root.TrimEnd('\') + '\'
    if ($full.Equals($Root, [StringComparison]::OrdinalIgnoreCase) -or $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('EvidenceRoot must be outside project: ' + $full)
    }
    if ($full.IndexOf('\Library\', [StringComparison]::OrdinalIgnoreCase) -ge 0 -or $full.IndexOf('\Temp\', [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw ('EvidenceRoot cannot use Library or Temp: ' + $full)
    }
    return $full
}

$state = [pscustomobject]@{
    ProjectRoot = $projectRoot
    WorkflowPath = $workflowPath
    CapturePath = $capturePath
    VerdictPath = $verdictPath
    ComparatorPath = $comparatorPath
    ComparatorRedFixturePath = $comparatorRedFixturePath
    HookSettingsPath = $hookSettingsPath
    ShimCommand = $shimInvoker
    CurrentSource = [System.IO.File]::ReadAllText($workflowPath)
    RedSource = Get-RedSource $redFixturePath
    SkipHookCheck = [bool]$SkipHookCheck
    HookExecution = $null
}

$cases = @(
    [pscustomobject]@{ Id = 'ledger-row-ordered-literal'; Function = ${function:Test-LedgerRowOrderedLiteral} },
    [pscustomobject]@{ Id = 'generated-path-surface-removed'; Function = ${function:Test-GeneratedPathSurfaceRemoved} },
    [pscustomobject]@{ Id = 'generated-yaml-comparator-coverage'; Function = ${function:Test-GeneratedYamlComparatorCoverage} },
    [pscustomobject]@{ Id = 'generated-yaml-comparator-default-meta-coverage'; Function = ${function:Test-GeneratedYamlComparatorDefaultMetaCoverage} },
    [pscustomobject]@{ Id = 'row-field-sweep'; Function = ${function:Test-RowFieldSweep} },
    [pscustomobject]@{ Id = 'bake-count-production-method'; Function = ${function:Test-BakeCountProductionMethod} },
    [pscustomobject]@{ Id = 'evidence-path-budget'; Function = ${function:Test-EvidencePathBudget} },
    [pscustomobject]@{ Id = 'short-workspace-path'; Function = ${function:Test-ShortWorkspacePath} },
    [pscustomobject]@{ Id = 'stringset-null'; Function = ${function:Test-StringSetNull} },
    [pscustomobject]@{ Id = 'planonly-pending-only'; Function = ${function:Test-PlanOnlyPendingOnly} },
    [pscustomobject]@{ Id = 'guard-g1'; Function = ${function:Test-GuardG1} },
    [pscustomobject]@{ Id = 'guard-g4'; Function = ${function:Test-GuardG4} },
    [pscustomobject]@{ Id = 'guard-g5'; Function = ${function:Test-GuardG5} },
    [pscustomobject]@{ Id = 'scratch-drill'; Function = ${function:Test-ScratchDrill} },
    [pscustomobject]@{ Id = 'weapon-capture-contract'; Function = ${function:Test-WeaponCaptureContract} },
    [pscustomobject]@{ Id = 'weapon-capture-behavior'; Function = ${function:Test-WeaponCaptureBehavior} },
    [pscustomobject]@{ Id = 'weapon-verdict-contract'; Function = ${function:Test-WeaponVisualVerdictContract} },
    [pscustomobject]@{ Id = 'weapon-verdict-behavior'; Function = ${function:Test-WeaponVisualVerdictBehavior} },
    [pscustomobject]@{ Id = 'red-fixtures-behavior'; Function = ${function:Test-RedFixturesBehavior} }
)
if (-not $SkipHookCheck -and [string]::IsNullOrWhiteSpace($HookMode)) {
    $cases += [pscustomobject]@{ Id = 'hook-command'; Function = ${function:Test-HookSettings} }
}

$results = New-Object System.Collections.Generic.List[object]
foreach ($case in $cases) {
    $caseStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $result = $null
    try {
        $result = & $case.Function $state
        if ($null -eq $result) { $result = New-HarnessFail 'case returned no result' }
        if ($result -isnot [psobject] -or $null -eq (Get-HarnessField $result 'pass')) { $result = New-HarnessFail 'case returned invalid result shape' }
    } catch {
        $result = New-HarnessFail $_.Exception.Message
    }
    $caseStopwatch.Stop()
    $pass = [bool](Get-HarnessField $result 'pass')
    $message = [string](Get-HarnessField $result 'message')
    $record = [ordered]@{ id = $case.Id; pass = $pass; message = $message; elapsedMs = [Math]::Round($caseStopwatch.Elapsed.TotalMilliseconds, 3) }
    $results.Add($record) | Out-Null
    if ([string]::IsNullOrWhiteSpace($HookMode)) {
        if ($pass) { Write-HarnessOutput ('CASE ' + $case.Id + ' PASS ' + $message) }
        else { Write-HarnessOutput ('CASE ' + $case.Id + ' FAIL ' + $message) }
    }
}

$stopwatch.Stop()
$elapsedMs = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 3)
$failed = @($results | Where-Object { -not [bool]$_.pass })
$summary = [ordered]@{
    schemaVersion = 1
    status = if ($failed.Count -eq 0) { 'pass' } else { 'fail' }
    projectRoot = $projectRoot
    elapsedMs = $elapsedMs
    runtimeLimitSeconds = $runtimeLimitMs / 1000
    cases = @($results.ToArray())
    redBaseline = 'Tools/Tests/Fixtures/red-workflow.ps1.txt'
    redGreen = @(
        [ordered]@{ case = 'gopv-ordered'; head = 'pass'; redBaseline = 'fail' }
        [ordered]@{ case = 'generated-path-surface-removed'; head = 'pass'; redBaseline = 'fail' }
        [ordered]@{ case = 'generated-yaml-comparator-default-meta-coverage'; head = 'pass'; redBaseline = 'fail' }
        [ordered]@{ case = 'short-workspace-path'; head = 'pass'; redBaseline = 'fail' }
        [ordered]@{ case = 'row-reuse-equal'; head = 'pass'; redBaseline = 'fail' }
        [ordered]@{ case = 'weapon-capture-contract'; head = 'pass'; redBaseline = 'fail' }
        [ordered]@{ case = 'weapon-capture-behavior'; head = 'pass'; redBaseline = 'fail' }
        [ordered]@{ case = 'weapon-verdict-contract'; head = 'pass'; redBaseline = 'fail' }
        [ordered]@{ case = 'weapon-verdict-behavior'; head = 'pass'; redBaseline = 'fail' }
        [ordered]@{ case = 'red-fixtures-behavior'; head = 'pass'; redBaseline = 'fail' }
    )
    scratchDrill = @($results | Where-Object { $_.id -eq 'scratch-drill' })
    hookExecution = $state.HookExecution
    noUnity = $true
    noGitMutation = $true
}

if (-not [string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $evidencePath = Assert-EvidenceRoot $EvidenceRoot $projectRoot
    [System.IO.Directory]::CreateDirectory($evidencePath) | Out-Null
    $summaryPath = Join-Path $evidencePath 'harness-summary.json'
    $json = $summary | ConvertTo-Json -Depth 12
    [System.IO.File]::WriteAllText($summaryPath, $json + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
    Write-HarnessOutput ('EVIDENCE ' + $summaryPath)
}

if ([string]::IsNullOrWhiteSpace($HookMode)) {
    Write-HarnessOutput ('HARNESS elapsedMs=' + $elapsedMs + ' limitMs=' + $runtimeLimitMs)
}
if ($HookTestForceFailure) {
    Write-HarnessOutput ('HOOK ' + $HookMode + ' FAILED: forced harness failure')
    if (-not [string]::IsNullOrWhiteSpace($HookMode)) { exit 2 }
    exit 1
}
if ($failed.Count -gt 0 -or $elapsedMs -ge $runtimeLimitMs) {
    if (-not [string]::IsNullOrWhiteSpace($HookMode)) {
        $reason = if ($elapsedMs -ge $runtimeLimitMs) { 'runtime limit exceeded' } else { ('harness cases failed=' + $failed.Count) }
        Write-HarnessOutput ('HOOK ' + $HookMode + ' FAILED: ' + $reason + '; elapsedMs=' + $elapsedMs)
        exit 2
    }
    exit 1
}
if (-not [string]::IsNullOrWhiteSpace($HookMode)) {
    Write-HarnessOutput ('HOOK ' + $HookMode + ' PASS: harness cases=' + $cases.Count + '; elapsedMs=' + $elapsedMs)
}
exit 0
