# Bake Waste Elimination Coding Plan

Status: accepted
Source: direct request; synthesis of `plans/compile-and-bake-investigation-handoff.md` + 3-subagent audit (process, forensics, test strategy) of run `20260811T084153389-f901a0`
Run ID: direct
Plan ID: direct
Attempt ID: direct
Covered Requirements: direct request
Baseline: 7abaccafe9361aaf1dc5c36615bc808d0d19cb9b
Dependencies: None

## Objective

Make AI-orchestrated development fast and reliable by removing the property that caused 6 of 7 wasted production bakes: workflow decisions observable only by paying for a Unity bake. Four moves -> (1) faithful `-PlanOnly` dry-run + pre-bake assertion hoist; (2) atomic-write consolidation closing live D3-class sites; (3) bake staleness gate relocated into C# builder, PowerShell bake-reuse subsystem deleted, bake inputs narrowed to lighting-only; (4) sub-10s harness test layer + policy circuit-breakers.

Completion boundary: harness unit/static suite green at final head; `-PlanOnly` fixture demonstrates reuse without Unity; one real `ProductionPrepare` + generated-output disposition + separate `ProductionValidate` at exact final SHA.

## Scope

- in: `Tools/Validation/Invoke-MovementLabWorkflow.ps1`; `Assets/_Game/Editor/MovementLabBuilder.cs`; `Assets/_Game/Editor/MovementLab/MovementLabManifestStore.cs`; `MovementLabPreBakeGate.cs`; `MovementLabStageRunner.cs`; `MovementLabLightingProfiles.cs`; new `Assets/_Game/Editor/MovementLab/MovementLabAtomicFile.cs`; new `Tools/Tests/` (3 files); `.claude/settings.json` hook; `AGENTS.md`; `.agents/skills/orchestrate-implementation/SKILL.md`; `.agents/skills/write-orchestrator-coding-plan/SKILL.md`; `.agents/skills/loop-orchestrator/SKILL.md`; `.agents/skills/loop-orchestrator/references/state-and-recovery.md`; `.agents/skills/loop-orchestrator/agents/merging.md`
- out: gameplay/runtime scripts; arena/lighting look; Unity/package versions; scene fileID or lightmap determinism work (refuted, `plans/compile-and-bake-investigation-handoff.md:113-116`); psm1 extraction of harness (rejected, module-scope divergence on 29 `$script:ProjectRoot` refs); Pester 5 installation (PSGallery blocked non-interactive; only Pester 3.4.0 present); EditMode C# test tier (deferred follow-up; needs `InternalsVisibleTo` production change); lease/lock/path-safety harness machinery (next audit target, untouched here)
- out: re-diffing prior run evidence; migrating/backfilling historical ledger rows (prohibited; subsystem deletion makes moot)

## Repository Findings

