# Compile And Bake Issues Fix Coding Plan

Status: accepted
Source: `plans/compile_and_bake_issues_fix.md`
Run ID: direct
Plan ID: direct
Attempt ID: direct
Covered Requirements: direct request
Baseline: 7ea182440ad82eafaab08fbeba0421cb9abc8ac6
Dependencies: None

## Objective

Make MovementLab compile, validation, and bake workflow report useful batches of semantic defects per Unity run; remove review-marker and automated image-capture gates from routine proof; retain safe structural, ownership, process, and manual-capture behavior; synchronize agent policy with simplified workflow.

Completion boundary: source changes reviewed; one real `ProductionPrepare` run completes without review-marker inputs; one separate `ProductionValidate` run executes semantic validation without capture; generated outputs from same production run handled under authoritative builder rules; no automated visual proof required.

## Scope

- in: semantic-validation accumulation at MovementLab entry/stage boundaries; outgoing-manifest consistency demotion with structural safety retained; manual capture warning/informational behavior; workflow review-marker demolition; routine capture removal; PowerShell semantic/preflight/postflight accumulation; orchestration policy synchronization
- in: `Assets/_Game/Editor/MovementLabBuilder.cs`; `Assets/_Game/Editor/MovementLab/MovementLabValidator.cs`; `Assets/_Game/Editor/MovementLab/MovementLabPreBakeGate.cs`; `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs`; `Assets/_Game/Editor/MovementLab/MovementLabManifestStore.cs`; `Assets/_Game/Editor/BrightArenaVisualCapture.cs`; `Tools/Validation/Invoke-MovementLabWorkflow.ps1`; `Tools/Validation/Capture-BrightArenaVisuals.ps1`; deletion of `Tools/Validation/Write-MovementLabPreBakeReviewMarker.ps1`; named policy files
- in: conditional builder-owned generated outputs reported by final `ProductionPrepare`, reviewed semantically, committed separately when changed
- out: phase-2 demolition of `MovementLabStageGraph`, `MovementLabStageRunner`, manifest store, or pre-bake gate
- out: gameplay, arena content, lighting look, shader design, package, Unity-version, runtime, collision, or input changes
- out: blanket removal of source/input digests, plan/artifact/evidence hashes, GUID/meta checks, process locks, path safety, writer convergence, or atomic writes
- out: new automated capture, screenshot comparison, image threshold, or agent visual-inspection proof
- out: new tests; use existing validation workflows and safe static alternate proof
- out: restoring deleted `plans/graphics-overhaul-run-efficiency-coding-plan.md`; commit `510141b` intentionally removed it before baseline
- out: deleting `C:\wt\eff-091dc7` or branch `codex/graphics-overhaul-run-efficiency-20260810T111004014-091dc7`; cleanup remains user-authorized operator action after writer/process recheck

## Repository Findings

- observed: baseline `7ea182440ad82eafaab08fbeba0421cb9abc8ac6`, branch `3_vs_3_bots`, clean worktree during planning
- observed: `MovementLabBuilder.ValidateMovementLab()` performs production-profile/stale gates before `MovementLabValidator.Validate(true, true)`
- observed: `MovementLabValidator.ValidateMovementLabInternal()` mixes asset prerequisites, scene/component lookup, gameplay wiring, prefab/material/importer checks, lighting checks, project settings, and missing-component checks in one first-fail path
- observed: `MovementLabValidator.Validate()` and `ValidateFastPersistedSemantics()` repeat importer/animator/prefab/arena checks after core validation already invokes overlapping checks
- observed: `MovementLabStageGraph.Probe()` accumulates stage reasons but throws inside stage loop for first blocking identity family
- observed: `MovementLabPreBakeGate.ValidateAndWritePassRecord()` writes pass record only after persisted-state, semantic, and prepared-scene validation, but each phase remains first-fail
- observed: 385 `throw new` sites exist under `Assets/_Game/Editor/MovementLab`; many belong to writers, path safety, convergence, bake mutation, or fast-preview restoration and must remain fail-fast
- observed: `MovementLabManifestStore.IsCurrentAndReadable()` combines schema/path/stage structure with top-level fingerprint/signature consistency; `WriteAtomic()` hard-rejects any false result before existing-manifest authorization check
- observed: `BrightArenaVisualCapture.Capture()` calls `ValidateMovementLab()`, rejects dirty scene, enforces image/budget heuristics, and requires wrapper-provided exact Git SHA; `Capture-BrightArenaVisuals.ps1` is sole working batch-mode manual caller
- observed: `Invoke-MovementLabWorkflow.ps1` exposes `[switch]$Capture`; one `capture-validator` row represents either image capture or semantic validation, allowing historical capture evidence to skip direct semantic validation
- observed: `ProductionPrepare` requires `Write-MovementLabPreBakeReviewMarker.ps1`, review-report parameters, marker ledger row, marker reuse, and readback although execution orchestrator already owns review/fix state
- observed: `Invoke-MovementLabWorkflow.ps1` has 117 throws; safe accumulation targets are independent preflight facts, probe contract facts, mode contract facts, and postflight facts; lease/path/process/external-command failures are safety boundaries
- observed: policy contradictions remain in `AGENTS.md`, `orchestrate-implementation`, `write-orchestrator-coding-plan`, `loop-orchestrator`, state/recovery, and merging instructions
- observed: efficiency plan output SHA `104e6d132bf53213ba7981bcd3f1be36e0e04292` is ancestor of baseline through merge `e999a7d41aa9498b99b0f9e61c6af4e19db7cc66`; old worktree is clean but still registered
- gap: routine proof does not batch safe independent validation failures and still carries obsolete review-marker/capture gates
- constraint: Unity compile is authoritative; one Unity process per project; use short project path, hidden `Start-Process -Wait -PassThru`, warm private `Library`, and process/lock release checks
- constraint: generated outputs never gate on cross-run byte equality; source/input digests and same-run artifact/evidence integrity hashes remain valid
- constraint: automated visual acceptance is prohibited; human High/Low visual review remains on demand
- proposed: keep shared accumulator type inside `MovementLabValidator.cs` to avoid new Unity asset/meta creation

