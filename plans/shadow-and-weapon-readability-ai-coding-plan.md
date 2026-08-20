# Shadow, Weapon, And Explosion Readability Coding Plan

Status: ready
Source: direct user request plus supplied screenshots
Baseline: `37f51088ff83a65fe7543998530d2c9166fdc686`
Authority: direct plan; do not load generic orchestration skill
Primary scene: `Assets/_Game/Scenes/MovementLab.unity`

## Objective

Raise arena shadow floor and first-person weapon readability. Make rocket explosion visible footprint match gameplay blast radius. Avoid washed sky, flattened depth, gameplay changes, or production-scale rendering systems. Preserve generated-scene authority, URP High/Low profiles, asset GUIDs, prefab provenance, and one-writer Unity workflow.

## Scope

- in: weapon metal/dark base textures; weapon PBR channel balance; weapon material scalar contract; production ambient/shadow balance; explosion gameplay-radius cue; explosion runtime scale/tint contract; matching builder validation; stage invalidation; texture regeneration; production bake; generated-output review; High/Low visual acceptance
- out: blast radius/force/damage/falloff changes; weapon geometry/UV changes; new shaders; new render layers; dedicated viewmodel camera; dedicated viewmodel light; sky replacement; global SSAO retune; exposure lift; bloom retune; arena geometry change; manual generated-scene edits

## Decisions

- root causes: weapon base textures intrinsically dark; weapon dark map currently metallic; weapon maps highly smooth/occluded; production scene uses `ambientIntensity=1`, sun `shadowStrength=1`; bright sky plus weak shadow fill creates crushed range.
- first-pass strategy: improve authored weapon reflectance and existing lighting ratios. No new rendering subsystem.
- lighting target:
  - `RenderSettings.ambientIntensity`: `1.0 -> 1.4`
  - sun `shadowStrength`: `1.0 -> 0.82`
  - sun intensity: keep `1.1`
  - post exposure: keep `0`
  - ACES, contrast `5`, saturation `4`, bloom: unchanged
  - SSAO: unchanged unless final evidence isolates SSAO as remaining cause
- weapon texture target:
  - metal palette shadow/base/highlight: `(0.18,0.12,0.075)`, `(0.50,0.36,0.21)`, `(0.78,0.60,0.36)`
  - dark palette shadow/base/highlight: `(0.045,0.055,0.070)`, `(0.14,0.16,0.19)`, `(0.32,0.35,0.38)`
  - metal map metallic: `0.65`; smoothness range: `0.52..0.74`
  - dark map metallic: `0.05`; smoothness range: `0.38..0.58`
  - metal/dark AO range: `0.78..0.94`
  - material occlusion strength for metal/dark and shotgun aliases: `0.70`
  - accent material, emission, texture resolution, UV tiling, normals: unchanged
- acceptance priority: readable silhouettes and part separation over strict physical realism. Weapons may remain dark; they must not read as black cutouts.
- explosion baseline: `ExplosionResolver` already passes `blastRadius=11.7` to `ExplosionVfxSpawner`; `ExplosionVfx` scales root by `radius / ReferenceVisualRadius`. Current validation proves scale math only. It does not prove any dense, visible particle reaches `ReferenceVisualRadius=4.5`; sparse sparks dominate nominal reach while core animation remains small.
- explosion target:
  - gameplay `blastRadius`, force, damage, falloff, occlusion: unchanged
  - add one low-opacity `BlastRadiusCue` particle system to generated explosion prefab
  - authored cue final diameter: `2 * ReferenceVisualRadius = 9.0`
  - runtime root scale: unchanged `blastRadius / ReferenceVisualRadius`
  - resulting cue world radius: exactly requested `blastRadius`; `11.7` radius -> `23.4` world-unit final diameter
  - cue orientation: horizontal billboard centered at explosion origin; communicates ground footprint while existing fireball/sparks communicate 3D blast
  - cue lifetime: `0.28s`; size-over-lifetime `0.15 -> 1.0`; alpha peak `<=0.22`; no light, collider, Rigidbody, gameplay query, or persistent object
  - cue team tint: same immutable Blue/Red/Neutral snapshot as Flash
  - cue represents maximum blast surface-distance boundary. Occlusion may reduce force inside boundary; visual does not encode occlusion.
- escalation: add viewmodel-only fill-light design only when final accepted texture/lighting pass still fails weapon acceptance. Escalation requires separate plan because layer/culling/wiring contracts expand scope.
- subjective proof: semantic validation proves contract persistence, not visual quality. User playtest remains visual authority.