- observed: `Tools/Validation/Invoke-MovementLabWorkflow.ps1:1583` -> `if ($PlanOnly) { $row.status = 'deferred' }` unconditional, runs AFTER `Merge-ExistingLedger:1494` computed `reused|invalidated` -> every reuse verdict erased before evidence write -> reuse observable only via real bake. Root enabler of bakes #5-#7 and terminal PlanOnly wall of prior run
- observed: `:1534` `Assert-ProbeContractForMode` for ProductionPrepare runs AFTER bake `:1526`; bakes #1-#3 died in post-bake assertions consuming nothing the bake produced
- observed: `:1004` sets `invalidated` with empty `invalidation_reason`; indistinguishable from `:1583` clobber in emitted evidence
- observed: D1/D2/D3 handoff fixes already at HEAD (`fe83833` -> IDictionary branch `:847-850` + harness script removed from `$productionBakeInputs:777-798`; `7b4208c` -> lighting retry). Tests are regression pins, not fix drivers
- observed: 3 raw `File.Replace` sites unguarded -> `MovementLabManifestStore.cs:569`, `MovementLabPreBakeGate.cs:425`, `MovementLabStageRunner.cs:258`; `ReplaceAtomicWithRetry` duplicated byte-identical in `MovementLabLightingProfiles.cs:261` + `MovementLabManifestStore.cs:356`. D3 class open; each site can kill a future bake post-completion
- observed: `:930-932` residual dictionary-blind idiom inside `Test-ProductionBakeReuseProof` (function deleted by T4)
- observed: bake-reuse subsystem ~250 lines (`Test-ProductionBakeReuseProof:889`, `Test-ProductionBakeProbeReuseProof:942`, `Confirm-ProductionBakeReuse:958`, `Get-PriorManifestForRow:880`, `Refresh-ProductionBakeOutputState:971`, `Merge-ExistingLedger:1040-1067` bake branches) -> lifetime hit rate 0 reuses / 7 bakes; activation flag `-LedgerPath` named in zero skill files
- observed: `$productionBakeInputs:777-798` still contains `MovementLabMaterialPipeline.cs`, `MovementLabPrefabPipeline.cs`, `MovementLabSceneComposer.cs`, `MovementLabArenaPipeline.cs`, `GraphicsQualityConfigurator.cs`, `Assets/InputSystem_Actions.inputactions(+.meta)`, `Packages/*`, `ProjectSettings/ProjectVersion.txt` -> input-map tuning invalidates 6-min lighting bake (live D2-class)
- observed: `ProductionPrepare` chain `:1511-1534` = Probe + StaleAssembly + PreBakeValidate + ProductionBake = 4 Unity launches; PreBakeValidate duplicates gate already inside `MovementLabBuilder.BakeMovementLabLighting` (`MovementLabBuilder.cs:87` calls `MovementLabPreBakeGate.ValidateAndWritePassRecord(Production)`)
- observed: `MovementLabBuilder.cs:75-100` `BakeMovementLabLighting` has no staleness guard, bakes unconditionally; digest already persisted (`Assets/_Game/Lighting/MovementLabLightingManifest.json` `lightingInputDigest`), probe already recomputes (`MovementLabStageGraph.Probe()`), builder already consumes `probe.IsStale(...)` at `:27`. `AGENTS.md` no-op-gate rule never applied to bake entrypoint
- observed: `Get-EnvironmentFingerprint:659-671` hashes `machine` + `os` + `powershell` -> OS build bump invalidates all proof on single-machine PoC
- observed: zero tests in repo; `com.unity.test-framework: 1.7.0` unused; Pester 3.4.0 only (no Pester 5 syntax support); `Find-Module` fails non-interactive
- observed: AST shim approach verified end-to-end under WinPS 5.1.26100.8521 -> `[Parser]::ParseFile` 0 errors, 62 `FunctionDefinitionAst`; selected functions + top-level `$script:` assignments -> `New-Module` -> callable; 189-436ms; D1 red reproduced at `73984e2` via `git show` + `ParseInput`, no checkout
- observed: uncommitted `AGENTS.md` change at baseline -> user deleted line "Test creation deferred unless user requests it" -> test carve-out user-authorized; preserve and complete in T6
- observed: generated outputs on branch baked at `73984e2`; later commits changed lighting sources (`MovementLabLightingProfiles.cs`) -> bake proof stale -> FINAL owes exactly one real bake
- observed: policy contradictions C1 (3 ledger writers vs "LP sole writer"), C2 (bake invalidation scope), C4 (pure-reattest wording vs mutating bake exception), C7 (stale `AGENTS.md:30` plan reference), C8 (harness invocation undocumented); recovery ladder `orchestrate-implementation/SKILL.md:85` authorizes replacement bake runs cost-blind
- constraint: one Unity process per project; short-path worktree; warm private `Library`; hidden `Start-Process -Wait -PassThru`; lock release checks
- constraint: builder-generated output bytes never gate; source/input digests and same-run evidence hashes remain valid
- constraint: live orchestrator may exist in `C:\wt\cbf-f901a0` -> never touch
- proposed: `Assets/_Game/Editor/MovementLab/MovementLabAtomicFile.cs` -> single shared atomic-replace helper
- proposed: `Tools/Tests/HarnessShim.psm1`, `Tools/Tests/MovementLabHarness.Tests.ps1`, `Tools/Tests/Invoke-HarnessTests.ps1` -> sub-10s harness suite; located outside `Tools/Validation` to avoid membership in `compile`/`production-validator` input paths (self-invalidation, exact D2 pathology)

## Decisions

- decision: fix observability first (T1), then encode as tests (T5) -> tests cannot assert on statuses the script overwrites
- decision: plain PowerShell assertion runner, not Pester -> Pester 3.4.0 cannot run Pester-5 syntax; gallery install blocked non-interactive; single AI consumer needs exit code + failure text only; file structure kept Pester-5-migratable
- decision: AST function shim, not psm1 extraction and not dot-source -> shim verified working; psm1 breaks `$script:` scope silently (new D1-class); dot-source executes 340 lines of side-effectful top-level code
- decision: bake gate lives in C# beside its data; harness keeps narrowed `$productionBakeInputs` digest as provenance only, never reuse comparator
- decision: skip `:930-932` idiom fix in T1 -> containing function deleted by T4; G1 static guard prevents reintroduction
- decision: keep Probe Unity step (produces probe JSON consumed by hoisted pre-bake assertions); delete only PreBakeValidate step (duplicate of in-bake gate)
- decision: harness postflight accepts `bakeCount 0` only when Unity log contains machine-detectable skip marker; row status `reused` on skip, `executed` on bake
- decision: `Get-EnvironmentFingerprint` drops `machine` + `os`, keeps `powershell` + Unity version/path
- decision: narrowed bake input list authored as exact literal derived from `MovementLabStageGraph` lighting stage contract + `Assets/_Game/Lighting/**`; locked permanently by test case `bake-inputs-literal`
- decision: red/green discipline mechanical -> regression case carries `RedAtSha`; runner executes case against HEAD text (must pass) and `git show <sha>` blob (must fail); passing both -> case fails as decoration. Applies only to functions existing at both SHAs
- decision: FINAL real `ProductionPrepare` expected to bake exactly once (lighting source changed since `73984e2` under new contract); plan acceptance is the user authority for that run
- assumption: uncommitted `AGENTS.md` test-deferral deletion is intentional user carve-out; T6 commits it plus replacement rule
- question: None

## Execution Graph

