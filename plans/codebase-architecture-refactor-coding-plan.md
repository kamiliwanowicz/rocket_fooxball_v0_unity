# Codebase Architecture Refactor Coding Plan

Status: accepted
Source: direct user request for repository-wide Unity architecture audit and refactor plan
Run ID: direct
Plan ID: direct
Attempt ID: direct
Covered Requirements: repository organization; god-class removal; SOLID ownership; explicit dependencies; extension/readability/maintenance; Unity serialization safety; staged deterministic generation
Baseline: 35bdd7c84bc7f14799a34b30ecbcd559f3c10b1a
Dependencies: None

## Objective

Transform current POC into clear domain modules with explicit ownership, narrow contracts, deterministic runtime lifecycles, and stage-keyed editor generation. Preserve gameplay tuning, generated asset identities, public Unity entrypoints, accepted High/Low graphics state, and authoritative MovementLab composition. Completion requires clean committed source, explicit pre-bake gate, at most one expected production bake, stable no-op rebuild, persisted reference validation, and architecture documentation matching code.

## Scope

- in: `Assets/_Game/Scripts/Runtime/**`; runtime/editor asmdefs; `Assets/_Game/Editor/MovementLabBuilder.cs`; new editor modules under `Assets/_Game/Editor/MovementLab/`; minimal `BrightArenaVisualCapture` manifest compatibility; builder-owned prefabs/scene/manifest/settings touched by migration; `plans/runtime-architecture.md`
- in: folder/namespace organization; URP assembly isolation; explicit serialized composition; canonical public APIs; builder contract/state/generation/validation split; stage-keyed invalidation; explicit pre-bake/bake commands; blast/camera rule extraction; architecture-driven ball/projectile/match lifecycle fixes
- out: gameplay tuning changes; new mechanics; multiplayer/network abstractions; event bus; DI framework; service locator; ScriptableObject tuning layer; package/Unity version changes; model/texture/shader redesign; new automated test assemblies; manual generated-YAML edits
- out: waived graphics T8 scope: 24 captures, CP8, target-PC performance, broad visual taste acceptance
- out: broad refactor of cohesive `GraphicsQualityConfigurator` and `BrightArenaVisualCapture` workflows; only shared contract/schema adjustments required by staged builder state

## Repository Findings

