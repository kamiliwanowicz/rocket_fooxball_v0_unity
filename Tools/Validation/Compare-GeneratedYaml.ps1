# Semantic comparison of builder-generated Unity YAML between two Git revisions.
# Reads blobs through `git show` only; never touches the working tree.
# Unity YAML is line-structured, so line/regex parsing is deliberate here: no general YAML parser.
[CmdletBinding()]
param(
    [string]$Base,
    [string]$Head,
    # Empty means every path in the workflow's authoritative generated inventory.  Explicit
    # paths remain useful for a narrow review, but are rejected unless that inventory owns them.
    [string[]]$Path = @(),
    [switch]$FailOnDangling,
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = $null
$script:AnchorPattern = '^--- !u!(-?\d+) &(-?\d+)( stripped)?\s*$'
$script:RefPattern = '\{fileID: (-?\d+)(?:, guid: ([0-9a-fA-F]{32}), type: (-?\d+))?\}'

function Get-ShortHash {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value)) } finally { $sha.Dispose() }
    return ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant().Substring(0, 16)
}

# Positional token intentionally starts with a literal '$'. Any replacement string carrying it
# must be escaped through ConvertTo-ReplacementLiteral: bare '$LOCAL...' inside a double-quoted
# string expands as a PowerShell variable and silently yields empty text.
function Get-LocalToken {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    return '$LOCAL' + $Value
}

function ConvertTo-ReplacementLiteral {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    return $Value.Replace('$', '$$')
}

function Invoke-GitCapture {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& git -C $script:RepoRoot @Arguments 2>$null)
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    return [pscustomobject]@{ ExitCode = $code; Lines = @($output | ForEach-Object { [string]$_ }) }
}

function Invoke-GitNullDelimitedCapture {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    # Git permits newlines in filenames. Use its NUL-delimited path output so an untracked
    # generated asset cannot be split into several candidates or silently omitted.
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = 'git'
    $startInfo.Arguments = '-C "' + $script:RepoRoot + '" ' + ($Arguments -join ' ')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $output = $process.StandardOutput.ReadToEnd()
    $errorOutput = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw ('Git path listing failed: ' + $errorOutput.Trim()) }
    return ,@($output.Split([char[]]@([char]0), [StringSplitOptions]::RemoveEmptyEntries))
}