`START -> {T1 -> CP1 || T2 -> CP2 -> T3 -> CP3} -> JOIN1 -> T4 -> CP4 -> {T5 -> CP5 || T6 -> CP6} -> JOIN2 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in
- gates: `START` -> baseline snapshot in isolated short-path worktree, owned paths clean except recorded `AGENTS.md` deletion, one Unity lease; `JOIN1` -> CP1 + CP3 accepted, no writers; `JOIN2` -> CP5 + CP6 accepted, all accepted findings fixed, no writers; `FINAL` -> harness suite green, plan-only fixture proof, one real production run, exact-head validation, cleanliness
- lane 1: T1 sole writer of workflow ps1 until T4
- lane 2: T2 -> T3 serial (shared C# compile environment, one Unity lease); paths disjoint from lane 1
- T4 requires JOIN1: same-file dependency on T1; behavioral dependency on T3 guard existing before PowerShell reuse deletion
- T5 || T6 after CP4: disjoint paths (`Tools/Tests` + `.claude` vs policy files); both pin/document post-T4 contract
- shared paths, generated outputs, product decisions -> sequential throughout

## Tasks

### T1: Faithful PlanOnly + pre-bake assertion hoist

- objective: `-PlanOnly` emits true merge verdicts; every assertion not consuming bake output runs before Unity bake step
- slice_boundary: dominant invariant = workflow decisions observable without bake; includes status-stamp fix, invalidation-reason fill, assertion reordering; excludes reuse-subsystem deletion (T4) and any C#
- covered_requirements: direct request slice (observability)
- owner: W1 implementation worker
- depends_on: None
- owns: `Tools/Validation/Invoke-MovementLabWorkflow.ps1`
- protected: `Merge-ExistingLedger` verdict semantics; lease/lock/path/process/atomic evidence functions; evidence hashing; Unity invocation machinery
- read_paths: owned path; `plans/compile-and-bake-investigation-handoff.md`; prior-run ledger fixtures under `<git-common-dir>/orchestrate-implementation/compile-and-bake-issues-fix/evidence/` (read-only, `\\?\` prefix + `[System.IO.File]::ReadAllText`; never `Get-ChildItem -Recurse`)
- validation_environment: Windows PowerShell 5.1 parser + `-PlanOnly` runs; durable evidence outside project; zero Unity
- unity_mutation: false
- expensive_proof_owner: execution orchestrator
- expensive_proof_run_point: FINAL
- proof_invalidation_paths: owned path
- focused_reads: `:1583` stamp loop; `:1494` merge call site; `:1004` invalidation write; `:1511-1534` ProductionPrepare step chain + `Assert-ProbeContractForMode` call sites; postflight block for bake-independent predicates
- implementation:
  1. `:1583` -> `if ($PlanOnly -and $row.status -eq 'pending') { $row.status = 'deferred' }`; `reused`/`invalidated` survive to emitted ledger
  2. `:1004` and every `invalidated` assignment -> require non-empty `invalidation_reason`; write specific reason string (`'prior status <s> not accepted'`, `'input digest changed: <field>'`, etc.)
  3. Move ProductionPrepare `Assert-ProbeContractForMode` (`:1534`) to immediately after probe read, before StaleAssembly/PreBakeValidate/ProductionBake `Invoke-UnityStep` calls
  4. Audit postflight predicates: relocate every predicate whose inputs exist pre-bake (mode contract, config/argument shape, probe-derived facts) to pre-bake position; keep genuinely bake-dependent predicates (clean-tree, generated inventory, bake count, HEAD stability) postflight. Record classification list in return evidence
  5. No behavior change to non-PlanOnly execution ordering beyond assertion hoist
- done when: `-PlanOnly` with prior accepted-ledger fixture emits `reused` rows in evidence JSON; malformed probe fixture fails before any Unity step would launch; every `invalidated` row carries non-empty reason
- checks:
  - check_id: `t1-powershell-parse`; tier: `fast`; mutates_project: `false`; input_paths: owned path; input_digest: execution-computed; environment_fingerprint: WinPS parser version; invalidation_paths: owned path; subsumes: None; run_point: before CP1; evidence: `[Parser]::ParseFile` zero errors
  - check_id: `t1-planonly-fixture`; tier: `fast`; mutates_project: `false`; input_paths: owned path + fixture copies in durable temp; input_digest: execution-computed; environment_fingerprint: WinPS + Git version; invalidation_paths: owned path; subsumes: None; run_point: before CP1; evidence: (a) `ProductionPrepare -PlanOnly -LedgerPath <prior-executed-ledger-copy>` at matching SHA -> rows `reused`, zero empty `invalidation_reason`; (b) same command with digest-mismatched fixture -> `invalidated` + named reason; (c) malformed probe fixture -> failure before Unity step, process/lock clean
- proof: fixture pair (a)/(b) is discriminatory -> same code path, opposite verdicts, both visible in PlanOnly output; impossible at baseline where `:1583` erased both
- review_focus: stamp condition accidentally preserves stale `pending`->`deferred` semantics for real runs -> false green; hoisted assertion reads probe field populated only by bake -> pre-bake crash on green path; postflight predicate relocated that actually consumes bake output -> false pass
- review_checkpoint: CP1
- return_evidence: changed line spans; pre/post assertion classification table; fixture ledgers + emitted results paths; parse log

### T2: One atomic-replace helper, five call sites

- objective: single shared `ReplaceAtomicWithRetry`; zero raw `File.Replace` in editor tooling
- slice_boundary: dominant invariant = every manifest/evidence/probe write atomic-with-retry; includes helper extraction + 5 site routings; excludes builder bake logic (T3)
- covered_requirements: direct request slice (D3 class closure)
- owner: W2 implementation worker
- depends_on: None
- owns: new `Assets/_Game/Editor/MovementLab/MovementLabAtomicFile.cs`; `Assets/_Game/Editor/MovementLab/MovementLabManifestStore.cs`; `Assets/_Game/Editor/MovementLab/MovementLabPreBakeGate.cs`; `Assets/_Game/Editor/MovementLab/MovementLabStageRunner.cs`; `Assets/_Game/Editor/MovementLab/MovementLabLightingProfiles.cs`
- protected: write ordering/authorization semantics at each site; manifest schemas; `MovementLabBuilder.cs`
- read_paths: owned paths; `MovementLabManifestStore.cs:356` + `MovementLabLightingProfiles.cs:261` (canonical duplicate bodies, byte-identical)
- validation_environment: isolated short-path worktree; one hidden compile-only Unity batch (`6000.5.6f1`, warm private `Library`); no bake
- unity_mutation: false
- expensive_proof_owner: execution orchestrator
- expensive_proof_run_point: FINAL
- proof_invalidation_paths: owned paths
- focused_reads: raw sites `MovementLabManifestStore.cs:569`, `MovementLabPreBakeGate.cs:425`, `MovementLabStageRunner.cs:258` -> each site's temp-file lifecycle and backup expectations before routing
- implementation:
  1. Create `internal static class MovementLabAtomicFile` with `ReplaceAtomicWithRetry(string sourcePath, string destinationPath)` -> lift canonical body (win32 retry classification 32/33/1175, bounded retry, temp cleanup in `finally`) verbatim from `MovementLabManifestStore.cs:356`
  2. Replace both private duplicates with calls; delete duplicate bodies
  3. Route the 3 raw `File.Replace` sites through helper; preserve each site's existing temp-write-then-replace sequence and error surface (exception type callers observe on final failure unchanged)
  4. No signature/behavior change outside replacement mechanics
- done when: `rg 'File.Replace\('` over `Assets/_Game/Editor` matches only inside `MovementLabAtomicFile`; Unity compile clean
- checks:
  - check_id: `t2-unity-compile`; tier: `fast`; mutates_project: `false`; input_paths: owned paths + editor assembly deps + `Packages` + `ProjectSettings`; input_digest: execution-computed; environment_fingerprint: Unity `6000.5.6f1`, short path, warm `Library`; invalidation_paths: same; subsumes: None; run_point: before CP2; evidence: hidden compile log, exit 0, zero Console errors, process/lock release
  - check_id: `t2-raw-replace-sweep`; tier: `fast`; mutates_project: `false`; input_paths: `Assets/_Game/Editor`; input_digest: execution-computed; environment_fingerprint: `rg` version; invalidation_paths: `Assets/_Game/Editor`; subsumes: None; run_point: before CP2; evidence: single-container match set
- proof: sweep is discriminatory (5 matches at baseline -> 1 container at head); FINAL bake exercises lighting + manifest write paths live
- review_focus: helper drops a site's backup-path semantics -> silent data loss on replace failure; retry loop swallows non-transient IO error class -> corruption proceeds; probe/evidence write ordering changed -> harness reads partial file
- review_checkpoint: CP2
- return_evidence: helper source; per-site before/after call diff; sweep output; compile log

### T3: Bake staleness gate in builder

- objective: `BakeMovementLabLighting` self-skips when lighting inputs current; skip machine-detectable
- slice_boundary: dominant invariant = bake cost gated beside its data; includes guard + skip logging + probe write on skip; excludes harness consumption of skip (T4)
- covered_requirements: direct request slice (bake gate relocation)
- owner: W3 implementation worker
- depends_on: CP2 accepted (serialized C# lane)
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`
- protected: bake implementation body; `MovementLabPreBakeGate`; `MovementLabStageGraph.Probe()` semantics; lighting profiles; generated outputs
- read_paths: `MovementLabBuilder.cs:20-100`; `MovementLabStageGraph` probe/staleness API; `Assets/_Game/Lighting/MovementLabLightingManifest.json` shape
- validation_environment: isolated short-path worktree; one hidden compile-only Unity batch; no bake
- unity_mutation: false
- expensive_proof_owner: execution orchestrator
- expensive_proof_run_point: FINAL
- proof_invalidation_paths: owned path; `MovementLabStageGraph.cs`; `Assets/_Game/Lighting`
- focused_reads: `MovementLabBuilder.cs:27` existing `probe.IsStale(...)` consumption pattern; `:37` + `:97` `WriteProbeIfRequested` call points -> skip path must still write requested probe
- implementation:
  1. Top of `BakeMovementLabLighting` (`:75`): run stage-graph probe; when production profile active AND `!probe.IsStale(Lighting)` AND `!probe.IsStale(BakedOutput)` -> log exact marker `[MovementLab] production bake skipped: lighting inputs current (digest <lightingInputDigest>)`, call `WriteProbeIfRequested`, return without bake
  2. Stale or non-production path unchanged; guard purely subtractive on green-current state
  3. Marker string defined as `internal const` for harness/test reference stability
