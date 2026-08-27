# Arena Lighting Coding Plan

Status: accepted
Source: direct user request
Run ID: direct-arena-lighting
Plan ID: arena-lighting
Attempt ID: direct-20260825-01
Covered Requirements: one even arena-global light; weaker readable shadows; no arena point/spot fill lights; emissive goal and wall presentation preserved; generated scene and baked-output proof
Baseline: `5ce2a74d0044500f4204de012f9fee82ba0a5346`
Dependencies: completed prior visual foundation; no active code dependency

## Objective

Make arena lighting predictable and easy to tune. Author one dedicated global sun, soften its shadows, remove realtime goal accents and baked wall-fill spots, preserve emissive presentation, then prove generated and persisted outputs through builder protocol.

## Scope

- in: arena scene creation; `Environment/Sun`; arena light removal; lighting validation; affected stage fingerprints; generated scene, lighting, build-manifest, and baked outputs
- out: arena textures, materials, meshes, tiling, sky art, ambient palette, reflection setup, fog, post-processing look, player presentation, gameplay, HUD, custom shaders, manual Unity edits, routine Development bakes

## Decisions

- assumption: existing arena geometry, materials, emissive presentation, sky, ambient colors, reflection probes, fog, bloom, SSAO, ACES, and exposure remain accepted.
- decision: execution uses integration branch `codex/arena-lighting-<attempt8>`, short worktree `C:\wt\alt<attempt8>`, short evidence alias `C:\wt\alt<attempt8>e`, preserved private Library, clean baseline, and no Unity process owning project path.
- decision: preserve global sun rotation `(50,330,0)`, color `(1,.8392157,.6392157,1)`, intensity `2.4`, mixed mode, and soft shadows. Set shadow strength to `.25`.
- decision: `AssembleGameplaySceneStage` uses `NewSceneSetup.EmptyScene`. `BindSceneEnvironment` always creates dedicated `Environment/Sun`; no arbitrary `Light` search or default-scene light reuse.
- decision: remove four realtime goal accent lights and six baked wall-fill spot lights plus obsolete contracts. Preserve corresponding emissive presentation materials and objects.
- decision: `Environment` contains exactly one `Light`: `Environment/Sun`. Validator scopes assertion to `Environment` descendants and rejects missing, duplicate, or unexpected environment lights.
- decision: retain existing non-light presentation values. Do not compensate for removed fills by changing ambient, sky, probes, fog, bloom, SSAO, exposure, or color grading.
- decision: bump only serialized/stage contracts whose source, input, output, or validation contract changes. Generated-output byte hashes remain provenance only, never staleness predicates.
- decision: Fast iteration runs no Development or Production bake. Final builder protocol lets production preparation reuse current valid lighting or execute exactly one required bake.
- question: None

## Execution Graph

`START -> T1 -> CP1 -> T2 -> CP2 -> FINAL`

- gates: `START` -> clean baseline, writable short worktree/evidence/private Library, harness green, no owning Unity process; `CP1` -> source review accepted; `CP2` -> generated comparator and persisted validation accepted; `FINAL` -> clean committed head
- rejection loop: Critical/High finding -> new task-scoped fix worker -> rerun invalidated checks -> fresh review

## Tasks

### T1: Author arena-global lighting contract

- objective: create one softer global sun, remove arena fill lights, and validate exact persisted environment-light state
- covered_requirements: one even global arena light; weaker shadows; no point/spot fill lights; unchanged non-light presentation
- owner: `worker-T1 (luna_max)`
- dependencies: `START`
- parallel_contract: sole source owner
- owns: `Assets/_Game/Editor/MovementLab/MovementLabContract.cs`; `Assets/_Game/Editor/MovementLab/MovementLabSceneComposer.cs`; `Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs`; `Assets/_Game/Editor/MovementLab/MovementLabValidator.cs`; `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs`
- protected: arena textures, materials, meshes, and tiling; sky, fog, post-processing, ambient, and reflection values; runtime gameplay; all generated assets
- read_paths: `MovementLabSceneComposer.AssembleGameplaySceneStage` -> scene creation; `MovementLabSceneComposer.BindSceneEnvironment` -> environment composition; `MovementLabLightingPipeline` -> sun and removed-light authoring; `MovementLabValidator` -> persisted scene contract; `MovementLabStageGraph` -> affected fingerprints
- validation_environment: source-only worker; no Unity asset writes; integrated builder execution belongs to T2
- unity_mutation: `false`
- implementation: use `EmptyScene`; create exact `Environment/Sun`; set fixed transform, color, intensity, mixed mode, soft shadows, and `.25` shadow strength; remove goal accent and wall-fill `Light` creation while retaining emissive objects; delete obsolete light constants and validator expectations; require exactly one environment light with exact persisted identity and properties; update only affected contract versions and stage inputs.
- done when: source diff stays inside declared files; builder cannot regenerate removed fill lights; validator rejects duplicate, missing, or incorrect environment sun; unrelated presentation contracts remain unchanged
- checks: `git diff --check -- Assets/_Game/Editor/MovementLab/MovementLabContract.cs Assets/_Game/Editor/MovementLab/MovementLabSceneComposer.cs Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs Assets/_Game/Editor/MovementLab/MovementLabValidator.cs Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs`
- review_focus: default-scene duplicate light, arbitrary light search, removed fills still generated, emissive presentation removal, unrelated visual drift, incomplete stage fingerprint, weak persisted assertion
- review_checkpoint: `CP1 -> fresh sol_medium Critical/High reviewer`