## Decisions

- assumption: direct request covers unresolved TODO sections 1, 2, 2b, and 4 from source handoff; stale plan-move item is satisfied by current intentional deletion and receives no restoration task
- decision: accumulate only read-only semantic predicates and independent workflow facts; keep mutation, restoration, lease, path containment, external-process failure, atomic-write, and convergence guards fail-fast
- decision: one ordered `MovementLabValidationAccumulator` crosses root semantic stages; section boundaries stop dependent subsection after prerequisite failure while unrelated sections continue
- decision: accumulator catches expected validation exceptions only (`InvalidOperationException` and explicitly classified argument-contract exceptions); unexpected engine/runtime exceptions propagate
- decision: preserve parameterless compatibility validator entrypoints; shared-root overloads append to supplied accumulator and only root calls terminal throw
- decision: no mechanical leaf-throw rewrite across every pipeline; split `ValidateMovementLabInternal()` into independent named sections and wrap existing leaf validators at safe granularity. This targets 4-6 useful defect batches without moving mutator safety checks
- decision: `MovementLabFastModeSession` restoration/apply/hash guards remain fail-fast; they protect reversible editor state, not independent semantic validation
- decision: outgoing manifest write hard-fails structural/schema/path/stage defects, warns on derived path-union/fingerprint/signature inconsistency, then writes; trusted existing-manifest authorization remains unchanged
- decision: manual capture keeps rendering, PNG integrity, metrics, budget accounting, source/status protection, and output files. Scene dirtiness plus image/budget heuristics become warnings/informational fields, never visual acceptance gates
- decision: remove C# expected-SHA argument/environment plumbing while preserving wrapper before/after HEAD and scoped-status checks plus informational manifest provenance
- decision: `ProductionValidate` always executes `MovementLabBuilder.ValidateMovementLab()` under new `production-validator` row. Historical `capture-validator` rows never migrate or reuse
- decision: remove review-marker script, parameters, ledger row, read/reuse logic, and result fields atomically; execution checkpoint state remains review authority
- decision: new PowerShell validation predicates are warning/collection-first; only proven retained invariants stay hard failures
- decision: production bake invalidation binds lighting inputs, not bake-generated output paths. Same-run generated-only commit may reattest bake evidence when source/input digest, environment, `lightingInputDigest`, and workflow-reported after-output inventory match; this is provenance, not cross-run determinism
- decision: final production workflow runs once after all source reviews/fixes; manual capture does not run
- question: None

## Execution Graph