- observed: `Assets/_Game/Scenes/MovementLab/` -> baked lighting output folder, not code owner
- observed: `Assets/_Game/Editor/MovementLabBuilder.cs:23` -> actual god class; 4,885 lines; owns paths/specs, importers, materials, animator controllers, prefabs, arena, scene wiring, lighting, generated-state hashing, validation, serialized helpers
- observed: public builder surface -> `BuildMovementLab()` and `ValidateMovementLab()`; `BrightArenaVisualCapture` calls validator directly
- observed: current build order -> global no-op gate -> quality -> importers -> materials -> Rocket -> Ball -> Player -> Explosion -> scene -> environment -> production bake -> validation -> fingerprint -> manifest
- observed: global `ComputeBuilderSignature()` mixes validator, gameplay, graphics, Blender, texture, model, and builder bytes. Any edit invalidates whole state and can trigger multi-minute bake
- observed: `TryReuseGeneratedState()` catches semantic validation failure and falls back to rebuild; validator bug can therefore trigger production bake
- observed: graphics execution postmortem reports retained bake about 265 seconds, many repeated/aborted bakes, and validator/material/reference failures discoverable before bake
- observed: accepted graphics state -> merge `35bdd7c84bc7f14799a34b30ecbcd559f3c10b1a`; T1-T7 accepted; T8/CP8/manual performance waived by user; handoff stored at `plans/graphics-overhaul-run-efficiency-handoff.md`
- observed: current manifest and current source/output fingerprints are stale relative to worktree; dirty fingerprinted paths include `ArenaGlow.mat`, `RocketHot.mat`, and `WeaponAccent.mat`
- observed: build/validation duplicate material, containment, lighting, and serialized-field contracts; `BuildArena(...)` receives 15 material/surface arguments
- observed: source signature excludes several builder-wired runtime types, runtime metas, `GamePhysicsSettings`, and input asset
- observed: builder mutates `ProjectSettings/TagManager.asset`, but output fingerprint omits it
- observed: runtime state owners match `plans/runtime-architecture.md`: `PlayerMotor`, `BallMotor`, `RocketLauncher`, `RocketProjectile`, `GoalTrigger`, `MatchController`
- observed: `ExplosionResolver` crosses physics query, target collection, blast rules, shield policy, VFX spawn, and camera feedback
- observed: `PlayerCameraFeedback` crosses speed FOV, shake, goal orbit, hierarchy lookup, culling, overlay visibility, and rollback while remaining sole camera writer
- observed: `MatchController`, `GoalTrigger`, and `BallMotor` use scene-wide lookup fallbacks despite builder-persisted references
- observed: unused compatibility aliases expand public surface; repository/YAML scan finds no callers for audited aliases
- observed: ball contact-assist and blast impulses share one queue; successful kick clears both, producing order-dependent blast loss
- observed: projectile deferred cleanup lacks `Cancelled` terminal state; late collision can detonate after match cleanup
- observed: public gameplay gate can disagree with match state; reset repeats projectile cleanup, camera restoration, and input clearing
- observed: GoalTrigger analytic crossing is authoritative; trigger occupancy and `previousSignedDistance` state are unused
- observed: main runtime assembly references URP only for `GraphicsQualityRuntime`
- observed: runtime scripts use flat `RocketFooxball` namespace/folder despite project contract naming `RocketFooxball.Runtime`
- observed: retained component identity comes from `.cs.meta` GUID; prefab/scene links also depend on prefab-local `fileID`; namespace/assembly moves require reload proof
- observed: no project test assemblies exist; repository policy defers new tests unless explicitly requested
- constraint: direct serialized references; narrow upward events; one state owner; fixed-step critical simulation; `CharacterController` player; `Rigidbody` ball/projectile
- constraint: builder remains sole authority for generated prefabs, scene, materials, wiring, build settings, physics settings, and semantic validation
- proposed: `Assets/_Game/Editor/MovementLab/*.cs` -> contract, stage graph, generated state, import, material, animator, prefab, arena, lighting, scene-composition, pre-bake gate, and validation modules
- proposed: runtime domain folders -> `Input`, `Movement`, `Ball`, `Weapons`, `Match`, `Feedback`, `Diagnostics`, `Physics`, `Rendering`
- proposed: `RocketFooxball.Rendering` child asmdef -> sole runtime URP dependency; gameplay assembly stays `RocketFooxball.Runtime`

## Decisions

- assumption: graphics-plan move/handoff and three dirty generated materials are resolved or committed before architecture execution; launch-checkout changes remain untouched
- decision: execution starts from clean committed descendant of audit baseline in isolated writable `C:\wt\<id>` worktree; baseline records inspected state, not authority to overwrite current checkout
- decision: preserve direct-reference/state-owner architecture. No DI/event framework, generic repository/service layer, or speculative multiplayer abstraction
- decision: retain cohesive Unity owners. `PlayerMotor`, `BallMotor`, `RocketLauncher`, `RocketProjectile`, `GoalTrigger`, and `MatchController` remain public state owners; extracted rules never mutate Unity state
- decision: `MovementLabBuilder` remains public compatibility facade and composition authority; internal modules remain in existing Editor assembly
- decision: split build commands -> `AssembleMovementLab()` owns importers/materials/prefabs/gameplay scene; `ValidateMovementLabPreBake()` owns all fast persisted checks; `BakeMovementLabLighting()` owns explicit production bake; `ValidateMovementLab()` owns clean-process read-only validation
- decision: `BuildMovementLab()` compatibility entrypoint runs assembly plus pre-bake validation. It never starts production bake implicitly; stale lighting exits with exact explicit-bake action
- decision: semantic validation failure stops with exact reason. No catch-and-rebuild/bake fallback
- decision: stage keys replace global source signature. Stages -> importer; material/prefab; gameplay scene; quality; lighting input; baked output. Validator source owns no outputs and appears in no generation key
- decision: lighting input digest covers static renderers/material appearance, lights, sky, probes, lighting settings, relevant quality state, and Unity version. Gameplay-only or validator-only edits preserve baked outputs
- decision: output fingerprint drift stops with exact changed path list. No silent repair. Validator changes zero asset/object state and pre/post hashes remain identical
- decision: production bake requires durable pre-bake pass record bound to exact SHA. Maximum two bake attempts; third requires user approval
- decision: build/validation consume same immutable typed specs. No duplicated geometry/material/quality/transition contracts
- decision: preserve public component class names and retained `.cs.meta` GUIDs. Namespace migration uses `MovedFrom` metadata on serialized types plus save/reload proof
- decision: namespaces -> `RocketFooxball.Runtime.<Domain>`; shared contracts may use `RocketFooxball.Runtime`; Editor namespace stays `RocketFooxball.Editor`
- decision: one extra runtime assembly only -> `RocketFooxball.Rendering`; main Runtime drops URP; Editor references Runtime plus Rendering
- decision: Input System stays in Runtime. Second input-source abstraction waits for concrete bot/replay/network requirement
- decision: serialized MonoBehaviour tuning stays POC config authority. ScriptableObject config waits for second scene/preset
- decision: core owners require builder-wired references and fail fast. Diagnostics may retain discovery fallback
- decision: compatibility aliases removed only after C# plus UnityEvent/YAML proof. Canonical APIs remain stable
- decision: ball fix splits contact-assist and external impulse queues; kick clears contact assist only; current kick formula/timing remains
- decision: projectile state -> `Flying | Detonated | Cancelled`; cancelled projectile rejects detonation; hit position uses explicit presence flag
- decision: goal reporting -> narrow `GoalCrossed` event; Match subscribes to two serialized goals and remains sole score/state/reset owner
- decision: gameplay gate becomes private match operation; duplicate cleanup/restoration calls collapse into one owner call per transition
- decision: `PlayerCameraFeedback` remains sole camera Transform/FOV writer; plain models calculate FOV, shake, orbit; viewmodel/crosshair become explicit refs
- decision: `ExplosionResolver` remains Unity physics adapter; pure `BlastMath` owns formulas; `ExplosionVfxSpawner` owns presentation; `GoalShieldSet` owns shared shield collider catalog
- decision: no new test assemblies. Unity compile, staged builder gates, semantic validator, GUID/provenance checks, and focused manual scenarios form proof boundary
- question: None