### T2: Generate and validate arena outputs

- objective: run authoritative builder protocol, freeze exact generated changes, and prove persisted production lighting state
- covered_requirements: generated scene correctness; baked-lighting consistency; separate-process validation
- owner: `execution-orchestrator (sol_high)`
- dependencies: `CP1`
- parallel_contract: sole Unity and generated-output owner
- owns: `Assets/_Game/Scenes/MovementLab.unity`; `Assets/_Game/Generated/MovementLabBuildManifest.json`; `Assets/_Game/Lighting/MovementLabLightingSettings.asset`; `Assets/_Game/Lighting/MovementLabVolumeProfile.asset`; `Assets/_Game/Lighting/MovementLabLightingManifest.json`; `Assets/_Game/Scenes/MovementLab/LightingData.asset`; generated lightmaps and reflection probes declared by current builder inventory; paired `.meta` files; immutable workflow and comparator evidence
- protected: all source after CP1; unrelated generated/project assets
- read_paths: `MovementLabBuilder.ProbeMovementLabGeneratedState` -> pre-run selector; `Invoke-MovementLabWorkflow.ps1` -> ProductionPrepare and ProductionValidate; `Compare-GeneratedYaml.ps1` -> authoritative change classifier; builder output contract -> exact generated inventory
- validation_environment: clean accepted CP1 head; sole Unity lease; preserved private Library; fresh harness before each Unity-mutating workflow; hidden waited Unity processes; process and project-lock release required
- unity_mutation: `true`
- implementation: capture pre-status and `sourceFreezeSha`; run harness; probe generated state; select `reused` only when production profile plus Lighting and BakedOutput stages are current, otherwise `executed`; run one ProductionPrepare workflow; require zero bakes plus exact reuse marker or exactly one successful bake; run generated comparator from `sourceFreezeSha` to WORKTREE with `-FailOnDangling`; accept only builder-traced outputs with complete semantic coverage, zero increased dangling references, zero GUID churn, intact asset/meta pairs, and zero unsupported or undeclared paths; commit accepted generated outputs separately; run ProductionValidate from clean exact commit in separate Unity process.
- done when: production lighting current; environment contains exact sun and no removed fill lights after reopen; comparator accepts every generated change; separate validator reports exact final SHA; worktree clean
- checks: harness -> pass under 90s; ProductionPrepare -> selector-matched `reused|executed`, bake count `0|1`, valid marker, process/lock release; comparator -> `DANGLING:0`, `GUID:0`, `PAIRS:0`, `UNSUPPORTED:0`; ProductionValidate -> executed, exact SHA, current Lighting/BakedOutput, persisted environment-light contract green
- review_focus: wrong bake selector, duplicate bake, generated scope escape, stale or malformed marker, GUID/pair damage, removed lights persisting after reload, validation before generated commit
- review_checkpoint: `CP2 -> fresh sol_medium Critical/High generated and lighting review`

## Execution Assignments

- workers: T1 -> `worker-T1 (luna_max)`; T2 -> `execution-orchestrator (sol_high)`
- reviewers: fresh `sol_medium`; Critical/High only; fixes go to new exact-scope workers

## Final Verification

- exact head: clean committed source plus separate accepted generated-output commit when comparator selects changes
- checks: `git diff --check`; harness green; selector-governed ProductionPrepare green; comparator complete; separate ProductionValidate green at exact final SHA
- inspect: one `Environment/Sun`; no goal accent or wall-fill `Light` objects; emissive presentation retained; no arena surface or unrelated visual change; generated diff entirely comparator-selected
- invalidation: source fix -> CP1 and all T2 proof; generated change -> comparator, CP2 review, and ProductionValidate; lighting-input change -> production selector re-probe

## Generated Output Contract

- notation: `pair(path)` -> exact asset plus `path.meta`; excludes `ProjectSettings/**` and evidence
- source boundary: T1 files commit before `sourceFreezeSha`
- T2 outputs: exact current builder-owned scene, build manifest, lighting settings/profile/manifest, LightingData, lightmaps, and reflection probes
- rejection: changed path outside authoritative inventory; incomplete semantic header; increased dangling reference; existing GUID churn; broken asset/meta pair; unsupported type; undeclared output

## Handoff

- residual risks: removing local fill lights changes contrast near goals and walls; semantic validation proves composition, while human visual review remains on demand
- authority: execution may mutate only declared source and builder-owned outputs, run one selector-governed production preparation, and commit source/generated slices; replacement bake after failed executed bake requires user approval