`START -> {T1 -> CP1 -> T2 -> CP2 || T3 -> CP3 -> T4 -> CP4 || T5 -> CP5} -> JOIN1 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in
- gates: `START` -> exact baseline snapshot bound in isolated short-path worktree, owned paths clean, one Unity lease available; `JOIN1` -> CP1-CP5 accepted and no writers running; `FINAL` -> production workflow, generated-output disposition, exact-head validation, scope, cleanliness, and evidence checks pass
- fan-out: launch T1, T3, and T5 together
- lane 1: T2 starts only after CP1 accepts accumulator behavior; shared C# compile environment stays serialized
- lane 2: T4 starts only after CP3 removes obsolete workflow surface; same PowerShell file has one writer
- lane 3: T5 stays path-disjoint from product/tool lanes
- join: every branch checkpoint gates `JOIN1`; no production bake before join
- generated-output rule: final proof owner is sole Unity/project writer; conditional generated diff receives separate builder-output commit and semantic inspection before exact-head validation

## Tasks

### T1: Batch MovementLab semantic validation failures

- objective: one validation invocation reports ordered violations from all safe independent MovementLab semantic sections while preserving prerequisite guards and writer safety
- slice_boundary: dominant invariant = accumulate read-only semantic failures; includes root builder/profile/stale handling, core scene sectioning, pre-bake phase aggregation, delayed stage-identity throw; excludes manifest/capture demotions and PowerShell workflow
- covered_requirements: accumulator refactor
- owner: W1 exact `luna_max` implementation worker
- depends_on: None
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`; `Assets/_Game/Editor/MovementLab/MovementLabValidator.cs`; `Assets/_Game/Editor/MovementLab/MovementLabPreBakeGate.cs`; `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs`
- protected: runtime scripts; generated assets; pipeline builder/mutator methods; `MovementLabFastModeSession` lifecycle guards; manifest authorization; lighting bake implementation
- read_paths: owned paths; `Assets/_Game/Editor/MovementLab/MovementLabImportPipeline.cs`; `MovementLabAnimatorPipeline.cs`; `MovementLabPrefabPipeline.cs`; `MovementLabArenaPipeline.cs`; `MovementLabMaterialPipeline.cs`; `MovementLabLightingPipeline.cs`; `MovementLabSceneComposer.cs`; `MovementLabSerializedProperties.cs`; `MovementLabFastModeSession.cs`
- validation_environment: isolated short-path Unity worktree; Unity `6000.5.6f1`; one hidden compile-only batch process; warm private `Library`; no concurrent Unity writer
- unity_mutation: false
- expensive_proof_owner: active execution orchestrator
- expensive_proof_run_point: FINAL after CP1-CP5
- proof_invalidation_paths: owned paths; called validator pipeline paths; `ProjectSettings`; `Packages`
- focused_reads: `MovementLabValidator.Validate*()` for duplicate control flow; `MovementLabBuilder.ValidateMovementLab()` for profile/stale ordering; `MovementLabPreBakeGate.ValidateAndWritePassRecord()` for write barrier; `MovementLabStageGraph.Probe()` for in-loop throw
- implementation:
  1. Add internal accumulator in `MovementLabValidator.cs`: ordered violation records with scope/check/message; stable dedupe; `Add`; `Capture`; `HasViolations`; one numbered `ThrowIfAny(operation)`. No static/global mutable state
  2. Catch only expected validation-contract exceptions. Preserve first scope/check context. Propagate unexpected engine/runtime faults
  3. Split `ValidateMovementLabInternal()` into dependency-aware sections: asset/shader prerequisites; scene/root prerequisites; gameplay and serialized wiring; imported visual/animator/prefab checks; material/importer checks; arena checks; lighting/baked checks; render/physics/build settings; missing components
  4. Asset/path loops run one accumulator capture per path. Missing scene/root/component adds one prerequisite violation and skips only dependent subsection. Unrelated sections continue when inputs exist
  5. Remove duplicate post-core validation calls or route them once through accumulator. Preserve exact full/fast/pre-bake coverage; do not silently drop importer, animator, prefab, arena, lighting, shader, GUID/reference, or missing-component checks
  6. Add collector overloads for full, fast persisted, and pre-bake semantic entrypoints. Parameterless/current signatures create collector, call overload, then throw once
  7. In `MovementLabBuilder.ValidateMovementLab()`, collect invalid production profile and stale non-lighting facts. Invalid profile runs non-baked semantic coverage to avoid baked-output cascades; valid profile runs baked coverage. Raw-output and stale-lighting informational messages stay warnings/logs. Write requested probe before terminal aggregate when safe
  8. In `MovementLabPreBakeGate`, share one collector across persisted non-lighting state, semantic validation, and prepared-scene probe. Missing/invalid probe skips dependent digest/pass construction. Call terminal throw before pass-record write
  9. In `MovementLabStageGraph.Probe()`, collect blocking identity tokens for every stage, finish loop/current-state reasoning, then throw one ordered identity violation summary. Preserve semantic GUID/meta and missing-output protection
  10. Leave stage writers, bake calls, manifest authorization, fast-preview restoration, path normalization, and process safety fail-fast
- done when: full/fast/pre-bake entrypoints have one terminal semantic throw; safe sections append multiple violations; no pass record writes after any violation; production profile error no longer hides independent non-baked semantic defects; stage identity scan reports all blocking stage families
- checks:
  - check_id: `t1-static-accumulator`; tier: `fast`; mutates_project: `false`; input_paths: owned paths; input_digest: execution-computed from exact file bytes; environment_fingerprint: PowerShell + Git version; invalidation_paths: owned paths; subsumes: None; run_point: before CP1; evidence: `rg` audit showing one root `ThrowIfAny` per entrypoint, named section captures, no collector use in mutators; execution state records `executed_sha`, `validated_sha`, `status`, evidence path/digest
  - check_id: `t1-unity-compile`; tier: `fast`; mutates_project: `false`; input_paths: owned paths + called editor assemblies + `Packages` + `ProjectSettings`; input_digest: execution-computed; environment_fingerprint: Unity `6000.5.6f1`, short absolute worktree path, warm private `Library`; invalidation_paths: same input paths; subsumes: None; run_point: before CP1; evidence: hidden compile-only Unity log, exit 0, zero Console/compiler errors, process/lock release; execution state records exact SHAs and evidence digest
- proof: safe alternate proof = static dependency-section audit plus green Unity compile; discriminatory multi-defect runtime proof deferred because repository forbids new tests and tracked-asset fault injection. FINAL real validation proves green-path preservation
- review_focus: accumulator catches unexpected faults or continues dependent Unity calls after missing prerequisites -> hides corruption/crashes; pass record/probe writes after violations -> false bake readiness; duplicate-removal drops semantic check family -> invalid scene accepted
- review_checkpoint: CP1
- return_evidence: changed symbols/paths; section/check inventory; compile log; remaining fail-fast classification; residual unaggregated leaf boundaries

### T2: Demote obsolete hard gates and keep capture manual

