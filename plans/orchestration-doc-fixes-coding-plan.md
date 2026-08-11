# Orchestration Doc + Harness Fixes

Scope: fix confirmed defects in `.agents/skills/*` orchestration docs, `AGENTS.md` gaps, two code defects in harness/workflow. Direct edits, no orchestration skill. Source: audit of last 11 commits (`5a8a92e..cec01c6`) vs current docs.

Ordering: F1 first (merge blocker). F2-F3 code+doc pairs. F4-F8 doc-only, independent, any order.

## F1 — orphan baseline SHA breaks harness post-merge

Priority: CRITICAL. Merge blocker.

`Tools/Tests/Invoke-HarnessTests.ps1:144` -> `git show '73984e2:Tools/Validation/Invoke-MovementLabWorkflow.ps1'`. Also pinned `Tools/Tests/Invoke-HarnessTests.ps1:229` `redAtSha = '73984e2'`.

`73984e2` reachable only from `3_vs_3_bots`, `codex/bake-waste-elimination-*`, `codex/compile-bake-fix-*`. NOT ancestor of `main`.

Failure chain: squash/rebase merge -> SHA orphaned -> `throw 'Unable to read RedAtSha source'` -> harness exits non-zero -> mandatory pre-gate (`AGENTS.md:60`) can never pass -> all Unity work blocked on `main`.

Fix options, pick one:
- preferred: vendor red baseline as committed fixture file `Tools/Tests/Fixtures/red-workflow.ps1.txt` -> drop `git show` dependency entirely.
- alt: tag the commit (`git tag harness-red-baseline 73984e2`) + push tag, read via tag name.
- alt: on `git show` failure -> skip red-green cases with explicit `SKIP` result, never `throw`.

Reject: leaving raw SHA. Merge strategy is not controllable from harness.

Verify: `git merge-base --is-ancestor <ref> main` passes, or fixture path exists; harness green.

## F2 — impossible validate-before-bake ordering

Priority: HIGH.

`.agents/skills/loop-orchestrator/SKILL.md:84` -> "Before project-mutating production-final Unity proof, require ... and direct `RocketFooxball.Editor.MovementLabBuilder.ValidateMovementLab()` semantic validation."

`Assets/_Game/Editor/MovementLabBuilder.cs:123-124` -> `ValidateMovementLab()` fails closed when `bakedProfile != production` ("MovementLab validation requires a production lighting bake"). Validation before first production bake is unreachable.

Ground truth ordering: `AGENTS.md:74` -> one authoritative build -> THEN `ValidateMovementLab()` in separate process. Also `Invoke-MovementLabWorkflow.ps1:1277` `ProductionValidate` requires probe `bakedProfile=production`.

Cause: leftover of `ValidateMovementLabPreBake` row deleted in `e9d5c24`.

Fix: reorder to `ProductionPrepare` bake -> `-Mode ProductionValidate` semantic pass. Preconditions kept before bake = zero writers, clean exact source SHA, one Unity lease, accepted reviews/fixes. Drop `ValidateMovementLab()` from pre-bake precondition list.

## F3 — development bakes poison production bake budget

Priority: HIGH. Code + doc.

`Tools/Validation/Invoke-MovementLabWorkflow.ps1:1013` -> `if ($Method -match 'BakeMovementLabLighting') { $script:BakeCount++ }`. Substring regex also matches `...BakeMovementLabLightingDevelopment` (`:1429`). Reset to 0 happens only on production skip-marker path (`:1086`).

Effect: every `-Mode Development` run writes `bakeCount=1` to `workflow-result.json` (`:1573`). Skill rule "sum `bakeCount` across bound runs; cumulative `>=2` -> `blocked`" then counts sanctioned dev iteration (`AGENTS.md:79` "Development bake is explicit, on-demand") against production budget -> permanently blocks a production bake that never ran.

Code fix (root cause): exact match on production method.
```powershell
if ($Method -eq 'RocketFooxball.Editor.MovementLabBuilder.BakeMovementLabLighting') { $script:BakeCount++ }
```
Safe: dev path never reads `BakeCount`; production marker classification (`:1086`, `:1091`) unaffected.