function Get-RevisionPaths {
    param([Parameter(Mandatory = $true)][string]$Revision)
    if ($Revision -ceq 'WORKTREE') {
        # --cached retains tracked paths (including deleted ones); --others adds untracked,
        # non-ignored generated outputs so the candidate union can report them as additions.
        $paths = Invoke-GitNullDelimitedCapture -Arguments @('ls-files', '-z', '--cached', '--others', '--exclude-standard')
    } else {
        $paths = Invoke-GitNullDelimitedCapture -Arguments @('ls-tree', '-rz', '--name-only', $Revision)
    }
    return ,@($paths | ForEach-Object { ([string]$_).Replace('\', '/') } | Where-Object { $_ })
}

function Get-ChangedPaths {
    param([Parameter(Mandatory = $true)][string]$BaseRevision, [Parameter(Mandatory = $true)][string]$HeadRevision)

    # Git can identify changed tracked files without loading every generated lightmap or
    # ProjectSettings YAML into the semantic graph comparer.  Presence changes are added below
    # so untracked WORKTREE additions remain covered too.
    if ($HeadRevision -ceq 'WORKTREE') {
        return Invoke-GitNullDelimitedCapture -Arguments @('diff', '--name-only', '-z', $BaseRevision)
    }
    if ($BaseRevision -ceq 'WORKTREE') {
        return Invoke-GitNullDelimitedCapture -Arguments @('diff', '--name-only', '-z', $HeadRevision)
    }
    return Invoke-GitNullDelimitedCapture -Arguments @('diff', '--name-only', '-z', $BaseRevision, $HeadRevision)
}

function Get-AuthoritativeInventoryRoots {
    param([Parameter(Mandatory = $true)][string]$Revision)

    # The workflow owns the generated-inventory contract.  Parse its literal root array from
    # each revision instead of maintaining a second comparator-specific path list that can drift.
    $workflowPath = 'Tools/Validation/Invoke-MovementLabWorkflow.ps1'
    $lines = Get-BlobLines $Revision $workflowPath
    if ($null -eq $lines) { throw ('Authoritative inventory source is missing in ' + $Revision + ': ' + $workflowPath) }

    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseInput(($lines -join "`n"), [ref]$tokens, [ref]$errors)
    if (@($errors).Count -gt 0) {
        throw ('Authoritative inventory source failed to parse in ' + $Revision + ': ' + (@($errors | ForEach-Object { $_.Message }) -join ' | '))
    }
    $assignments = @($ast.FindAll({
        param($node)
        return $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -ceq 'script:AuthoritativeInventory'
    }, $true))
    if ($assignments.Count -ne 1) { throw ('Expected one $script:AuthoritativeInventory assignment in ' + $Revision + ', found ' + $assignments.Count + '.') }

    $roots = @($assignments[0].Right.FindAll({
        param($node)
        return $node -is [System.Management.Automation.Language.StringConstantExpressionAst]
    }, $true) | ForEach-Object { ([string]$_.Value).Trim().Replace('\\', '/') } | Where-Object { $_ })
    if ($roots.Count -eq 0) { throw ('Authoritative inventory is empty in ' + $Revision + '.') }
    return @($roots | Sort-Object -Unique)
}

function Test-AuthoritativeInventoryMember {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$InventoryRoots
    )

    $candidate = $RelativePath.Replace('\\', '/').TrimStart('/')
    foreach ($root in $InventoryRoots) {
        $normalizedRoot = ([string]$root).Replace('\\', '/').Trim('/')
        if ($candidate.Equals($normalizedRoot, [StringComparison]::OrdinalIgnoreCase) -or
            $candidate.StartsWith($normalizedRoot + '/', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Get-PathCoverageKind {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    switch ([IO.Path]::GetExtension($RelativePath).ToLowerInvariant()) {
        '.unity' { return 'supported text' }
        '.prefab' { return 'supported text' }
        '.mat' { return 'supported text' }
        '.controller' { return 'supported text' }
        '.asset' { return 'supported text' }
        '.json' { return 'supported text' }
        '.meta' { return 'unsupported metadata' }
        default { return 'unsupported binary' }
    }
}

function Resolve-SelectedPaths {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Candidates,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$InventoryRoots,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$RequestedPaths
    )

    $authoritative = @($Candidates | Where-Object {
        Test-AuthoritativeInventoryMember -RelativePath ([string]$_) -InventoryRoots $InventoryRoots
    } | Sort-Object -Unique)
    if ($RequestedPaths.Count -eq 0) { return $authoritative }

    $selected = New-Object System.Collections.Generic.List[string]
    foreach ($pattern in $RequestedPaths) {
        $normalized = ([string]$pattern).Trim().Replace('\\', '/')
        if ([string]::IsNullOrWhiteSpace($normalized)) { throw 'Requested path patterns must be non-empty.' }
        $matches = @($Candidates | Where-Object { $_ -like $normalized } | Sort-Object -Unique)
        if ($matches.Count -eq 0) { throw ('Requested path is uncovered by both revisions: ' + $normalized) }
        $uncovered = @($matches | Where-Object {
            -not (Test-AuthoritativeInventoryMember -RelativePath ([string]$_) -InventoryRoots $InventoryRoots)
        })
        if ($uncovered.Count -gt 0) {
            throw ('Requested path is outside the authoritative generated inventory: ' + ($uncovered -join ', '))
        }
        foreach ($match in $matches) {
            if (-not $selected.Contains($match)) { $selected.Add($match) | Out-Null }
        }
    }
    return @($selected.ToArray() | Sort-Object)
}

function Get-BlobLines {
    param([Parameter(Mandatory = $true)][string]$Revision, [Parameter(Mandatory = $true)][string]$RelativePath)
    if ($Revision -ceq 'WORKTREE') {
        $full = Join-Path $script:RepoRoot $RelativePath
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { return $null }
        return ,@([IO.File]::ReadAllLines($full))
    }
    $result = Invoke-GitCapture @('show', ($Revision + ':' + $RelativePath))
    if ($result.ExitCode -ne 0) { return $null }
    return ,@($result.Lines)
}

function Read-YamlDocuments {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Lines)
    $docs = New-Object System.Collections.Generic.List[object]
    $current = $null
    foreach ($line in $Lines) {
        $trimmed = ([string]$line).TrimEnd()
        if ($trimmed -match $script:AnchorPattern) {
            $current = [pscustomobject]@{ ClassId = $Matches[1]; Anchor = $Matches[2]; Body = (New-Object System.Collections.Generic.List[string]) }
            $docs.Add($current) | Out-Null
            continue
        }
        if ($null -ne $current) { $current.Body.Add($trimmed) | Out-Null }
    }
    return ,@($docs.ToArray())
}

function Get-ModificationEntries {
    param([Parameter(Mandatory = $true)]$Document)
    $entries = New-Object System.Collections.Generic.List[object]
    $current = $null
    foreach ($line in $Document.Body) {
        if ($line -match '^\s*- target: (\{.*\})\s*$') {
            $current = [ordered]@{ target = $Matches[1]; propertyPath = ''; value = ''; objectReference = '' }
            $entries.Add($current) | Out-Null
            continue
        }
        if ($null -eq $current) { continue }
        if ($line -match '^\s+propertyPath:\s*(.*)$') { $current.propertyPath = $Matches[1].Trim(); continue }
        if ($line -match '^\s+value:\s*(.*)$') { $current.value = $Matches[1].Trim(); continue }
        if ($line -match '^\s+objectReference:\s*(.*)$') { $current.objectReference = $Matches[1].Trim(); continue }
        if ($line -match '^  \S') { $current = $null }
    }
    return ,@($entries.ToArray())
}

function Get-DocumentGraphSignature {
    param(
        [Parameter(Mandatory = $true)]$Document,
        [Parameter(Mandatory = $true)]$Anchors,
        [Parameter(Mandatory = $true)][scriptblock]$GetLocalLabel
    )

    # Length-prefixing preserves the exact non-reference YAML and makes reference tokens
    # unambiguous even if a document body happens to contain text resembling one.
    $body = $Document.Body -join "`n"
    $signature = New-Object Text.StringBuilder
    [void]$signature.Append('C').Append($Document.ClassId.Length).Append(':').Append($Document.ClassId)
    $position = 0
    foreach ($match in [regex]::Matches($body, $script:RefPattern)) {
        $literal = $body.Substring($position, $match.Index - $position)
        [void]$signature.Append('T').Append($literal.Length).Append(':').Append($literal)

        $fileId = $match.Groups[1].Value
        $guid = $match.Groups[2].Value
        if (-not [string]::IsNullOrEmpty($guid)) {
            $reference = 'E:' + $guid.ToLowerInvariant() + ':' + $fileId
        } elseif ($fileId -eq '0') {
            $reference = 'N'
        } elseif ($Anchors.ContainsKey($fileId)) {
            $label = & $GetLocalLabel $fileId
            $reference = 'L' + $label.Length + ':' + $label
        } else {
            $reference = 'D'
        }
        [void]$signature.Append('R').Append($reference.Length).Append(':').Append($reference)
        $position = $match.Index + $match.Length
    }
    $tail = $body.Substring($position)
    [void]$signature.Append('T').Append($tail.Length).Append(':').Append($tail)
    return $signature.ToString()
}

function Get-InitialGraphLabels {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()]$Documents,
        [Parameter(Mandatory = $true)]$Anchors
    )

    $labels = @{}
    foreach ($document in $Documents) {
        $signature = Get-DocumentGraphSignature $document $Anchors { param($id) return '' }
        $labels[$document.Anchor] = $signature
    }
    return $labels
}

function Refine-CanonicalGraphLabels {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()]$Documents,
        [Parameter(Mandatory = $true)]$Anchors,
        [Parameter(Mandatory = $true)]$InitialLabels
    )

    $labels = @{}
    foreach ($anchor in $InitialLabels.Keys) { $labels[$anchor] = [string]$InitialLabels[$anchor] }

    # Refinement can only split an existing equivalence class.  Keep each vertex's current
    # color in its next signature: otherwise an individualized "!" color can disappear when
    # its outgoing references look like an unmarked sibling, causing exact canonicalization
    # to recurse without reducing that sibling cell.
    for ($round = 0; $round -lt $Documents.Count; $round++) {
        $signatures = @{}
        foreach ($document in $Documents) {
            $currentLabel = [string]$labels[$document.Anchor]
            $documentSignature = Get-DocumentGraphSignature $document $Anchors {
                param($id)
                return $labels[$id]
            }
            # Length-prefix both fields so a label cannot run into YAML signature text.
            $signatures[$document.Anchor] = 'P' + $currentLabel.Length + ':' + $currentLabel + 'S' + $documentSignature.Length + ':' + $documentSignature
        }

        # Do not shorten signatures to hashes: a collision here could hide a semantic change.
        # Ordinal ordering also makes labels independent of the source document/fileID order.
        $unique = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
        foreach ($signature in $signatures.Values) { [void]$unique.Add([string]$signature) }
        [string[]]$ordered = @($unique)
        [Array]::Sort($ordered, [StringComparer]::Ordinal)
        $labelsBySignature = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
        for ($index = 0; $index -lt $ordered.Length; $index++) { $labelsBySignature.Add($ordered[$index], ('L' + $index)) }

        $next = @{}
        $classRepresentatives = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
        $classesRefined = $false
        foreach ($document in $Documents) {
            $anchor = $document.Anchor
            $nextLabel = $labelsBySignature[$signatures[$anchor]]
            $next[$anchor] = $nextLabel

            $currentLabel = [string]$labels[$anchor]
            if ($classRepresentatives.ContainsKey($currentLabel)) {
                if ($classRepresentatives[$currentLabel] -cne $nextLabel) { $classesRefined = $true }
            } else {
                $classRepresentatives.Add($currentLabel, $nextLabel)
            }
        }
        $labels = $next
        if (-not $classesRefined) { break }
    }
    return $labels
}