- objective: preserve manifest structural trust and manual capture operability while removing outgoing byte-consistency, dirty-scene, and heuristic visual hard stops
- slice_boundary: dominant invariant = warnings replace obsolete non-safety gates; includes manifest predicate split and manual capture provenance simplification; excludes routine workflow capture branch, owned by T3
- covered_requirements: last hard gates; agent image-verification scrap
- owner: W2 exact `luna_max` implementation worker
- depends_on: CP1 accepted
- owns: `Assets/_Game/Editor/MovementLab/MovementLabManifestStore.cs`; `Assets/_Game/Editor/BrightArenaVisualCapture.cs`; `Tools/Validation/Capture-BrightArenaVisuals.ps1`
- protected: `MovementLabGeneratedState` schema; existing-manifest migration authorization; atomic replacement; asset/meta path containment; `Invoke-MovementLabWorkflow.ps1`; capture image files outside source tree
- read_paths: owned paths; `MovementLabStageGraph.cs`; `MovementLabContract.cs`; `MovementLabBuilder.cs`; source handoff resolved hash exceptions
- validation_environment: same isolated short-path worktree after CP1; one hidden Unity compile process; PowerShell AST parse; no actual capture
- unity_mutation: false
- expensive_proof_owner: active execution orchestrator
- expensive_proof_run_point: FINAL `ProductionPrepare` exercises manifest write; manual capture excluded
- proof_invalidation_paths: owned paths; manifest schema/state capture paths; workflow generated-output inventory
- focused_reads: `IsCurrentAndReadable()` structural and consistency clauses; `WriteAtomic()` authorization/write order; `BrightArenaVisualCapture.Capture()`, `AnalyzeImage()`, `ScanBudgets()`, `ReadExpectedGitSha()`, wrapper pre/post source checks
- implementation:
  1. Split manifest validation into structural/schema/path/stage violations and derived consistency violations. Structural layer keeps schema, required fields/stages, normalized unique repository paths, output entries, missing/digest shape, stale arrays, and required-stage coverage hard
  2. Keep `IsCurrentAndReadable()` strict for trusted existing-manifest reads. `WriteAtomic()` hard-fails structural violations, logs ordered warning for derived path-union/fingerprint/source/output signature mismatch, then continues to unchanged existing-manifest authorization and atomic write
  3. Replace capture dirty-scene throw with warning. Keep active-scene identity and cleanup/restoration hard
  4. Keep PNG encoding/decoding/dimensions, render ability, file creation, source status, process/lock, and budget-accounting self-test hard. Image luminance/color and render-budget heuristic failures log warnings and remain informational in manifest; `manifest.pass` means technical capture completed, never visual acceptance
  5. Add explicit manifest field/message stating human visual review required. Do not add automated approval result
  6. Remove C# expected-SHA command/environment constants, parsing, normalization, and equality gate. Read current Git HEAD for informational manifest provenance. Wrapper keeps before/after exact HEAD, scoped status, and required source-file hash checks
  7. Remove wrapper argument `-brightArenaExpectedGitSha`. Keep standalone wrapper -> `BrightArenaVisualCapture.Capture` connection and manual command usability
- done when: structurally unsafe outgoing manifest still rejects; derived consistency mismatch warns; trusted malformed existing manifest still requires authorization; manual capture script remains callable directly; scene dirtiness/image/budget heuristics cannot decide build/validation/plan pass
- checks:
  - check_id: `t2-manifest-capture-static`; tier: `fast`; mutates_project: `false`; input_paths: owned paths; input_digest: execution-computed; environment_fingerprint: PowerShell parser + Git; invalidation_paths: owned paths; subsumes: None; run_point: before CP2; evidence: AST parse, `rg` proving no expected-SHA argument plumbing and retained direct capture execute method, classified manifest hard/warning predicates; execution state records exact SHAs and evidence digest
  - check_id: `t2-unity-compile`; tier: `fast`; mutates_project: `false`; input_paths: owned C# paths + editor assembly dependencies + `Packages` + `ProjectSettings`; input_digest: execution-computed; environment_fingerprint: Unity `6000.5.6f1`, short path, warm private `Library`; invalidation_paths: same; subsumes: `t1-unity-compile`; run_point: before CP2; evidence: hidden compile-only Unity log, exit 0, zero Console/compiler errors, process/lock release
- proof: structural/consistency branch source audit plus compile; FINAL production manifest write proves green write path. Manual capture intentionally not run and cannot become acceptance evidence
- review_focus: structural/path/stage safety accidentally demoted -> invalid manifest written; existing authorization removed -> trusted state overwritten; image heuristics still set technical failure -> visual gate survives; manual wrapper/C# connection broken -> on-demand capture lost
- review_checkpoint: CP2
- return_evidence: structural versus warning predicate map; capture schema/log behavior; parser/compile logs; manual command retained; residual manual-capture risks

### T3: Remove review-marker and routine capture workflow