## Execution Graph

`START -> T1 -> CP1 -> T2 -> CP2 -> T3 -> CP3 -> T4 -> CP4 -> T5 -> CP5 -> T6 -> CP6 -> T7 -> CP7 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in; `+` requires every named predecessor
- gates: `START` -> graphics T1-T7 accepted/merged; waiver handoff acknowledged; plan/handoff/dirty material state resolved into clean commit; isolated short-path worktree; Unity `6000.5.6f1`; no Unity process/lock; baseline GUID/fileID/hash snapshot captured
- gates: `FINAL` -> exact committed head; every checkpoint accepted; pre-bake pass; explicit bake; no-op rebuild; separate pure validator; GUID/provenance and focused gameplay checks pass; owned paths clean
- rule: all tasks serialized because namespace/assembly contracts, builder state graph, serialized wiring, generated assets, and single Unity project state overlap
- rule: T1-T6 use compile-only Unity where needed. No generated mutation or production bake before final accepted source checkpoint T7

## Tasks

### T1: Runtime module, namespace, and assembly boundary

- objective: replace flat runtime layout with domain folders and isolate URP without changing component identity or runtime behavior
- covered_requirements: organization; dependency direction; namespace consistency; extension/readability
- owner: W1 implementation worker, `luna_max`
- depends_on: clean accepted start SHA
- owns: `Assets/_Game/Scripts/Runtime/**`; paired moved `.meta`; `Assets/_Game/Scripts/Runtime/RocketFooxball.Runtime.asmdef`; new `Assets/_Game/Scripts/Runtime/Rendering/RocketFooxball.Rendering.asmdef`; `Assets/_Game/Editor/RocketFooxball.Editor.asmdef`; imports/path references in `Assets/_Game/Editor/*.cs`
- protected: tuning/defaults; serialized field names/types; component class names; public behavior; retained script GUIDs; prefabs/scene/materials/settings/manifest
- focused_reads: runtime/editor asmdefs; runtime cross-references; builder source inputs; prefab/scene script GUID map
- implementation: move scripts plus `.meta` into `Input`, `Movement`, `Ball`, `Weapons`, `Match`, `Feedback`, `Diagnostics`, `Physics`, `Rendering`. Keep root asmdef above gameplay folders; child asmdef only in Rendering
- implementation: migrate namespaces to `RocketFooxball.Runtime.<Domain>`; add `UnityEngine.Scripting.APIUpdating.MovedFrom` to serialized component/enum types moved from `RocketFooxball`; update references atomically
- implementation: move `GraphicsQualityRuntime.cs` plus `.meta` to Rendering; Rendering asmdef references URP; main Runtime removes URP; Editor adds Rendering reference. Keep Input System in Runtime
- implementation: update temporary builder source paths for moved files; T2 replaces list with stage contracts. Preserve class names/GUIDs
- done when: Unity compiles; Runtime has no URP dependency; prefab/scene script GUID map unchanged; reload scan finds no missing component
- checks: worker -> rename/meta map; Unity compile-only at short path exits `0`; YAML GUID scan matches baseline; scoped `git diff --check`; invalidated by namespace/asmdef/path edits
- proof: compile plus unchanged custom-script GUID map
- review_focus: Critical/High missing MonoBehaviour, cyclic asmdef, lost meta, namespace drift, runtime-to-Editor dependency, tuning/API change
- review_checkpoint: CP1
- return_evidence: move/namespace/asmdef maps, compile log, before/after GUID map, residual migration risk

### T2: MovementLab contract and stage-keyed generated state

- objective: replace monolithic invalidation with explicit stage DAG, safe manifest state, and fast pre-bake gate
- covered_requirements: god-class foundation; deterministic incremental generation; bake efficiency; single source of truth
- owner: W2 implementation worker, `sol_high`
- depends_on: accepted CP1 SHA
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`; new `Assets/_Game/Editor/MovementLab/MovementLabContract.cs`; `MovementLabSerializedProperties.cs`; `MovementLabStageGraph.cs`; `MovementLabGeneratedState.cs`; `MovementLabManifestStore.cs`; `MovementLabPreBakeGate.cs`; required `.meta`; minimal manifest reader change in `Assets/_Game/Editor/BrightArenaVisualCapture.cs`
- protected: public builder/validator entrypoints; path safety; atomic manifest writes; generated asset GUIDs; current visuals/tuning; runtime sources
- focused_reads: builder paths/specs, current fingerprint/signature/reuse logic, bake call, semantic validator, capture manifest reader, `plans/graphics-overhaul-run-efficiency-handoff.md`
- implementation: move paths, tuning, geometry/spec DTOs, owned output sets, and transition specs into typed readonly `MovementLabContract`; build and validation share same specs
- implementation: move serialized setters/checkers/prefab resolver into `MovementLabSerializedProperties`; preserve exact field-name errors
- implementation: define explicit stage DAG and immutable inputs/outputs. Importer -> source FBX/PNG/meta + importer contract. MaterialPrefab -> imported dependency hashes + material/prefab/runtime serialized contracts. GameplayScene -> prefabs + arena/containment/wiring contract. Quality -> configurator + URP/settings. Lighting -> saved static scene/material/light/sky/probe/settings dependency state + Unity version. BakedOutput -> bake files/metas
- implementation: use `AssetDatabase.GetAssetDependencyHash()` where imported dependency state is authoritative; use sorted byte hashes for repository/project files. Each stage manifest records schema, input digest, output paths/digest, Unity version, predecessor digests
- implementation: validator/editor module source excluded from generation keys. Builder-wired runtime behavior source excluded unless serialized layout/default contract changes output; explicit serialized-contract version key handles that change
- implementation: output drift returns exact changed/missing paths and stops. Semantic failure stops. Remove automatic catch -> rebuild/bake behavior
- implementation: add `AssembleMovementLab()`, `ValidateMovementLabPreBake()`, `BakeMovementLabLighting()`. Keep `BuildMovementLab()` as assemble + pre-bake compatibility facade; stale lighting requests explicit bake without starting it
- implementation: pre-bake gate validates saved/reloaded importers, materials, prefabs, scene, persistent Volume/sharedProfile/subassets, keywords/maps, geometry/budgets, path/meta coverage, source reviews/fixes marker, finalized lighting digest. Write durable pass record bound to exact SHA outside `Temp/Library/worktree`
- implementation: add `ProjectSettings/TagManager.asset` to owned output coverage; input asset and relevant metas enter stage inputs; manifest self-hash loop remains excluded. Capture reads compatible shared manifest fields only
- done when: compile passes; validator-only/gameplay-behavior-only source edits cause zero stage invalidation; semantic failure cannot reach bake; changed output reports exact path
- checks: worker -> compile-only Unity; stage matrix/source audit; non-mutating digest probes; failure-path inspection; `git diff --check`; invalidated by stage key/manifest/gate edit
- proof: discriminatory matrix records expected invalidation for validator, gameplay behavior, serialized prefab contract, material appearance, static mesh/light/probe, quality contract edits
- review_focus: Critical/High missing dependency edge, false bake reuse, silent rebuild, unsafe manifest write, semantic failure reaching bake, self-hash loop
- review_checkpoint: CP2
- return_evidence: stage DAG, invalidation matrix, manifest schema, compile log, exact failure behavior, residual forced-full-rebuild risk

### T3: MovementLab domain pipeline extraction

- objective: reduce `MovementLabBuilder` to thin command facade and split generation/validation by stable domain
- covered_requirements: primary god-class removal; cohesion; readability; maintainable extension seams
- owner: W3 implementation worker, `luna_max`
- depends_on: accepted CP2 SHA
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`; new `Assets/_Game/Editor/MovementLab/MovementLabImportPipeline.cs`; `MovementLabMaterialPipeline.cs`; `MovementLabAnimatorPipeline.cs`; `MovementLabPrefabPipeline.cs`; `MovementLabArenaPipeline.cs`; `MovementLabLightingPipeline.cs`; `MovementLabSceneComposer.cs`; `MovementLabValidator.cs`; required `.meta`
- protected: public commands/menu paths; stage graph; exact object/hierarchy names; prefab creation order; save/import/reload barriers; lighting semantics; runtime behavior; generated assets
- focused_reads: accepted builder and T2 modules; prefab/scene YAML graph; import/material/controller/build/validator seams
- implementation: facade exposes commands, resolves stage state, orders pipelines, and logs outcomes only. Domain pipelines never call facade
- implementation: import pipeline owns importer mutation/validation and clip/avatar lookup. Material pipeline owns material creation, keyword persistence, validation, and typed `MovementLabMaterialCatalog`
- implementation: animator pipeline owns controller generation, transition specs, exact validation, stale subasset cleanup; preserve world-controller in-place GUID behavior
- implementation: prefab pipeline owns Player/Ball/Rocket/Explosion plus crosshair and prefab/VFX validation; preserve Rocket reload before Player binding and Explosion persistent-component reload before scene binding
- implementation: arena pipeline owns goals/containment/architecture plus shared geometry validation. Lighting pipeline owns environment/volume/probes and explicit bake only; scene composer owns prefab instantiation/runtime wiring
- implementation: `MovementLabValidator` clean-loads scene, dispatches domain validators, checks provenance/missing components, and performs zero repair/dirty-state clearing. Material authoring/settlement belongs material pipeline before pre-bake
- implementation: preserve object creation order where local fileIDs may depend on it. Every old method maps to one module owner; no generic utility bucket
- done when: builder is thin facade; extracted modules compile; validator has no write/repair calls; no generated asset mutated before T7
- checks: worker -> method ownership inventory; compile-only Unity; facade dependency scan; duplicate contract scan; validator mutation scan; `git diff --check`; invalidated by module/facade edit
- proof: old method inventory maps one-to-one; command call graph shows assemble -> pre-bake -> explicit bake -> validate and no hidden bake path
- review_focus: Critical/High lost stage, reversed import barrier, validator mutation, facade back-call, controller GUID risk, duplicated source of truth
- review_checkpoint: CP3
- return_evidence: module map, command call graph, removed duplication/repair list, compile log

### T4: Explicit runtime composition and canonical API

- objective: make builder wiring mandatory, remove hidden core discovery, and narrow public surface without changing owner behavior
- covered_requirements: explicit dependencies; maintainability; API clarity; fail-fast composition
- owner: W4 implementation worker, `luna_max`
- depends_on: accepted CP3 SHA
- owns: runtime core components under `Ball`, `Match`, `Movement`, `Input`, `Weapons`, `Feedback`, `Diagnostics`; MovementLab prefab/scene composer and validator modules; `plans/runtime-architecture.md`
- protected: same-object required-component fallback; diagnostics discovery; serialized fields; score/reset/tuning; component GUIDs; generated assets
- focused_reads: all scene-wide lookup call sites; builder wiring/validators; alias call scan across C# and YAML; ownership doc
- implementation: remove scene-wide lookup from `BallMotor`, `GoalTrigger`, `MatchController`; validate required serialized dependencies in `Awake`, disable with exact error when invalid. Keep local required `GetComponent`
- implementation: builder supplies every cross-object dependency; validators check every persisted player/ball/goal/match/camera/presentation reference after reload
- implementation: keep diagnostics discovery fallback; HUD reads owner properties instead of querying `CharacterController`
- implementation: remove compatibility aliases only after repository plus YAML/UnityEvent scan proves zero callers; one canonical API per operation
- implementation: update runtime architecture doc with folders/namespaces, Runtime/Rendering boundary, direct-reference rule, canonical APIs, composition root, fixed/update ownership
- done when: production runtime contains no scene-wide discovery; references validate non-null/persistent; public API inventory is canonical
- checks: worker -> compile-only Unity; lookup/alias scans; serialized-reference validator inventory; doc diff; `git diff --check`; invalidated by API/wiring/doc edit
- proof: dependency map gives every core reference one source plus persisted check
- review_focus: Critical/High null dependency, removed live caller, builder string mismatch, behavior drift
- review_checkpoint: CP4
- return_evidence: dependency map, removed aliases/call proof, validator additions, compile log, doc update

### T5: Deterministic ball, projectile, goal, and match lifecycle

- objective: remove order races and contradictory lifecycle state while preserving numeric tuning and ownership
- covered_requirements: predictable physics; one state truth; clear extension contracts
- owner: W5 implementation worker, `terra_high`
- depends_on: accepted CP4 SHA
- owns: `Assets/_Game/Scripts/Runtime/Ball/BallMotor.cs`; `Ball/BallKick.cs`; new `Ball/BallMotionRules.cs`; `Weapons/RocketLauncher.cs`; `Weapons/RocketProjectile.cs`; `Match/GoalTrigger.cs`; `Match/MatchController.cs`; new `Match/MatchRules.cs`; related builder composer/validator modules
- protected: movement/ball/rocket values; kick formula/timing; blast falloff; goal geometry; freeze duration; score direction; spawn/reset transforms; component identities
- focused_reads: ball impulse/kick/contact paths; projectile destroy/detonate; goal state; match gate/reset; fixed-step callers
- implementation: split contact-assist and external impulse queues. Kick clears contact assist only; blast survives. Extract finite/clamp/resistance/kick calculations into pure `BallMotionRules`; BallMotor remains sole Rigidbody writer
- implementation: add `Flying | Detonated | Cancelled`; cleanup cancels before deferred destroy; cancelled projectile ignores sweep/collision/trigger; explicit hit-point presence replaces zero-vector sentinel; termination unregisters once
- implementation: remove unused goal trigger occupancy/previous distance; retain analytic plane crossing, deadband, one-event latch, opening bounds. Emit `GoalCrossed`; Match subscribes/unsubscribes to serialized goals
- implementation: extract score/state/timer transition calculation into pure `MatchRules`; MatchController remains sole state owner/Unity adapter. Make gameplay gate private; execute cleanup/camera/input owner calls once per transition
- done when: compile passes; kick cannot erase blast; cancelled rocket cannot explode; one crossing scores once; gate/state cannot diverge
- checks: worker -> compile-only Unity; constant diff; source state traces; validators cover event owners/reset state; `git diff --check`; manual Unity scenarios deferred to T7; invalidated by lifecycle edit
- proof: arrow traces for kick+blast, cleanup+late collision, repeated goal occupancy, goal freeze/reset
- review_focus: Critical/High impulse loss/duplication, double unregister, post-cancel explosion, missed/double goal, match deadlock, tuning drift
- review_checkpoint: CP5
- return_evidence: state traces, unchanged constant proof, compile log, residual physics timing risk

### T6: Explosion and camera feedback responsibility split

- objective: isolate blast rules/presentation and camera models while retaining single Unity state writers
- covered_requirements: SOLID boundaries; testable rules; readable presentation; explicit scene contracts
- owner: W6 implementation worker, `terra_high`
- depends_on: accepted CP5 SHA
- owns: `Assets/_Game/Scripts/Runtime/Weapons/ExplosionResolver.cs`; new `Weapons/BlastMath.cs`; `Weapons/ExplosionTargetCollector.cs`; `Weapons/GoalShieldSet.cs`; new `Feedback/ExplosionVfxSpawner.cs`; `Feedback/PlayerCameraFeedback.cs`; new `Feedback/SpeedFovModel.cs`; `Feedback/CameraShakeModel.cs`; `Feedback/GoalOrbitModel.cs`; related builder composer/validator modules
- protected: blast radius/strength/falloff/occlusion/underfoot formulas; overlap capacities; VFX prefab/lifetime; camera tuning; camera sole-writer rule; shield semantics
- focused_reads: ExplosionResolver; PlayerCameraFeedback; Ball shield setup; builder hierarchy/wiring; player viewmodel/crosshair/layer contract
- implementation: `BlastMath` owns pure falloff/radial/underfoot impulse and finite guards. `ExplosionTargetCollector` owns fixed-capacity dedupe/closest-surface selection; Resolver owns Unity overlap/raycast and dispatch
- implementation: add `GoalShieldSet` as single serialized collider catalog consumed by ball collision ignore and blast occlusion; builder creates/wires/validates
- implementation: add `ExplosionVfxSpawner` with serialized prefab and `Play(origin)`; Resolver delegates one VFX per accepted explosion
- implementation: keep PlayerCameraFeedback public facade/sole camera writer. Plain models own FOV, shake, orbit calculation/state. Explicit viewmodel/crosshair refs replace name search after builder migration
- done when: Resolver contains Unity query/orchestration only; camera facade contains lifecycle/write ordering only; formulas/tuning match baseline; all refs persisted
- checks: worker -> compile-only Unity; formula constant/parity vectors; responsibility scan; builder reference inventory; `git diff --check`; invalidated by blast/camera/wiring edit
- proof: vectors for blast center/edge/occluded/underfoot low-high speed and camera shake/celebration begin-end-reset
- review_focus: Critical/High force drift, missed target, shield regression, duplicate VFX, camera restoration loss, serialized null
- review_checkpoint: CP6
- return_evidence: responsibility map, parity record, compile log, wiring contract, residual visual risk

### T7: Staged authoritative generation, explicit bake, and final proof

- objective: integrate final source once, prove pre-bake state, run bounded explicit bake, verify no-op reuse/pure validation, and bind clean committed SHA
- covered_requirements: Unity asset safety; deterministic generation; bake efficiency; architecture completion; regression proof
- owner: W7 implementation worker, `luna_max`
- depends_on: accepted CP6 SHA
- owns: final fixes under `Assets/_Game/Editor/**` and runtime owned paths; builder-owned prefabs/scene/materials/controllers/lighting/settings; `Assets/_Game/Generated/MovementLabBuildManifest.json`; paired `.meta`; runtime architecture doc; durable evidence under `<git-common-dir>/architecture-evidence/<attempt-id>/`
- protected: authored models/textures/shaders; accepted graphics design; gameplay tuning; input mappings; prefab/main asset GUIDs; launch-checkout state; Unity/package versions
- focused_reads: accepted T1-T6; stage DAG; pre-bake gate; GUID/fileID/hash baseline; graphics efficiency handoff; Unity short-path/process rules
- implementation: freeze accepted source checkpoint after CP6 and capture GUIDs, critical fileIDs, owned hashes, status, stage inputs/outputs. Acquire one Unity lease for short project path; durable logs/evidence live outside worktree `Temp/Library`
- implementation: compile with `-batchmode -nographics -quit`. Run `AssembleMovementLab()`; save/reload generated non-lighting state. Run `ValidateMovementLabPreBake()` in separate process. Require durable pass record bound to exact source SHA and finalized lighting digest
- implementation: run `BakeMovementLabLighting()` explicitly with graphics device initialized, no `-nographics`. One expected production bake; wait for process/lock release; validate bake outputs; retry only after exact diagnosed fix; maximum two attempts, third needs user approval
- implementation: commit source plus generated outputs. From exact committed head run `BuildMovementLab()` compatibility facade; require every generation stage reuse, zero writes, explicit lighting reuse, no bake, identical owned hashes
- implementation: run `ValidateMovementLab()` in separate clean process. Require zero writes/hash drift, persisted refs/provenance, no missing components, exact quality/physics/tuning, complete stage manifests
- implementation: inspect YAML/GUIDs: retained scripts/prefabs/main assets unchanged; critical refs nonzero; Player/Ball source provenance valid; new shield/VFX/camera refs valid. Unexpected local fileID churn accepted only for same-run rebuilt dependents with reload proof
- implementation: focused manual scenarios -> rocket jump; kick plus queued blast; cancelled rocket cleanup; one-score goal latch; five-second freeze/reset; camera celebration restoration; High/Low semantic switch. No tuning adjustment in architecture scope
- done when: exact committed head passes compile, assemble, pre-bake, explicit bake, no-op Build, pure validator, GUID/provenance, focused gameplay; worktree clean
- checks: worker -> hidden `Start-Process -Wait -PassThru` Unity `6000.5.6f1`; graphics-enabled bake; process/lock proof each command; stage/output SHA-256 manifests; scoped `git diff --check`; invalidated by any source/builder/generated/config edit
- proof: durable evidence bundle -> exact SHA, commands/exits/logs, pre-bake marker, bake count/timing, no-op stage markers, before/after hashes/GUIDs/fileIDs, validator marker, manual scenario record
- review_focus: Critical/High missing ref/script, wrong invalidation, hidden bake/write, validator mutation, GUID churn, gameplay/tuning drift, evidence bound to wrong SHA
- review_checkpoint: CP7
- return_evidence: final SHA, clean status, full evidence manifest/hash, changed paths, residual risks

## Execution Assignments

- workers: T1 -> W1 `luna_max`; T2 -> W2 exact `sol_high`; T3 -> W3 `luna_max`; T4 -> W4 `luna_max`; T5 -> W5 `terra_high`; T6 -> W6 `terra_high`; T7 -> W7 `luna_max`
- review_checkpoints: CP1 -> fresh `sol_medium`, module/identity migration; CP2 -> fresh `sol_medium`, stage DAG/manifest/pre-bake safety; CP3 -> fresh `sol_medium`, builder module ownership/validator purity; CP4 -> fresh `sol_medium`, dependency/API wiring; CP5 -> fresh `sol_medium`, lifecycle semantics; CP6 -> fresh `sol_medium`, blast/camera parity; CP7 -> fresh `sol_medium`, exact-SHA staged generation/evidence
- checkpoint rule: one frozen committed SHA per worker; Critical/High findings only; fresh fix worker; no fix re-review; rerun invalidated checks
- execution contract: exact `sol_high` execution orchestrator uses `$orchestrate-implementation`; workers receive only assigned task, predecessor SHA, owned/protected paths, and proof boundary

## Final Verification

- exact head: clean committed SHA descending from clean accepted start and every checkpoint; plan bytes unchanged
- compile: Unity `6000.5.6f1` exits `0`; zero C#/shader Console errors; no Editor lock
- architecture: Runtime -> Rendering only; Editor -> Runtime + Rendering; no runtime -> Editor; no core scene-wide lookup; docs match namespaces/APIs
- source: MovementLabBuilder thin facade; every old method has one module owner; typed contracts drive build/validation; validator performs zero mutation
- invalidation: validator edit -> compile/validate only; gameplay behavior edit -> compile only; serialized contract -> prefab/scene; material/static/light/probe -> lighting; exact paths explain every invalidation
- Unity: assemble/pre-bake pass; explicit bake only; committed-head Build reuses all stages and does not bake/write; separate validator exits `0` and changes zero hashes
- serialization: retained GUIDs unchanged; critical refs have nonzero fileIDs; prefab provenance valid; no missing components/unrelated reserialization
- gameplay: tuning unchanged; named lifecycle/blast/camera scenarios pass
- waived evidence: no claim for 24-image visual acceptance or target-PC performance; waiver remains recorded in graphics handoff
- Git: launch-checkout state preserved; isolated worktree clean; `git diff --check`; committed diff limited to owned paths
- invalidation after final edit: rerun compile plus every affected stage; lighting digest change requires new pre-bake pass and bounded explicit bake; validator-only edit never bakes

## Handoff

- changed paths: runtime domain folders/metas/namespaces; Runtime/Rendering asmdefs; Editor asmdef/imports; MovementLab facade/modules/stage manifests; runtime rule/presentation modules; authoritative generated outputs; runtime architecture doc
- residual risks: namespace/assembly migration needs reload proof despite GUID preservation; prefab-local fileIDs may change; missing stage edge needs scheduled forced-full-rebuild comparison; no automated gameplay suite by policy; waived visual/performance acceptance remains outside architecture claim
- authority: execution orchestrator owns isolated branch/worktree; user authorizes integration after exact-SHA evidence; generated outputs change only through staged builder; third production bake requires user approval

## Done Criteria

- every requirement maps to task, owner, check, proof
- every task passes implementation design gate
- Execution Graph includes every task/checkpoint once and serializes shared code/assets/Unity state
- every worker maps to one fresh checkpoint
- exact audit baseline factual; execution start uses clean committed descendant
- retained Unity identities/provenance proven after reload
- builder split removes god-class responsibilities and global invalidation
- gameplay owners remain cohesive; extracted rules/presentation have one caller/owner
- validator-only/gameplay-only edit cannot trigger bake; semantic failure stops before bake
- no speculative framework/config/test layer added
- execution route uses immutable accepted artifact and `$orchestrate-implementation`
- final checks bind clean committed head or blocker names one needed authority action