function Get-CanonicalGraphLabels {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()]$Documents,
        [Parameter(Mandatory = $true)]$Anchors
    )
    return Refine-CanonicalGraphLabels $Documents $Anchors (Get-InitialGraphLabels $Documents $Anchors)
}

function Get-CanonicalOrderedGraphText {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()]$Documents,
        [Parameter(Mandatory = $true)]$Anchors,
        [Parameter(Mandatory = $true)]$Labels
    )

    # At a discrete refinement leaf, color order gives a labeling independent of fileIDs.
    $orderedAnchors = @($Labels.Keys | Sort-Object { [string]$Labels[$_] })
    $order = @{}
    for ($index = 0; $index -lt $orderedAnchors.Count; $index++) { $order[$orderedAnchors[$index]] = 'N' + $index }
    $byAnchor = @{}
    foreach ($document in $Documents) { $byAnchor[$document.Anchor] = $document }
    $canonical = New-Object Text.StringBuilder
    foreach ($anchor in $orderedAnchors) {
        $signature = Get-DocumentGraphSignature $byAnchor[$anchor] $Anchors { param($id) return $order[$id] }
        [void]$canonical.Append('D').Append($signature.Length).Append(':').Append($signature)
    }
    return $canonical.ToString()
}

function Get-CanonicalComponentText {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()]$Documents,
        [Parameter(Mandatory = $true)]$Anchors
    )

    # Exact individualization/refinement canonicalization.  WL alone cannot distinguish every
    # graph (for example one directed 6-cycle vs two directed 3-cycles).  Split each remaining
    # color class, refine again, and retain the lexicographically least fully labeled graph.
    # Components are handled independently below, preventing factorial work across disconnected
    # repeated objects while retaining an exact canonical form for every connected component.
    # Scriptblocks invoked with '&' have a child scope; retain the evolving best candidate in
    # a mutable object so every recursive branch updates the same result.
    $result = [pscustomobject]@{ Best = $null }
    $visit = $null
    $visit = {
        param($labels)
        $cells = New-Object 'System.Collections.Generic.Dictionary[string,System.Collections.Generic.List[string]]' ([StringComparer]::Ordinal)
        foreach ($anchor in $labels.Keys) {
            $color = [string]$labels[$anchor]
            if (-not $cells.ContainsKey($color)) { $cells.Add($color, (New-Object 'System.Collections.Generic.List[string]')) }
            $cells[$color].Add([string]$anchor) | Out-Null
        }
        $ambiguous = @($cells.Keys | Where-Object { $cells[$_].Count -gt 1 } | Sort-Object { $cells[$_].Count }, { $_ })
        if ($ambiguous.Count -eq 0) {
            $candidate = Get-CanonicalOrderedGraphText $Documents $Anchors $labels
            if ($null -eq $result.Best -or [string]::CompareOrdinal($candidate, $result.Best) -lt 0) { $result.Best = $candidate }
            return
        }

        $cell = $cells[$ambiguous[0]]
        foreach ($anchor in $cell) {
            $individualized = @{}
            foreach ($id in $labels.Keys) { $individualized[$id] = [string]$labels[$id] }
            # This marker is branch-local, not derived from the fileID.  Each candidate is
            # explored, so it cannot make a renumbering semantically visible.
            $individualized[$anchor] = '!' + $individualized[$anchor]
            & $visit (Refine-CanonicalGraphLabels $Documents $Anchors $individualized)
        }
    }
    & $visit (Get-CanonicalGraphLabels $Documents $Anchors)
    return $result.Best
}

