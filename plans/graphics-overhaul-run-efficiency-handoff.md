# Graphics Overhaul Run Efficiency Handoff

## Core diagnosis

- Iteration bottleneck: global builder invalidation -> full scene regeneration -> `Lightmapping.Bake()`. Warm import cache not primary cause.
- `MovementLabBuilder.ComputeBuilderSignature()` hashes builder, validator, graphics/runtime, gameplay, Blender generators, textures, models. Any byte change invalidates all generated state.
- `TryReuseGeneratedState()` converts semantic validation failure into automatic rebuild. Validator, persistence, fingerprint, or unrelated source bugs can trigger production bake.
- Production bake inside routine edit loop caused repeated 3-5 minute waits. T7 + CP7 consumed about `2h46m`; trace recorded over 12 completed production bakes plus aborted bakes.
- Expensive loop amplified by broad downstream-proof rule in `plans/comprehensive-graphics-overhaul-coding-plan.md:304`.
- Relevant code:
  - `Assets/_Game/Editor/MovementLabBuilder.cs:495` -> build entry/no-op gate
  - `Assets/_Game/Editor/MovementLabBuilder.cs:1919` -> production bake
  - `Assets/_Game/Editor/MovementLabBuilder.cs:2025` -> reuse validation/fallback
  - `Assets/_Game/Editor/MovementLabBuilder.cs:2234` -> global signature

## Required fixes

### Split builder operations

Implement explicit operations with isolated ownership:

- `EnterMovementLabFastMode()` -> runtime/editor preview profile only; zero production asset writes.
- `BuildMovementLabFast()` -> changed non-lighting stages + fast validation; zero bake, capture, full proof.
- `AssembleMovementLab()` -> importers, materials, prefabs, gameplay scene.
- `ValidateMovementLabPreBake()` -> all non-lighting semantic + persistence checks.
- `BakeMovementLabLightingDevelopment()` -> explicit tagged development bake.
- `BakeMovementLabLighting()` -> explicit production bake.
- `ValidateMovementLab()` -> clean-process, read-only persisted validation.

Required behavior:

- Stale generation input -> rebuild affected stage only.
- Output fingerprint drift -> stop with exact changed paths.
- Semantic validation failure -> stop before bake.
- Validator-only edit -> compile + validate; zero generated writes.
- Unchanged lighting-input digest -> preserve baked outputs.
- Fast-mode lighting-input edit -> mark production lighting stale; keep preview available.
- Production bake -> explicit lighting-stage entry only.

### Replace global signature with stage keys

- Importer key -> source FBX/PNG + importer contracts.
- Material/prefab key -> imported dependencies + serialized prefab contracts.
- Gameplay-scene key -> prefabs + gameplay wiring contract.
- Quality key -> URP assets/settings/configurator.
- Lighting key -> static renderers/materials + lights + sky + probes + lighting settings + Unity version.
- Baked-output key -> scene lighting data + lightmaps + probe cubemaps + paired `.meta` files.
- Validator -> zero output ownership; excluded from generation keys.
- Imported dependency state -> use `AssetDatabase.GetAssetDependencyHash()` where suitable.
- Dependency coverage -> periodic forced-full-rebuild comparison detects missing edges.

Gameplay-scene digest:

- Canonical YAML: LF normalization + ordinal document ordering.
- Exclude only `!u!157 LightmapSettings` document.
- Persist builder-created `UniversalAdditionalLightData` before digest.
- Document reorder -> same digest.
- `LightmapSettings` mutation -> same digest.
- Gameplay content or `fileID` mutation -> changed digest.

### Add fast, development, and production lighting states

Fast mode default:

- Baked GI preview: off; accepted baked files untouched.
- Realtime GI: off.
- Lighting: realtime directional sun + `Trilight` ambient.
- Ambient sky/equator/ground: `(0.62, 0.70, 0.78)` / `(0.48, 0.52, 0.56)` / `(0.28, 0.31, 0.35)`; intensity `1.6`.
- Renderer bindings: snapshot baked/realtime indices + scale offsets; detach with index `-1`; restore exact state after preview.
- Shadows: off by default; optional low-cost directional shadow for movement/depth review.
- HDR, SSAO, bloom, post-processing, reflection bake: off.
- Reflection source: sky/default reflection or last stable cubemap.
- Quality: dedicated `Iteration`/Low URP profile.
- Production outputs, scenes, GUIDs, manifests, quality defaults: unchanged.