## Acceptance Views

- 1920x1080 High: arena center facing each goal; each side wall in sun-facing and shadow-facing directions; both goal recesses.
- 1920x1080 High: rocket launcher and shotgun at center, side wall, and goal recess positions.
- 1920x1080 High: rocket detonation at arena center, beside wall, inside goal recess, and near camera; inspect full radius cue plus core explosion.
- 1920x1080 Low: one center and one shadow-side pass for both weapons.
- 1920x1080 Low: one arena-center rocket detonation; radius cue remains visible without bloom dependency.
- pass:
  - weapon receiver, dark grip/body, and emissive accent remain distinguishable without muzzle flash or goal light
  - participant and ball silhouettes remain readable against shadowed walls
  - shadowed wall texture retains visible large-scale pattern instead of merging into near-black field
  - sky/cloud area does not become more clipped or washed than baseline screenshots
  - goal/team emissive colors remain saturated; contact shadows remain visible
  - explosion radius cue expands to exact `11.7` world-unit radius/`23.4` diameter and remains readable against bright/dark floor
  - core fireball remains visually dense; radius cue shows affected area without blinding camera or implying extra damage outside boundary
- fail:
  - exposure-style whole-frame brightening
  - flat scene with lost grounding/contact depth
  - weapon readability depends on camera facing sun
  - High passes but Low produces black weapon or wall silhouettes
  - explosion fireball remains tiny with only sparse sparks near gameplay boundary
  - explosion cue overshoots/undershoots gameplay radius or persists long enough to obscure combat

## Execution Graph

`START -> T0 -> {T1 -> CP1 || T2 -> CP2 || T3 -> CP3} -> JOIN1 -> T4 -> CP4 -> SOURCE_GATE -> T5 -> VG1 -> SOURCE_FREEZE -> T6 -> CP6 -> FINAL`

- `||`: parallel source-only work in one scoped worktree with disjoint file ownership
- `JOIN1`: CP1, CP2, and CP3 accepted, including fresh-worker Critical/High fixes
- `SOURCE_GATE`: combined source diff accepted; targeted `BlastMathTests` pass; Unity process/lock released
- `VG1`: development-lighting visual gate; may loop to owning source task before source freeze
- `SOURCE_FREEZE`: accepted source plus weapon texture outputs committed; exact SHA recorded; no non-generated dirt
- T6: sole production Unity/project/generated writer; no concurrent Unity, Blender, asset generation, or source edits

## T0: Baseline And Worktree Gate

- owner: execution orchestrator
- profile: none
- dependencies: none
- worktree: short writable `C:\wt\<id>` at baseline; private `Library/`; evidence alias `C:\wt\<id>e`; deepest evidence path write-probed
- checks:
  - `git rev-parse HEAD` equals baseline
  - `git status --short` empty or exact user-owned changes explicitly excluded before dispatch
  - no Unity process using project; no project lock
  - capture current weapon texture hashes, material scalar values, lighting manifest profile/digest, and supplied screenshots as baseline evidence when still available
- rule: workers do not commit, run Unity, edit generated scene/prefabs/materials, or touch files outside ownership
- done when: scoped-clean writable execution root and evidence root confirmed

## T1: Weapon Texture And Material Readability

- owner: worker-T1 (`luna_max`)
- dependencies: T0
- parallel: yes, with T2 and T3
- unity_mutation: false
- owns:
  - `Tools/Blender/generate_retro_textures.py`
  - `Assets/_Game/Textures/RetroWeaponMetal.png`
  - `Assets/_Game/Textures/RetroWeaponMetal_Normal.png`
  - `Assets/_Game/Textures/RetroWeaponMetal_MetallicSmoothness.png`
  - `Assets/_Game/Textures/RetroWeaponMetal_Occlusion.png`
  - `Assets/_Game/Textures/RetroWeaponDark.png`
  - `Assets/_Game/Textures/RetroWeaponDark_Normal.png`
  - `Assets/_Game/Textures/RetroWeaponDark_MetallicSmoothness.png`
  - `Assets/_Game/Textures/RetroWeaponDark_Occlusion.png`