function Get-CanonicalGraphText {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()]$Documents,
        [Parameter(Mandatory = $true)]$Anchors,
        [Parameter(Mandatory = $true)]$Labels
    )

    # Partition on weakly connected local-reference components.  There are no local edges
    # between these components, so sorting exact component forms is both exact and much faster
    # than individualizing interchangeable documents from separate components together.
    $documentsByAnchor = @{}
    $neighbours = @{}
    foreach ($document in $Documents) {
        $documentsByAnchor[$document.Anchor] = $document
        $neighbours[$document.Anchor] = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    }
    foreach ($document in $Documents) {
        foreach ($match in [regex]::Matches(($document.Body -join "`n"), $script:RefPattern)) {
            $target = $match.Groups[1].Value
            if ($target -ne '0' -and $Anchors.ContainsKey($target)) {
                [void]$neighbours[$document.Anchor].Add($target)
                [void]$neighbours[$target].Add($document.Anchor)
            }
        }
    }

    $remaining = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($anchor in $documentsByAnchor.Keys) { [void]$remaining.Add($anchor) }
    $componentForms = New-Object System.Collections.Generic.List[string]
    while ($remaining.Count -gt 0) {
        $start = @($remaining | Sort-Object)[0]
        $queue = New-Object 'System.Collections.Generic.Queue[string]'
        $componentAnchors = New-Object 'System.Collections.Generic.List[string]'
        $queue.Enqueue($start)
        [void]$remaining.Remove($start)
        while ($queue.Count -gt 0) {
            $anchor = $queue.Dequeue()
            $componentAnchors.Add($anchor) | Out-Null
            foreach ($target in $neighbours[$anchor]) {
                if ($remaining.Remove($target)) { $queue.Enqueue($target) }
            }
        }
        $componentDocuments = @($componentAnchors | ForEach-Object { $documentsByAnchor[$_] })
        $componentAnchorMap = @{}
        foreach ($anchor in $componentAnchors) { $componentAnchorMap[$anchor] = $Anchors[$anchor] }
        $componentForms.Add((Get-CanonicalComponentText $componentDocuments $componentAnchorMap)) | Out-Null
    }
    [string[]]$ordered = @($componentForms.ToArray())
    [Array]::Sort($ordered, [StringComparer]::Ordinal)
    $canonical = New-Object Text.StringBuilder
    foreach ($component in $ordered) { [void]$canonical.Append('G').Append($component.Length).Append(':').Append($component) }
    return $canonical.ToString()
}

