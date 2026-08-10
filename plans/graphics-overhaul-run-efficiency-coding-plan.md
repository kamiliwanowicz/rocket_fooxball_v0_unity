# Graphics Overhaul Run Efficiency Coding Plan

Status: in progress; CP5 recovery open
Source: direct request based on `plans/graphics-overhaul-run-efficiency-handoff.md`
Run ID: direct
Plan ID: graphics-overhaul-run-efficiency
Attempt ID: 20260810T111004014-091dc7
Covered Requirements: stage-local MovementLab generation; fast preview/build loop; development and production lighting profiles; read-only exhaustive validation; shader/reflection cleanup; targeted vectorized texture generation; cost-aware orchestration scripts; final exact-SHA proof
Baseline: 7a4dafe58ef2f43284c017bf8938dfb0c1a1daef
Execution start SHA: 2a45007da3e386b6bb67ea31647afe88147d4af4
Dependencies: None

## Implementation Progress

### Execution State

- worktree: `C:\wt\eff-091dc7`
- branch: `codex/graphics-overhaul-run-efficiency-20260810T111004014-091dc7`
- current committed HEAD: `ec8d1e015e65ea1e0b29d4800be4ec56bcb3b9a7`
- immutable snapshot: `<git-common-dir>/orchestrate-implementation/graphics-overhaul-run-efficiency/executions/20260810T111004014-091dc7.md`
- snapshot SHA-256: `5fe68ee42c98d7a961dba80e4fb67c6050f92b8a7ceabe84883fd39617c048e6`
- evidence root: `<git-common-dir>/movement-lab-proof/20260810T111004014-091dc7/`
- Unity: `6000.5.6f1`; private warm `Library`; no production bake run
- agent state: CP5 fix worker stopped; Sol-high investigator `exec-cp5-investigate-091dc7-01` complete; no continuing worker authorized
- generated state: intermediate Development/Iteration/scene/lighting outputs dirty in worktree; preserve unstaged until T7

### Completed Gates

- T1 + CP1: complete
  - implementation commit: `24040c6e`
  - CP1 fix commit: `7008993864`
  - result: cost-aware orchestration docs/scripts, review-marker writer, workflow wrapper, ledger/invalidation contracts
  - proof: PowerShell parse, Markdown link/heading resolution, schema/trace/safety checks, PlanOnly workflow
- T2 + CP2: complete
  - implementation commit: `163f7b2358`
  - CP2 fix commit: `026b4c46`
  - result: NumPy vectorized targeted generator, 15-family registry, canonical PNG compressor, deterministic manifest/comparator
  - proof: isolated `--family wall` -> four accepted hashes, two previews, audit PASS, unselected 44 repo PNG hashes/mtimes unchanged
  - deferred by plan: full `--family all --proof-two-run` remains T7 production-final proof
- T3 + CP3: complete
  - implementation commit: `3c9a9ea`
  - result: `RetroPowerGrid.shader` reserved local `line` -> `gridLine`; expression-only change
  - proof: static equivalence + JOIN1 Unity shader compile
- T4 + CP4: complete
  - implementation commit: `66cf132`
  - CP4 fix commit: `2c9c1bf`
  - result: stage-local generation, schema-7 manifest/probe, selective runner, explicit ownership/digests, force-full comparator, bake reuse
- JOIN1: complete
  - compile-fix commit: `45a348548`
  - proof: serialized Unity compile/probe PASS; no generated writes/bake
- T5 feature source: implemented; CP5 recovery/proof open
  - implementation commit: `a320fc10`
  - accepted fix-chain commits: `ebac6ebb` -> `82f4fcee` -> `905e1e4b` -> `060e6149` -> `a1448f61` -> `73c61288` -> `2490dc8b` -> `8d7758c1` -> `b63a3b39` -> `1e28df8d` -> `ec8d1e01`
  - result: transient Fast session, lifecycle restoration, Iteration quality, Development/Production profiles, material bake purity, typed profile manifest, profile-bound pre-bake gate
  - Fast proof: `BuildMovementLabFast` PASS at `2490dc8b`; preview apply/restore PASS; no bake
  - latest source compile: PASS at `ec8d1e01`
  - CP5 review report: `<git-common-dir>/movement-lab-proof/20260810T111004014-091dc7/cp5/review-exec-cp5-review-091dc7-01.json`
  - review report SHA-256: `1496f449fbebf614e9eea59a544ebe93bd8e2c21aadf9058dee30d42fe850ea2`
  - existing `ec8d1e01` review marker becomes historical after next source commit; create new exact-SHA marker

### CP5 Open Recovery

#### Why CP5 Expanded

- static review found seven High lifecycle/profile/purity defects; fixes passed editor-project builds
- serialized Unity runs then exposed persisted-state edges sequentially -> profile ordering, scene reopen, Fast lifecycle restore, reflection API state, quality/gameplay contract migration, pre-bake handoff, `.slnx` checkpoint exemption
- each fix retained fail-closed validation; production bake stayed deferred
- latest remaining defect occurs after successful Development bake, not during compile/profile setup/bake

#### Current Failure

- command: `BakeMovementLabLightingDevelopment()` at `ec8d1e01`
- bake: `Lightmapping.Bake()` PASS in `22.06s`
- command: exit `1`; probe JSON absent
- error prefix: `InvalidOperationException: MovementLab output drift in MaterialPrefab; generation stopped.`
- drift set: exactly 35 raw SHA-256 mismatches -> 30 materials, four prefabs, one controller; zero metas
- live files: zero trailing-whitespace lines after failure
- manifest: schema `7`, structurally valid, stale only for `Lighting,BakedOutput`, stored old raw MaterialPrefab hashes
- log: `<git-common-dir>/movement-lab-proof/20260810T111004014-091dc7/t5-postfix/development-bake-ec8d1e01.log`

#### Failure Mechanism

`Lightmapping.Bake()` -> material hash/dirty guard PASS -> scene save -> broad `NormalizeGeneratedYamlWhitespace()` -> 35 MaterialPrefab-owned YAML files trimmed -> lighting manifest write uses non-throwing `Probe(false)` -> `RevalidatePassRecord()` uses `Probe(true)` -> raw output drift hard stop

- bake purity guard brackets only `Lightmapping.Bake()`; post-bake normalizer runs after guard
- broad normalizer owns full generated YAML list -> materials, prefabs, controllers, scene, metas
- normalization changed bytes without semantic material/prefab edits
- hard stop correct: stage manifest promises byte identity; validator cannot infer harmless drift from hashes alone

#### Recovery Deadlock

- narrow future normalization prevents recurrence; existing 35 mismatches remain
- `MovementLabStageRunner.RunSelective()` starts with `Probe(true)` -> throws before authoritative writer
- current-schema manifest remains readable -> `EnsureWriteAuthorization()` returns without consuming migration authorization
- normal `AuthorizeMovementLabManifestMigration()` cannot reach replay path
- direct manifest/output edit would bypass builder ownership and exact drift proof

#### Uncommitted Prevention Patch

