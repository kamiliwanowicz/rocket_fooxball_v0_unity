# Compile/Bake Pipeline Fix Handoff

Reader role: agent tasked with making Unity build/bake dev-process smooth. T7 + run-efficiency plan DONE — worktree `C:\wt\eff-091dc7` merged into `new_graphics` @ `e999a7d`, 44 commits. This doc = what remains to fix so next plan does not take 10x estimated time. Verified against merged code 2026-08-11.

## Why runs took 6h not 1h (forensic result, 43 fix commits classified)

- 58% self-inflicted machinery (leftover byte gates 6, policy contradictions 15, blind-authored checks 5); genuine Unity behavior + real bugs only 26%; content 14%
- structural cause -> first-fail throws + ~15-20min Unity cycle per blocker -> N defects = N runs
- every "relax/unblock" commit added compensating fail-closed checks; ~50% re-blocked within 3 commits (`53333d6`, `bfd5582`, `d8aecd3`, `a1448f6`, `2490dc8` all did this)
- worst offender: `MovementLabStageGraph.cs` drift-gate family -> 9 fix cycles, 4 accreted migration allowlists; wholesale deletion at first blocker would have saved 8 cycles
- all 7 Unity-behavior blockers = assertions written blind against un-run engine APIs (`reflectionBounces=0`, `customReflection` mode mismatch, scene handle after reopen, YAML `- ` marker regex, HLSL `line` keyword, `GetCorrespondingObjectFromSource` on persistent, active-scene-after-gate)

## TODO (priority order)

### 1. Accumulator refactor — highest leverage, NOT done

- current counts: `Assets/_Game/Editor/MovementLab/*.cs` -> 385 throws (was 389; net deletion ~zero); all Editor C# 438; `Tools/Validation/*.ps1` 199; combined 637
- one-defect-per-run failure mode structurally unfixed
- convert stage validators -> collect all violations -> single throw at stage end. Pattern already exists: `MovementLabStageGraph` violations list, `Invoke-MovementLabWorkflow.ps1:630` demote-to-invalidated
- density targets: `Invoke-MovementLabWorkflow.ps1` 117, `MovementLabPrefabPipeline.cs` 66, `MovementLabLightingPipeline.cs` 45, `MovementLabFastModeSession.cs` 38, `MovementLabPreBakeGate.cs` 34
- estimated effect: ~25 machinery cycles -> 4-6

### 2. Kill last hard gates

- `Assets/_Game/Editor/MovementLab/MovementLabManifestStore.cs:197` -> "manifest write rejected" on recomputed-fingerprint mismatch -> LAST builder-output byte gate in repo. Delete or demote to warning.
- `Assets/_Game/Editor/BrightArenaVisualCapture.cs:243` -> "scene became dirty during validation" hard throw -> fires whenever any validator mutates state. Fix = demote to warning; validator-mutation audit separately.
- `Tools/Validation/Write-MovementLabPreBakeReviewMarker.ps1` -> 50 throws, review-marker era; policy rejects review markers -> candidate wholesale delete.
- `BrightArenaVisualCapture.cs:466` -> 40-char git SHA plumbing, exactness-era; currently satisfied by wrapper (`Capture-BrightArenaVisuals.ps1:157`), non-blocking -> delete when touching file, not urgent.

### 2b. Scrap agent image verification

- USER DIRECTIVE: agent-driven image/capture verification takes too long -> remove from routine workflow
- drop capture steps + screenshot-diff/visual-inspection gates from build/validate loops: `BrightArenaVisualCapture.cs`, `Capture-BrightArenaVisuals.ps1`, `-Capture` mode of `Invoke-MovementLabWorkflow.ps1`, capture rows in evidence ledger
- visual acceptance -> human eyeballs on demand, not automated gate; keep capture scripts runnable manually but never required for pass/fail
- future plans -> no "capture N views + verify no magenta" validation steps; shader-compile check (cheap, semantic) suffices for magenta class

### 3. Phase-2 demolition (separate plan)

- machinery ~6430 LOC vs content ~3650; no-op build costs only ~15s -> staleness-tracking machinery has low value
- candidates: `MovementLabStageGraph` + `MovementLabStageRunner` + `MovementLabManifestStore` + most `MovementLabPreBakeGate` -> survivor set ~500 LOC semantic checks (prefab provenance, emission flags, shader compile, GUID/.meta pair, geometry budgets)
- keep battle-tested checks; they carry paid tuition

### 4. Codify process rules (add to AGENTS.md or skill)

- new validation check -> lands as warning; promote to throw only after 1 green real run. Applies double to any assertion about Unity engine round-trip behavior — never author fail-closed against un-run API.
- relaxation/unblock commit -> must be net-subtractive in gate files (deletions > insertions). Violations this run: `bfd5582` +130, `53333d6` +165 StageGraph, `d8aecd3`, `a1448f6` — each produced follow-on blockers.
- 2nd fix in same gate predicate -> delete predicate family wholesale, stop patching
- policy pivot -> execute as ONE grep-sweep commit (`SHA256`, `Get-FileHash`, `ComputeHash`, `-cne`, `HashFile`), not incremental; incremental removal cost 6 extra cycles this run
- NEVER re-add byte/hash equality on builder outputs, any language
- one Unity process at a time; `-batchmode -quit`; check `UnityLockfile` released

### 5. Doc/branch hygiene

- `plans/graphics-overhaul-run-efficiency-coding-plan.md` -> still `Status: in progress`, T7 unmarked, `Orchestrator Resume Point` points at dead worktree/HEAD `be51d6f` -> mark complete, move to `plans/completed/`
- worktree `C:\wt\eff-091dc7` + branch `codex/graphics-overhaul-run-efficiency-20260810T111004014-091dc7` -> merged, deletable
- `AGENTS.md` -> verified CLEAN: byte-compare/reserialization/no-op-gate rules all updated (`:54`, `:65`, `:71`, `:78`); no action

## Resolved — do not re-investigate

- byte-exactness gates: `AssertExactEquality` gone repo-wide; capture byte gates deleted (`eab1518`, `9d65444`, `51b0197`, `f5ec7c2` dirty-scope); `.meta` digest -> GUID-only compare, violation-collected (`d8aecd3` + `cee70d6`)
- PS1 dirty-tree throws narrowed to `ProductionPrepare` only (`:1057/:1150/:1151`); `ProductionValidate -Capture` unaffected
- fast-mode `HashFile` check (`MovementLabFastModeSession.cs:262`) -> soft skip, benign
- remaining PS1 hash throws = same-run self-consistency or source-tamper detect, not cross-run determinism -> acceptable
- shader `line` keyword, binary `LightingData.asset` normalization corruption, capture budget caps (50k->100k tris), emission-map, `Test-Path -and` parse bug -> all fixed in merged history

## Evidence

- forensic commit classification: range `2a45007..104e6d1`, pivot `bfd5582`
- experiments/costs: `plans/graphics-overhaul-run-efficiency-handoff.md` (dev bake 31s, production bake 265s, no-op 15s, warm compile 13s, 40-vs-200 probes visually indistinguishable)
- pass records: `.git/architecture-evidence/movement-lab-prebake/passes/`
