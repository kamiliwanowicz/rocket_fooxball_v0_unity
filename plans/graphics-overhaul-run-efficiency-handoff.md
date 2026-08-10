# Graphics Overhaul Run Efficiency Handoff

## Purpose

Postmortem + next-run guardrails for `plans/comprehensive-graphics-overhaul-coding-plan.md` execution.

Use before next orchestrated Unity graphics, generated-scene, lighting, or Blender-heavy run.

## Operating priority

1. iteration speed
2. gameplay correctness + readability
3. production graphics + exhaustive proof at explicit checkpoints

Fast mode remains default during active development. Production graphics activate only for explicit visual-quality checkpoint or final proof. Preserve production assets/settings; switch profile instead of deleting accepted work.

Remediation order: fast-mode command/profile -> targeted stage execution -> bake optimization -> production polish/proof.

Iteration budgets:

- warm code edit -> compile + focused test/play start: target `<=30s`
- targeted visual edit -> changed import/material/prefab + fast validation/play start: target `<=60s`
- predicted command over `60s` -> checkpoint work, outside iteration loop
- routine code/visual edit -> zero production bake, capture, full hash sweep, or Build 1/Build 2 proof

## Accepted state

- branch: `new_graphics`
- merge commit: `35bdd7c84bc7f14799a34b30ecbcd559f3c10b1a`
- accepted execution commit: `8db82b4244c4424a8f0a26098764c4da22f08785`
- completed scope: T1-T7
- waived by user: T8 captures, CP8 review, manual gameplay acceptance, target-PC performance run
- preserved unrelated pre-integration edit: `stash@{0}` texture-generator change
- final accepted checks:
  - Unity compile exit `0`
  - full exact 200-light-probe bake exit `0`
  - Build 2 explicit reuse; no bake; 243 fingerprint hashes changed `0`
  - separate `ValidateMovementLab()` exit `0`; 243 hashes changed `0`
  - Blender texture/model audits pass

## Run timeline

- plan commit -> merge: `6h44m51s`
- source foundations commit: `12:21`
- CP2 source fixes commit: `13:11`
- rocket model commit: `13:25`
- integrated builder commit: `14:25`
- CP6 fixes commit: `14:38`
- T7 source start marker: `15:08`
- T7 accepted commit: `17:35`
- CP7 persisted-lighting fixes: `17:54`
- merge: `18:15`

T7 + CP7 occupied about `2h46m`. Conversation trace records well over 12 production bake completions plus aborted bakes. Unity retained only subset of logs because later launches cleaned `Temp`.

## Measured costs

- warm execution `Library/`: about `1.7 GiB`
- warm initial Asset Database refresh: `6.085s`
- initial domain reload: `3.734s`
- one full build Asset Database refreshes: `81`, aggregate `17.59s`
- Build 2 no-op process: about `15s`
- separate validator process: about `15s`
- retained full light bake: `264856.98ms`
- retained bake probe workload: `40,960,000` samples across `200` positions
- texture generator: about `8.5m` per full run during CP2 fixes