function Get-YamlAnalysis {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Lines)
    $docs = Read-YamlDocuments $Lines
    $anchors = @{}
    foreach ($doc in $docs) { $anchors[$doc.Anchor] = $doc.ClassId }

    $nonZero = 0
    $dangling = 0
    $guidCounts = @{}
    $provenance = New-Object System.Collections.Generic.List[string]
    $instances = @{}

    foreach ($doc in $docs) {
        $bodyText = ($doc.Body -join "`n")
        foreach ($match in [regex]::Matches($bodyText, $script:RefPattern)) {
            $fileId = $match.Groups[1].Value
            $guid = $match.Groups[2].Value
            if ($fileId -ne '0') { $nonZero++ }
            if ([string]::IsNullOrEmpty($guid)) {
                if ($fileId -ne '0' -and -not $anchors.ContainsKey($fileId)) { $dangling++ }
            } else {
                $key = $guid.ToLowerInvariant()
                if ($guidCounts.ContainsKey($key)) { $guidCounts[$key] = $guidCounts[$key] + 1 } else { $guidCounts[$key] = 1 }
            }
        }
        foreach ($field in @('m_SourcePrefab', 'm_CorrespondingSourceObject', 'm_PrefabAsset')) {
            foreach ($match in [regex]::Matches($bodyText, ('^\s*' + $field + ':\s*(\{.*\})\s*$'), 'Multiline')) {
                $value = $match.Groups[1].Value
                if ($value -match 'guid: ([0-9a-fA-F]{32})') { $provenance.Add($field + '=' + $Matches[1].ToLowerInvariant()) | Out-Null }
            }
        }
    }

    $labels = Get-CanonicalGraphLabels $docs $anchors
    $evaluator = [Text.RegularExpressions.MatchEvaluator] {
        param($m)
        if (-not [string]::IsNullOrEmpty($m.Groups[2].Value)) { return ('{EXT:' + $m.Groups[2].Value.ToLowerInvariant() + ':' + $m.Groups[1].Value + '}') }
        $id = $m.Groups[1].Value
        if ($id -eq '0') { return '{NULL}' }
        if ($labels.ContainsKey($id)) { return ('{' + (Get-LocalToken $labels[$id]) + '}') }
        return '{DANGLING}'
    }
    foreach ($doc in $docs) {
        if ($doc.ClassId -eq '1001') {
            $entries = Get-ModificationEntries $doc
            # objectReference points at local anchors, so canonicalize before hashing or every
            # renumbering round reports a false instance change.
            $signatureLines = @($entries | ForEach-Object { [regex]::Replace(([string]$_.target + '|' + [string]$_.propertyPath + '|' + [string]$_.value + '|' + [string]$_.objectReference), $script:RefPattern, $evaluator) } | Sort-Object)
            $named = @($entries | Where-Object { [string]$_.propertyPath -eq 'm_Name' } | ForEach-Object { [string]$_.value })
            $source = ''
            if (($doc.Body -join "`n") -match 'm_SourcePrefab: \{fileID: -?\d+, guid: ([0-9a-fA-F]{32})') { $source = $Matches[1].ToLowerInvariant() }
            $name = if ($named.Count -gt 0) { $named[0] } else { '#' + (Get-ShortHash ($signatureLines -join "`n")) }
            $key = $source + '/' + $name
            $suffix = 0
            while ($instances.ContainsKey($key)) { $suffix++; $key = $source + '/' + $name + '~' + $suffix }
            $instances[$key] = Get-ShortHash ($signatureLines -join "`n")
        }
    }
    $canonical = Get-CanonicalGraphText $docs $anchors $labels

    return [pscustomobject]@{
        IsYaml = $true
        DocCount = $docs.Count
        Anchors = $anchors
        NonZeroRefs = $nonZero
        Dangling = $dangling
        GuidCounts = $guidCounts
        Provenance = @($provenance.ToArray() | Sort-Object)
        Instances = $instances
        Canonical = $canonical
    }
}

function Get-TextAnalysis {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][string[]]$Lines)
    $normalized = @($Lines | ForEach-Object { ([string]$_).TrimEnd() })
    return [pscustomobject]@{
        IsYaml = $false
        DocCount = $normalized.Count
        Anchors = @{}
        NonZeroRefs = 0
        Dangling = 0
        GuidCounts = @{}
        Provenance = @()
        Instances = @{}
        Canonical = Get-ShortHash ($normalized -join "`n")
    }
}

function Get-Analysis {
    param([AllowNull()][AllowEmptyCollection()][string[]]$Lines)
    if ($null -eq $Lines) { return $null }
    if (@($Lines | Where-Object { $_ -match $script:AnchorPattern }).Count -gt 0) { return Get-YamlAnalysis $Lines }
    return Get-TextAnalysis $Lines
}

function Format-Delta {
    param([int]$BaseValue, [int]$HeadValue)
    $delta = $HeadValue - $BaseValue
    $sign = if ($delta -gt 0) { '+' } else { '' }
    return ([string]$BaseValue + ' -> ' + [string]$HeadValue + ' (' + $sign + [string]$delta + ')')
}

function Compare-KeyedCounts {
    param([Parameter(Mandatory = $true)]$BaseMap, [Parameter(Mandatory = $true)]$HeadMap)
    $keys = @(@($BaseMap.Keys) + @($HeadMap.Keys) | Sort-Object -Unique)
    $changed = New-Object System.Collections.Generic.List[string]
    foreach ($key in $keys) {
        $b = if ($BaseMap.ContainsKey($key)) { $BaseMap[$key] } else { 0 }
        $h = if ($HeadMap.ContainsKey($key)) { $HeadMap[$key] } else { 0 }
        if ("$b" -cne "$h") { $changed.Add([string]$key + ' ' + [string]$b + ' -> ' + [string]$h) | Out-Null }
    }
    return ,@($changed.ToArray())
}

function Get-CountMap {
    param([AllowEmptyCollection()][string[]]$Values)
    $map = @{}
    foreach ($value in @($Values)) {
        if ($map.ContainsKey($value)) { $map[$value] = $map[$value] + 1 } else { $map[$value] = 1 }
    }
    return $map
}

