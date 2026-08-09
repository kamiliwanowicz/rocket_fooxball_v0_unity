# Graphics Overhaul Run Efficiency Handoff

## Purpose

Postmortem + next-run guardrails for `plans/comprehensive-graphics-overhaul-coding-plan.md` execution.

Use before next orchestrated Unity graphics, generated-scene, lighting, or Blender-heavy run.

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

`AssembleMovementLab()` -> importers, materials, prefabs, gameplay scene

`ValidateMovementLabPreBake()` -> all non-lighting semantic + persistence checks

`BakeMovementLabLighting()` -> explicit production bake

`ValidateMovementLab()` -> clean-process, read-only persisted validation

Required behavior:

- stale generation input -> rebuild affected generation stage
- output fingerprint drift -> stop with exact path diff
- semantic validation failure -> stop; no automatic bake
- validator-only change -> compile + validate; zero generated writes
- unchanged lighting-input digest -> preserve baked outputs
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

Expected invalidation matrix:

- validator edit -> compile + validator
- gameplay behavior edit -> compile
- serialized prefab-contract edit -> prefab + gameplay scene
- material appearance edit -> material + lighting
- static mesh/light/probe edit -> lighting
- quality validator edit -> compile + validator

## Pre-bake gate

Production bake may start only when all conditions pass:

- all invalidating writers stopped
- source checkpoint committed
- source review completed
- accepted Critical/High fixes applied
- Unity compile passed
- importers/materials/prefabs saved, reloaded, validated
- scene saved, reopened, validated
- Volume uses expected persisted `sharedProfile`
- Volume components exist as nonzero persistent subassets
- material keywords/maps pass clean-process validation
- geometry/render budgets pass
- generated path + paired-meta coverage pass
- lighting-input digest finalized

Completion: gate emits one durable machine-readable pass record bound to exact SHA.

## Expensive-proof ordering

Use:

`implementation -> static/compile/fast semantic checks -> review -> fixes -> source freeze -> expensive proof once`

Avoid:

`implementation -> expensive proof -> review -> fixes -> expensive proof again`

Final builder proof remains:

`Build 1 -> process/lock release -> hash snapshot -> Build 2 reuse -> zero hash drift -> process/lock release -> separate Validate -> zero hash drift`

Build pair counts as one validation attempt.

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

## Lighting experiments

Run after builder remediation. One variable per experiment. Same scene, Unity version, path, warm Library, power state.

1. Probe scaling
   - variants: `40`, `80`, `200`
   - record preprocess, lightmap, probe, reflection, import, total durations
   - inspect player/ball/rocket lighting near goals, walls, ramps, center

2. Development bake profile
   - lower indirect/environment samples
   - probe multiplier `1`
   - lower lightmap resolution
   - one bounce where visually adequate
   - final profile used only after source freeze

3. CPU vs GPU
   - current machine: Intel Core Ultra 5 135U; integrated Intel Graphics; reported 2 GiB VRAM
   - benchmark; no default switch without stable-output + quality evidence

4. No-bake POC A/B
   - baked current state vs realtime sun + sky ambient + SSAO + stable reflection cubemap
   - blind visual/readability comparison
   - remove baked-GI subsystem if no material gameplay/readability win

5. Static lighting scene split
   - `MovementLabLighting.unity` -> static renderers, sun, probes, baked data
   - `MovementLabGameplay.unity` -> colliders, player, ball, controllers, HUD
   - additive-load + alignment validation required

## Reflection-probe audit

Current builder runs `Lightmapping.Bake()`, then explicitly runs `BakeReflectionProbe()` for three probes.

Current outputs include:

- four scene-folder `ReflectionProbe-*.exr` files
- three named `Assets/_Game/Lighting/ReflectionProbe_*.exr` files
- scene probe `m_CustomBakedTexture` references are zero
- named EXR GUIDs have no textual project references

Before deletion/change:

- query `AssetDatabase.GetDependencies()` for scene + `LightingData.asset`
- compare probe assignments and captures with explicit loop removed
- retain only authoritative referenced outputs

## Validator purity

Current validator restores URP Lit keywords, mutates in-memory materials, then clears dirty state. This fixed no-op drift but weakens read-only semantics.

Target:

- process A -> author/import/save materials
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

## Next remediation completion criteria

- validator-only source edit causes no bake and zero owned-output writes
- semantic pre-bake failure exits before `Lightmapping.Bake()`
- gameplay-only edit leaves lighting-input digest unchanged
- lighting-input edit triggers exactly one explicit bake
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