Development bake profile:

- Progressive CPU; lightmap resolution `5` texels/m.
- Bounces: minimum `1`, maximum `1`.
- Samples: direct `16`, indirect `128`, environment `64`.
- Light probes: `80`; multiplier `1`.
- Reflection resolution: `64`.
- Manifest records profile ID + every quality setting.
- Outputs tagged `development`; production validator rejects tag/settings mismatch.

Production bake profile:

- Progressive CPU; lightmap resolution `10` texels/m.
- Indirect bounces: `2`.
- Light probes: `200`.
- Reflection probes: three baked probes at `128` resolution.

### Make pre-bake validation exhaustive and read-only

Pre-bake validation must prove:

- Serialized references nonzero and backed by expected prefab sources.
- Importers, materials, prefabs saved, reloaded, valid.
- Canonical gameplay + owned-output hashes stable across save/reopen.
- `Volume.sharedProfile` persists; expected components exist as persistent subassets.
- Emissive materials retain `BakedEmissive` + `_EMISSION` after clean reload.
- Builder-created Sun/accent lights retain `UniversalAdditionalLightData`.
- Geometry/render budgets and generated path/paired-meta coverage pass.
- Lighting-input digest finalized.

Remove validator repair behavior:

- Material author/import step -> persist deterministic `globalIlluminationFlags` before keyword state.
- Bake step -> zero material saves.
- Validator -> public-state checks only; zero asset/object mutation.
- Pre/post material hashes -> identical.
- Recurring URP local-keyword instability -> replace private YAML keyword parsing with project-owned PBR shader contract.

### Fix known blockers and waste

- Fix `Assets/_Game/Shaders/RetroPowerGrid.shader:115-116`: D3D11 errors `syntax error: unexpected token 'line'` and `saturate` zero-parameter call. Current failure renders containment ceiling magenta and blocks visual acceptance.
- Remove explicit named reflection-probe render loop and unreferenced `Assets/_Game/Lighting/ReflectionProbe_*.exr` files. Update builder contract, validator, fingerprint, and paired `.meta` coverage together.
- Keep scene-folder `ReflectionProbe-0.exr` through `ReflectionProbe-3.exr`; all remain direct `LightingData.asset` dependencies.
- Reject root-only `Arena` + `Environment` additive lighting split. First separate renderers from colliders/gameplay, define prefab/component ownership, rewire cross-scene references, then prove persisted additive load.
- Rewrite `Tools/Blender/generate_retro_textures.py` hot paths with NumPy-vectorized 1024/2048 generation.
- Add `--family` or equivalent targeted texture generation + changed-family semantic audit.
- Keep full two-run texture hash proof for post-review accepted state only.

## Experiment results

Binding: Unity `6000.5.6f1`, Standalone Windows, warm isolated `Library/`, branch `codex/graphics-efficiency-experiments-46075b`, SHA `7ed37f173903826e0c770fd96d4c59c44dd111b3`, harness `Assets/_Game/Editor/MovementLabEfficiencyExperiments.cs`, evidence `C:\rfx\46075b`.

### Baseline costs

- Warm `Library/`: about `1.7 GiB`.
- Warm initial Asset Database refresh: `6.085s`; domain reload: `3.734s`.
- Warm compile steady state: about `12-13s`; `<=30s` target passed.
- One full build: `81` Asset Database refreshes, `17.59s` aggregate.
- Build 2 no-op process: about `15s`; separate validator: about `15s`.
- Retained production bake: `264.857s`; `40,960,000` probe samples across `200` positions.
- Cold fresh-`Library/` attempt: `630.679s`, then harness compile error. Cold import remains expensive; warm cache not iteration bottleneck.
- Full texture-generator run: about `8.5m`.

### Probe scaling