- `Assets/_Game/Editor/MovementLabBuilder.cs` -> both bake paths call `MovementLabLightingPipeline.NormalizePostBakeYamlWhitespace()`
- `Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs` -> normalization limited to scene + `LightingData.asset`
- required refinement: use explicit `MovementLabContract.ScenePath` + `MovementLabContract.BakedLightingPath + "/LightingData.asset"`; avoid positional `GeneratedBakedLightingPaths[0]`
- protected bytes: material/prefab/controller outputs excluded
- static proof: editor project restore/build PASS, zero errors; no Unity run

#### Proposed Fail-Closed Fix

1. Keep narrow post-bake normalization patch with explicit paths.
2. `MovementLabStageGraph` -> add `PreviousMaterialContract = "material-prefab-contract:3"`; bump `MaterialContract` to `material-prefab-contract:4`.
3. Add one-time migration predicate:
   - stage exactly `MaterialPrefab`
   - prior contract exactly `material-prefab-contract:3;serialized:1`
   - current contract exactly `material-prefab-contract:4;serialized:1`
   - drift nonempty
   - every `changed:`/`missing:` token targets exact MaterialPrefab owned output/meta set
4. Predicate suppresses initial drift exception only. Preserve `contract-changed` + every drift reason; stage remains stale.
5. Runner executes authoritative MaterialPrefab generator -> targeted save/import/reload -> raw hash recapture -> stage-record merge.
6. Contract-4 probe re-enables normal hard stop. Later unchanged-contract byte drift fails before writer.

#### Rejected Fixes

- canonical/semantic output hashes -> hide future whitespace/manual byte drift
- generic `declaredInputChanged` drift allowance -> broad overwrite authority
- manifest schema bump -> unrelated full-manifest migration
- generic authorization bypass -> current valid manifest trust weakened
- `MarkCurrent()` over live drift -> adopts untrusted bytes without authoritative replay
- direct generated-file/manifest repair -> violates builder ownership

### Resume Gate

1. Inspect uncommitted narrow-normalization patch. Replace positional baked path with explicit `LightingData.asset` path.
2. Fresh fix worker -> exact MaterialPrefab contract `3 -> 4` migration gate. Source only; no generated edits/Git/Unity.
3. Root -> static checks, commit source, create new exact-SHA CP5 review marker. CP5 fixes receive no re-review.
4. Unity compile.
5. Fast build 1 -> exact v3-to-v4 migration accepted; authoritative MaterialPrefab replay; zero non-lighting stale stages.
6. Fast build 2 -> no-op; identical raw non-lighting hashes.
7. Capture MaterialPrefab hashes -> Development bake -> post-bake probe current Development -> hashes unchanged.
8. Separate probe process -> PASS. Production validator -> expected explicit `current profile=Development` rejection.
9. Disposable validation copy -> mutate contract-4 MaterialPrefab output -> pre-write hard stop.
10. CP5 accepted only after steps 1-9. Then T6 -> CP6 -> SOURCE_FREEZE -> T7. Production bake remains T7-only.

### Pending Gates

- CP5: open; blocked only by exact recovery/validation sequence above
- T6 + CP6: not started
- SOURCE_FREEZE: not reached
- T7 + CP7: not started
- FINAL: not reached

## Objective

Make routine graphics iteration fast and explicit. Validator/code edits must compile or validate without generated writes or bake. Non-lighting changes must rebuild only affected stages. Fast preview must remain usable while production lighting is stale. Development bake must provide bounded local GI proof. Production bake, full texture determinism proof, and visual capture must run once after source review/fixes unless final invalidating change requires rerun. Completion requires clean committed exact head, unchanged production defaults/GUIDs, deterministic generated state, read-only validation, and orchestration evidence proving skipped work was safely skipped.

## Scope

- in: `Assets/_Game/Editor/MovementLabBuilder.cs`; `Assets/_Game/Editor/MovementLab/**`; `Assets/_Game/Editor/GraphicsQualityConfigurator.cs`; required iteration-quality and development-lighting assets; authoritative generated MovementLab outputs
- in: `Assets/_Game/Shaders/RetroPowerGrid.shader`; redundant named reflection EXRs plus paired metas; `Tools/Blender/generate_retro_textures.py`
- in: `.agents/skills/orchestrate-implementation/**`; `.agents/skills/write-orchestrator-coding-plan/**`; `.agents/skills/loop-orchestrator/**`; `AGENTS.md`; new MovementLab orchestration wrappers under `Tools/Validation/`
- in: machine-readable stage probe; review-marker writer; fast/development/production workflow coordinator; exact check-ledger/invalidation contracts
- out: root-only `Arena`/`Environment` additive scene split; renderer/collider prefab ownership migration; gameplay mechanics/tuning; Unity/package upgrade; new test assemblies; manual generated-YAML edits; target-PC performance acceptance; broad visual redesign
- out: production High/Low behavior/default changes; accepted production lighting GUID replacement; scene-folder `ReflectionProbe-0.exr` through `ReflectionProbe-3.exr` removal

## Repository Findings

- observed: `Assets/_Game/Editor/MovementLabBuilder.cs:13-99` -> explicit assemble/pre-bake/production-bake/final-validate commands already exist; no hidden bake inside `BuildMovementLab()`
- observed: `Assets/_Game/Editor/MovementLabBuilder.cs:25-47` -> any stale non-lighting stage still calls full `AssembleMovementLabUnstaged()`
- observed: `Assets/_Game/Editor/MovementLab/MovementLabSceneComposer.cs:39-200` -> monolithic assembly rewrites quality, importers, materials, prefabs, project settings, scene, lighting environment, and manifest-adjacent state
- observed: `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs:50-140` -> six-stage probe exists, but full predecessor digests make gameplay-only changes stale production lighting
- observed: `MovementLabStageGraph.cs:110-114` -> stored Unity version comparison invalidates every stage despite per-stage `IncludeUnityVersion`
- observed: `MovementLabStageGraph.cs:227-295` -> canonical gameplay scene hash already normalizes LF, sorts documents, and excludes only `!u!157 LightmapSettings`
- observed: `MovementLabStageGraph.cs:116-129` -> trusted output drift stops with exact paths; importer source assets are also modeled as outputs, misclassifying legitimate source changes
- observed: `Assets/_Game/Editor/MovementLab/MovementLabPreBakeGate.cs:55-93` -> broad pre-bake gate writes external pass record but lacks project-output pre/post hash guard
- observed: `Assets/_Game/Editor/MovementLab/MovementLabSceneComposer.cs:268-290` -> validation calls `GetUniversalAdditionalCameraData()`, which can add missing component in memory
- observed: `Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs:314-350` -> bake repairs material keywords twice, saves them later, renders redundant named reflection probes, and writes untyped lighting manifest
- observed: `Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs:287-310` -> only one production-like profile; direct/indirect/environment samples and probe multiplier remain implicit serialized state
- observed: `Assets/_Game/Lighting/MovementLabLightingSettings.asset` -> retained production samples `32/512/256`, probe multiplier `4`, minimum/maximum bounces `2/2`
- observed: `Assets/_Game/Editor/GraphicsQualityConfigurator.cs:21-24,237-277,354-373` -> exactly High/Low; current/default forced High; no Iteration profile
- observed: `Assets/_Game/Shaders/RetroPowerGrid.shader:115-116` -> `line` identifier causes recorded D3D11 parser failure and downstream invalid `saturate` parse
- observed: `Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs:329-340` -> explicit named reflection loop writes three unreferenced `Assets/_Game/Lighting/ReflectionProbe_*.exr` files
- observed: `Assets/_Game/Scenes/MovementLab/ReflectionProbe-0.exr` through `ReflectionProbe-3.exr` -> direct `LightingData.asset` dependencies; all remain authoritative
- observed: `Tools/Blender/generate_retro_textures.py:1109-1199` -> every call regenerates 44 textures and 25 previews; no CLI family selection or two-run comparator
- observed: texture hot paths -> scalar Python surface/weapon/ball/rocket/sky generation, Python channel-list audits, nearest-neighbor expansion, and duplicate PNG compression/hash passes
- observed: no repository test sources/harnesses exist; historical efficiency harness absent; old `C:\rfx\46075b` evidence proves baseline only
- observed: `.agents/skills/orchestrate-implementation/SKILL.md:128-144`, loop merge rules, and generic check state can repeat worker/final/merge Unity proof because cost, inputs, invalidation, mutation, and subsumption are not recorded
- observed: pre-bake exact-SHA review marker has no repository writer; first production attempt can fail after review because orchestration cannot create required evidence
- constraint: Unity `6000.5.6f1`; one Editor per short-path worktree; one warm unshared `Library`; all Unity import/build/bake/capture operations serialized
- constraint: generated assets remain builder-owned; asset/meta pairs move or delete together; production bake remains explicit lighting entry only
- proposed: `Assets/_Game/Editor/MovementLab/MovementLabStageRunner.cs` -> stage-local writer ordering and manifest merge
- proposed: `Assets/_Game/Editor/MovementLab/MovementLabFastModeSession.cs` -> transient preview snapshot/apply/restore/save guard
- proposed: `Tools/Validation/Write-MovementLabPreBakeReviewMarker.ps1` -> exact reviewed-source evidence writer
- proposed: `Tools/Validation/Invoke-MovementLabWorkflow.ps1` -> machine-probed Fast, Development, ProductionPrepare, ProductionValidate workflows