- done when: compile clean; guard readable as single early-return block; marker constant referenced nowhere else yet
- checks:
  - check_id: `t3-unity-compile`; tier: `fast`; mutates_project: `false`; input_paths: owned path + editor deps + `Packages` + `ProjectSettings`; input_digest: execution-computed; environment_fingerprint: Unity `6000.5.6f1`, short path, warm `Library`; invalidation_paths: same; subsumes: `t2-unity-compile`; run_point: before CP3; evidence: compile log exit 0, zero Console errors, lock release
- proof: static review of guard + compile; live skip/bake behavior proven at FINAL (first run bakes -> stale inputs; hypothetical second run would skip -> marker). Deferred runtime proof acceptable: guard consumes only pre-existing, already-consumed probe API
- review_focus: guard skips when `BakedOutput` stale but `Lighting` current -> stale lightmaps shipped as current; probe not written on skip -> harness probe assertions fail; guard runs in non-production profile -> fast-preview iteration broken
- review_checkpoint: CP3
- return_evidence: guard diff; marker constant; compile log

### T4: Harness simplification — delete bake-reuse subsystem, narrow inputs, drop duplicate Unity step

- objective: harness trusts builder gate; production-bake rows carry provenance only; ProductionPrepare launches 3 Unity steps not 4
- slice_boundary: dominant invariant = one bake decision owner (C# builder); includes subsystem deletion, input narrowing, step removal, skip-marker consumption, fingerprint slimming; excludes test layer (T5) and policy text (T6)
- covered_requirements: direct request slice (subsystem deletion)
- owner: W4 implementation worker
- depends_on: JOIN1 (CP1 + CP3 accepted)
- owns: `Tools/Validation/Invoke-MovementLabWorkflow.ps1`
- protected: generic exact-SHA row reuse for fast rows; compile + `production-validator` input sets (workflow script stays member); lease/lock/path/process/atomic machinery; evidence hashing; T1 changes
- read_paths: owned path post-T1; `MovementLabBuilder.cs` post-T3 (marker constant); `MovementLabStageGraph` lighting stage contract (authoritative narrowed input list source)
- validation_environment: WinPS parser + all four modes `-PlanOnly`; durable evidence outside project; zero Unity
- unity_mutation: false
- expensive_proof_owner: execution orchestrator
- expensive_proof_run_point: FINAL
- proof_invalidation_paths: owned path; `MovementLabBuilder.cs`; `MovementLabStageGraph.cs`
- focused_reads: `Merge-ExistingLedger:980-1075` branch structure -> excise bake-special branches, keep generic; `:1511-1534` step chain; `:1528` bake-count postflight; `Get-EnvironmentFingerprint:659-671`
- implementation:
  1. Delete `Test-ProductionBakeReuseProof`, `Test-ProductionBakeProbeReuseProof`, `Confirm-ProductionBakeReuse`, `Get-PriorManifestForRow`, `Refresh-ProductionBakeOutputState`, and production-bake-specific branches of `Merge-ExistingLedger` (~250 lines). Generic exact-SHA reuse for non-bake rows unchanged
  2. Replace `$productionBakeInputs` with exact lighting-only literal: `Assets/_Game/Lighting/**` members + lighting stage sources from `MovementLabStageGraph` contract (`MovementLabLightingPipeline.cs`, `MovementLabLightingProfiles.cs`, lighting settings asset, volume profile). Drop material/prefab/scene-composer/arena/graphics-configurator sources, `InputSystem_Actions.inputactions(+.meta)`, `Packages/*`, `ProjectVersion.txt`. Digest recorded as provenance in row; no comparator consumes it
  3. Delete `PreBakeValidate` `Invoke-UnityStep` from ProductionPrepare chain and its `prebake-validate` ledger row (gate runs inside bake, `MovementLabBuilder.cs:87`). Keep Probe step
  4. ProductionBake step: parse Unity log for T3 marker constant -> present + `bakeCount 0` -> row status `reused`, record marker line as evidence; absent -> require `bakeCount == 1`, status `executed`. Any other combination -> hard fail with named reason
  5. `Get-EnvironmentFingerprint` -> drop `machine`, `os`; keep `powershell`, Unity version/path
  6. Update mode-contract/postflight expectations for removed row and 3-step chain
- done when: all four modes `-PlanOnly` produce planned ledgers with no `prebake-validate` row; deleted symbols zero references; narrowed literal in place; parse clean
- checks:
  - check_id: `t4-powershell-parse`; tier: `fast`; mutates_project: `false`; input_paths: owned path; input_digest: execution-computed; environment_fingerprint: WinPS parser; invalidation_paths: owned path; subsumes: `t1-powershell-parse`; run_point: before CP4; evidence: zero AST errors; zero references to deleted function names
  - check_id: `t4-planonly-modes`; tier: `fast`; mutates_project: `false`; input_paths: owned path + project/version/config; input_digest: execution-computed; environment_fingerprint: short path, WinPS, durable evidence root; invalidation_paths: same; subsumes: `t1-planonly-fixture`; run_point: before CP4; evidence: four modes planned results outside project; ProductionPrepare plan lists exactly Probe/StaleAssembly/ProductionBake; no project diff; lock release
- proof: `rg` for deleted symbols (present at baseline, absent at head) + plan-only chain diff (4 steps -> 3) discriminatory; live skip-marker consumption proven at FINAL
- review_focus: generic reuse branch accidentally excised with bake branches -> fast rows never reuse; marker parse accepts partial/stale log -> false `reused` on failed bake; narrowed list omits real lighting input -> stale bake accepted as current (Critical); bake-count hard-fail path removed -> multi-bake regression invisible
- review_checkpoint: CP4
- return_evidence: deleted symbol list + line counts; narrowed literal with per-entry rationale; plan-only outputs; marker-consumption diff

### T5: Harness test layer — AST shim, regression pins, static guards, hook

- objective: sub-10s executable suite pinning D1/D2 classes and post-T4 contract; runs automatically on harness/tooling edits and before any Unity-mutating workflow step
- slice_boundary: dominant invariant = every past harness defect class has a red-verified executable check; includes shim, cases, guards, runner, hook; excludes production-file changes (none permitted)
- covered_requirements: direct request slice (test layer)
- owner: W5 implementation worker
- depends_on: CP4 accepted
- owns: new `Tools/Tests/HarnessShim.psm1`; new `Tools/Tests/MovementLabHarness.Tests.ps1`; new `Tools/Tests/Invoke-HarnessTests.ps1`; `.claude/settings.json`
- protected: `Tools/Validation/**` (read-only subject); `Assets/**`; `.agents/**`
- read_paths: `Tools/Validation/Invoke-MovementLabWorkflow.ps1` post-T4; `git show 73984e2:Tools/Validation/Invoke-MovementLabWorkflow.ps1` (red-pin blob); `Assets/_Game/Editor/MovementLab/*.cs` (G4 sweep + T3 marker constant)
- validation_environment: WinPS 5.1; zero Unity; zero network; disk writes only to durable temp
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_run_point: None
- proof_invalidation_paths: `Tools/Tests`; `Tools/Validation`
- focused_reads: shim prototype results (Repository Findings) -> `ParseFile` -> select `FunctionDefinitionAst` by name + top-level `AssignmentStatementAst` constants -> `New-Module`/`Import-Module`
- implementation:
  1. `HarnessShim.psm1` (~40 lines): `Import-HarnessFunctions -Source <path|scriptText> -FunctionNames <string[]> -VariableNames <string[]>` -> AST parse -> extracted bodies + constant assignments -> in-memory module. Accepts `git show` text for cross-revision runs
  2. `Invoke-HarnessTests.ps1`: plain assertion runner (Pester-3-only environment); per-case function `Test-<CaseId>` returning pass/fail + message; exit 0/1; failure lines `CASE <id> FAIL <message>`; optional `-EvidenceRoot` writes JSON summary to durable evidence
  3. Cases (each 1-line intent):
     - `shim-export-set` -> module exports exactly requested set (meta-guard: renamed production function cannot silently vacuate suite)
     - `gopv-ordered` -> `Get-ObjectPropertyValue` reads `[ordered]@{}` key; `RedAtSha 73984e2` (D1 pin)
     - `gopv-json` -> reads `ConvertFrom-Json` property (working path preserved)
     - `gopv-absent` -> `$null` for absent key on both shapes
     - `ledger-row-ordered-literal` -> AST asserts `New-LedgerRow` emits `[ordered]` literal (keeps `gopv-ordered` load-bearing)
     - `row-reuse-equal` -> `Test-RowReuseProof` valid for json-prior vs ordered-row equal digests; `RedAtSha 73984e2`
     - `row-reuse-diff` -> invalid when digests differ (discriminator)
     - `row-field-sweep` -> every `New-LedgerRow` field (AST-harvested key list) readable via `Get-ObjectPropertyText` on fresh row (catches future D1 at any new field)
     - `bake-inputs-literal` -> `$productionBakeInputs` equals T4 narrowed literal exactly (D2 permanent lock)
     - `bake-inputs-asymmetry` -> `Tools/Validation` in compile + `production-validator` inputs, absent from bake inputs
     - `path-intersects` -> exact/descendant/ancestor match, sibling-prefix non-match
     - `stringset-null` -> `Test-StringSetEqual` false for real list vs `@($null)`
     - `planonly-pending-only` -> AST asserts PlanOnly stamp statement conditions on `pending` status (T1 pin)
  4. Static guards in same runner:
     - `G1` -> scan `MemberExpressionAst` for `PSObject.Properties` on possibly-dictionary operands; line+reason allowlist for provably-JSON-derived sites; new unallowlisted site -> fail
     - `G3` -> `Get-ObjectPropertyValue` body contains `IDictionary` type-test branch
     - `G4` -> `File.Replace(` in `Assets/_Game/Editor/**/*.cs` only inside `MovementLabAtomicFile`
     - `G5` -> `[Parser]::ParseFile` zero errors on every `Tools/Validation/*.ps1`
  5. Red/green: cases with `RedAtSha` run twice -> HEAD text must pass, `git show <sha>` blob must fail; pass-at-both -> runner fails case as decoration
  6. `.claude/settings.json`: `PostToolUse` hook, matcher `Edit|Write` on `Tools/Validation/*.ps1` + `Assets/_Game/Editor/MovementLab/*.cs` -> run `Tools/Tests/Invoke-HarnessTests.ps1`; non-zero surfaces to agent
  7. Total runtime budget 10s; measured shim baseline 189-436ms/invocation
- done when: runner exits 0 at head; deliberately reverting `:847-850` IDictionary branch in scratch copy -> `gopv-ordered` + `G3` fail; red-pins verified failing at `73984e2`
- checks:
  - check_id: `harness-unit`; tier: `fast`; mutates_project: `false`; input_paths: `Tools/Validation`, `Tools/Tests`, `Assets/_Game/Editor/MovementLab`; input_digest: execution-computed; environment_fingerprint: WinPS 5.1 + Git version; invalidation_paths: same; subsumes: `t4-powershell-parse` (via G5); run_point: before any `Invoke-MovementLabWorkflow.ps1` invocation including `-PlanOnly`; evidence: runner exit 0, JSON summary in durable evidence, red-pin double-run records
- proof: red/green double-run is self-demonstrating discriminatory evidence; scratch-revert drill proves detection, not decoration
- review_focus: shim silently drops renamed export and `shim-export-set` list stale -> vacuous suite; G1 allowlist too broad -> next D1 idiom passes; hook command path wrong -> never fires and nobody notices (verify with one recorded hook execution)
- review_checkpoint: CP5
- return_evidence: case inventory + pass log; red-pin failure transcripts; scratch-revert drill output; hook execution record; measured runtime

### T6: Policy synchronization — circuit breakers, harness contract, ceremony reduction

- objective: orchestration policy caps Unity spend, documents harness invocation, and stops mandating ceremony no machine reads
- slice_boundary: dominant invariant = one consistent post-T4 process contract; includes AGENTS.md, 5 skill/policy files; excludes any code
- covered_requirements: direct request slice (process changes)
- owner: W6 implementation worker
- depends_on: CP4 accepted
- owns: `AGENTS.md`; `.agents/skills/orchestrate-implementation/SKILL.md`; `.agents/skills/write-orchestrator-coding-plan/SKILL.md`; `.agents/skills/loop-orchestrator/SKILL.md`; `.agents/skills/loop-orchestrator/references/state-and-recovery.md`; `.agents/skills/loop-orchestrator/agents/merging.md`
- protected: other skills; plan artifacts; handoff docs; all source
- read_paths: owned files; T4 final harness contract; T5 runner entrypoint
- validation_environment: Markdown/link/`rg` static inspection only
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_run_point: None
- proof_invalidation_paths: owned files
- focused_reads: `orchestrate-implementation/SKILL.md:85` recovery bake authorization; `:150-160` checkpoint defaults; `write-orchestrator-coding-plan/SKILL.md:117-119` check contract; `loop-orchestrator/SKILL.md:12` mandatory stages; `state-and-recovery.md:109-132` ledger section; `AGENTS.md` validation section + stale `:30` reference
- implementation:
  1. `AGENTS.md`: commit user's test-deferral deletion; add "Harness/tooling tests: run `Tools/Tests/Invoke-HarnessTests.ps1` before any Unity-mutating workflow step; sub-10s, no Unity"; document builder bake skip-gate + marker; remove stale `plans/comprehensive-graphics-overhaul-coding-plan.md` reference; keep gameplay-test deferral posture
  2. `orchestrate-implementation/SKILL.md`: add run-level bake budget -> cumulative `bakeCount` across all `workflow-result.json` of a run > 2 -> halt `blocked` with evidence; add `expected_status` declaration before each production-final invocation, observed != expected twice consecutively -> halt, suspect comparator, no further Unity; recovery replacement bake requires explicit user authority (delete blanket `:85` authorization); document harness invocation contract (script path, `-Mode`, `-PlanOnly`, `-LedgerPath`, `-EvidenceRoot`, rule "carry `-LedgerPath` from previous accepted invocation"); add PlanOnly-fixture-first rule (reproduce harness defects `-PlanOnly` with fixture before any production run); add `harness-unit` as mandatory pre-Unity gate
  3. `write-orchestrator-coding-plan/SKILL.md`: replace 10-field check-row mandate for ordinary tasks with one line `proof: <command> -> <expected>`; full row contract retained only for `production-final` tier
  4. `loop-orchestrator/SKILL.md`: `single_plan` runs skip BREAKDOWN and MERGING agent stages; LP dispatches planner + execution orchestrator directly
  5. `state-and-recovery.md` + `merging.md`: harness `check-ledger.json` is the sole executed ledger, state file holds pointer + digest only (resolves C1); bake invalidation wording -> exact narrowed lighting-input contract (resolves C2); replace pure-reattest wording with explicit builder-gate reattest rule (resolves C4)
  6. Preserve/update all Markdown links and headings together
- done when: `rg` finds no blanket recovery-bake authority, no full check-row mandate for fast tier, no stale plan reference; every process stage names same bake-decision owner (builder gate) and same pre-Unity test gate
- checks:
  - check_id: `t6-policy-sweep`; tier: `fast`; mutates_project: `false`; input_paths: owned files; input_digest: execution-computed; environment_fingerprint: `rg` + link resolution; invalidation_paths: owned files; subsumes: None; run_point: before CP6; evidence: classified matches for bake-budget/expected-status/harness-contract presence and removed-mandate absence; valid link targets; `git diff --check` clean
- proof: sweep distinguishes added circuit-breaker text, removed ceremony text, and untouched safety text per file
- review_focus: budget/halt wording ambiguous -> orchestrator argues past it under pressure; check-row removal accidentally covers production-final tier -> final proof contract lost; ledger consolidation deletes evidence-hash integrity rule
- review_checkpoint: CP6
- return_evidence: per-file clause diff; sweep classification; link audit

## Execution Assignments

- workers: T1 -> W1 -> workflow observability; T2 -> W2 -> atomic-write consolidation; T3 -> W3 -> builder bake gate; T4 -> W4 -> harness simplification; T5 -> W5 -> test layer; T6 -> W6 -> policy sync
- fan-out: W1 and W2 launch from same start SHA, paths disjoint; W5 and W6 launch together after CP4 at accepted head, paths disjoint
- lane continuation: W3 after CP2 at accepted lane head (serialized C# compile environment); W4 after JOIN1 at accepted head
- review_checkpoints: CP1 -> fresh reviewer -> T1/W1; CP2 -> fresh reviewer -> T2/W2; CP3 -> fresh reviewer -> T3/W3; CP4 -> fresh reviewer -> T4/W4; CP5 -> fresh reviewer -> T5/W5; CP6 -> fresh reviewer -> T6/W6; all per-worker default, worker terminal + committed frozen SHA
- fan-in: JOIN1 -> CP1 + CP3 accepted, findings fixed, no writers; JOIN2 -> CP5 + CP6 accepted, findings fixed, no writers

## Final Verification

- exact head: all task commits + accepted fixes committed; worktree clean; final SHA descends from baseline `7abacca…`
- final owner: execution orchestrator; one Unity lease; short absolute path; warm private `Library`
- sequence:
  1. `harness-unit` -> `Tools/Tests/Invoke-HarnessTests.ps1` exit 0 (mandatory before any workflow invocation)
  2. `final-static-scope` -> `git diff --check`; `baseline..HEAD` path scope matches plan owned paths; deleted-symbol sweep; PowerShell AST parse; clean status
  3. `t1-planonly-fixture` rerun at final head -> reuse observable, reasons non-empty
  4. `production-prepare` -> tier `production-final`; mutates_project `true`; one real `Invoke-MovementLabWorkflow.ps1 -Mode ProductionPrepare`; expected exactly one bake (lighting sources changed since `73984e2`); Probe/StaleAssembly/ProductionBake chain only; evidence outside project; zero Console errors; lock release. input_paths: narrowed lighting literal; input_digest execution-computed; environment_fingerprint: Unity `6000.5.6f1` + short path + warm `Library` + WinPS; invalidation_paths: narrowed lighting literal; subsumes: final compile, stage probe
  5. generated-output disposition -> inspect `changedGeneratedPaths` against authoritative inventory; semantic diff review; commit separately `chore: regenerate MovementLab outputs`; reject GUID churn/broken meta pairs/unrelated reserialization
  6. `production-validator` -> tier `production-final`; mutates_project `false`; separate `Invoke-MovementLabWorkflow.ps1 -Mode ProductionValidate` at exact final SHA; direct `MovementLabBuilder.ValidateMovementLab`; zero Console errors; unchanged HEAD; lock release
- inspect: source diff per task; 3-step ProductionPrepare chain in result; skip-marker consumption path present (exercised only if a second prepare ever runs); hook execution record
- invalidation: any owned source/tool edit -> rerun `harness-unit` + `final-static-scope`; lighting-input edit (narrowed literal) -> rerun ProductionPrepare + ProductionValidate; editor/tool/config edit -> rerun ProductionValidate; policy edit -> rerun `t6-policy-sweep`

## Handoff

- changed paths: T1-T6 owned paths; new `MovementLabAtomicFile.cs`; new `Tools/Tests/` (3 files); `.claude/settings.json`; one generated-output commit from FINAL bake
- residual risks: EditMode C# test tier not built (deferred; needs `InternalsVisibleTo`); lease/lock/path-safety harness machinery still untested (next audit target); skip-marker live path proven only when a second production run occurs; Pester-5 migration deferred until install feasible
- authority: implementation via `$orchestrate-implementation` on isolated branch/worktree; FINAL real bake authorized by this plan's acceptance; integration/user-branch merge requires user authority

## Done Criteria

- every covered requirement maps to task, owner, check, and proof;
- every task passes implementation design gate;
- every task passes worker-review sizing gate: one meaningful outcome, cohesive reasoning, bounded failure domain, one proof boundary, and no incidental standalone slice;
- Execution Graph includes every task and review checkpoint exactly once and makes every sequential dependency, parallel lane, and join gate explicit;
- every implementation worker maps to one review checkpoint; no grouped checkpoints;
- exact baseline and dependencies are factual;
- execution route uses immutable attempt-bound snapshot and `$orchestrate-implementation`;
- final production proof has one owner, at most one production bake, no capture, direct semantic validator, exact-head evidence, and clean process/lock release;
- final checks bind clean committed head or blocker names needed action.