- `40` probes -> bake `224.263s`, process `237.080s`, outputs `26,052,580` bytes.
- `80` probes -> bake `204.313s`, process `218.307s`, outputs `26,076,206` bytes.
- `200` probes -> bake `208.982s`, process `220.080s`, outputs `26,142,196` bytes.
- `40`/`80` versus `200` visual delta -> below `0.47/255` maximum fixed-view mean absolute RGB difference.
- Result: run variance exceeded probe-count effect. Keep `200` production probes.

### Development bake

- Bake `31.461s`; process `43.670s`; LightBaker `19.789s`; probe render `5.436s`.
- Outputs: `10,389,759` bytes.
- Versus production `200`: `6.64x` faster bake, about `60%` smaller output.
- Maximum fixed-view mean absolute RGB difference: `0.4744/255`.
- Ball, ramp, goals, walls, center remained readable.
- Result: accepted for local GI evaluation; invalid as final bake proof.

### CPU versus GPU

- Progressive CPU -> bake `31.461s`, process `43.670s`.
- Intel Graphics OpenCL -> bake `35.300s`, process `46.408s`; shader load `17.638s`.
- Visual delta: `0.2806/255` maximum fixed-view mean absolute RGB difference.
- Result: Progressive CPU remains default; GPU startup cost erased render-time gain.

### No-bake preview

- Initial failure: lightmaps cleared while renderer lightmap indices stayed bound -> static renderers sampled missing data.
- Corrected state: detached renderer bindings + realtime sun + `Trilight` ambient + no shadows/lightmaps/probes/post/HDR.
- Corrected dark-pixel fractions: FirstPerson `0.100`, Overview `0.001`, NorthGoal `0.000`, SouthGoal `0.001`, Ramp `0.003`, BallDetail `0.015`.
- Baked fractions: FirstPerson `0.262`, Overview `0.001`, NorthGoal `0.045`, SouthGoal `0.150`, Ramp `0.165`, BallDetail `0.141`.
- Result: readable routine-work default; flatter depth and weaker polish than baked state.

### Static lighting scene split

- Clone/alignment: `9` roots, `152` transforms, `0` failures.
- Inventory: `68` static renderers, `5` lights, `1` `LightProbeGroup`, `3` reflection probes, `37` colliders, `33` gameplay `MonoBehaviour`s.
- Blockers: `28` mixed-ownership objects, `24` renderer/collider co-locations, `40` gameplay-owned objects under proposed lighting roots, `4` cross-root serialized references.
- Cross-root references: two `GoalTrigger.ball`, `MatchController.northGoal`, `MatchController.southGoal`.
- Result: root-only split rejected; `targetApproved=false`.

### Reflection probes

- Scene assignments: Center -> `ReflectionProbe-1.exr`; EastGoal -> `ReflectionProbe-0.exr`; WestGoal -> `ReflectionProbe-2.exr`.
- `ReflectionProbe-3.exr`: direct `LightingData.asset` dependency; preserve.
- Three named `Assets/_Game/Lighting/ReflectionProbe_*.exr`: absent from scene and direct/recursive `LightingData.asset` dependencies.
- Explicit named-probe loop: `1.985s`; rewrote only unreferenced named EXRs.
- Result: named loop and named EXRs removable; scene-folder outputs authoritative.

## Completion criteria

- Validator-only edit -> zero bake and zero owned-output writes.
- Semantic pre-bake failure -> exits before `Lightmapping.Bake()`.
- Gameplay-only edit -> lighting-input digest unchanged.
- Fast mode -> warm code iteration `<=30s`; targeted visual iteration `<=60s`; zero production bake/full proof.
- Fast mode -> accepted lighting outputs and production quality assets unchanged.
- Visual lighting-input edit -> production lighting marked stale; fast preview remains available.
- Development bake -> exact non-final profile recorded; production validation rejects output.
- Stale production lighting at explicit checkpoint -> exactly one production bake.
- Build 2 -> reuse + zero changed hashes.
- Separate validator -> zero changed hashes.
- Texture-family edit -> no full-suite regeneration.
- Full two-run texture proof -> once after accepted review fixes.
