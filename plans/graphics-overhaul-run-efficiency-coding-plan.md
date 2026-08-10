# Graphics Overhaul Run Efficiency Coding Plan

Status: in progress; revised 2026-08-10 — user-authorized rescope; T6 slim, T7 collapsed
Source: direct request based on `plans/graphics-overhaul-run-efficiency-handoff.md`
Run ID: direct
Plan ID: graphics-overhaul-run-efficiency
Attempt ID: 20260810T111004014-091dc7
Covered Requirements: stage-local MovementLab generation; fast preview/build loop; development and production lighting profiles; slim semantic validation; shader/reflection cleanup; targeted vectorized texture generation; cost-aware orchestration scripts; single final production pass
Baseline: 7a4dafe58ef2f43284c017bf8938dfb0c1a1daef
Execution start SHA: 2a45007da3e386b6bb67ea31647afe88147d4af4
Dependencies: None

## Revision 2026-08-10 (authoritative for remaining gates)

- authority: user-directed rescope at CP5-complete HEAD `be51d6fe85ac31220a87b986a9cea928cbc857a3`
- root cause of run slowness -> byte-exactness machinery, not bake; `Lightmapping.Bake()` PASS `22.06s`; CP5 deadlock came from raw-hash drift hard stop after harmless whitespace normalization; seven High defects + eleven fix commits all in validation/lifecycle machinery, zero in game code
- removed from remaining plan: raw byte-hash drift hard stops on builder-owned YAML; pre/post mutation hash snapshots; two-run texture proof; forced-full rebuild hash comparison; Build-2 no-op hash proof; pre-bake review markers; SOURCE_FREEZE gate; exact-SHA evidence ceremony
- retained: GUID/meta stability guard; semantic validation; explicit bake-only commands; scoped clean Git history
- immutable snapshot `<git-common-dir>/orchestrate-implementation/graphics-overhaul-run-efficiency/executions/20260810T111004014-091dc7.md` -> historical record only; this revision supersedes it for T6/CP6/T7/CP7/FINAL

## Orchestrator Resume Point

New orchestrator given this plan starts exactly here; no other entry valid.

1. Read Revision 2026-08-10 + both Policy sections first -> they override snapshot and any earlier byte-exactness contract.
2. Verify state: worktree `C:\wt\eff-091dc7`; branch `codex/graphics-overhaul-run-efficiency-20260810T111004014-091dc7`; committed HEAD `be51d6fe85ac31220a87b986a9cea928cbc857a3`; no Unity process/lock on worktree.
3. Do NOT reset/clean worktree. Dirty state intentional -> partial T6 edits in `MovementLabMaterialPipeline.cs`, `MovementLabSerializedProperties.cs`, `MovementLabSceneComposer.cs`; dirty generated outputs stay unstaged until T7 step 7.
4. Prior T6 workers stopped under old scope -> discard their context; dispatch one fresh W6 worker on rescoped T6 below; worker first action = per-file salvage/revert triage of the three partial files.
5. After W6 returns -> CP6 review -> T7 -> CP7 -> FINAL. Single worker lane; one Unity process at a time.
6. Never require: review markers, SOURCE_FREEZE, byte-hash proofs, two-run texture proof, Build-2 hash comparison. Reviewer findings demanding these -> reject as policy-contradicting.

## Policy: Generated Output Drift (accept fileID churn)

- byte churn in builder-owned generated YAML accepted -> materials, prefabs, controllers, scene, lighting data, manifests: fileID reorder, reserialization, whitespace, bake nondeterminism all legitimate
- hard line: `.meta` files + GUIDs stay stable; GUID change, meta loss, or broken asset/meta pair -> hard stop
- drift guard demoted: owned-output raw SHA-256 mismatch -> informational log only; never stops builder; staleness decided by input digests, never output bytes
- semantic validation replaces byte identity -> compile clean, references resolve, expected components/subassets present, shader compiles, zero console errors
- clean-history rule: generated churn commits separately -> `chore: regenerate MovementLab outputs`; source commits never mix generated churn

## Policy: Lighting Demoted, Not Reverted

- primary iteration path -> Fast preview (`BuildMovementLabFast`; realtime sun + trilight ambient); zero bake in inner loop
- Development bake -> on-demand best-effort command; 80-probe bounded profile retained; post-bake hash revalidation removed (CP5 failure source); bake failure -> report + exit, never recovery deadlock
- Production bake -> milestone-only explicit command for final visuals/screenshots; not gated pipeline stage; no review marker, no pass-record binding
- retained cheap check: production validator rejects development-tagged lighting as final

## Implementation Progress