- protected: texture `.meta` files; FBX files; weapon geometry; editor builder/pipelines; accent texture/material contract
- implementation:
  - apply exact palette/PBR targets from Decisions in scalar and vectorized generator paths
  - keep scalar/vectorized generation byte-equivalent
  - add `weapon_readability` semantic audit covering metal/dark base luminance, metallic bounds, smoothness bounds, AO bounds, output dimensions, and selected-family execution
  - publish required builder scalar target: `WeaponMetal`, `WeaponDark`, `ShotgunMetal`, `ShotgunDark` occlusion strength `0.70`; T4 applies and validates it
  - run targeted CPython generator twice: `--family weapon-metal --family weapon-dark`; capture SHA-256 after each run; require identical eight output hashes
  - inspect `11_retroweaponmetal_tile.png`, `11_retroweaponmetal_pbr_ball.png`, `12_retroweapondark_tile.png`, `12_retroweapondark_pbr_ball.png`
  - keep preview/manifest files under `Temp/BlenderPreviews/RetroTextures/`; do not commit
- Blender note: texture generator explicitly supports CPython and owns no mesh/FBX. No Blender geometry export, six-view mesh preview, or Unity import run belongs in T1.
- checks:
  - targeted generator exit `0`, `AUDIT RetroTextures: PASS`, `weapon_readability` pass
  - two-run hash equality for eight outputs
  - `git diff --check --` owned source paths
  - existing texture `.meta` GUIDs byte-identical
- review_focus: dark map left metallic; scalar/vectorized mismatch; accidental accent/normal/resolution change; audit accepting stale values
- review_checkpoint: CP1, fresh `sol_medium`; report only Critical/High; each fix assigned to new `luna_max` worker owning exact failed files
- done when: weapon maps retain style/detail while meeting declared reflectance bounds and deterministic regeneration contract

## T2: Arena Shadow-Fill Contract

- owner: worker-T2 (`luna_max`)
- dependencies: T0
- parallel: yes, with T1 and T3
- unity_mutation: false
- owns: `Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs`
- protected: `MovementLabFastModeSession.cs`; `GraphicsQualityConfigurator.cs`; `MovementLabLightingProfiles.cs`; lighting settings YAML; volume profile YAML; scene; baked outputs
- implementation:
  - introduce named production constants for ambient intensity `1.4` and sun shadow strength `0.82`
  - apply constants in `BindSceneEnvironment`
  - update read-only lighting validation to require exact values with existing tolerance
  - leave sun intensity, sky material, fog, exposure, ACES, contrast, saturation, bloom, reflection probes, light probes, and accent lights unchanged
  - do not edit `LightingSettings` serialized fields or generated assets
- checks:
  - `git diff --check -- Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs`
  - inspection proves every authored value has matching validator expectation
  - search proves old inline `ambientIntensity=1` and `shadowStrength=1` contracts no longer survive in production pipeline validation
- review_focus: brightening direct/sky range instead of shadow floor; mismatched validator; unintended bake/profile/SSAO changes
- review_checkpoint: CP2, fresh `sol_medium`; report only Critical/High; each fix assigned to new `luna_max` worker
- done when: production builder owns one explicit reversible shadow-fill contract

## T3: Explosion Radius-Cue Runtime Contract

- owner: worker-T3 (`luna_max`)
- dependencies: T0
- parallel: yes, with T1 and T2
- unity_mutation: false
- owns:
  - `Assets/_Game/Scripts/Runtime/Feedback/ExplosionVfx.cs`
  - `Assets/_Game/Scripts/Tests/EditMode/BlastMathTests.cs`
- protected: `ExplosionResolver.cs`; `ExplosionVfxSpawner.cs`; `RocketProjectile.cs`; editor builder; explosion prefab/materials/textures; gameplay blast constants
- implementation:
  - preserve `ExplosionResolver -> ExplosionVfxSpawner.Play(origin, blastRadius, team) -> ExplosionVfx.Play(radius, team)` data flow
  - add `ReferenceVisualDiameter = ReferenceVisualRadius * 2f`
  - add pure radius helper proving `authoredDiameter * 0.5 * ComputeVisualScale(requestedRadius)`; nonfinite inputs or nonpositive authored diameter return `0`; finite zero/negative requested radius preserves current `0.01` minimum visual radius
  - find configured `BlastRadiusCue` system during `Play`; apply same `GetFlashColor(team)` result to Flash and cue
  - preserve one-shot guard, root uniform scaling, configured-system fallback, and maximum-lifetime cleanup
  - extend `BlastMathTests`: `9.0` authored cue diameter at requested `11.7` produces `11.7` world radius; zero requested radius produces `0.01`; invalid inputs produce `0`; gameplay falloff boundary remains `11.7`
  - no duplicated serialized blast radius in VFX or spawner