## Decisions

- assumption: development probe layout uses deterministic spatially stratified 80-position subset of existing 200-position production lattice; no production layout/tuning change
- assumption: retained production sample values stay `direct=32`, `indirect=512`, `environment=256`, probe multiplier `4`, minimum/maximum bounces `2/2`; manifest makes retained values explicit
- decision: preserve existing public compatibility commands. Add `EnterMovementLabFastMode()`, `ExitMovementLabFastMode()`, `BuildMovementLabFast()`, and `BakeMovementLabLightingDevelopment()`
- decision: fast mode remains transient. Persist dedicated Iteration URP assets/quality entry, but keep High current/default and High/Low assets unchanged. Enter/exit never saves project assets
- decision: fast session requires loaded clean MovementLab scene at entry. Capture quality index, renderer baked/realtime indices and scale offsets, renderer probe/shadow state, sun, ambient/reflection, volume, and camera HDR/post state. Restore before asset save, scene close, play-mode transition, assembly reload, builder mutation, validation, bake, and Editor quit
- decision: direct preview setters use no `Undo`, `SetDirty`, or save. `AssetModificationProcessor.OnWillSaveAssets` restores preview before permitting MovementLab/project-settings save
- decision: fast default -> detached renderer baked/realtime bindings; realtime directional sun; no shadows; Trilight ambient `(0.62,0.70,0.78)` / `(0.48,0.52,0.56)` / `(0.28,0.31,0.35)` at `1.6`; realtime GI/HDR/SSAO/bloom/post off; sky/default or retained stable reflection. Optional sun-shadow menu toggle stays off by default
- decision: stage DAG separates ordering dependencies from digest dependencies. Lighting waits for material/gameplay/quality assembly, but hashes only explicit lighting inputs; gameplay behavior/wiring changes do not inherit full gameplay output digest
- decision: source assets are stage inputs, never owned outputs. Imported dependency hashes remain observed state used downstream. Builder-owned importer metas remain drift-protected outputs
- decision: stage probe determines input staleness before dependency-state comparison. Manual drift in trusted owned outputs always stops; changed input may change derived dependency state without false output-drift failure
- decision: stage runner executes stale stages topologically: `Quality`, `Importer`, `MaterialPrefab`, `GameplayScene`. Each successful stage saves/imports/reloads its ownership, updates only its manifest record, and retains still-valid lighting/baked records
- decision: unchanged recomputed lighting digest preserves accepted baked files and manifest records. Changed lighting digest marks production lighting stale while fast preview remains available
- decision: development bake uses explicit development LightingSettings asset/profile, 80-probe layout, reflection resolution `64`, resolution `5`, min/max bounces `1/1`, direct/indirect/environment samples `16/128/64`, probe multiplier `1`. It writes normal Unity bake paths plus manifest tag `development`; production validation rejects it
- decision: production bake restores exact 200-probe production layout and retained production settings before gate, then runs exactly one `Lightmapping.Bake()` and Unity-managed reflection output. Three scene probes remain `128`; four scene-folder cubemaps remain required dependencies
- decision: lighting manifest schema records profile ID/tag, every lighting setting, probe layout/count/multiplier, reflection probe count/resolution, Unity version, source SHA, lighting-input digest, and output hashes
- decision: remove explicit `BakeReflectionProbe` loop and three named lighting EXRs/metas atomically. Preserve scene-folder reflection outputs and expected count `4`
- decision: material authoring sets deterministic `globalIlluminationFlags` before `_EMISSION` keyword/state. Bake performs zero material repair/save. Validation uses public material/shader state after reload; private YAML keyword parsing removed
- decision: pre-bake/final validation is read-only relative to project tree. External evidence write remains allowed. Validator captures owned hashes and dirty state before/after and fails on mutation
- decision: no new test assemblies. Product entrypoints, workflow wrappers, controlled expected-failure commands, hashes, shader messages, and fixed visual captures form proof
- decision: texture generator requires NumPy, reports actionable import failure, records Python/NumPy/zlib versions, preserves custom PNG byte contract, and must remain byte-identical to accepted texture outputs
- decision: targeted `--family` writes/audits only selected family and selected previews. Canonical full manifest remains full-run only. `--proof-two-run` valid only for full selection and compares 44 texture plus 25 preview hashes
- decision: orchestration check tiers -> `fast`, `development`, `production-final`. Ledger records inputs/digests/environment/mutation/invalidation/subsumption. Exact valid evidence is reused; production-final proof runs after all source review/fixes
- decision: one-plan fast-forward or unchanged integration content reuses valid proof after cheap SHA/content attestation. Changed invalidating input reopens only intersecting checks. Bake/capture/manual proof never reattested across changed lighting/render inputs
- question: None

## Execution Graph