### Execution State

- worktree: `C:\wt\eff-091dc7`; branch `codex/graphics-overhaul-run-efficiency-20260810T111004014-091dc7`
- committed HEAD: `be51d6fe85ac31220a87b986a9cea928cbc857a3`; CP5 complete
- T6: incomplete; both T6 workers stopped; partial uncommitted T6 edits -> `MovementLabMaterialPipeline.cs`, `MovementLabSerializedProperties.cs`, `MovementLabSceneComposer.cs`
- generated state: development lighting/scene/material/manifest outputs dirty in worktree; preserve unstaged until T7 generated commit
- Unity `6000.5.6f1`; private warm `Library`; no production bake yet

### Completed Gates (condensed)

- T1+CP1 -> cost-aware orchestration skills/scripts, review-marker writer, workflow wrapper, ledger/invalidation contracts; commits `24040c6e`, `7008993864`
- T2+CP2 -> NumPy vectorized texture generator, 15-family registry, `--family` targeted runs, deterministic manifest/comparator; commits `163f7b2358`, `026b4c46`
- T3+CP3 -> `RetroPowerGrid.shader` reserved `line` -> `gridLine`; commit `3c9a9ea`
- T4+CP4 -> stage-local generation, schema-7 manifest/probe, selective stage runner, explicit ownership; commits `66cf132`, `2c9c1bf`
- JOIN1 -> serialized Unity compile/probe PASS; commit `45a348548`
- T5+CP5 -> transient Fast session + lifecycle restore, Iteration quality profile, Development/Production lighting profiles, material bake purity, typed profile manifest; CP5 fix chain ends `be51d6fe`; Fast build PASS; Development bake PASS `22.06s`; byte-drift deadlock resolved via contract migration, then superseded by drift policy above

### Pending Gates

- T6 (rescoped slim) -> CP6 -> T7 (collapsed) -> CP7 -> FINAL

## Objective

Fast graphics iteration. Code/validator edits compile without generated writes or bake. Non-lighting changes rebuild only affected stages. Fast preview usable while production lighting stale. Bakes explicit and on demand. Validation semantic, never byte-exact. GUIDs/metas stable. Completion -> clean committed head, source and generated churn in separate commits, one clean-process validation PASS.

## Scope

- in: `Assets/_Game/Editor/MovementLabBuilder.cs`; `Assets/_Game/Editor/MovementLab/**`; `Assets/_Game/Editor/GraphicsQualityConfigurator.cs`; iteration-quality and development-lighting assets; authoritative generated MovementLab outputs
- in: `Assets/_Game/Shaders/RetroPowerGrid.shader`; redundant named reflection EXRs plus paired metas; `Tools/Blender/generate_retro_textures.py`
- in: orchestration skills + `Tools/Validation/` wrappers (shipped by T1; binding for future runs)
- out: `Arena`/`Environment` scene split; renderer/collider prefab ownership migration; gameplay mechanics/tuning; Unity/package upgrade; new test assemblies; manual generated-YAML edits; target-PC performance acceptance; broad visual redesign
- out: production High/Low behavior/default changes; scene-folder `ReflectionProbe-0.exr` through `ReflectionProbe-3.exr` removal

## Constraints

- Unity `6000.5.6f1`; one Editor per short-path worktree; warm unshared `Library`; all Unity operations serialized
- generated assets builder-owned; asset/meta pairs move or delete together; bake runs only via explicit lighting commands
- no test assemblies; proof = product entrypoints, validation commands, fixed captures

## Decisions (active)

- compatibility commands preserved; added `EnterMovementLabFastMode()`, `ExitMovementLabFastMode()`, `BuildMovementLabFast()`, `BakeMovementLabLightingDevelopment()`
- fast mode transient; Iteration URP assets persisted; High current/default and High/Low assets unchanged; enter/exit never saves project assets
- stage DAG separates ordering deps from digest deps; lighting hashes only explicit lighting inputs; gameplay-only edits never stale production lighting
- development profile: 80-probe stratified subset, reflection `64`, samples `16/128/64`, bounces `1/1`, tag `development`; production profile: 200-probe lattice, retained samples `32/512/256`, multiplier `4`, bounces `2/2`, three scene probes `128`, tag `production`
- named reflection EXR loop + three `Assets/_Game/Lighting/ReflectionProbe_*.exr` assets/metas removed atomically; scene-folder reflection outputs (four) preserved
- material authoring sets `globalIlluminationFlags` before `_EMISSION`; bake performs zero material repair/save; validation via public state after reload; private YAML keyword parsing removed
- texture generator: NumPy required, byte-identical to accepted outputs, `--family` targeted runs; `--proof-two-run` exists but unused in this plan
- drift + lighting policies above override any earlier byte-exactness decision