- checks:
  - `git diff --check --` owned paths
  - targeted EditMode test source compiles by inspection; execution deferred to SOURCE_GATE after T4 builder integration
- review_focus: second radius source drifting from gameplay; diameter/radius confusion; tint applied only to core; invalid input producing NaN/infinite scale
- review_checkpoint: CP3, fresh `sol_medium`; report only Critical/High; fixes assigned to new `luna_max` worker
- done when: runtime exposes one pure, testable mapping from requested gameplay radius to visible cue radius

## T4: Builder, Stage Invalidation, And Integration

- owner: worker-T4 (`luna_max`)
- dependencies: CP1 + CP2 + CP3
- parallel: no
- unity_mutation: false
- owns:
  - `Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs`
  - `Assets/_Game/Editor/MovementLab/MovementLabMaterialPipeline.cs`
  - `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs`
- protected: T1/T2/T3 owned files; workflow scripts; generated manifest; scene; materials; prefabs; baked outputs
- implementation:
  - apply `0.70` occlusion strength to generated weapon metal/dark and shotgun alias materials; update exact material validation
  - add generated `BlastRadiusCue` as fifth explosion particle system: one burst particle, `0.28s` lifetime, `9.0` start size, zero speed, horizontal billboard, hierarchy scaling mode, size curve `0.15 -> 1.0`, alpha peak `<=0.22`, shared non-additive `Explosion.mat`, deterministic seed, explicit sorting order
  - configure cue color-over-lifetime as white RGB plus fade so runtime team start color remains authoritative
  - update explosion prefab validator: exact five names; cue parameters; final authored radius `4.5`; runtime world radius equals `BlastRadius`; total burst count `38`; serialized array size `5`; no physics/light; material provenance stable
  - keep root prefab unit scale; runtime remains sole radius scaler
  - bump material-prefab contract version once for weapon scalar/texture semantic change
  - bump lighting contract version once for ambient/shadow semantic change
  - add `Tools/Blender/generate_retro_textures.py` to material-prefab explicit input set
  - confirm weapon texture PNGs already enter material-prefab digest through `MovementLabContract.ImportedAssetPaths`; add missing explicit path only if inspection disproves this
  - confirm `MovementLabLightingPipeline.cs` remains lighting input
  - run stage-graph self-checks available through existing code; do not invent new workflow surface
- checks:
  - `git diff --check --` owned paths
  - static inspection: T1/T3/T4 source/output change marks MaterialPrefab stale; T2 change marks Lighting/BakedOutput stale
  - no unrelated runtime/gameplay stage invalidation added
- review_focus: cue diameter/radius mismatch; camera-facing cue instead of horizontal footprint; additive flash blinding scene; missing tint/array/provenance validation; digest blind spot causing production bake skip
- review_checkpoint: CP4, fresh `sol_medium`; report only Critical/High; fixes assigned to new `luna_max` worker
- done when: generated prefab has exact visible radius cue and stage probe cannot report current against stale weapon, explosion, or lighting inputs

## T5: Candidate Build And Visual Gate

- owner: execution orchestrator
- dependencies: CP4
- parallel: no
- unity_mutation: true
- pre-gate:
  - finish source edits
  - preserve warm private `Library/`
  - close interactive Unity; confirm process/lock release
  - run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1`; require exit `0` under 90 seconds
  - run targeted Unity EditMode `BlastMathTests` through hidden `Start-Process -Wait -PassThru`; require exit `0`, zero test failures, process/lock release before visual workflow
- source integration:
  - inspect combined diff for ownership overlap and unintended files
  - commit source changes and deterministic weapon texture outputs only when execution authority includes commits
  - record candidate SHA
- workflow:
  - run `Invoke-MovementLabWorkflow.ps1 -Mode Development` in one hidden `Start-Process -Wait -PassThru` Unity lane
  - classify result from exit code, workflow JSON, compile errors, exception, and semantic result; ignore known-benign licensing/D3D messages
  - development bake is disposable intermediate; never commit its baked outputs
- visual gate VG1:
  - user tests Acceptance Views at High and Low
  - pass -> continue
  - weapon-only fail -> return exact evidence to new T1-fix worker
  - arena-only fail -> return exact evidence to new T2-fix worker
  - explosion-only fail -> return exact evidence to new T3 runtime worker or T4 builder worker according to failed ownership
  - independent failures -> owning fix tasks may run in parallel again; rerun invalidated CPs and T4 integration, then rerun T5
  - maximum planned tuning loops: two. After two failed conservative loops, stop and propose viewmodel-fill escalation instead of accumulating ad hoc light/exposure changes.
- done when: targeted blast tests pass; development result passes semantic gate; user accepts lighting, weapon, and explosion visual direction

## T6: Production Bake, Generated Review, Final Validation

- owner: worker-T6 (`luna_max`) for mutation; execution orchestrator for final validate
- dependencies: VG1 + clean committed source freeze
- parallel: no; sole Unity/project/generated writer
- worktree state:
  - record `sourceFreezeSha`
  - source plus eight weapon texture outputs committed
  - only development-generated outputs may be dirty before production prepare
  - no Unity process/lock
- pre-gate: rerun harness immediately before production mutation
- production prepare:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode ProductionPrepare -ProjectPath C:\wt\<id> -EvidenceRoot C:\wt\<id>e -AttemptId <attempt>`
  - require successful workflow, current production profile, one authoritative build, and production bake executed because lighting digest changed
  - valid source-defined reuse marker may be accepted only when probe proves lighting inputs current; unexpected reuse after T2/T4 change triggers digest investigation before proceeding
  - capture ledger path, input digest, generated hashes, generated hash digest, process exit code, and probe
