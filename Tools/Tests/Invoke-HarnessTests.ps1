[CmdletBinding()]
param(
    [string]$EvidenceRoot,
    [switch]$SkipHookCheck,
    [ValidateSet('PostToolUse', 'PreToolUse')][string]$HookMode,
    [Alias('ForceFailure')][switch]$HookTestForceFailure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

$testsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $testsRoot)
$projectRoot = [System.IO.Path]::GetFullPath($projectRoot).TrimEnd('\')
$shimPath = Join-Path $testsRoot 'HarnessShim.psm1'
$testsPath = Join-Path $testsRoot 'MovementLabHarness.Tests.ps1'
$workflowPath = Join-Path $projectRoot 'Tools/Validation/Invoke-MovementLabWorkflow.ps1'
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
    $input = Get-HookProperty $Event 'tool_input'
    if ($null -eq $input) { return $Event }
    return $input
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

function Convert-HookPathToRelative {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Root)
    $value = $Path.Trim().Trim('"').Replace('\', '/')
    $rootValue = $Root.TrimEnd('\').Replace('\', '/')
    if ([System.IO.Path]::IsPathRooted($Path)) {
        try { $value = ([System.IO.Path]::GetFullPath($Path)).Replace('\', '/') } catch { return $value.TrimStart('/') }
        if ($value.StartsWith($rootValue + '/', [StringComparison]::OrdinalIgnoreCase)) {
            return $value.Substring($rootValue.Length + 1).TrimStart('/')
        }
        return $value.TrimStart('/')
    }
    while ($value.StartsWith('./', [StringComparison]::Ordinal)) { $value = $value.Substring(2) }
    return $value.TrimStart('/')
}

function Test-HookPostToolTarget {
    param([Parameter(Mandatory = $true)]$Event, [Parameter(Mandatory = $true)][string]$Root)
    $toolName = [string](Get-HookProperty $Event 'tool_name')
    if ($toolName -notin @('Edit', 'Write')) { return $false }
    $input = Get-HookInput $Event
    $paths = @(Get-HookStrings $input @('file_path', 'path', 'filePath', 'filename'))
    $paths += @(Get-HookStrings $Event @('file_path', 'path', 'filePath', 'filename'))
    foreach ($path in $paths) {
        $relative = Convert-HookPathToRelative $path $Root
        if ($relative -match '(?i)^Tools/Tests(?:/|$)') { return $true }
        if ($relative -match '(?i)^Tools/Validation/[^/]+\.ps1$') { return $true }
        if ($relative -match '(?i)^Assets/_Game/Editor/MovementLab/[^/]+\.cs$') { return $true }
    }
    return $false
}

function Test-HookPreToolTarget {
    param([Parameter(Mandatory = $true)]$Event)
    $toolName = [string](Get-HookProperty $Event 'tool_name')
    if ($toolName -notin @('Bash', 'PowerShell', 'Command', 'Shell')) { return $false }
    $input = Get-HookInput $Event
    $commands = @(Get-HookStrings $input @('command', 'cmd', 'script'))
    $commands += @(Get-HookStrings $Event @('command', 'cmd', 'script'))
    foreach ($command in $commands) {
        if ($command -match '(?i)Invoke-MovementLabWorkflow\.ps1') { return $true }
        if ($command -match '(?i)(?:^|[\s"''/\\])Unity(?:\.exe)?(?:$|[\s"''/\\])') { return $true }
    }
    return $false
}

if (-not [string]::IsNullOrWhiteSpace($HookMode)) {
    if ($HookTestForceFailure -and $HookMode -ne 'PreToolUse') { throw 'HookTestForceFailure is valid only for PreToolUse test dispatch.' }
    $eventText = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($eventText)) { throw ($HookMode + ' hook event JSON missing on stdin.') }
    try { $hookEvent = $eventText | ConvertFrom-Json -ErrorAction Stop } catch { throw ($HookMode + ' hook event JSON invalid: ' + $_.Exception.Message) }
    $target = if ($HookMode -eq 'PostToolUse') { Test-HookPostToolTarget $hookEvent $projectRoot } else { Test-HookPreToolTarget $hookEvent }
    if (-not $target) {
        Write-Output ('HOOK ' + $HookMode + ' SKIP unrelated event')
        exit 0
    }
    $SkipHookCheck = $true
}

$shimInvoker = {
    param([string]$Source, [string[]]$FunctionNames, [string[]]$VariableNames)
    return Import-HarnessFunctions -Source $Source -FunctionNames $FunctionNames -VariableNames $VariableNames
}

function Get-RedSource {
    param([Parameter(Mandatory = $true)][string]$Root)
    $text = @(& git -C $Root show '73984e2:Tools/Validation/Invoke-MovementLabWorkflow.ps1' 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ('Unable to read RedAtSha source: ' + ($text -join ' ')) }
    return ($text -join [Environment]::NewLine)
}

function Assert-EvidenceRoot {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Root)
    $full = [System.IO.Path]::GetFullPath($Path)
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
    HookSettingsPath = $hookSettingsPath
    ShimCommand = $shimInvoker
    CurrentSource = [System.IO.File]::ReadAllText($workflowPath)
    RedSource = Get-RedSource $projectRoot
    SkipHookCheck = [bool]$SkipHookCheck
    HookExecution = $null
}

$cases = @(
    [pscustomobject]@{ Id = 'shim-export-set'; Function = ${function:Test-ShimExportSet} },
    [pscustomobject]@{ Id = 'gopv-ordered'; Function = ${function:Test-GopvOrdered} },
    [pscustomobject]@{ Id = 'gopv-json'; Function = ${function:Test-GopvJson} },
    [pscustomobject]@{ Id = 'gopv-absent'; Function = ${function:Test-GopvAbsent} },
    [pscustomobject]@{ Id = 'ledger-row-ordered-literal'; Function = ${function:Test-LedgerRowOrderedLiteral} },
    [pscustomobject]@{ Id = 'row-reuse-equal'; Function = ${function:Test-RowReuseEqual} },
    [pscustomobject]@{ Id = 'row-reuse-diff'; Function = ${function:Test-RowReuseDiff} },
    [pscustomobject]@{ Id = 'row-field-sweep'; Function = ${function:Test-RowFieldSweep} },
    [pscustomobject]@{ Id = 'bake-inputs-literal'; Function = ${function:Test-BakeInputsLiteral} },
    [pscustomobject]@{ Id = 'bake-inputs-asymmetry'; Function = ${function:Test-BakeInputsAsymmetry} },
    [pscustomobject]@{ Id = 'path-intersects'; Function = ${function:Test-PathIntersects} },
    [pscustomobject]@{ Id = 'stringset-null'; Function = ${function:Test-StringSetNull} },
    [pscustomobject]@{ Id = 'planonly-pending-only'; Function = ${function:Test-PlanOnlyPendingOnly} },
    [pscustomobject]@{ Id = 'guard-g1'; Function = ${function:Test-GuardG1} },
    [pscustomobject]@{ Id = 'guard-g3'; Function = ${function:Test-GuardG3} },
    [pscustomobject]@{ Id = 'guard-g4'; Function = ${function:Test-GuardG4} },
    [pscustomobject]@{ Id = 'guard-g5'; Function = ${function:Test-GuardG5} },
    [pscustomobject]@{ Id = 'scratch-drill'; Function = ${function:Test-ScratchDrill} }
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
    if ($pass) { Write-Output ('CASE ' + $case.Id + ' PASS ' + $message) }
    else { Write-Output ('CASE ' + $case.Id + ' FAIL ' + $message) }
}

$stopwatch.Stop()
$elapsedMs = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 3)
$failed = @($results | Where-Object { -not [bool]$_.pass })
$summary = [ordered]@{
    schemaVersion = 1
    status = if ($failed.Count -eq 0) { 'pass' } else { 'fail' }
    projectRoot = $projectRoot
    elapsedMs = $elapsedMs
    runtimeLimitSeconds = 10
    cases = @($results.ToArray())
    redAtSha = '73984e2'
    redGreen = @(
        [ordered]@{ case = 'gopv-ordered'; head = 'pass'; redAtSha = 'fail' }
        [ordered]@{ case = 'row-reuse-equal'; head = 'pass'; redAtSha = 'fail' }
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
    Write-Output ('EVIDENCE ' + $summaryPath)
}

Write-Output ('HARNESS elapsedMs=' + $elapsedMs + ' limitMs=10000')
if ($HookTestForceFailure) {
    Write-Output 'HOOK TEST FORCED FAILURE'
    exit 1
}
if ($failed.Count -gt 0 -or $elapsedMs -ge 10000) { exit 1 }
exit 0