## Execution Graph

`START -> T1 -> CP1 -> {T2 -> CP2 || T3 -> CP3 || T4 -> CP4} -> JOIN1 -> T5 -> CP5 -> T6 -> CP6 -> T7 -> CP7 -> FINAL`

- T1 through CP5 complete; SOURCE_FREEZE gate removed
- gates: `FINAL` -> clean committed head; compile zero errors; one clean-process capture/validate PASS; retained GUID map unchanged vs baseline; scoped diff only
- rule: every Unity process serialized; single worker lane from T6 onward

## Tasks

### T1-T5: complete

See Completed Gates. Shipped contracts remain binding except where Revision 2026-08-10 policies override byte-exactness behavior.

### T6: Slim semantic validation and drift-guard relaxation

- objective: validation catches real breakage -> compile, references, shader, GUIDs, profile tag; drift guard can never stop or deadlock builder on byte churn
- covered_requirements: semantic validation; drift policy implementation; lighting demotion implementation; zero-repair material purity
- owner: fresh W6 implementation worker, `luna_max`
- depends_on: CP5 accepted HEAD `be51d6fe85ac31220a87b986a9cea928cbc857a3`
- owns: `MovementLabPreBakeGate.cs`; `MovementLabValidator.cs`; `MovementLabSerializedProperties.cs`; `MovementLabMaterialPipeline.cs`; `MovementLabLightingPipeline.cs`; `MovementLabSceneComposer.cs`; drift-guard paths in `MovementLabStageGraph.cs`/`MovementLabStageRunner.cs`; required facade edits; `AGENTS.md` policy alignment
- protected: generated outputs until T7; runtime behavior/tuning; public facade names; High/Low defaults; GUID/meta guard and asset/meta pair enforcement must survive
- first action: inspect partial uncommitted T6 edits in the three owned files above -> salvage edits matching this slim scope; revert remainder before new work; record salvage/revert decision per file
- implementation (kept from original T6 scope):
  - replace validation-time `GetUniversalAdditional*Data()` with non-creating `TryGetComponent`/public inspection; validator calls no setter/save/import/refresh/dirty-clear/repair
  - persisted references -> nonzero scene object IDs via `GlobalObjectId`/serialized APIs; prefab-derived refs verify source provenance; validate all builder-created cross-object refs
  - `Volume.sharedProfile` persistent; each expected component a persistent subasset with nonzero local ID
  - material purity -> deterministic `globalIlluminationFlags` before keyword state; remove private YAML keyword parser and both bake-time material repair/save calls; clean-reload emissive checks (`RocketHot`, `ArenaGlow`, `WeaponAccent`) via public state
  - shader gate -> `Shader.isSupported` + `ShaderUtil.GetShaderMessages`; any error blocks bake/capture
  - replace facade-wide bake `AssetDatabase.SaveAssets()` with scene/lighting-output-scoped persistence
  - typed lighting manifest tag check -> production validator rejects development tag with explicit production-bake instruction
- implementation (new, drift + lighting policy):
  - demote owned-output raw-hash drift stop -> GUID/meta existence + stability checks plus semantic validation; byte mismatch logs informational and proceeds; recorded hashes refresh on next successful stage write
  - remove pre/post owned-hash mutation snapshots from validator and bake paths
  - remove post-bake hash revalidation from both bake paths (`RevalidatePassRecord` hard-stop chain that caused CP5 deadlock)
  - remove now-dead CP5 recovery machinery (contract-migration predicate, whitespace-normalization special cases) where trivially separable; otherwise leave minimal
- implementation (repo-wide policy alignment, `AGENTS.md`):
  - "Unity asset safety" -> keep GUID-churn rejection; drop reserialization rejection for builder-owned generated assets; add drift policy summary: generated YAML byte churn accepted, GUID/meta stability enforced, generated churn commits separately as `chore: regenerate MovementLab outputs`
  - "Validation" -> replace build-twice hash-comparison rule with: builder-generated change -> one build + `ValidateMovementLab()` semantic PASS in separate process; remove "any mismatch -> nondeterministic build bug" byte criterion
  - "Unity execution" builder no-op gate -> reword fingerprint check as input-digest staleness check; output bytes never gate rebuild
  - add lighting posture line: Fast preview = default iteration path; Development bake on demand; Production bake milestone-only explicit command
  - doc edits land in same commit wave as T6 code so policy text and enforcing code stay consistent