`START -> T1 -> CP1 -> {T2 -> CP2 || T3 -> CP3 || T4 -> CP4} -> JOIN1 -> T5 -> CP5 -> T6 -> CP6 -> SOURCE_FREEZE -> T7 -> CP7 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in; `+` requires every named predecessor
- gates: `START` -> clean exact baseline, short writable isolated worktree, warm private `Library`, no Unity process/lock, retained GUID/hash snapshot
- gates: `JOIN1` -> CP2/CP3/CP4 accepted; exact source path ownership disjoint; PowerShell/Python static checks pass; one serialized Unity compile confirms joined source and shader
- gates: `SOURCE_FREEZE` -> CP1-CP6 accepted; every Critical/High fix committed; zero writers; clean non-generated source; exact SHA plus review-report digests bound; production-final checks still pending
- gates: `FINAL` -> CP7 accepted; clean committed exact head; full texture two-run proof; fast/development proofs; exactly one production bake after source freeze; no-op build; clean-process production validation/capture; zero output drift
- rule: T1 serialized before product fan-out because it edits active agent/orchestration instructions
- rule: T2/T3/T4 launch together after CP1. Paths disjoint: texture generator; one shader; MovementLab C# stage infrastructure
- rule: T5/T6 serialized because facade, stage graph, contracts, lighting, material, and validation paths overlap
- rule: every Unity process serialized. Parallel source workers run static checks only until JOIN1
- rule: T7 owns sole shared generated-output/proof environment after every source checkpoint and fix

## Tasks

### T1: Cost-aware orchestration and workflow scripts

- objective: make future orchestrated runs schedule fast checks during coding and one invalidation-aware production proof after review/fixes
- covered_requirements: orchestration script changes; flexible iteration; parallel-safe scheduling; exact-SHA evidence reuse; pre-bake review-marker creation
- owner: W1 implementation worker, `luna_max`
- depends_on: baseline SHA
- owns: `.agents/skills/orchestrate-implementation/SKILL.md`; `.agents/skills/write-orchestrator-coding-plan/SKILL.md`; `.agents/skills/loop-orchestrator/SKILL.md`; `.agents/skills/loop-orchestrator/references/state-and-recovery.md`; `.agents/skills/loop-orchestrator/agents/task-breakdown.md`; `.agents/skills/loop-orchestrator/agents/merging.md`; `AGENTS.md`; new `Tools/Validation/Write-MovementLabPreBakeReviewMarker.ps1`; new `Tools/Validation/Invoke-MovementLabWorkflow.ps1`
- protected: current invocation route; immutable plan snapshot/exact-SHA rules; sole Git/state ownership; child lifecycle; user-branch authority; Critical/High review policy; product files; Unity assets
- focused_reads: current skill check/review/merge/resume rules; pre-bake review/pass schemas; `Capture-BrightArenaVisuals.ps1`; handoff timing and completion criteria
- implementation: replace one-worker candidate size assumption with one coherent ownership/recovery/expensive-proof environment. Candidate may contain multiple workers. Add breakdown fields `read_paths`, `validation_environment`, `unity_mutation`, `expensive_proof_owner`, `expensive_proof_run_point`, `proof_invalidation_paths`
- implementation: extend planner check contract with `check_id`, `tier`, `mutates_project`, `input_paths`, `input_digest`, `environment_fingerprint`, `invalidation_paths`, `subsumes`, `run_point`, `evidence`. Require one owner for each production-final proof after source fan-in/review/fixes
- implementation: retain per-worker review default. Permit grouped checkpoint only for existing stronger-boundary reasons; expensive-check reduction alone remains insufficient. Reviews never imply Unity bake/capture rerun
- implementation: orchestrator builds check ledger before dispatch. Workers run fast/local checks only unless task explicitly owns development proof. Before project-mutating Unity proof -> zero writers, clean exact source SHA, one Unity lease, accepted source reviews/fixes, review marker
- implementation: final verification executes pending/invalidated ledger rows only. Reuse exact-SHA evidence directly. Reattest pure check at descendant SHA only when ancestry holds, declared input digest/environment match, and `git diff` across every invalidation path is empty. Never reattest bake/capture/manual proof after render/lighting input change
- implementation: loop state records check tier/status (`pending|executed|reused|deferred|invalidated`), executed/validated SHA, inputs/digest, environment, mutation flag, evidence path/digest, invalidation paths, subsumed checks. Resume and merge use ledger mechanically
- implementation: intermediate merge waves run Git/scope/downstream-contract checks. Final wave runs union pending production proof once. One-plan unchanged fast-forward reuses plan proof after cheap attestation. Merge/fix invalidates intersecting rows only
- implementation: `AGENTS.md` clarifies successful Unity build/validate already supplies compile proof; builder protocol subsumes generic build/validate; capture subsumes separate validator because capture invokes `ValidateMovementLab()`; production sequence runs after final source diff only
- implementation: marker writer accepts exact project/source/reviewed SHAs, checkpoint/reviewer/report paths and SHA-256s, finding dispositions. Verify clean non-generated source, ancestry, report bytes, required fields, expected Git-common destination. Write create/replace atomically with schema consumed by pre-bake gate; no Git mutation
- implementation: workflow wrapper supports `Fast`, `Development`, `ProductionPrepare`, `ProductionValidate`. Require short absolute project path, exact Unity `C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe`, hidden `Start-Process -Wait -PassThru`, exclusive process/lock checks, warm private Library, durable evidence outside project
- implementation: workflow reads machine probe JSON contract from T4. `Fast` -> compile/probe/`BuildMovementLabFast`; `Development` -> fast build + explicit development bake; `ProductionPrepare` -> review marker + probe + stale assembly + optional forced-full non-lighting comparison + pre-bake + at most one production bake + output hash + no-op build/hash comparison; `ProductionValidate` -> clean final SHA + capture or standalone validator + unchanged hashes
- implementation: script never stages/commits. Emit JSON result with exact SHA, commands/exits/logs, elapsed times, probe records, bake counts, hashes, changed generated paths, evidence manifest SHA-256, and lock-release proof
- done when: future LP/planner/orchestrator can distinguish fast/development/production checks, safely reuse valid evidence, create pre-bake marker, and avoid duplicate validation at merge/final boundaries
- checks: worker -> Markdown links/headings, PowerShell parser via `[scriptblock]::Create`, schema field cross-check, `git diff --check`; no Unity run; invalidated by orchestration/PowerShell contract edit
- proof: trace validator-only, shader-only, lighting-input, one-plan fast-forward, multi-wave merge, and post-proof fix cases through ledger; each reaches minimal required check set without weakening exact-SHA acceptance
- review_focus: Critical/High stale evidence reuse, missing invalidation edge, control-plane contradiction, marker forgery/path escape, Git mutation by wrapper, parallel Unity lease, skipped required final proof
- review_checkpoint: CP1
- return_evidence: changed instruction headings, ledger schema, workflow state trace, parsed script paths, safety audit, residual environment assumptions

### T2: Vectorized targeted texture iteration

- objective: reduce generator runtime and support family-local generation/audit without changing accepted texture bytes
- covered_requirements: NumPy hot paths; `--family`; changed-family audit; final-only full two-run proof
- owner: W2 implementation worker, `luna_max`
- depends_on: accepted CP1 SHA
- owns: `Tools/Blender/generate_retro_textures.py`
- protected: `Assets/_Game/Textures/**`; all paired metas/GUIDs; map dimensions/channels/seams/poles/atlas regions/palette; 44-output and 25-preview full contracts; Unity importer files
- focused_reads: generation kernels `periodic_noise`, surface/ball/rocket/VFX/sky functions; audits; preview builder; main/full manifest; MovementLab imported texture paths
- implementation: add required NumPy import with actionable Blender/CPython error. Record Python, NumPy, zlib, generator source SHA-256, selected families in manifest
- implementation: use C-order `numpy.uint8[height,width,4]` arrays. Vectorize periodic surface fields/normals, detail normal, nearest resize, weapon emission, ball dot/centre evaluation in bounded row chunks, rocket region masks, glow/shield/sprite/sky kernels, channel/seam/normal/semantic audits. Preserve scalar float64 ordering where output rounding depends on it
- implementation: retain fixed seed/LCG phases, `u8` round-to-nearest behavior, channel packing, atlas half-open bounds, explicit repeated edge/pole copies, local PNG encoder, zlib level/filter/chunk order
- implementation: encode each PNG once; write and hash same byte array. Apply same rule to previews
- implementation: central family registry IDs -> `grass`, `wall`, `trim`, `hazard`, `detail-normal`, `weapon-metal`, `weapon-dark`, `weapon-accent`, `ball`, `rocket`, `rocket-glow`, `smoke`, `explosion`, `shield`, `sky`. Each entry owns output names, generator, preview, semantic audit, expected count
- implementation: `--family <id>` repeatable; default/all selects every family. Targeted run emits selected outputs/previews and separate targeted manifest, runs generic map audits plus selected family semantic audits, skips full 44-count/memory/canonical-manifest assertions, touches no unselected production path
- implementation: full run retains canonical manifest, exact count/memory/semantic checks. `--proof-two-run` allowed only with full selection; perform two complete passes in same process/environment, compare sorted 44 output hashes and 25 preview hashes, fail on first named mismatch, preserve second/canonical bytes
- implementation: fix `_region_stats` width assumption and partial-audit direct indexing while retaining current results
- done when: targeted family run generates/audits only family; optimized full output bytes match baseline; full proof mode deterministically compares two runs
- checks: worker -> `python ... --family wall` with pre/post hashes for four wall PNGs and all unselected PNGs; require selected bytes match baseline, targeted audit PASS, no unselected timestamp/hash change; static Python compile; no full run; invalidated by generator edit
- proof: targeted wall run plus synthetic altered run-2 hash verifies comparator names mismatch; performance record compares targeted elapsed time against historical 8.5-minute full baseline without claiming final full timing
- review_focus: Critical/High texture byte/semantic drift, unselected write, excess memory, rounding/seam/pole regression, incomplete audit, nondeterministic manifest, Blender NumPy incompatibility
- review_checkpoint: CP2
- return_evidence: family registry/output map, baseline/target hashes, audit output, elapsed time, Python/NumPy versions, residual full-run risk

### T3: Containment shader compiler blocker

- objective: remove D3D11 parser failure without visual/material contract change
- covered_requirements: RetroPowerGrid compile fix; readable containment ceiling/walls
- owner: W3 implementation worker, `luna_max`
- depends_on: accepted CP1 SHA
- owns: `Assets/_Game/Shaders/RetroPowerGrid.shader`
- protected: shader path/name/GUID/meta; properties; blend/depth/cull state; grid math/colors/fog/alpha; containment materials/renderers
- focused_reads: fragment lines `97-119`; containment material shader GUID bindings; recorded error text
- implementation: rename reserved local `line` to `gridLine`; pass `gridLine * _Alpha` as one argument to `saturate`; change no math/constants/state
- done when: source contains no reserved `line` identifier and joined Unity import reports zero RetroPowerGrid/D3D11 errors
- checks: worker -> scoped diff and `git diff --check`; Unity shader import deferred to JOIN1; invalidated by shader edit
- proof: before/after fragment expression equivalence plus JOIN1 `ShaderUtil`/Unity log evidence
- review_focus: Critical/High shader parse failure, alpha/grid behavior drift, property/GUID change
- review_checkpoint: CP3
- return_evidence: exact changed expression, protected-state diff, deferred compile requirement

### T4: Stage-local generation and machine-readable state

- objective: turn existing stage probe into selective writer with precise stale reasons, safe manifest retention, and lighting-neutral bake reuse
- covered_requirements: explicit operations; affected-stage rebuild; stage keys; output drift; canonical scene digest; machine probe; forced-full dependency coverage seam
- owner: W4 implementation worker, `luna_max`
- depends_on: accepted CP1 SHA
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`; `Assets/_Game/Editor/MovementLab/**`; new `MovementLabStageRunner.cs` plus meta; required importer/scene pipeline edits; `Assets/_Game/Generated/MovementLabBuildManifest.json` schema source only, not generated bytes
- protected: runtime code; generated assets/scene/materials/prefabs/controllers/settings; lighting data/lightmaps/reflections; asset GUIDs; production bake; validator source exclusion from generation keys
- focused_reads: facade; stage graph/state/store; monolithic composer; all pipeline apply/load/save boundaries; current manifest; pre-bake gate; BrightArena callsite
- implementation: separate stage definition fields -> ordering predecessors, digest inputs, owned outputs, observed imported dependencies, Unity-version inclusion. Store precise stale reasons per stage and prior/current input/output/profile state
- implementation: importer key -> raw FBX/PNG plus paired meta source inputs, importer contract, Unity version. Owned outputs -> builder-managed importer metas only. Store `AssetDatabase.GetAssetDependencyHash()` as observed imported state used by material/prefab key, not source-owned drift path
- implementation: material/prefab key -> imported dependency state + shaders/input actions + serialized contract. Gameplay key -> persisted prefab dependency state + gameplay wiring/scene contract. Quality key -> package/configurator + High/Low/Iteration URP/settings outputs. Validator/editor implementation source excluded
- implementation: lighting key -> production LightingSettings bytes, static renderer mesh/material identities and render/lightmap/probe/shadow state, static transforms, exact material dependency hashes, lights plus public URP additional-light fields, sky/ambient/fog/reflection, light/reflection probe layouts, volume shared profile/subassets, Unity version. Exclude full gameplay/material/quality predecessor output digests
- implementation: baked-output key -> raw scene LightingData/lightmap/scene-folder reflection assets and paired metas plus typed lighting manifest. Preserve raw scene ownership for bake while gameplay stage uses canonical scene hash
- implementation: apply Unity-version comparison only where stage includes it. Validate manifest top-level signature/fingerprint/path union against stage records before read/write; normalize/unique every output path; bump schema
- implementation: trusted output drift always reports sorted `missing:`/`changed:` owned paths and stops. Input-stale stage may update derived dependency state, but manual owned-output drift still stops before writer
- implementation: add `MovementLabStageRunner`. Split composer into stage operations with persisted catalog reload barriers. Execute only stale `Quality`, `Importer`, `MaterialPrefab`, `GameplayScene` in topological order. After each success save/import/reload owned paths and atomically merge record; retain downstream Lighting/BakedOutput records until recomputed lighting input proves stale
- implementation: `AssembleMovementLab()` invokes selective runner. `BuildMovementLab()` remains assemble + read-only pre-bake + explicit stale-lighting error. No operation except lighting commands calls `Lightmapping.Bake()`
- implementation: add force-all non-lighting runner used only by final workflow. Capture hashes before/after forced rebuild; exact equality required. Never invoke bake from forced comparison
- implementation: keep canonical algorithm LF-normalized, document-order-independent, excluding only `!u!157 LightmapSettings`. Add read-only invariant self-check using current scene bytes: reordered docs unchanged; modified LightmapSettings unchanged; modified non-lighting `fileID`/content changed
- implementation: add probe JSON schema consumed by T1 wrapper. Custom command-line output path; fields -> schema, exact HEAD, Unity version, manifest status, stale stages/reasons, lighting input digest, source signature, output fingerprint, baked profile, fingerprint paths and hashes. Normal staleness exits success; drift/corruption exits failure
- implementation: add sky importer contract/meta coverage while preserving current accepted sky wrap/filter/color behavior unless explicit importer mismatch requires generated-stage update
- done when: validator-only edit yields no stage stale; gameplay-only wiring edit rebuilds gameplay only and preserves lighting digest; material/static/light change stales exact downstream stages; one output mutation stops with exact path; no-op assembly writes nothing
- checks: worker -> static call graph, stage matrix, manifest schema validation, canonical self-check design, `rg` proving sole bake calls, `git diff --check`; JOIN1 Unity compile/probe only, no generated writer; invalidated by stage/manifest/pipeline edit
- proof: recorded matrix covers validator, runtime behavior, texture source, importer meta, material, dynamic prefab, gameplay wiring, static renderer, light/probe, quality, baked file, Unity version; each expected stale/reuse reason exact
- review_focus: Critical/High missing edge, false lighting reuse, source-as-output drift, silent overwrite, stale manifest retention error, unsafe path/write, hidden bake, canonical hash collision
- review_checkpoint: CP4
- return_evidence: stage graph/ownership matrix, selective call graph, manifest/probe schema, canonical invariants, compile/probe log after JOIN1, residual dependency risk

### T5: Fast preview and explicit lighting profiles

- objective: add zero-save fast preview, bounded development bake, exact production bake, and remove redundant reflection work
- covered_requirements: fast mode defaults/restoration; Build fast; development profile; production profile; stale lighting behavior; reflection cleanup
- owner: W5 implementation worker, `luna_max`
- depends_on: accepted JOIN1 SHA
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`; `Assets/_Game/Editor/GraphicsQualityConfigurator.cs`; `Assets/_Game/Editor/MovementLab/**`; new `MovementLabFastModeSession.cs` plus meta; new `Assets/Settings/PC_Iteration_RPAsset.asset`/meta; new `Assets/Settings/PC_Iteration_Renderer.asset`/meta; new `Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset`/meta; delete three `Assets/_Game/Lighting/ReflectionProbe_*.exr` plus metas
- protected: High/Low asset bytes and GUIDs except quality list addition; High current/default; production scene/lightmap/LightingData GUIDs; scene-folder reflection outputs/metas; gameplay/runtime contracts; accepted graphics tuning outside named profiles
- focused_reads: accepted T4 stage contracts; quality configurator/runtime camera policy; lighting pipeline/settings/manifest; handoff preview experiment; reflection dependency audit; pre-bake pass binding
- implementation: configure dedicated Iteration URP copy with HDR, SSAO, post, reflection atlas work, additional lights, and all shadows off; main directional light per-pixel on; Low-like render scale/mipmap/aniso. Add quality index `2`; keep High index/current/Standalone default `0`, Low index `1`, existing High/Low assets unchanged
- implementation: add transient fast session and facade enter/exit methods. Require MovementLab scene loaded and clean at entry. Snapshot every field listed in Decisions by stable hierarchy/component identity. Apply direct non-dirty setters. Detach both baked/realtime renderer indices with `-1`, retain exact scale offsets in snapshot, apply specified ambient/sun/quality/post/reflection state
- implementation: default shadow off; separate menu toggle may enable only low-cost directional hard shadow during active session. No other fast contract changes
- implementation: restore exact snapshot on explicit exit and every lifecycle/save/builder gate. Compare restored values, original scene dirty state, production asset hashes, lighting-data binding, and quality defaults; fail closed before save on mismatch
- implementation: `BuildMovementLabFast()` restores active preview, selective-assembles stale non-lighting stages, runs fast persisted semantic checks without clean-SHA review marker/pass record/baked proof, marks/logs production lighting stale when digest changed, then re-enters preview interactively. Batch mode applies/asserts/restores preview within command
- implementation: define immutable `Development` and `Production` lighting specifications. Author/load two persisted LightingSettings assets before bake. Bake methods only select already-valid spec; no setting/material repair inside `Lightmapping.Bake()` boundary
- implementation: development prep validates non-lighting state, binds development settings, applies deterministic 80-position stratified subset spanning every production height/bounds region, sets three probe resolutions `64`, saves only lighting-owned scene/settings state, then runs profile-bound pre-bake gate and one bake. Manifest/output state tag `development`
- implementation: production prep restores exact 200-position existing lattice, retained production samples/multiplier/bounces, resolution `10`, three probe resolutions `128`, production settings binding; save/reload and run profile-bound gate before one bake. Manifest/output state tag `production`
- implementation: generated-state/profile probe treats development output as production-stale. `BuildMovementLabFast()` accepts it for preview; `BuildMovementLab()` and final validator reject with explicit production-bake instruction
- implementation: remove explicit named reflection loop, constants, outputs, validator/pre-bake/fingerprint coverage, three named EXR assets/metas. Keep all scene-folder `ReflectionProbe-0..3` paths/metas and direct LightingData dependency checks
- implementation: profile manifest schema records every specification field, actual counts/resolutions, source SHA, input digest, Unity version, output path hashes. Write atomically; no raw interpolated JSON
- done when: fast session leaves zero production writes and restores exact state; fast build never bakes/full-proves; development output is tagged/non-final; production entry alone creates production tag; redundant named EXRs absent; scene-folder four remain
- checks: worker -> static state-field inventory, profile table-to-code audit, save-guard trace, reflection reference scan, Unity compile + fast enter/apply/restore hash check, explicit development bake once (`<=60s` target from warm command excluding initial import) after source join; no production bake; invalidated by lighting/quality/scene/profile edit
- proof: fast command shows readable preview state, detached indices, target timing, exact restored snapshots/hashes; development command records settings/count/tag and production validator expected rejection; reflection dependency list retains four scene outputs
- review_focus: Critical/High preview persistence, lost renderer binding, dirty scene overwrite, High/default change, wrong probe/profile state, development accepted as production, material save, missing retained reflection dependency
- review_checkpoint: CP5
- return_evidence: preview snapshot schema/diff, timing, quality/profile records, development manifest/rejection, reflection deletion/preservation map, changed generated paths, residual editor-lifecycle risk

### T6: Exhaustive read-only pre-bake and final validation

- objective: make every semantic failure occur before bake and prove validation mutates no project output
- covered_requirements: persisted references/provenance; hash stability; Volume/URP/emission persistence; material purity; shader errors; budgets/path/meta; profile validation; zero repair
- owner: W6 implementation worker, `luna_max`
- depends_on: accepted CP5 SHA
- owns: `Assets/_Game/Editor/MovementLab/MovementLabPreBakeGate.cs`; `MovementLabValidator.cs`; `MovementLabSerializedProperties.cs`; `MovementLabMaterialPipeline.cs`; `MovementLabLightingPipeline.cs`; required contract/stage/facade validation edits
- protected: generated outputs until T7; runtime behavior/tuning; public facade names; material/shader appearance; pass/review exact-SHA binding; production bake ownership
- focused_reads: accepted T4/T5 state/profile contracts; all validator call trees; serialized scene/prefab refs; material authoring/keywords; URP helper behavior; bake output dependencies/budgets
- implementation: replace every validation-time `GetUniversalAdditional*Data()` with non-creating `TryGetComponent`/public inspection. Validator opens persisted assets/scene read-only, calls no setter/save/import/refresh/dirty-clear/repair
- implementation: pre/post snapshot -> manifest-owned raw hashes, stage digests, scene dirty state, loaded generated material dirty flags, asset identities. Successful or failed validation must leave exact equality; external pass/result evidence excluded
- implementation: pre-bake order -> restore fast mode; validate manifest structure/drift; reopen persisted importers/materials/prefabs/scene/settings; validate semantics/provenance/budgets; rerun canonical/output hashes; verify clean source/review/profile; write external pass last. Semantic/hash failure cannot reach bake
- implementation: use `GlobalObjectId`/serialized public APIs to require nonzero scene object IDs. For prefab-derived refs require expected original prefab/component source and persistent GUID/local file ID where applicable. Validate all builder-created cross-object refs, not only explosion prefab
- implementation: require `Volume.sharedProfile` persistent and each expected component a persistent subasset with nonzero local ID; reject missing/duplicate/unexpected required components
- implementation: material authoring sets `globalIlluminationFlags` deterministically before keyword state. Clean-reload validation requires emissive materials (`RocketHot`, `ArenaGlow`, `WeaponAccent`) retain `BakedEmissive`, `_EMISSION`, emission map/color/strength; non-emissive contract remains exact. Remove private YAML keyword parser and both bake repair calls
- implementation: before/after every bake assert all generated material file hashes/dirty states identical. Replace facade-wide bake `AssetDatabase.SaveAssets()` with scene/lighting-output-specific persistence; production/development settings already settled before gate
- implementation: require Sun/accent `UniversalAdditionalLightData` persisted and exact public values; include shadow strength/bias and relevant URP fields in lighting digest/revalidation
- implementation: parse typed lighting manifest. Development validator checks exact development settings/tag. Production `ValidateMovementLab()` requires production tag/settings, exact 200 probes, three `128` scene probes, four scene-folder cubemaps as direct LightingData dependencies, nonzero LightingData scene reference, renderer indices/scale offsets consistent with loaded LightingData
- implementation: verify production/development LightingSettings asset values and scene binding before bake. Pass record binds profile ID, source SHA, Unity, lighting digest, review report digests, validated-stage list; revalidate every bound field immediately before `Lightmapping.Bake()`
- implementation: validate RetroPowerGrid via `Shader.isSupported` and `ShaderUtil.GetShaderMessages`; any error blocks bake/capture. Retain exact geometry/render budgets and paired-meta/generated-path coverage; add missing shader/profile/reflection path changes
- implementation: final validation success marker emitted only after importer, animator, prefab, arena, lighting, profile, shader, path, budget, hash, and mutation guards all pass
- done when: validator-only edit compiles/validates with zero generated writes; deliberate semantic failure exits before bake counter changes; bake changes zero material hashes; production validator rejects development tag; clean final validator changes zero owned hashes
- checks: worker -> Unity compile; read-only validator hash guard on development state with expected production rejection; controlled copied/evidence-level semantic fault proves no bake call; static mutation scan; `git diff --check`; no production bake; invalidated by validator/material/profile/stage edit
- proof: evidence includes pre/post hashes/dirty state, exact deliberate failure, zero bake marker, reference/provenance inventory, material reload state, profile rejection, shader message list
- review_focus: Critical/High hidden mutation, false pass, semantic gap reaching bake, prefab/fileID misclassification, material hash drift, development accepted, LightingData/reflection mismatch, success marker before failure
- review_checkpoint: CP6
- return_evidence: validation call graph, mutation audit, reference/material/profile inventories, fault proof, compile log, remaining unautomated risk

### T7: Final staged generation and one production proof bundle

- objective: run expensive work once after accepted source, publish authoritative outputs, and bind reusable exact-SHA evidence
- covered_requirements: final texture proof; forced dependency comparison; fast/development targets; exactly one production bake; Build 2 reuse; pure final validation; visual capture; clean exact head
- owner: W7 implementation worker for generated workflow, `luna_max`; execution orchestrator owns commits and post-commit validation
- depends_on: SOURCE_FREEZE exact SHA after CP1-CP6/fixes
- owns: builder-owned generated importers/metas, materials, prefabs, controllers, scene, quality/profile assets, lighting settings/manifests, `Assets/_Game/Generated/MovementLabBuildManifest.json`, scene-folder lighting outputs/metas; deletion of redundant named EXRs/metas; durable evidence under `<git-common-dir>/movement-lab-proof/<attempt-id>/`
- protected: source after freeze; texture/meta GUIDs; retained script/prefab/material/scene/LightingData GUIDs; High/Low defaults; launch checkout; Unity/package versions; user branch
- focused_reads: accepted source SHA; T1 workflow contract; T2 generator; T4 probe/stage matrix; T5 profiles; T6 validator; baseline GUID/hash map; handoff timings
- implementation: execution orchestrator drains writers, commits accepted source, records exact SOURCE_FREEZE SHA, review-report bytes/digests and finding dispositions. Create review marker through T1 script. Any later source edit invalidates freeze and returns through affected checkpoint before expensive proof
- implementation: run `python Tools/Blender/generate_retro_textures.py --family all --proof-two-run` once. Require 44 output/25 preview equality, all audits/memory pass, no texture/meta GUID change, no visual-byte change from accepted baseline. Unexpected byte change blocks before Unity generation
- implementation: run workflow `Fast`; require warm compile/code iteration `<=30s`, targeted visual iteration `<=60s`, zero bake/capture/full proof, production output/High-Low hash equality
- implementation: run force-full non-lighting comparison once from SOURCE_FREEZE. First selective assembly settles stale stages; forced rebuild produces identical importer/material/prefab/gameplay/quality hashes and same lighting digest. Difference names missing dependency edge and blocks before bake
- implementation: run workflow `Development`; require exact development manifest/settings, 80 probes, three `64` reflection probes, process target near historical `43.670s` and `<=60s`, readable fixed captures, expected production-validator rejection. Development output remains intermediate only
- implementation: run `ProductionPrepare`; restore production layout/profile, rerun exhaustive pre-bake gate, execute exactly one production `Lightmapping.Bake()` after SOURCE_FREEZE, no explicit named reflection loop, no material hash change. Require production tag, 200 probes, three `128` probes, four scene-folder cubemaps, all baked outputs/metas
- implementation: snapshot owned hashes. Run `BuildMovementLab()` in second process; require every non-lighting stage reused, lighting production current, no bake, no project write, identical hashes. Return generated path set/evidence to orchestrator
- implementation: orchestrator stages only T7 owned generated paths/deletions, commits, freezes exact generated HEAD, verifies clean status/scope/GUID map. No worker performs Git mutation
- implementation: at committed HEAD run `ProductionValidate -Capture`. Existing bright-arena capture supplies separate clean-process `ValidateMovementLab()`; do not launch redundant standalone validator. Require six captures, no magenta containment, zero shader/Console errors, unchanged owned hashes, process/lock release. If capture omitted by explicit authority, run standalone validator instead
- implementation: workflow writes evidence manifest covering commands, exit codes, logs, elapsed times, probe JSON, review marker, bake count, texture/owned/GUID hashes, profile manifests, fixed captures, expected development rejection, no-op/final hash comparisons, exact SOURCE_FREEZE and final SHA
- done when: exact committed head has production outputs only, CP7 accepts generated/evidence diff, worktree clean, every production-final ledger row executed/reused validly, no pending invalidation
- checks: worker/orchestrator -> exact commands above from one short worktree/warm Library; `$unity` path fixed to installed `6000.5.6f1`; graphics enabled only for development/production bake/capture; `git diff --check`; invalidated by source/generator/builder/profile/generated/config edit according to ledger
- proof: discriminatory bundle demonstrates targeted/full texture behavior, fast zero-write timing, forced rebuild equivalence, development rejection, one production bake, no-op Build 2, pure capture-validator, retained GUIDs/reflections
- review_focus: Critical/High evidence bound wrong SHA, second production bake, development artifact retained, output/GUID drift, missing deleted meta, material mutation, hidden no-op write, shader/magenta capture, incomplete ledger
- review_checkpoint: CP7
- return_evidence: SOURCE_FREEZE/final SHAs, clean status, changed paths, evidence root/manifest digest, timings, bake count, hashes/GUIDs, captures, check-ledger final state, residual machine-performance risk

## Execution Assignments

- workers: T1 -> W1 `luna_max`; parallel wave T2 -> W2 `luna_max`, T3 -> W3 `luna_max`, T4 -> W4 `luna_max`; T5 -> W5 `luna_max`; T6 -> W6 `luna_max`; T7 -> W7 `luna_max`
- review_checkpoints: CP1 -> fresh exact `sol_high`, orchestration/evidence safety; CP2 -> fresh exact `sol_high`, generator determinism/scope; CP3 -> fresh exact `sol_high`, shader equivalence; CP4 -> fresh exact `sol_high`, stage/invalidation safety; CP5 -> fresh exact `sol_high`, preview/profile/reflection safety; CP6 -> fresh exact `sol_high`, validator purity/completeness; CP7 -> fresh exact `sol_high`, generated outputs/evidence exact-SHA acceptance
- fan-out: T2/T3/T4 launch together after CP1; each per-worker checkpoint dispatches immediately on worker return; siblings continue
- join: JOIN1 waits for CP2+CP3+CP4, then runs one Unity compile/probe. No Unity process starts from parallel worker lanes
- source freeze: T5/T6 accepted sequentially; every source fix committed before T7. T7 is sole generated/Unity proof lane
- execution contract: exact `sol_high` execution orchestrator uses `$orchestrate-implementation`; current attempt follows accepted snapshot even while T1 changes future orchestration instructions

## Final Verification

- exact head: clean committed descendant of execution start SHA; SOURCE_FREEZE and generated final SHA recorded; immutable execution-snapshot bytes unchanged; live plan progress section may advance
- control plane: all skill links/headings valid; structured check ledger complete; PowerShell wrappers parsed; no user-branch/state/Git ownership regression
- compile: one joined source compile plus final workflow import at exact relevant SHAs; zero C#/shader/Console errors; no extra compile when later process already supplies same proof
- stage matrix: validator/runtime gameplay edits avoid generated stages; importer/material/gameplay/quality execute selectively; lighting hashes only render inputs; exact drift reasons/paths
- fast: zero production asset writes; exact snapshot restore; `<=30s` warm code loop and `<=60s` targeted visual loop; no bake/capture/full proof
- development: exact profile/tag/settings; 80 probes; three `64` reflection probes; production validator rejects; non-final outputs overwritten before final commit
- production: one explicit bake after SOURCE_FREEZE; production tag/settings; 200 probes; three `128` probes; scene-folder reflection `0..3` retained/direct; named lighting EXRs absent
- materials/validation: pre/post hashes equal through validation and bake; no private keyword parser/repair; emissive public state persists; all required refs/fileIDs/provenance/URP subassets pass
- textures: full two-run command invoked once after source review/fixes; 44 texture and 25 preview hashes equal across runs and accepted baseline; no meta/GUID churn
- deterministic generation: forced-full non-lighting comparison equal; Build 2 reuses/writes nothing; capture-validator in separate committed process writes no project output
- visuals: six fixed captures; readable arena/ball/goals/ramps; containment shader supported and non-magenta; development evidence never claimed as final quality proof
- Git: scoped diff only; deleted asset/meta pairs complete; no unrelated reserialization/IDE churn; retained GUID map equal; `git diff --check`
- invalidation: post-proof edit reruns only ledger rows whose input/invalidation paths intersect diff; lighting/render change reruns production bake/capture; validator-only edit reruns compile/read-only validate, never bake

## Handoff

- changed paths: orchestration skills/state/prompts; `AGENTS.md`; MovementLab workflow scripts; texture generator; RetroPowerGrid shader; MovementLab facade/stage/lighting/validation modules; Iteration URP assets; development LightingSettings; authoritative generated assets/manifests; removed named reflection EXRs/metas
- residual risks: fast editor crash before lifecycle restore relies on unsaved transient state; Blender bundled NumPy version unverified until execution; machine timing varies; no formal test assemblies; dependency coverage proof is periodic forced rebuild, not exhaustive static graph proof
- authority: execution orchestrator owns isolated implementation branch/worktree and commits; generated writers run only after source freeze; live plan progress edit user-authorized; implementation integration still requires explicit approval

## Done Criteria

- every direct requirement maps to task, owner, check, and proof
- every task passes implementation design gate; no worker chooses stage ownership, profile values, preview lifecycle, evidence reuse, or expensive-check run point
- Execution Graph contains T1-T7 and CP1-CP7 once; parallel paths disjoint; shared contracts/assets/Unity environment serialized
- every worker maps to one fresh checkpoint; T7 begins only after all source reviews/fixes
- baseline SHA factual; worktree starts clean; unrelated changes preserved
- validator-only edit -> zero bake and zero project-owned writes
- semantic pre-bake failure -> zero `Lightmapping.Bake()` calls
- gameplay-only edit -> unchanged lighting-input digest
- visual lighting edit -> production stale plus usable fast preview
- development output -> exact non-final tag and production rejection
- production checkpoint -> exactly one bake unless named invalidating fix requires documented rerun
- Build 2 and final capture-validator -> zero changed hashes
- targeted texture edit -> no unselected/full-suite generation; full two-run proof -> once after accepted source fixes
- immutable snapshot + `$orchestrate-implementation` route preserved; final evidence binds clean committed SHA or blocker names one required action