Evidence currently under `C:\wt\gfx-20260809113252-abdb90\Temp\T7\`. Treat as disposable. Future evidence must live outside Unity `Temp/`, worktree deletion scope, and `Library/`.

## Primary root cause

Monolithic invalidation, not slow import cache.

`MovementLabBuilder.ComputeBuilderSignature()` hashes:

- full `MovementLabBuilder.cs`
- validator implementation
- graphics configurator/runtime
- gameplay scripts
- Blender generators
- source textures/models

Any byte change -> global signature mismatch -> `TryReuseGeneratedState()` rejects reuse -> full scene regeneration -> `Lightmapping.Bake()`.

`TryReuseGeneratedState()` also catches any semantic validation exception and converts failure into automatic rebuild. Validator bug, persisted-reference bug, fingerprint bug, or unrelated source edit can therefore trigger production bake.

Key source locations:

- `Assets/_Game/Editor/MovementLabBuilder.cs:495` -> build entry/no-op gate
- `Assets/_Game/Editor/MovementLabBuilder.cs:1919` -> production bake
- `Assets/_Game/Editor/MovementLabBuilder.cs:2025` -> reuse validation/fallback
- `Assets/_Game/Editor/MovementLabBuilder.cs:2234` -> global signature

## Expensive failure loop

Observed post-bake failures:

- URP Lit local keyword state after light/probe bake
- architecture triangle validator counted instances against unique-mesh budget
- `Volume.profile` clone failed persistence; needed `sharedProfile`
- `VolumeProfile` components lacked persistent subassets
- generated material dirty state changed on validator shutdown
- baked-lighting outputs missing from fingerprint
- `GraphicsQualityRuntime.cs` missing from source signature

Most failures were discoverable before production bake or did not invalidate lighting inputs.

Current broad plan invalidation rule (`comprehensive-graphics-overhaul-coding-plan.md:304`) amplified each small correction into downstream compile + two builds + validator + capture requirements.

## Secondary cost: texture pipeline

`Tools/Blender/generate_retro_textures.py` is dependency-free Python. No `bpy` dependency.

Observed pipeline:

- initial late failed run
- two accepted deterministic runs
- stopped extra run
- CP2 run 1 + run 2
- ball-normal scale change
- CP2 run 3 + run 4

Full-suite proof ran before review, then ran again after review fixes.

Target pipeline:

- CPython execution
- NumPy-vectorized 1024/2048 map generation
- `--family` or equivalent targeted generation
- cheap semantic audit per changed family
- representative preview before review
- full two-run hash proof once, after accepted review fixes

## Cache diagnosis

Current project settings:

- `ProjectSettings/EditorSettings.asset` cache-server mode `0`
- cache endpoint empty
- import-result caching disabled
- retained build log reports `cache server=0`

Accelerator helps cold worktree imports, shader artifacts, and repeated imported source assets. Accelerator does not cache custom scene generation or eliminate GI/light-probe computation.

Required cache rules:

- one persistent `Library/` per worktree
- no shared, junctioned, symlinked, or concurrently writable `Library/`
- preserve warm `Library/` between retries
- clear `Library/` or GI cache only with corruption evidence
- keep GI cache on SSD
- optional Accelerator namespace: `RocketFooxball_6000.5`
- one trusted uploader; worker worktrees download-only where practical
- measure cold import before/after Accelerator setup

## Builder remediation target

Split current command:

`EnterMovementLabFastMode()` -> iteration quality profile + simple realtime lighting; zero production-asset rewrite

`BuildMovementLabFast()` -> changed non-lighting stages + fast validation; zero bake/capture/full-proof work

`AssembleMovementLab()` -> importers, materials, prefabs, gameplay scene

`ValidateMovementLabPreBake()` -> all non-lighting semantic + persistence checks

`BakeMovementLabLightingDevelopment()` -> explicit fast local bake

`BakeMovementLabLighting()` -> explicit production bake

`ValidateMovementLab()` -> clean-process, read-only persisted validation

Required behavior:

- stale generation input -> rebuild affected generation stage
- output fingerprint drift -> stop with exact path diff
- semantic validation failure -> stop; no automatic bake
- validator-only change -> compile + validate; zero generated writes
- unchanged lighting-input digest -> preserve baked outputs
- fast mode -> preserve production lighting outputs; record stale production-lighting state separately
- fast profile -> separate non-production quality state; no production URP/scene/lighting mutation
- development bake output -> tagged non-final; rejected by production validation
- production bake starts only through explicit lighting stage

## Stage-keyed invalidation

Replace one global signature with dependency DAG:

- importer stage -> source FBX/PNG + importer contracts
- material/prefab stage -> imported dependencies + serialized prefab contracts
- gameplay scene stage -> prefabs + gameplay wiring contract
- quality stage -> URP assets/settings/configurator
- lighting stage -> static renderers/materials + lights + sky + probes + lighting settings + Unity version
- baked-output stage -> lighting data, lightmaps, probe cubemaps, paired metas
- validator stage -> no output ownership; excluded from generation keys

Use `AssetDatabase.GetAssetDependencyHash()` for imported asset dependency state where suitable. Schedule forced full-rebuild comparison to detect missing dependency edges.

Scene digest rules:

- GameplayScene -> canonical gameplay YAML: LF normalization, ordinal document ordering, exclude only `!u!157 LightmapSettings` document
- BakedOutput -> raw scene + lighting data + lightmaps + probe cubemaps + paired metas
- builder-created URP lights -> persist `UniversalAdditionalLightData` before GameplayScene digest
- digest tests -> document reorder unchanged; LightmapSettings mutation unchanged; gameplay content or fileID mutation changed

Expected invalidation matrix:

- validator edit -> compile + validator
- gameplay behavior edit -> compile
- serialized prefab-contract edit -> prefab + gameplay scene
- material appearance edit in fast mode -> material preview + mark production lighting stale
- static mesh/light/probe edit in fast mode -> affected preview stage + mark production lighting stale
- explicit production checkpoint with stale lighting -> lighting
- quality validator edit -> compile + validator

## Iteration and bake modes

Default path:

`iteration fast mode -> optional development bake for GI evaluation -> production bake after source freeze`

### Iteration fast mode

Use for runtime gameplay, validator, input, HUD, materials, VFX, models, arena composition, and normal visual iteration.

Graphics profile:

- baked GI: off for preview; accepted baked files preserved untouched
- realtime GI: off
- lighting: realtime directional sun + `Trilight` ambient
- ambient: sky `(0.62, 0.70, 0.78)`, equator `(0.48, 0.52, 0.56)`, ground `(0.28, 0.31, 0.35)`, intensity `1.6`
- renderer bindings: snapshot baked/realtime lightmap indices + scale offsets; set indices to `-1`; restore exact state after preview
- shadows: off by default; one low-cost directional shadow only when movement/depth readability needs it
- HDR, SSAO, bloom, post-processing: off
- reflection-probe bake: off; use sky/default reflection or last stable cubemap
- render quality: dedicated `Iteration`/Low URP profile; lower Game view resolution/render scale where useful

Execution rules:

- code edit -> Unity compile + focused existing test; launch play mode
- visual edit -> import changed source family + rebuild changed material/prefab/scene slice + fast semantic check; launch play mode
- production-lighting input change -> mark stale; continue fast preview without bake
- `BuildMovementLab()`, `Lightmapping.Bake()`, reflection bake, capture, full two-run texture proof, and Build 1/Build 2 proof remain checkpoint commands
- fast-mode activation changes runtime/editor selection only; production assets, scenes, bake outputs, GUIDs, manifests, and quality defaults remain unchanged
- fast-mode visual result proves gameplay readability and composition only; production-lighting acceptance remains pending

### Fast development bake

Measured accepted profile:

- lightmapper: Progressive CPU
- lightmap resolution: `5` texels/m
- bounces: `1` minimum, `1` maximum
- samples: direct `16`, indirect `128`, environment `64`
- light-probe positions: `80`
- probe multiplier: `1`
- reflection resolution: `64`
- measured bake/process: `31.46s` / `43.67s`
- measured outputs: `10,389,759` bytes
- production comparison: `6.64x` faster bake, about `60%` smaller outputs, maximum fixed-view mean absolute RGB difference `0.4744/255`

Development bake rules:

- local iteration evidence only; never satisfies final bake proof
- manifest + lighting digest record profile ID and every quality setting
- outputs remain tagged `development` until overwritten by production bake
- final validator rejects development tag or setting mismatch
- profile switch invalidates lighting stage

### Production bake

- Progressive CPU
- lightmap resolution: `10` texels/m
- indirect bounces: `2`
- light-probe positions: `200`
- three baked reflection probes at `128` resolution
- run once after reviews, accepted fixes, pre-bake gate, and source freeze
- exact committed-SHA evidence only

## Pre-bake gate

Production bake may start only when all conditions pass:

- all invalidating writers stopped
- source checkpoint committed
- source review completed
- accepted Critical/High fixes applied
- Unity compile passed
- importers/materials/prefabs saved, reloaded, validated
- scene saved/reopened twice; canonical gameplay + owned-output hashes stable
- Volume uses expected persisted `sharedProfile`
- Volume components exist as nonzero persistent subassets
- emissive materials retain `BakedEmissive` + `_EMISSION` after clean-process reload
- builder-created Sun + accent lights retain `UniversalAdditionalLightData`
- geometry/render budgets pass
- generated path + paired-meta coverage pass
- lighting-input digest finalized

Completion: gate emits one durable machine-readable pass record bound to exact SHA.

## Cheap contract gates

Run before generated writes or lighting:

- static/editor checks -> validator asset paths, required serialized references, nonzero prefab fileIDs, prefab-source provenance
- migration checks -> exact assembly/namespace plus `/` separator for nested `MovedFrom` source types
- namespace checks -> qualify `UnityEngine.Physics` inside `RocketFooxball.Runtime.Physics` boundary
- camera checks -> persisted HDR, URP post-processing, SMAA state
- material checks -> emission map/color, `globalIlluminationFlags`, `_EMISSION` agree after reload
- scene checks -> required URP companion components already serialized

## Expensive-proof ordering

Use:

`implementation -> static/compile/fast semantic checks -> review -> fixes -> source freeze -> expensive proof once`

Avoid:

`implementation -> expensive proof -> review -> fixes -> expensive proof again`

Final builder proof remains:

`Build 1 -> process/lock release -> hash snapshot -> Build 2 reuse -> zero hash drift -> process/lock release -> separate Validate -> zero hash drift`

Build pair counts as one validation attempt.

Inner loop may batch compile + targeted semantic checks into one Unity process. Final persisted proof keeps separate Build 2 and Validate processes.

## Orchestration limits

- default autonomous budget: `60m`
- hard continuation gate: `90m`
- implementation worker cap: `3`
- concurrent reviewer cap: `1`
- Unity/build owner cap: `1`
- same failing gate: maximum `2` attempts
- production bake attempts per checkpoint: maximum `2`
- third production bake: explicit user approval
- worker expected duration over `45m`: split before dispatch
- worker same failure twice: retire + fresh worker
- no durable progress for `20m`: stop, checkpoint, reassess
- one follow-up correction maximum per worker
- completed-but-unintegrated WIP cap: `2` tasks
- shared-state worker dispatch -> predecessor committed; exact SHA frozen before child preflight
- shared HEAD advances before child starts -> retire dispatch; issue fresh exact-SHA contract
- parallel work -> read-only analysis or disjoint source paths only; Unity/generated-state mutation remains serial

Status heartbeat every `15m` and on phase transition:

`phase | elapsed/budget | completed | active | retry count | next proof | ETA range | risk | stop SHA`

ETA slip over `25%`, second failure, new scope, or projected budget overrun -> immediate user update.

## Unity lease

Lease key: canonical absolute project path.

Only designated Unity/build owner runs Unity mutation, bake, capture, or final validation.

Acquire:

- exact short project path resolved
- no Unity process targets project
- project lock absent
- previous lease released
- exact Editor version `6000.5.6f1`
- explicit `-buildTarget StandaloneWindows64`
- absolute durable log path

Release:

- command exit captured
- owned PID tree exited
- lock absent
- log archived
- pre/post status + hashes archived

Workspace hygiene:

- atomic evidence write -> short same-directory `.tmp-<guid>`; never append temp suffix to full final path
- generated worktree `.slnx` -> exclude before Unity launch
- failed Unity run -> verify matching process absent before stale lock cleanup
- command wrapper `finally` -> process check, lock check, IDE-churn cleanup, result write

Compile-only -> `-batchmode -nographics -quit`.

Lighting/reflection/capture -> `-batchmode`; graphics device initialized. `-nographics` produced RenderTexture warnings during prior headless bake and cannot prove rendered probe/capture quality.

## Durable evidence contract

Bind `evidence_root` outside `Temp/`, `Library/`, and disposable worktree root.

Per command:

- `command.txt`
- `result.json`: SHA, cwd, Unity version, start/end, PID, exit, timeout state
- complete log
- marker summary
- pre/post Git status
- input/output hash manifests
- process/lock-release proof

Per checkpoint:

- task + execution IDs
- review base/frozen SHA
- finding dispositions
- invalidation set
- accepted, waived, blocked state per requirement
- evidence-manifest SHA-256

Hashing cost controls:

- full owned/protected baseline -> capture once per checkpoint
- intermediate command -> hash invalidation set + status-changed candidates
- final proof -> one full owned/protected inventory
- evidence manifest -> write once after evidence set closes; exclude manifest/self-hash recursion

## Lighting experiment results

Run: `2026-08-10`, Unity `6000.5.6f1`, Standalone Windows, Balanced power, isolated warm `Library/`.

Execution binding:

- experiment branch: `codex/graphics-efficiency-experiments-46075b`
- final SHA: `7ed37f173903826e0c770fd96d4c59c44dd111b3`
- harness: `Assets/_Game/Editor/MovementLabEfficiencyExperiments.cs`
- evidence root: `C:\rfx\46075b`
- evidence manifest SHA-256: `c5b3877af3041f9e46c1c4527cc32b03838bd57d9492b4d0fe5f8d12a333f54a`
- final compile: `13.156s`, exit `0`
- final read-only audit: `10.134s`, exit `0`
- generated-lighting restoration: `23` files checked, `0` baseline mismatches

### Probe scaling

- `40`: bake `224.263s`, process `237.080s`, LightBaker `198.682s`, probe render `6.160s`, outputs `26,052,580` bytes
- `80`: bake `204.313s`, process `218.307s`, LightBaker `180.861s`, probe render `5.762s`, outputs `26,076,206` bytes
- `200`: bake `208.982s`, process `220.080s`, LightBaker `184.914s`, probe render `5.485s`, outputs `26,142,196` bytes
- result: no monotonic speed reduction; run variance exceeds probe-count effect
- visual delta: `40`/`80` versus `200` below `0.47/255` maximum fixed-view mean absolute RGB difference
- decision: keep `200` production probes; probe reduction adds no useful bake-speed win

### Development bake

- Progressive CPU, `80` probes: bake `31.461s`, process `43.670s`, LightBaker `19.789s`, probe render `5.436s`
- outputs: `10,389,759` bytes
- production `200` comparison: bake `6.64x` faster; process below `60s` iteration target; outputs about `60%` smaller
- visual delta: maximum fixed-view mean absolute RGB difference `0.4744/255`
- readability: ball, ramp, goals, walls, center retained
- decision: accept measured development CPU profile for local GI checks; reject as final bake proof

### CPU versus GPU

- CPU: bake `31.461s`, process `43.670s`
- Intel Graphics OpenCL GPU: bake `35.300s`, process `46.408s`, LightBaker `22.515s`, probe render `0.703s`, shader load `17.638s`
- CPU/GPU visual delta: maximum fixed-view mean absolute RGB difference `0.2806/255`
- decision: keep Progressive CPU default; GPU adds startup cost without total-time win

### No-bake A/B

- initial failure cause: global lightmaps cleared while renderer lightmap indices remained bound -> static renderers sampled missing data
- corrected profile: detach renderer lightmap bindings + realtime sun + `Trilight` ambient + no shadows/lightmaps/probes/post/HDR
- corrected dark-pixel fractions: FirstPerson `0.100`, Overview `0.001`, NorthGoal `0.000`, SouthGoal `0.001`, Ramp `0.003`, BallDetail `0.015`
- baked dark-pixel fractions: FirstPerson `0.262`, Overview `0.001`, NorthGoal `0.045`, SouthGoal `0.150`, Ramp `0.165`, BallDetail `0.141`
- result: corrected no-bake profile readable; flatter depth and weaker polish than baked state
- decision: no-bake default for routine work; preserve production baked GI

### Static lighting scene split

- clone/alignment proof: `9` roots, `152` transforms, `0` alignment failures
- ownership inventory: `68` static renderers, `5` lights, `1` LightProbeGroup, `3` reflection probes, `37` colliders, `33` gameplay MonoBehaviours
- blockers: `28` mixed-ownership objects, `24` renderer/collider co-locations, `40` gameplay-owned objects under proposed lighting roots, `4` cross-root serialized references
- cross-root references: two `GoalTrigger.ball`; `MatchController.northGoal`; `MatchController.southGoal`
- result: root-only `Arena` + `Environment` split rejected; `targetApproved=false`
- next step: separate renderers from colliders/gameplay, define prefab/component ownership, rewire cross-scene references, then run persisted additive load proof

### Run findings

- warm compile steady state: about `12-13s`; `<=30s` goal passed
- cold fresh-`Library/` attempt: `630.679s`, then harness compile error; cold worktree import remains high-cost
- evidence under long repo path hit old-.NET atomic-temp path limit; short `C:\rfx\46075b` root fixed run
- capture blocker: `Assets/_Game/Shaders/RetroPowerGrid.shader` lines `115-116` fail D3D11 compilation (`syntax error: unexpected token 'line'`; `saturate` receives zero parameters) -> containment ceiling magenta
- visual acceptance: blocked until shader fix; timing/readability comparisons remain usable because defect appears across compared profiles

## Reflection-probe audit

Unity dependency audit completed.

- authoritative outputs: scene-folder `ReflectionProbe-0.exr` through `ReflectionProbe-3.exr`; all direct `LightingData.asset` dependencies
- scene assignments: Center -> `ReflectionProbe-1.exr`; EastGoal -> `ReflectionProbe-0.exr`; WestGoal -> `ReflectionProbe-2.exr`
- `ReflectionProbe-3.exr`: direct `LightingData.asset` dependency; preserve
- duplicate outputs: three named `Assets/_Game/Lighting/ReflectionProbe_*.exr`; absent from scene and `LightingData.asset` direct/recursive dependencies
- explicit named-probe loop: Center `1.460s`, West `0.268s`, East `0.256s`, total `1.985s`
- loop result: named EXRs rewritten but remain unreferenced
- decision: remove explicit named-probe loop + named EXRs only with builder contract, validator, fingerprint, and paired-meta updates

## Validator purity

Current validator restores URP Lit keywords, mutates in-memory materials, then clears dirty state. This fixed no-op drift but weakens read-only semantics.

Target:

- process A -> author/import/save materials; persist deterministic `globalIlluminationFlags` before keyword state
- process B -> bake; no material save
- process C -> clean-load public-state validation
- validator changes zero asset/object state
- pre/post material hashes identical

Recurring URP local-keyword instability -> evaluate project-owned PBR shader contract instead of private YAML keyword parsing.

## Unsafe shortcuts

- shared writable `Library/` across worktrees -> reject
- routine cache deletion -> reject
- manifest hash manual edit -> reject
- timestamp-only invalidation -> reject
- validator asset repair -> reject
- copy `LightingData.asset` across regenerated scene identity -> reject
- GPU switch without controlled benchmark -> reject
- bake inside importer/postprocess callback -> reject
- fast-mode switch that rewrites production quality/scene/lighting assets -> reject
- production bake or full proof inside routine iteration loop -> reject

## Next remediation completion criteria

- validator-only source edit causes no bake and zero owned-output writes
- semantic pre-bake failure exits before `Lightmapping.Bake()`
- gameplay-only edit leaves lighting-input digest unchanged
- fast mode handles code + visual iteration without production bake/full proof
- fast mode preserves accepted lighting outputs and production quality assets
- fast mode meets warm `30s` code / `60s` targeted-visual iteration goals or reports measured blocker
- visual lighting-input edit marks production lighting stale without blocking fast preview
- development bake records non-final profile + exact settings
- production validator rejects development bake outputs
- production checkpoint with stale lighting triggers exactly one explicit bake
- Build 2 reuses outputs with zero changed hashes
- separate validator changes zero hashes
- no command exceeds retry/budget policy silently
- every long command has durable evidence
- texture family iteration avoids full-suite regeneration
- final two-run texture proof executes once after review fixes

## Official Unity references

- Unity Accelerator: https://docs.unity3d.com/6000.0/Documentation/Manual/accelerator-configure.html
- Asset Database refresh: https://docs.unity3d.com/6000.5/Documentation/Manual/AssetDatabaseRefreshing.html
- Asset dependency hash: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.GetAssetDependencyHash.html
- Asset editing batches: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.StartAssetEditing.html
- Lightmap settings/samples: https://docs.unity3d.com/6000.0/Documentation/Manual/Lightmaps-reference.html
- Progressive GPU lightmapper: https://docs.unity3d.com/6000.0/Documentation/Manual/GPUProgressiveLightmapper.html
- Command-line arguments: https://docs.unity3d.com/6000.5/Documentation/Manual/EditorCommandLineArguments.html
- Lighting outputs: https://docs.unity3d.com/6000.0/Documentation/Manual/Lightmapping-bake.html