- done when: validator-only edit -> compile + zero generated writes; deliberate byte edit in owned material -> validation PASS with info log; GUID change -> hard stop; deliberate broken reference -> FAIL before any bake; both bake commands complete without post-bake hash stop; `AGENTS.md` contains zero byte-hash validation requirements for builder outputs
- checks: Unity compile zero errors; one `ValidateMovementLab()` run against current development-tagged state -> only expected failure is explicit development-tag rejection; no production bake; `git diff --check`; `AGENTS.md` diff inspected against both Policy sections
- review_focus: Critical/High -> validator mutation via component creation, missing reference/GUID check, GUID guard accidentally removed, hidden save, drift guard still able to stop builder, `AGENTS.md` contradiction with Policy sections
- review_checkpoint: CP6
- return_evidence: per-file salvage/revert decision, validation call graph, drift-guard diff, compile/validate logs

### T7: Single production pass and commits

- objective: publish authoritative generated outputs and final visuals once, with clean separated Git history
- owner: W7 implementation worker `luna_max` runs workflow; execution orchestrator owns Git
- depends_on: CP6 accepted; every fix committed; clean source tree
- steps:
  1. commit remaining source fixes; record final source SHA
  2. `python Tools/Blender/generate_retro_textures.py --family all` once; audits pass; no `--proof-two-run`; no texture/meta GUID change
  3. workflow `Fast` once -> compile + `BuildMovementLabFast` sanity; zero bake
  4. explicit production prepare + bake once -> 200-probe layout, three `128` scene probes, tag `production`; zero material repair; delete three named lighting EXRs with paired metas
  5. clean-process `ProductionValidate -Capture` -> six captures, no magenta containment, zero shader/Console errors; capture-invoked `ValidateMovementLab()` is the final validation; capture skip requires explicit user authority, then standalone validator instead
  6. GUID check -> retained GUID map equal vs baseline snapshot
  7. orchestrator stages generated paths + deletions and commits separately as `chore: regenerate MovementLab outputs`; worktree clean after
- dropped: SOURCE_FREEZE ceremony; review marker; forced-full rebuild comparison; Build-2 no-op hash proof; two-run texture proof; evidence-manifest digest binding
- evidence: command logs, bake count `1`, capture PNGs, GUID map result -> `<git-common-dir>/movement-lab-proof/20260810T111004014-091dc7/final/`
- done when: clean committed head; capture/validate PASS; GUID map unchanged; generated churn isolated in chore commit
- review_focus: Critical/High -> generated diff outside owned scope, GUID drift, missing deleted meta, second production bake, development artifact retained as final, magenta/shader capture failure
- review_checkpoint: CP7

## Execution Assignments

- T6 -> fresh W6 `luna_max`; CP6 -> fresh `sol_high`
- T7 -> W7 `luna_max` workflow; execution orchestrator owns commits; CP7 -> fresh `sol_high` reviews generated diff scope, GUID map, captures; no byte-hash review criteria
- every Unity process serialized; single worker lane from T6 onward

## Final Verification

- compile: zero C#/shader/Console errors at final head
- validation: one clean-process semantic validate PASS (capture path preferred; includes `ValidateMovementLab()`)
- GUIDs: retained GUID map equal vs baseline; deleted asset/meta pairs complete (three named lighting EXRs)
- lighting: production-tagged manifest at final head; development outputs overwritten before final commit
- Git: source commits and `chore: regenerate MovementLab outputs` commit separated; scoped diff; worktree clean; `git diff --check`
- no byte-hash acceptance criteria anywhere

## Handoff

- changed paths: orchestration skills/scripts; `AGENTS.md`; texture generator; RetroPowerGrid shader; MovementLab facade/stage/lighting/validation modules; Iteration URP assets; development LightingSettings; authoritative generated assets/manifests; removed named reflection EXRs/metas
- residual risks: bake/reserialization nondeterminism accepted by policy (no byte determinism proof); manual edits to generated YAML detectable only via GUID/semantic checks; fast editor crash before lifecycle restore relies on transient state; Blender NumPy version unverified until full run; machine timing varies
- authority: execution orchestrator owns worktree branch and commits; integration into user branch still requires explicit approval

## Done Criteria

- T6 slim validator merged: non-creating inspection, reference/GUID/shader/tag gates, drift guard demoted to GUID/semantic
- deliberate owned-YAML byte edit -> validation PASS + info log; GUID change -> hard stop; broken reference -> FAIL before bake
- exactly one full texture run, one production bake, one clean-process validate/capture in T7
- worktree clean at committed final head; retained GUID map unchanged; generated churn isolated in chore commit