Test: add case to `Tools/Tests/MovementLabHarness.Tests.ps1` pinning dev method -> no increment, production method -> increment.

Doc fix, same rule in 3 sites: `loop-orchestrator/SKILL.md:98`, `loop-orchestrator/references/state-and-recovery.md:117`, `orchestrate-implementation/SKILL.md:106`. Scope budget to results where `mode == 'ProductionPrepare'`.

## F4 — `harness-unit` reads as CLI argument

Priority: MED.

`.agents/skills/write-orchestrator-coding-plan/SKILL.md:124` -> "run `Tools/Tests/Invoke-HarnessTests.ps1` `harness-unit` first". Two adjacent backticked tokens parse as command+arg. Runner params: `EvidenceRoot`, `SkipHookCheck`, `HookMode`, `HookTestForceFailure` (`Tools/Tests/Invoke-HarnessTests.ps1:2-7`). Positional bind -> `-EvidenceRoot harness-unit` -> `Assert-EvidenceRoot` throws after all cases pass.

Runner has single fixed suite, no suite selector.

Fix: use full form from `AGENTS.md:60` -> `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1`. Keep `harness-unit` as prose suite name only, never adjacent to command. Peer wording already safe -> `orchestrate-implementation/SKILL.md:102` ("first for `harness-unit`" + "Never pass workflow arguments to test runner").

## F5 — reinstated pre-bake ceremony contradicts AGENTS.md

Priority: HIGH.

Sites: `loop-orchestrator/SKILL.md:84`, `loop-orchestrator/agents/merging.md:41`, `loop-orchestrator/references/state-and-recovery.md:118`, `orchestrate-implementation/SKILL.md:104`.

Claim: "Replacement invocation after any prior production-final attempt requires explicit user authority recorded before dispatch."

Conflicts:
- `AGENTS.md:80` -> "Later source edits reopen only affected checks. No extra pre-bake ceremony."
- `529748d` + `e9d5c24` -> builder self-skips on current lighting inputs; reused reattest records `bakeCount=0` (`Invoke-MovementLabWorkflow.ps1:1086`) -> reattest is already free.
- `Invoke-MovementLabWorkflow.ps1:921` -> `production-bake` row always `continue`d, never reused from prior ledger -> reattest is mandatory at every new SHA. Mandatory step gated behind manual authority = stall.

Fix: gate authority on predicted real rebuild only (lighting-input digest changed), not on any replacement invocation. Marker-proved reuse needs no authority.

Terminology drift to resolve in same pass: `SKILL.md:84` "Replacement bake" vs `merging.md:41` "Replacement invocation" -> pick "replacement bake".

## F6 — harness pre-gate is agent-enforced only

Priority: MED.

Orchestration runtime does not auto-run the harness. Nothing in the toolchain intercepts commands or edits. Manual invocation per `AGENTS.md:60` is the sole enforcement -> skills must treat it as a hard precondition the agent itself is responsible for, with explicit failure handling.

Two gaps:

Scope wording too narrow. `loop-orchestrator/SKILL.md:84`, `orchestrate-implementation/SKILL.md:94`, `merging.md:37` gate on "Unity-mutating workflow". Pre-gate belongs before any `Invoke-MovementLabWorkflow.ps1` or Unity invocation, including read-only `-Mode ProductionValidate` and `-PlanOnly` -> harness proves the wrapper itself is sound before trusting any verdict it emits.

Failure semantics absent. No skill states what happens when the harness is red. Required: harness non-zero -> `blocked`; repair harness by file edit; re-run to green before any workflow or Unity command. `orchestrate-implementation/SKILL.md:102` ("Harness defect -> run workflow `-PlanOnly` fixture first") inverts this — a red harness means the wrapper is unproven, so no workflow invocation is trustworthy until green.

Guard coverage worth naming in skills: editing `Tools/Tests/**`, `Tools/Validation/*.ps1`, or `Assets/_Game/Editor/MovementLab/*.cs` trips harness guards G1/G3/G4/G5 (`Tools/Tests/MovementLabHarness.Tests.ps1:494-528`) -> re-run harness after touching those paths.