function Invoke-SelfTest {
    $failures = New-Object System.Collections.Generic.List[string]

    # Regression: literal '$' in a replacement string must survive -replace.
    $token = Get-LocalToken 'abc123'
    if ($token -cne '$LOCALabc123') { $failures.Add('token literal broken: ' + $token) | Out-Null }
    $replaced = 'x &ANCHOR y' -replace '&ANCHOR', (ConvertTo-ReplacementLiteral $token)
    if ($replaced -cne 'x $LOCALabc123 y') { $failures.Add('replacement escaping lost literal $: ' + $replaced) | Out-Null }
    if ((ConvertTo-ReplacementLiteral $token) -cne '$$LOCALabc123') { $failures.Add('replacement literal not escaped') | Out-Null }

    $yamlA = @(
        '%YAML 1.1', '--- !u!1 &100', 'GameObject:', '  m_Name: Root', '  m_Component:', '  - component: {fileID: 200}',
        '--- !u!4 &200', 'Transform:', '  m_GameObject: {fileID: 100}', '  m_Father: {fileID: 999}')
    $yamlB = @(
        '%YAML 1.1', '--- !u!4 &777', 'Transform:', '  m_GameObject: {fileID: 555}', '  m_Father: {fileID: 999}',
        '--- !u!1 &555', 'GameObject:', '  m_Name: Root', '  m_Component:', '  - component: {fileID: 777}')
    $yamlC = @(
        '%YAML 1.1', '--- !u!1 &100', 'GameObject:', '  m_Name: Renamed', '  m_Component:', '  - component: {fileID: 200}',
        '--- !u!4 &200', 'Transform:', '  m_GameObject: {fileID: 100}', '  m_Father: {fileID: 999}')
    $a = Get-Analysis $yamlA
    $b = Get-Analysis $yamlB
    $c = Get-Analysis $yamlC
    if ($a.DocCount -ne 2) { $failures.Add('doc count wrong: ' + $a.DocCount) | Out-Null }
    if ($a.Dangling -ne 1) { $failures.Add('dangling count wrong: ' + $a.Dangling) | Out-Null }
    if ($a.Canonical -cne $b.Canonical) { $failures.Add('renumbered documents did not canonicalize equal') | Out-Null }
    if ($a.Canonical -ceq $c.Canonical) { $failures.Add('value change was not detected') | Out-Null }
    if ($a.NonZeroRefs -ne 3) { $failures.Add('nonzero ref count wrong: ' + $a.NonZeroRefs) | Out-Null }

    # Regression: the Transform documents have identical non-reference data. Their distinct
    # GameObject neighbourhoods must still keep a rewired Root reference semantically changed.
    $rewireA = @(
        '%YAML 1.1', '--- !u!1 &100', 'GameObject:', '  m_Name: Root', '  m_Target: {fileID: 201}',
        '--- !u!1 &101', 'GameObject:', '  m_Name: Target A', '--- !u!4 &201', 'Transform:', '  m_GameObject: {fileID: 101}', '  m_Father: {fileID: 0}', '  m_LocalPosition: {x: 0, y: 0, z: 0}',
        '--- !u!1 &102', 'GameObject:', '  m_Name: Target B', '--- !u!4 &202', 'Transform:', '  m_GameObject: {fileID: 102}', '  m_Father: {fileID: 0}', '  m_LocalPosition: {x: 0, y: 0, z: 0}')
    $rewireB = @(
        '%YAML 1.1', '--- !u!1 &100', 'GameObject:', '  m_Name: Root', '  m_Target: {fileID: 202}',
        '--- !u!1 &101', 'GameObject:', '  m_Name: Target A', '--- !u!4 &201', 'Transform:', '  m_GameObject: {fileID: 101}', '  m_Father: {fileID: 0}', '  m_LocalPosition: {x: 0, y: 0, z: 0}',
        '--- !u!1 &102', 'GameObject:', '  m_Name: Target B', '--- !u!4 &202', 'Transform:', '  m_GameObject: {fileID: 102}', '  m_Father: {fileID: 0}', '  m_LocalPosition: {x: 0, y: 0, z: 0}')
    if ((Get-Analysis $rewireA).Canonical -ceq (Get-Analysis $rewireB).Canonical) { $failures.Add('rewiring between structurally identical Transform targets was not detected') | Out-Null }

    # Regression: one directed 6-cycle and two disconnected directed 3-cycles have the same
    # 1-WL colors, document count, and local text. Exact component canonicalization must retain
    # their different topology instead of treating every node in the shared color as equivalent.
    $sixCycle = New-Object System.Collections.Generic.List[string]
    $twoCycles = New-Object System.Collections.Generic.List[string]
    $sixCycle.Add('%YAML 1.1') | Out-Null
    $twoCycles.Add('%YAML 1.1') | Out-Null
    for ($index = 0; $index -lt 6; $index++) {
        $id = 100 + $index
        $next = 100 + (($index + 1) % 6)
        $sixCycle.Add('--- !u!114 &' + $id) | Out-Null
        $sixCycle.Add('MonoBehaviour:') | Out-Null
        $sixCycle.Add('  m_Next: {fileID: ' + $next + '}') | Out-Null

        $otherId = 200 + $index
        $otherNext = 200 + ((($index % 3) + 1) % 3) + (3 * [int]($index / 3))
        $twoCycles.Add('--- !u!114 &' + $otherId) | Out-Null
        $twoCycles.Add('MonoBehaviour:') | Out-Null
        $twoCycles.Add('  m_Next: {fileID: ' + $otherNext + '}') | Out-Null
    }
    $sixCycleAnalysis = Get-Analysis @($sixCycle.ToArray())
    $twoCyclesAnalysis = Get-Analysis @($twoCycles.ToArray())
    if ($sixCycleAnalysis.Canonical -ceq $twoCyclesAnalysis.Canonical) { $failures.Add('symmetric cycle topology was not detected') | Out-Null }
    if ((Get-ShortHash $sixCycleAnalysis.Canonical) -ceq (Get-ShortHash $twoCyclesAnalysis.Canonical)) { $failures.Add('symmetric cycle canonical hash was not distinct') | Out-Null }

    # Regression: individualizing either identical child in this symmetric three-node cycle
    # must persist through refinement.  Before colors were included in the next signature,
    # the "!" marker vanished and exact canonicalization recursed forever on this graph.
    $individualizationA = @(
        '%YAML 1.1', '--- !u!1 &1', 'GameObject:', '  m_Left: {fileID: 2}', '  m_Right: {fileID: 3}',
        '--- !u!114 &2', 'MonoBehaviour:', '  m_Root: {fileID: 1}',
        '--- !u!114 &3', 'MonoBehaviour:', '  m_Root: {fileID: 1}')
    $individualizationB = @(
        '%YAML 1.1', '--- !u!114 &30', 'MonoBehaviour:', '  m_Root: {fileID: 10}',
        '--- !u!1 &10', 'GameObject:', '  m_Left: {fileID: 20}', '  m_Right: {fileID: 30}',
        '--- !u!114 &20', 'MonoBehaviour:', '  m_Root: {fileID: 10}')
    $individualizationWatch = [Diagnostics.Stopwatch]::StartNew()
    $individualizationAnalysisA = Get-Analysis $individualizationA
    $individualizationAnalysisB = Get-Analysis $individualizationB
    $individualizationWatch.Stop()
    if ($individualizationAnalysisA.Canonical -cne $individualizationAnalysisB.Canonical) { $failures.Add('individualized symmetric graph did not canonicalize equal after fileID renumbering') | Out-Null }
    if ($individualizationWatch.Elapsed.TotalSeconds -gt 5) { $failures.Add('individualized symmetric graph did not finish promptly: ' + $individualizationWatch.Elapsed.TotalSeconds + 's') | Out-Null }

    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) { Write-Output ('SELFTEST FAIL: ' + $failure) }
        throw ('Self-test failed with ' + $failures.Count + ' failure(s).')
    }
    Write-Output 'SELFTEST: ok'
}