- generated classification:
  - union: current authoritative manifest inventory plus exact existing task-owned weapon texture outputs
  - no new asset path expected
  - run `Compare-GeneratedYaml.ps1 -Base <sourceFreezeSha> -Head WORKTREE -FailOnDangling`
  - require selected path/header coverage with `SEMANTIC:`, `DANGLING:`, `GUID:`, `PAIRS:`, `UNSUPPORTED:`; no GUID churn, broken pairs, increased dangling refs, unsupported types, or untraced output
  - verify texture `.meta` files unchanged
  - generated MovementLab outputs commit separately as `chore: regenerate MovementLab outputs` when comparator selects paths and execution authority includes commits
  - require committed generated bytes match immutable prepare hashes
- review_checkpoint: CP6, fresh `sol_medium`; inspect only Critical/High plus generated semantics/GUID/provenance; fixes assigned to new worker and invalidated production steps rerun
- final validation:
  - rerun harness immediately before validator
  - `Invoke-MovementLabWorkflow.ps1 -Mode ProductionValidate ... -LedgerPath <prepare-ledger>` in separate Unity process
  - require zero compile/semantic errors, persisted references after reload, production profile, matching generated hash digest
  - rerun comparator from baseline to final SHA
  - `git diff --check`
  - `git status --short` empty at final committed boundary
- skipped checks: full EditMode suite; targeted `BlastMathTests` required because final diff changes explosion runtime mapping
- done when: production bake and separate-process validator pass; generated changes reviewed; final visual acceptance passes at High and Low

## Parallel Assignment Summary

- safe parallel: T1 weapon asset lane || T2 lighting lane || T3 explosion runtime lane
- safe parallel after returns: CP1 review || CP2 review || CP3 review
- conditional parallel: independent T1/T2/T3 fixes after VG1
- sequential: T4 builder integration -> targeted blast tests -> T5 development Unity -> user visual gate -> source freeze -> T6 production Unity -> comparator -> generated review -> separate validation
- prohibited parallel:
  - any Unity process with another Unity process for same project
  - Unity while texture generator writes imported assets
  - production bake while source workers edit
  - generated-output commit/review before comparator
  - final validation before production process and project lock release

## Handoff

- expected duration: source work 2-4 hours; development/production bake and visual loops dominate elapsed time
- expected commits when authorized:
  - `fix: improve shadow weapon and explosion readability` -> source changes
  - `chore: regenerate weapon texture maps` -> eight deterministic PNG outputs, optional separate commit before source freeze
  - `chore: regenerate MovementLab outputs` -> authoritative builder/bake outputs only
- residual risks:
  - ambient `1.4` and shadow strength `0.82` require human judgment across monitor brightness/HDR settings
  - metallic response depends on reflection probe coverage; final weapon check must include goal recesses and arena center
  - development bake can differ in noise/detail from production; final High playtest remains required
  - viewmodel-only fill may become necessary if readability must stay invariant across every light direction
  - `23.4`-diameter cue may obscure close-range combat if alpha/lifetime tuning exceeds declared bounds
- completion report: exact source/final SHAs; worker/review checkpoints; texture hashes; targeted blast test result; cue authored/world radius proof; Unity workflow statuses; bake executed/reused status; comparator summary; checks run; High/Low user acceptance; remaining escalation decision