- objective: simplify production workflow to orchestration-owned review state, one real bake row, and one direct semantic validation row with no capture alternative
- slice_boundary: dominant invariant = obsolete proof surfaces removed atomically; includes workflow API/ledger/result migration and marker-script deletion; excludes broader throw accumulation, owned by T4
- covered_requirements: kill last hard gates; scrap agent image verification; review-marker demolition
- owner: W3 exact `luna_max` implementation worker
- depends_on: None
- owns: `Tools/Validation/Invoke-MovementLabWorkflow.ps1`; delete `Tools/Validation/Write-MovementLabPreBakeReviewMarker.ps1`
- protected: `Tools/Validation/Capture-BrightArenaVisuals.ps1`; Unity source; lease/path/process/atomic evidence functions; evidence hashes; generated inventory; bake count
- read_paths: owned paths; manual capture wrapper; `MovementLabBuilder` public methods; orchestration skill current workflow/review contracts
- validation_environment: PowerShell AST/parser and plan-only workflow using isolated short-path worktree; durable evidence outside project; no Unity execution for worker proof
- unity_mutation: false
- expensive_proof_owner: active execution orchestrator
- expensive_proof_run_point: FINAL after all reviews/fixes
- proof_invalidation_paths: owned workflow path; policy files; `MovementLabBuilder` validation/bake entrypoints
- focused_reads: parameters; `New-CheckLedger`; `Merge-ExistingLedger`; `Read-ReviewMarker`; `Copy-CaptureEvidence`; mode switch; result/manifest schema
- implementation:
  1. Remove marker-only parameters: `SourceSha`, `ReviewedSha`, `Reviewer`, `ExecutionId`, `Checkpoint`, review report paths/hashes, and finding dispositions. Remove argument validation and ancestry gates tied only to marker
  2. Delete `review-marker` ledger row, reuse special cases, `Read-ReviewMarker()`, external script invocation/readback, marker result state, and marker/source/review result fields
  3. Delete `Write-MovementLabPreBakeReviewMarker.ps1` in same change. Keep `Test-IsAncestor()` because descendant ledger reuse still needs it
  4. Remove `[switch]$Capture`, `Copy-CaptureEvidence()`, capture state/result fields, capture command record, and capture branch
  5. Replace `capture-validator` with new `production-validator`; never alias/migrate historical row. Row always invokes `RocketFooxball.Editor.MovementLabBuilder.ValidateMovementLab` with `-nographics`, reads probe, and asserts `ProductionValidate` contract
  6. Keep row subsumption of `validator-readonly` if still semantically used. Ensure prior ledger containing `capture-validator` leaves `production-validator` pending
  7. Remove `capture` and `review-marker` from manual-proof classification. Bake remains non-reattestable after lighting-input changes
  8. Narrow `production-bake` input/invalidation sets to exact lighting inputs from `MovementLabStageGraph` contract plus source/config inputs; exclude bake-generated output paths. Preserve same-run generated inventory/hashes as provenance evidence, never cross-run equality gate
  9. Keep result `schemaVersion: 1` unless an observed repository consumer requires version bump. Result exposes no obsolete marker/capture fields; update all repository consumers in T5
- done when: `ProductionPrepare` accepts no review-marker arguments and has no marker row; `ProductionValidate` has no capture option and always runs direct semantic validator; old capture ledger cannot suppress new validation; marker script absent; evidence/source/input integrity remains
- checks:
  - check_id: `t3-powershell-parse`; tier: `fast`; mutates_project: `false`; input_paths: owned paths; input_digest: execution-computed; environment_fingerprint: Windows PowerShell parser version; invalidation_paths: owned paths; subsumes: None; run_point: before CP3; evidence: zero AST parse errors and `git diff --check`
  - check_id: `t3-obsolete-surface-sweep`; tier: `fast`; mutates_project: `false`; input_paths: owned paths; input_digest: execution-computed; environment_fingerprint: `rg` + Git version; invalidation_paths: owned paths; subsumes: None; run_point: before CP3; evidence: no workflow matches for capture parameter/row/evidence/copy/command or marker script/row/read/parameters; direct `production-validator` call present
  - check_id: `t3-planonly-production`; tier: `fast`; mutates_project: `false`; input_paths: workflow path + project/version/config files; input_digest: execution-computed; environment_fingerprint: short path, PowerShell, Unity path/version discovery, durable evidence root; invalidation_paths: workflow path + config; subsumes: None; run_point: before CP3; evidence: `ProductionPrepare -PlanOnly` and `ProductionValidate -PlanOnly` workflow results outside project, no marker/capture command, expected rows deferred, process/lock release
- proof: historical-ledger fixture copied to durable temporary evidence may contain executed `capture-validator`; plan-only new result must still contain pending/deferred `production-validator`. Fixture never enters product tree
- review_focus: partial marker deletion leaves ProductionPrepare hard failure; direct validator lost -> final semantic proof disappears; old row ID retained -> capture evidence skips validator; builder-output paths remain bake invalidators -> generated-only commit forces repeated bake
- review_checkpoint: CP3
- return_evidence: removed API/call graph; new ledger rows; AST/plan-only results; historical-row proof; deleted path; residual schema compatibility note

### T4: Accumulate PowerShell workflow validation facts