$script:RepoRoot = (& git -C $PSScriptRoot rev-parse --show-toplevel)
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($script:RepoRoot)) { throw 'Not inside a Git repository.' }
$script:RepoRoot = ([string]$script:RepoRoot).Trim().Replace('/', '\')

if ($SelfTest) { Invoke-SelfTest; exit 0 }
if ([string]::IsNullOrWhiteSpace($Base) -or [string]::IsNullOrWhiteSpace($Head)) { throw 'Base and Head are required (use WORKTREE for the working tree).' }

$basePaths = Get-RevisionPaths $Base
$headPaths = Get-RevisionPaths $Head
$candidates = @($basePaths + $headPaths | Sort-Object -Unique)
$inventoryRoots = @(
    (Get-AuthoritativeInventoryRoots $Base) +
    (Get-AuthoritativeInventoryRoots $Head) |
    Sort-Object -Unique
)
$selected = @(Resolve-SelectedPaths -Candidates $candidates -InventoryRoots $inventoryRoots -RequestedPaths @($Path))
$explicitPathRequest = @($Path).Count -gt 0
$basePathSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$headPathSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($relative in $basePaths) { [void]$basePathSet.Add($relative) }
foreach ($relative in $headPaths) { [void]$headPathSet.Add($relative) }
$changedPathSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$changedPaths = Get-ChangedPaths -BaseRevision $Base -HeadRevision $Head
foreach ($relative in $changedPaths) {
    [void]$changedPathSet.Add(([string]$relative).Replace('\\', '/'))
}
foreach ($relative in $candidates) {
    if ($basePathSet.Contains($relative) -ne $headPathSet.Contains($relative)) { [void]$changedPathSet.Add($relative) }
}
if (-not $explicitPathRequest) {
    $selected = @($selected | Where-Object { $changedPathSet.Contains([string]$_) })
}
if ($explicitPathRequest -and $selected.Count -eq 0) { throw 'No authoritative generated paths matched the requested path set.' }

Write-Output ('BASE: ' + $Base)
Write-Output ('HEAD: ' + $Head)

$anyChanged = $false
$totalBaseDangling = 0
$totalHeadDangling = 0
$reportedPathCount = 0
$semanticCheckedPathCount = 0
$notCheckedPathCount = 0

foreach ($relative in $selected) {
    $coverageKind = Get-PathCoverageKind $relative
    if ($coverageKind -cne 'supported text') {
        $basePresent = $basePathSet.Contains($relative)
        $headPresent = $headPathSet.Contains($relative)
        $reportedPathCount++
        $notCheckedPathCount++
        Write-Output ''
        Write-Output ('== ' + $relative)
        Write-Output ('  kind       ' + $coverageKind)
        Write-Output '  semantic   NOT CHECKED'
        if (-not $basePresent) { Write-Output '  presence   ADDED in head' }
        elseif (-not $headPresent) { Write-Output '  presence   REMOVED in head' }
        continue
    }

    $baseAnalysis = Get-Analysis (Get-BlobLines $Base $relative)
    $headAnalysis = Get-Analysis (Get-BlobLines $Head $relative)
    if ($null -eq $baseAnalysis -and $null -eq $headAnalysis) { continue }
    $reportedPathCount++
    $semanticCheckedPathCount++
    Write-Output ''
    Write-Output ('== ' + $relative)
    if ($null -eq $baseAnalysis) { Write-Output '  presence   ADDED in head'; $anyChanged = $true; $totalHeadDangling += $headAnalysis.Dangling; Write-Output ('  dangling   ' + $headAnalysis.Dangling); continue }
    if ($null -eq $headAnalysis) { Write-Output '  presence   REMOVED in head'; $anyChanged = $true; $totalBaseDangling += $baseAnalysis.Dangling; continue }

    $totalBaseDangling += $baseAnalysis.Dangling
    $totalHeadDangling += $headAnalysis.Dangling

    if (-not ($baseAnalysis.IsYaml -and $headAnalysis.IsYaml)) {
        $verdict = if ($baseAnalysis.Canonical -ceq $headAnalysis.Canonical) { 'IDENTICAL' } else { 'CHANGED' }
        Write-Output ('  kind       non-yaml text')
        Write-Output ('  lines      ' + (Format-Delta $baseAnalysis.DocCount $headAnalysis.DocCount))
        Write-Output ('  canonical  ' + $verdict)
        if ($verdict -ceq 'CHANGED') { $anyChanged = $true }
        continue
    }

    $addedAnchors = @($headAnalysis.Anchors.Keys | Where-Object { -not $baseAnalysis.Anchors.ContainsKey($_) } | Sort-Object)
    $removedAnchors = @($baseAnalysis.Anchors.Keys | Where-Object { -not $headAnalysis.Anchors.ContainsKey($_) } | Sort-Object)
    $guidChanges = Compare-KeyedCounts $baseAnalysis.GuidCounts $headAnalysis.GuidCounts
    $provenanceChanges = Compare-KeyedCounts (Get-CountMap $baseAnalysis.Provenance) (Get-CountMap $headAnalysis.Provenance)
    $instanceChanges = Compare-KeyedCounts $baseAnalysis.Instances $headAnalysis.Instances
    $verdict = if ($baseAnalysis.Canonical -ceq $headAnalysis.Canonical) { 'IDENTICAL' } else { 'CHANGED' }
    if ($verdict -ceq 'CHANGED') { $anyChanged = $true }

    # Default output carries only the lines a reviewer acts on: dangling references, prefab
    # instance drift, and the canonical verdict. Doc/anchor/ref/guid/provenance counts stay
    # computed and are one -Verbose away, next to the per-item change lists they summarize.
    Write-Output ('  dangling   ' + (Format-Delta $baseAnalysis.Dangling $headAnalysis.Dangling))
    Write-Output ('  instances  ' + $baseAnalysis.Instances.Count + ' -> ' + $headAnalysis.Instances.Count + ', changed ' + $instanceChanges.Count)
    Write-Output ('  canonical  ' + $verdict)

    if ($VerbosePreference -ne 'SilentlyContinue') {
        Write-Output ('  docs       ' + (Format-Delta $baseAnalysis.DocCount $headAnalysis.DocCount))
        Write-Output ('  anchors    +' + $addedAnchors.Count + ' -' + $removedAnchors.Count)
        Write-Output ('  refs       nonzero ' + (Format-Delta $baseAnalysis.NonZeroRefs $headAnalysis.NonZeroRefs))
        Write-Output ('  guids      ' + $baseAnalysis.GuidCounts.Count + ' -> ' + $headAnalysis.GuidCounts.Count + ', changed ' + $guidChanges.Count)
        Write-Output ('  provenance ' + $baseAnalysis.Provenance.Count + ' -> ' + $headAnalysis.Provenance.Count + ', changed ' + $provenanceChanges.Count)
        foreach ($anchor in $addedAnchors) { Write-Output ('    anchor+ ' + $anchor) }
        foreach ($anchor in $removedAnchors) { Write-Output ('    anchor- ' + $anchor) }
        foreach ($entry in $guidChanges) { Write-Output ('    guid    ' + $entry) }
        foreach ($entry in $provenanceChanges) { Write-Output ('    prov    ' + $entry) }
        foreach ($entry in $instanceChanges) { Write-Output ('    inst    ' + $entry) }
    }
}

Write-Output ''
Write-Output ('COVERAGE: ' + $reportedPathCount + '/' + $selected.Count + ' authoritative changed paths reported; semantic checked ' + $semanticCheckedPathCount + '; NOT CHECKED ' + $notCheckedPathCount)
Write-Output ('SEMANTIC: ' + $(if ($anyChanged) { 'changed' } else { 'identical' }))
Write-Output ('DANGLING: ' + $totalHeadDangling + ' (base ' + $totalBaseDangling + ')')

if ($FailOnDangling -and $totalHeadDangling -gt $totalBaseDangling) { exit 1 }
exit 0