## F7 — ledger ownership misattributed to harness

Priority: MED.

`loop-orchestrator/SKILL.md:84`, `loop-orchestrator/references/state-and-recovery.md:113`, `orchestrate-implementation/SKILL.md:184` -> "Harness writes sole executed `check-ledger.json`".

Truth: workflow writes `check-ledger.json` (`Invoke-MovementLabWorkflow.ps1:1522`, `:1554`); row `owner` literal is `workflow-orchestrator` (`:731`). Harness runner writes only `harness-summary.json` (`Tools/Tests/Invoke-HarnessTests.ps1:243`).

Fix: "Workflow writes sole executed `check-ledger.json`; harness writes `harness-summary.json`."

## F8 — AGENTS.md updates

Priority: MED. Net 87 -> 89 lines. No deletions.

E1, replace `AGENTS.md:26`:
```
- Editor tooling: command facade `Assets/_Game/Editor/MovementLabBuilder.cs` -> domain pipelines `Assets/_Game/Editor/MovementLab/*.cs`; namespace `RocketFooxball.Editor`
- Validation workflow `Tools/Validation/Invoke-MovementLabWorkflow.ps1`; harness suite `Tools/Tests/`
```
Rationale: `Tools/` tree absent from repo map though `AGENTS.md:81` already references its probe schema. `MovementLabBuilder.cs:10` self-describes as facade; 19 pipeline files live under `MovementLab/`.

E2, append after `AGENTS.md:52`:
```
- Atomic generated-file replacement: `File.Replace(` only in `Assets/_Game/Editor/MovementLab/MovementLabAtomicFile.cs`; every `Tools/Validation/*.ps1` must parse clean. Harness guards G4/G5 enforce both.
```
Rationale: guards `MovementLabHarness.Tests.ps1:503-528` hard-fail the pre-gate with no policy statement explaining the block.

Do not touch `AGENTS.md:66`. Bake marker text, `reused`/`bakeCount=0`, duplicate-marker failure all verified exact vs `MovementLabBuilder.cs:13` and `Invoke-MovementLabWorkflow.ps1:1081-1094`.

Do not touch `AGENTS.md:60`. Manual pre-gate command and `<10s` budget verified correct against `Tools/Tests/Invoke-HarnessTests.ps1:227,257`.

## F9 — dedup pass

Priority: LOW. Do last, after F2-F7 land.

Bake gate restated near-verbatim 4x -> `loop-orchestrator/SKILL.md:98`, `loop-orchestrator/agents/merging.md:41`, `loop-orchestrator/references/state-and-recovery.md:117-119`, `orchestrate-implementation/SKILL.md:104`. Already drifted (F5 terminology). Keep once in `state-and-recovery.md#production-bake-gate`; link from others.

`write-orchestrator-coding-plan/SKILL.md`: ~60-80 of 262 lines restate `AGENTS.md:66,74,75,79-80` or restate own rules 3x (decomposition mismatch `:45`,`:77`,`:84`; sizing gate `:92`,`:222`,`:258`; fan-out `:82`,`:175`,`:259`). Replace duplicated Unity/bake policy with pointer to `AGENTS.md`. Delete prose field list `:115` (conflicts with template `:181-202`); template is single source.

`orchestrate-implementation/SKILL.md:85` long-form 5-level recovery ladder duplicates compressed `:198` -> keep `:198`, delete `:85` long form.

Dead line: `write-orchestrator-coding-plan/SKILL.md:262` `git diff --check` over two skill paths the planner is forbidden to edit (`:8`, `:19`) -> always empty. Delete.

## Verification

Doc-only changes (F2, F4-F9): inspect diff. No Unity launch (`AGENTS.md:78`).

Code changes (F1, F3): run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1` -> expect all cases PASS, `<10s`, exit 0. No Unity mutation required.

F1 extra: confirm chosen baseline ref survives merge to `main`.

Cross-check after F2-F7: same rule must not disagree across `AGENTS.md`, `loop-orchestrator/*`, `orchestrate-implementation/SKILL.md`, `write-orchestrator-coding-plan/SKILL.md`.