- objective: report independent workflow contract defects together at safe phase boundaries without continuing after ownership, path, process, or command safety failure
- slice_boundary: dominant invariant = phase-local diagnostic accumulation; includes preflight, probe/mode contract, and postflight; excludes marker/capture machinery already removed by T3 and safety exceptions
- covered_requirements: accumulator refactor for validation wrapper
- owner: W4 exact `luna_max` implementation worker
- depends_on: CP3 accepted
- owns: `Tools/Validation/Invoke-MovementLabWorkflow.ps1`
- protected: manual capture wrapper; Unity/C# files; lease acquisition/release; path containment; `Start-Process`; external command failure; atomic evidence writes; immutable evidence checks
- read_paths: owned workflow after T3; source handoff throw classification; PowerShell AST function boundaries
- validation_environment: Windows PowerShell parser and plan-only modes; isolated short path; durable evidence outside project; no Unity run
- unity_mutation: false
- expensive_proof_owner: active execution orchestrator
- expensive_proof_run_point: FINAL real ProductionPrepare/ProductionValidate
- proof_invalidation_paths: owned workflow path; probe schema producer paths; policy check-ledger contract
- focused_reads: script-scope preflight; `Read-ProbeContract`; `Assert-ProbeContractForMode`; postflight dirty/head/bake/digest checks; lease/process/path/external command functions
- implementation:
  1. Add phase-local ordered violation list plus `Add-WorkflowViolation` and `Complete-WorkflowValidationPhase`. One terminal exception contains numbered, labeled violations
  2. Preflight: collect independent argument shape/count/version/repository/config errors before evidence directory or lease mutation. Path canonicalization/escape failure stays immediate when later checks cannot run safely
  3. Probe structural validation: collect missing fields, wrong types, invalid arrays/count parity, schema/version/status/profile/hash-shape inconsistencies. Null/unparseable JSON yields one root violation and skips dependent field checks
  4. Mode contract: collect all independent stale/profile/path coverage facts for selected mode; throw once before next Unity-mutating step
  5. Postflight: after Unity release, collect independent dirty scope, bake-count, HEAD stability, generated-scope, and evidence self-consistency facts; throw before final success/result publication
  6. Preserve fail-fast behavior for project-path escape, durable-evidence escape/overwrite, lease ownership, active Unity/project lock, failed Git/external/Unity process, timeout, failed lock release, atomic write failure, and malformed prior evidence that cannot be trusted
  7. Preserve `Merge-ExistingLedger` demotion-to-invalidated behavior for stale/reuse evidence; do not turn stale prior proof into terminal failure
  8. New predicates introduced by refactor log/collect warning first unless predicate already enforces retained safety invariant or existed and has recorded green behavior
- done when: representative malformed preflight/probe inputs return one phase exception containing every independent defect; no safety boundary continues after failure; real workflow success shape unchanged apart from removed marker/capture fields
- checks:
  - check_id: `t4-powershell-parse`; tier: `fast`; mutates_project: `false`; input_paths: owned path; input_digest: execution-computed; environment_fingerprint: Windows PowerShell parser version; invalidation_paths: owned path; subsumes: `t3-powershell-parse`; run_point: before CP4; evidence: zero AST errors and `git diff --check`
  - check_id: `t4-planonly-modes`; tier: `fast`; mutates_project: `false`; input_paths: owned path + project/version/config; input_digest: execution-computed; environment_fingerprint: short path, PowerShell, durable evidence root; invalidation_paths: same; subsumes: `t3-planonly-production`; run_point: before CP4; evidence: all four modes `-PlanOnly` produce planned results/ledgers, no capture/marker, no project Git diff, process/lock release
- proof: safe malformed copied probe JSON outside project supplies multiple independent schema defects; `ProductionValidate -PlanOnly -ProbePath <fixture>` must report all expected labels in one terminal message. Never mutate tracked probe/assets
- review_focus: safety throw converted to accumulator -> workflow mutates after unsafe state; dependent probe fields accessed after invalid root -> noise/crash; violation thrown after result marked complete -> false evidence; stale ledger becomes terminal -> resume path breaks
- review_checkpoint: CP4
- return_evidence: accumulated phase inventory; explicit fail-fast allowlist; parser/plan-only logs; malformed-probe aggregate output; project status proof

### T5: Synchronize orchestration and repository policy

- objective: make repository and agent instructions enforce no routine capture/review marker, warning-first validation, net-subtractive unblock changes, and two-fix predicate deletion
- slice_boundary: dominant invariant = one consistent process contract across planning, execution, resume, merge, and repository instructions; excludes product/tool implementation
- covered_requirements: codify process rules
- owner: W5 exact `luna_max` implementation worker
- depends_on: None
- owns: `AGENTS.md`; `.agents/skills/orchestrate-implementation/SKILL.md`; `.agents/skills/write-orchestrator-coding-plan/SKILL.md`; `.agents/skills/loop-orchestrator/SKILL.md`; `.agents/skills/loop-orchestrator/references/state-and-recovery.md`; `.agents/skills/loop-orchestrator/agents/merging.md`
- protected: other skills/agents; source handoff; current plan artifact; deleted historical plans; runtime/editor/tool source
- read_paths: owned files; `plans/compile_and_bake_issues_fix.md`; T3 intended workflow contract; linked headings from loop/orchestrator skills
- validation_environment: Markdown/link/`rg` static inspection only
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_run_point: None
- proof_invalidation_paths: owned policy files
- focused_reads: capture/bake ledger rules; review-marker pre-bake gates; workflow-result contract; planning check contract; loop execution gate; merge invalidation rule; AGENTS graphics/validation lines
- implementation:
  1. `AGENTS.md`: High/Low visuals and 1920x1080 performance receive human review on demand; capture never substitutes for semantic validator; production bake only after settled source/reviews; remove production capture mandate
  2. `orchestrate-implementation`: remove pre-bake review-marker contract and marker gate. Execution orchestrator's accepted checkpoint/fix state plus clean source and Unity lease gate production bake
  3. Remove capture row/proof/reattest/rerun/invalidation class from orchestrate check ledger. Keep ordinary English uses such as capturing exit code/report/facts
  4. `write-orchestrator-coding-plan`: plans must not require image capture, screenshot comparison, or agent visual verification; bake is only lighting production proof; new validator predicates start warning-only and promote after one representative green Unity run
  5. `loop-orchestrator/SKILL.md`: remove review-marker prerequisite missed by handoff; keep accepted reviews/fixes and one production-final proof owner
  6. state/recovery and merging: change bake/capture/manual rules to bake-only lighting-input invalidation; keep exact-SHA artifact/evidence integrity and check-ledger mechanics
  7. Add gate-remediation policy: unblock/relax change touching predicate family must be net-subtractive in that gate family; no compensating allowlist/replacement hard gate; second corrective change to same family deletes family unless separately named retained safety invariant requires authority
  8. State explicitly: builder-output byte/hash equality never gates; source/input digests and orchestration artifact/evidence hashes remain allowed
  9. Preserve all Markdown links/headings or update targets together. Do not restore deleted historical efficiency plan or unrelated completed plans
- done when: no policy instructs automated capture/visual verification or review marker as proof; every process stage agrees on bake-only invalidation and direct semantic validation; warning-first/net-subtractive/two-fix rules have one enforcement home and no contradiction
- checks:
  - check_id: `t5-policy-sweep`; tier: `fast`; mutates_project: `false`; input_paths: owned paths; input_digest: execution-computed; environment_fingerprint: `rg` + Markdown link checker/manual target resolution; invalidation_paths: owned paths; subsumes: None; run_point: before CP5; evidence: classified capture/visual/review-marker/hash matches, valid relative link targets/headings, no forbidden mandate
  - check_id: `t5-skill-diff-check`; tier: `fast`; mutates_project: `false`; input_paths: owned paths; input_digest: execution-computed; environment_fingerprint: Git version; invalidation_paths: owned paths; subsumes: None; run_point: before CP5; evidence: `git diff --check -- .agents/skills/write-orchestrator-coding-plan/SKILL.md .agents/skills/loop-orchestrator/agents/task-breakdown.md` plus broader owned-path diff check
- proof: exact `rg` sweep distinguishes prohibited image-capture proof from allowed capture-of-facts wording and distinguishes forbidden builder-output equality from allowed source/input/artifact/evidence hashes
- review_focus: one linked skill retains capture/marker gate -> removed workflow remains required and execution blocks; blanket hash wording removes immutable artifact/evidence integrity; two-fix rule permits deletion of GUID/meta/path/process safety without authority
- review_checkpoint: CP5
- return_evidence: changed policy clauses/headings; classified residual matches; link audit; diff-check output; residual operator cleanup note

## Execution Assignments

- workers: T1 -> W1 `luna_max` -> Unity semantic accumulator; T2 -> W2 `luna_max` -> manifest/manual-capture gates; T3 -> W3 `luna_max` -> workflow demolition; T4 -> W4 `luna_max` -> PowerShell accumulator; T5 -> W5 `luna_max` -> policy synchronization
- fan-out: W1, W3, W5 launch from same exact start SHA; paths disjoint
- lane continuation: W2 launches after CP1 at accepted lane head; W4 launches after CP3 at accepted lane head; each lane uses orchestrator-updated accepted branch head and owns no cross-lane path
- review_checkpoints: CP1 -> fresh exact `sol_high` reviewer -> T1/W1 -> worker terminal + committed frozen SHA; CP2 -> fresh exact `sol_high` reviewer -> T2/W2 -> CP1 accepted + worker terminal + frozen SHA; CP3 -> fresh exact `sol_high` reviewer -> T3/W3 -> worker terminal + frozen SHA; CP4 -> fresh exact `sol_high` reviewer -> T4/W4 -> CP3 accepted + worker terminal + frozen SHA; CP5 -> fresh exact `sol_high` reviewer -> T5/W5 -> worker terminal + frozen SHA
- grouped rationale: None; every implementation worker receives default independent checkpoint
- fan-in: JOIN1 requires CP1-CP5 accepted, all accepted findings fixed, conditional fix re-review completed only when `fix_loc > 300` or originating review reported more than 4 Critical/High findings, zero writers running

## Final Verification

- exact head: source changes and accepted fixes committed; worktree clean before production run; final returned SHA includes any accepted generated-output commit and descends from baseline
- final owner: active execution orchestrator; one Unity lease; no worker/reviewer active; short absolute project path; warm private `Library`
- check row:
  - check_id: `final-static-scope`
  - tier: `fast`
  - mutates_project: `false`
  - input_paths: every owned source/policy/tool path
  - input_digest: execution-computed at clean source SHA
  - environment_fingerprint: Git + PowerShell + repository path
  - invalidation_paths: every owned source/policy/tool path
  - subsumes: task static/parser/diff checks when exact input digests match
  - run_point: after JOIN1, before Unity
  - evidence: `git diff --check`, exact `baseline..HEAD` path scope, marker/capture/policy sweeps, PowerShell AST parse, clean status
  - execution state: record `executed_sha`, `validated_sha`, `status`, evidence path/digest
- check row:
  - check_id: `production-prepare`
  - tier: `production-final`
  - mutates_project: `true`
  - input_paths: accepted editor/tool source; `Packages`; `ProjectSettings`; `Assets/_Game/Lighting/MovementLabLightingSettings.asset`; development settings; volume profile; stage-defined lighting inputs
  - input_digest: execution-computed before run
  - environment_fingerprint: Unity `6000.5.6f1`; short absolute project path; warm private `Library`; GPU/graphics mode; PowerShell version
  - invalidation_paths: lighting source/input paths only; exclude workflow-reported bake-generated output paths from same-run commit
  - subsumes: final Unity compile; stage probe; pre-bake validator; production bake
  - run_point: after JOIN1 and clean source freeze
  - evidence: `Invoke-MovementLabWorkflow.ps1 -Mode ProductionPrepare` result/manifest/check-ledger outside project; no review arguments; exactly one bake; zero Console/compiler errors; no capture; lock release; probe current/production; manifest warning behavior observed if triggered
  - execution state: source `executed_sha`; generated-only descendant `validated_sha` allowed only under same-run provenance rule; evidence path/digest; status
- generated-output handling:
  1. Read workflow `changedGeneratedPaths`; reject any path outside script authoritative generated inventory
  2. Inspect scene/prefab/material/lighting/settings diffs semantically; reject GUID churn, broken asset/meta pairs, unexpected source changes, or unrelated reserialization
  3. If no generated diff, continue at same SHA
  4. If generated diff exists, stage only reported builder-owned paths and commit separately as `chore: regenerate MovementLab outputs`
  5. Reattest production-bake row to generated-only child only when source/input digest, `lightingInputDigest`, environment, and committed output bytes equal same-run recorded after inventory. Any input/source edit invalidates and requires one replacement production run
- check row:
  - check_id: `production-validator`
  - tier: `production-final`
  - mutates_project: `false`
  - input_paths: all accepted source/tool/policy paths plus final generated manifest/scene/prefab/material/lighting/settings paths
  - input_digest: execution-computed at final committed SHA
  - environment_fingerprint: Unity `6000.5.6f1`; same short path/private `Library`; PowerShell version
  - invalidation_paths: all input paths
  - subsumes: `validator-readonly`; final Unity compile
  - run_point: after generated-output disposition at exact clean final SHA
  - evidence: separate `Invoke-MovementLabWorkflow.ps1 -Mode ProductionValidate`; ledger row ID `production-validator` executed at final SHA; method `MovementLabBuilder.ValidateMovementLab`; no capture command/evidence; aggregated validator success; zero Console errors; unchanged Git HEAD/non-generated status; lock release
  - execution state: final `executed_sha = validated_sha = HEAD`; status/evidence path/digest
- inspect: source diff; deleted marker script; no capture branch; policy residual matches; workflow ledger/result; aggregated validator section boundaries; generated diff; `.meta` pairing; exact final clean status
- invalidation: any owned source/tool/policy edit reruns final static scope; editor/tool/config edit reruns ProductionValidate; lighting input/render-source edit reruns ProductionPrepare and ProductionValidate; same-run generated-output commit follows bounded reattestation above; manual capture never runs

## Handoff

- changed paths: T1-T5 owned paths; deletion of `Tools/Validation/Write-MovementLabPreBakeReviewMarker.ps1`; conditional workflow-reported builder-owned generated paths
- residual risks: accumulator reports one violation per guarded leaf subsection, not every leaf predicate; manual capture still depends on GPU-capable batch Unity and writes under `Temp`; old merged efficiency worktree/branch remain until separate user-authorized cleanup
- authority: implementation and plan-branch Git operations only through `$orchestrate-implementation`; integration/user-branch merge requires user authority; removal of `C:\wt\eff-091dc7` and old local branch requires separate explicit authority after confirming clean state, no Unity process/lock, no writer, and SHA reachability

## Done Criteria

- every covered requirement maps to task, owner, check, and proof;
- every task passes implementation design gate;
- every task passes worker-review sizing gate: one meaningful outcome, cohesive reasoning, bounded failure domain, one proof boundary, and no incidental standalone slice;
- Execution Graph includes every task and review checkpoint exactly once and makes every sequential dependency, parallel lane, and join gate explicit;
- every implementation worker maps to one review checkpoint; no grouped checkpoints;
- exact baseline and dependencies are factual;
- execution route uses immutable attempt-bound snapshot and `$orchestrate-implementation`;
- final production proof has one owner, one production bake, no capture, no review marker, direct semantic validator, exact-head evidence, and clean process/lock release;
- routine semantic validation batches safe independent failures without weakening writer, path, GUID/meta, manifest structure, lease, or process safety;
- final checks bind clean committed head or blocker names needed action.
