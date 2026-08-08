# Bright Retro Graphics Coding Plan

Status: accepted
Source: direct user request
Run ID: direct
Plan ID: direct
Attempt ID: direct
Covered Requirements: bright Quake-2-level visual uplift; low laptop requirements; textured arena; basic character and first-person animation; AI-maintainable asset pipeline; free-asset decision; explicit user actions
Baseline: 7a286f3088c554f196420e8430e3c9ee88400999
Dependencies: None

## Objective

Deliver bright retro-future sports-arena graphics with visible surface texture, modular low-poly architecture, basic movement/kick/recoil animation, richer lightweight VFX, deterministic AI-owned generation, and measurable low-spec budgets. Completion includes generated scene/prefabs/materials/controllers, build-twice determinism, separate-process validation, six visual captures, static render-budget report, and user laptop playtest instructions.

“Quake-2-level” means textured low-poly world, strong silhouettes, repeated modular detail, animated character states, readable weapon feedback, and sprite-based effects. It does not mean copying Quake II’s dark palette, assets, renderer, or exact style.

## Scope

- in: bright arcade sci-fi art direction; project-owned procedural textures; one-sample URP shaders; modular Blender arena kit; world `Idle`/`Run`/`Jump`/`Fall`/`Land`/`Kick`; existing FPS kick plus procedural launcher recoil; goal-shield motion; explosion/trail texture-sheet polish; camera/environment settings; builder/import/controller/validator/fingerprint integration; visual and budget evidence
- in: current PC quality baseline at `0.8` render scale, HDR off, MSAA 1x, main-light shadows off, additional lights off, SSAO inactive, SRP Batcher on
- out: gameplay physics/tuning changes; CharacterController or Rigidbody ownership changes; root motion; network animation; PBR normal/metal/roughness/displacement maps; HDR/bloom; realtime reflections; SSAO; baseline realtime shadows; baked lighting; external asset download/import; mobile pipeline repair; UI redesign; sound
- out: executing `plans/movementlab-validation-scripts-coding-plan.md`; this plan owns only its distinct bright-arena capture/report paths

## Repository Findings

- observed: `ProjectSettings/ProjectVersion.txt` -> Unity `6000.5.6f1`
- observed: Blender `4.5.10 LTS` -> `C:/Program Files/Blender Foundation/Blender 4.5/blender.exe`
- observed: `Assets/_Game/Editor/MovementLabBuilder.cs` -> sole generated scene/prefab/material/controller/import/settings authority; `BuildMovementLab()` and `ValidateMovementLab()` are batch entry points
- observed: `MovementLabBuilder.BuildMovementLab()` -> importer/material/prefab writes occur before `TryOpenExistingGeneratedScene()`; current no-op gate violates repository no-write-before-gate rule
- observed: `MovementLabBuilder.ComputeBuilderSignature()` -> hashes selected source files but omits `Tools/Blender/generate_low_poly_rocket.py`, generated FBX/PNG bytes, importer metadata, project render settings, and generated-output fingerprint
- observed: current worktree signature `a9801138a6ce07da73e0b9d75050f1e466e8f9541a07521f300669dd59fed864`; scene marker `06db50525ae374f3ce3acf465fc62b25c2fc7018f3382a5c94e3f065d2f262d8`; next integrated build must rebuild
- observed: `MovementLabBuilder.BuildArena()` -> visible arena uses Unity cubes: floor, six wall sections, two ramps, four markings, two goals; no imported architecture, ceiling detail, props, or decorative hierarchy
- observed: gameplay arena dimensions -> floor `130 × 1 × 90m`; wall height `8m`; goals at `x=±64`; opening `36 × 7m`; recess depth `9m`; containment ceiling `y=14`
- observed: `Assets/_Game/Shaders/RetroToonLit.shader` -> one texture sample, vertex color, three-band `N·L`; no ambient fill, view rim, emission, fog, or received shadow attenuation
- observed: `Assets/_Game/Shaders/RetroParticle.shader` -> one transparent sprite sample; no texture-sheet-specific shader work required
- observed: `ProjectSettings/QualitySettings.asset` -> `globalTextureMipmapLimit: 3`, reducing current `128px` textures to `16px`, `256px` textures to `32px`, and `32px` sprites to `4px` unless importer opts out
- observed: `Assets/Settings/PC_RPAsset.asset` -> render scale `0.8`, HDR off, MSAA `1`, main shadows off, additional lights off, SRP Batcher on
- observed: `Assets/Settings/PC_Renderer.asset` -> Forward+ serialized mode; SSAO feature inactive
- observed: `Tools/Blender/generate_low_poly_character.py` -> Generic `CharacterRig`, `2000–4000` triangles, exact `Idle` and `Kick`, in-place/root-locked contract, audited previews and FBX export
- observed: `Tools/Blender/generate_fps_kick_rig.py` -> Generic three-bone FPS leg, exact `Idle` and `Kick`, audited camera-frustum contract
- observed: `MovementLabBuilder.ConfigureRigModelImporter()`, `EnsureAnimatorController()`, and `ValidateAnimatorController()` -> destructive exact two-clip/two-state contract; world locomotion clips require importer/controller/validator redesign together
- observed: `Assets/_Game/Scripts/Runtime/PlayerPresentation.cs` -> triggers kick on both animators only
- observed: `Assets/_Game/Scripts/Runtime/PlayerMotor.cs` -> already exposes `HorizontalSpeed`, `Velocity`, `IsGrounded`, and `HasGroundContact`; animation needs no movement simulation edit
- observed: `Assets/_Game/Scripts/Runtime/RocketLauncher.cs` -> successful launch has no event; procedural viewmodel recoil needs one presentation-only signal
- observed: current VFX -> one rocket trail system capped at `48` particles; explosion has four systems and `37` burst particles, cleanup within `1.25s`
- observed: no third-party-art provenance convention exists
- constraint: Blender owns mesh, UV, normals, vertex color, rig, and animation; Unity owns gameplay objects, colliders, physics, materials, Animator, and scene placement
- constraint: builder-owned change -> source/builder edit, authoritative rebuild twice, stable output hashes, separate-process validation, visual inspection
- proposed: `Tools/Blender/generate_arena_kit.py` -> one deterministic modular environment source
- proposed: `Assets/_Game/Models/ArenaKit.fbx` -> named mesh subassets loaded by builder without imported gameplay components
- proposed: `Assets/_Game/Shaders/RetroShield.shader` -> two bounded transparent goal surfaces only
- proposed: `Assets/_Game/Editor/BrightArenaVisualCapture.cs` and `Tools/Validation/Capture-BrightArenaVisuals.ps1` -> non-mutating six-view capture plus static budget manifest
- proposed: `Assets/_Game/Generated/MovementLabBuildManifest.json` -> source signature plus generated-output fingerprint; excluded from its own fingerprint

## Decisions

- decision: art direction -> bright retro-future sports arena: pale sky blue, warm ivory architecture, turquoise floor, saturated blue/red team goals, cyan trim, yellow hazards, medium blue-grey shadows; no black walls, horror lighting, brown industrial gloom, or dark fog
- decision: brightness comes from high-value textures, shader ambient fill, warm main light, bright trilight ambient, and pale linear fog; HDR, bloom, extra lights, SSAO, and baseline shadows remain disabled
- decision: environment uses deterministic project-owned Blender modules plus generated textures. Core plan requires no external asset, account, manual download, attribution, or cost
- decision: optional free-pack expansion remains outside this plan. Preferred sources: [Quaternius Modular Sci-Fi MegaKit](https://quaternius.com/packs/modularscifimegakit.html) and [Kenney Modular Space Kit](https://kenney.nl/assets/modular-space-kit), both advertised by their creators as CC0. Import requires later user approval for exact source, license, attribution, visual fit, and cost; agent may then download from first-party source. User does not need to download manually
- decision: external-pack fallback -> if user declines approval, no implementation path changes; project-owned arena remains complete
- decision: preserve active PC URP settings. Do not switch renderer mode, enable shadows, or add post-processing in this uplift. Profile before any later quality tier
- decision: keep `QualitySettings.globalTextureMipmapLimit = 3`; set `TextureImporterSettings.ignoreMipmapLimit = true` only for small project-owned gameplay/art textures and validate it. This restores authored resolution without raising unrelated texture cost
- decision: authored texture maximum -> `256px` for character/ball/environment atlas-like sheets; most repeating surfaces `128px`; no texture over `512px`; sRGB, mipmaps, bilinear, anisotropy `0`
- decision: opaque surface shader remains one base-map sample. Add ambient, rim, emission, and URP fog only. No normal/specular/reflection/additional-light variants
- decision: goal shield uses one transparent sample plus cheap `_Time` scan/pulse. Exactly two shield quads; invisible BoxColliders retain gameplay ownership
- decision: arena gameplay colliders and dimensions remain unchanged. Imported meshes are renderer-only `Visual` children or decorative objects outside collision volume
- decision: one `ArenaKit.fbx` contains stable named mesh subassets: `ArenaGoalShell`, `ArenaRampRails`, `ArenaWallPylon`, `ArenaPerimeterTruss`, `ArenaScoreboard`; builder creates Unity render objects from exact mesh subassets
- decision: world animation uses one base layer with six states. `Kick` exits to `Idle`, `Run`, `Jump`, or `Fall` using current parameters; no AvatarMask/action layer or root motion
- decision: FPS kick rig/controller stays exact `Idle`/`Kick`. Launcher remains static FBX; recoil offsets only `WeaponVisual`, never camera, aim transform, projectile, or player root
- decision: no scene/object pooling change. Current projectile rate and particle ceilings remain safe; new texture-sheet animation changes appearance, not particle count
- assumption: commit `7a286f3088c554f196420e8430e3c9ee88400999` is complete implementation baseline. Uncommitted changes in current checkout are intentionally ignored and must not influence planned values, source signatures, generated outputs, checks, or handoff
- USER ACTION REQUIRED before final acceptance: play for about two minutes on target laptop and confirm bright/readable appearance plus acceptable smoothness. Automated static budgets and captures cannot prove subjective feel on that machine
- USER ACTION OPTIONAL: approve one named CC0 pack in a later task if more generic props are wanted. No download is required for this plan
- question: None

## Execution Graph

`START -> T1 -> CP1 -> {T2 -> CP2 || T3 -> CP3 || T4 -> CP4} -> JOIN1 -> T5 -> CP5 -> T6 -> CP6 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in; `+` requires every named predecessor
- gates: `START` -> isolated clean worktree at baseline `7a286f3088c554f196420e8430e3c9ee88400999`; do not copy uncommitted current-checkout changes; Blender 4.5.10 and Unity 6000.5.6f1 available; no project-owning Unity process or `Temp/UnityLockfile`
- gates: `JOIN1` -> CP2, CP3, and CP4 accepted; generated texture/model outputs exist; no Unity import attempted in parallel lanes; new `.meta` files intentionally deferred to sole T5 Unity owner
- gates: `FINAL` -> all checkpoints accepted; clean committed execution SHA; Blender audits pass; Unity build 2 reuses identical outputs; separate validator passes; six captures inspected; static budgets pass; user playtest remains named handoff action
- rule: T2/T3/T4 own disjoint source and output paths. They run Blender/Python only, never Unity. T5 alone imports and generates `.meta`, materials, controllers, prefabs, and scene
- rule: `MovementLabBuilder.cs`, generated/serialized assets, model/texture importer metadata, and Unity processes remain serialized

## Tasks

### T1: Early no-op gate and generated-output fingerprint

- objective: make builder reuse truly read-only before visual sources expand its output surface
- covered_requirements: deterministic AI-written pipeline; safe generated-asset ownership; low iteration cost
- owner: W1, implementation worker, `luna_max`
- depends_on: None
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`; new `Assets/_Game/Generated/MovementLabBuildManifest.json` after final T5 build only
- protected: `Assets/_Game/Scripts/Runtime/ExplosionResolver.cs`, `Assets/_Game/Scripts/Runtime/PlayerMotor.cs`, `plans/completed/core-behaviour.md`, all current generated assets until T5, `ProjectSettings/**`, `Packages/**`
- focused_reads: baseline `MovementLabBuilder.BuildMovementLab()`, `TryOpenExistingGeneratedScene()`, `ComputeBuilderSignature()`, `GeneratedYamlAssetPaths`, `NormalizeGeneratedYamlWhitespace()`, `ValidateMovementLab()`
- implementation: add manifest path constant under `Assets/_Game/Generated`; add serializable manifest DTO with schema version, source signature, generated-output fingerprint, Unity version, and sorted fingerprint path list; manifest is builder-owned but excluded from its own fingerprint
- implementation: split current flow into read-only `TryReuseGeneratedState(sourceSignature)` and write path. First call after resolving project root/signature; before `EnsureFolders()`, importer configuration, asset creation, `RegisterBuildScene()`, gravity, timestep, `SaveAssets()`, refresh, normalization, or prefab/scene open-save
- implementation: reuse requires manifest exists/parses, schema current, source signature exact, sorted path list exact, every fingerprint file exists, recomputed SHA-256 exact, scene marker exact, and full validator success. On success log one reuse marker and return with zero writes
- implementation: generated-output fingerprint hashes normalized repository-relative path plus raw bytes for builder-owned scene, prefabs, materials, physic material, controllers, generated source textures/models, relevant texture/model `.meta`, `ProjectSettings/EditorBuildSettings.asset`, `ProjectSettings/DynamicsManager.asset`, and `ProjectSettings/TimeManager.asset`; sort ordinal; exclude manifest and manifest `.meta`
- implementation: stale/missing/invalid reuse reports concise reason and enters current authoritative write path; do not swallow reason silently
- implementation: after final write path saves/imports, normalize owned YAML, refresh once, run full validator without manifest check, compute fingerprint, atomically write new manifest through same-directory unique temp plus no-overwrite replace semantics, import manifest, then validate manifest and outputs read-only
- implementation: add `Tools/Blender/generate_low_poly_rocket.py` and `Assets/_Game/Scripts/Runtime/RocketLauncher.cs` to source signature now; T5 extends same sorted list for new visual sources
- implementation: validator checks manifest schema, exact current source signature, exact fingerprint, and marker; internal post-build validation can skip manifest only until manifest write to avoid recursion
- done when: valid generated state returns before any write; stale state rebuilds once and records exact content fingerprint; baseline gameplay values remain unchanged
- checks: W1 runs `git diff --check`; static trace proves no mutating call precedes reuse gate; no Unity run at this checkpoint because T2–T5 invalidate generated outputs; reviewer checks circular hashing, path normalization, atomic replacement, missing-file routing, and baseline-only implementation
- proof: deliberate one-byte change to a copied fingerprint fixture or review-time simulated digest must route to rebuild; unchanged manifest/path set must route to reuse without `SaveAssets()` or normalization
- review_focus: Critical/High loss of user builder edits; false reuse; manifest self-hash loop; path traversal; partial manifest write; writes before gate; validator recursion; fingerprint omitting importer/project state
- review_checkpoint: CP1
- return_evidence: changed symbols; ordered preflight flow; complete fingerprint list; static branch proof; baseline-only diff; residual Unity-runtime verification deferred to T5

### T2: Bright surface, shield, and sprite-sheet sources

- objective: produce low-resolution bright textures and cheap shaders that materially improve surface readability without raising baseline render features
- covered_requirements: bright style; textures; low laptop requirements; AI-owned deterministic art
- owner: W2, implementation worker, `luna_max`, parallel lane A
- depends_on: CP1 accepted SHA
- owns: `Tools/Blender/generate_retro_textures.py`; `Assets/_Game/Shaders/RetroToonLit.shader`; new `Assets/_Game/Shaders/RetroShield.shader`; generated `Assets/_Game/Textures/RetroGrass.png`, `RetroBall.png`, `RetroExplosion.png`, `RetroSmoke.png`, `RetroWall.png`, `RetroTrim.png`, `RetroHazard.png`, `RetroShield.png`; existing paired `.meta` only where already present
- protected: `MovementLabBuilder.cs`; all FBX/`.blend`; runtime scripts; controllers; prefabs; scene; materials; `ProjectSettings/**`; `Assets/Settings/**`
- focused_reads: current texture generator/audits/previews; both current shaders; URP `Core.hlsl`/`RealtimeLights.hlsl` fog helpers from installed package; existing material property names
- implementation: retain deterministic seed and repository-relative paths. Generate exact textures: `RetroGrass` `128×128` seamless turquoise sports floor with pale lane flecks; `RetroWall` `128×128` seamless ivory panel/concrete with cyan seams; `RetroTrim` `128×128` seamless cyan/white tech trim; `RetroHazard` `128×128` seamless yellow/white diagonal hazard; `RetroShield` `128×128` clampable pale grid/hex mask; `RetroBall` `256×128`; `RetroExplosion` and `RetroSmoke` `128×128` four-by-four sheets with `32×32` cells
- implementation: texture audits require exact dimensions, finite RGBA, seamless opposite edges for repeat textures, non-flat value range, controlled alpha for sprite/shield textures, deterministic output hash on two same-source generator runs; previews under `Temp/BlenderPreviews/RetroTextures`, never commit
- implementation: `RetroToonLit` retains one `_BaseMap` sample, vertex color, instancing, target 2.0, SRP-batcher CBUFFER, DepthOnly, ShadowCaster; add `_AmbientColor`, `_AmbientStrength`, `_RimColor`, `_RimPower`, `_RimStrength`, `_EmissionColor`, `_EmissionStrength`; compute three-band main light plus ambient, restrained view rim, emission; add world position/fog varying, `#pragma multi_compile_fog`, `ComputeFogFactor`, `MixFog`
- implementation: do not add normal/specular/metal/roughness/AO textures, additional lights, screen textures, shadow sampling, alpha clipping, or post dependencies
- implementation: add `RocketFooxball/RetroShield`: transparent URP, one mask sample, alpha blend, `ZWrite Off`, `Cull Off`, target 2.0, instancing; properties `_BaseMap`, `_BaseColor`, `_EmissionColor`, `_PulseSpeed`, `_ScanScale`, `_Alpha`; use `_Time` for bounded pulse/scan; no depth texture, refraction, distortion, grab pass, or additional light
- implementation: preserve `RetroParticle` unchanged; Unity ParticleSystem Texture Sheet Animation selects sprite-sheet cells in T5
- done when: textures visibly communicate floor/wall/trim/hazard/energy classes; shaders compile by source inspection against installed URP includes; all source audits and previews pass
- checks: W2 resolves Python dependencies already used by generator; runs generator twice; records hashes, dimensions, audit output, and preview paths; inspects all texture previews; `git diff --check`; no Unity run in parallel lane
- proof: opposite-edge comparison proves seamless repeat maps; alpha histograms prove sprite/shield transparency; shader grep proves exactly one sampled texture per fragment path and no forbidden feature keyword
- review_focus: Critical/High non-deterministic texture bytes; broken seams/alpha; shader compile API mismatch; CBUFFER mismatch; accidental HDR/post/additional-light dependency; excessive transparency
- review_checkpoint: CP2
- return_evidence: changed source/output paths; two-run hashes; dimension/alpha/seam audit; shader property/sample list; preview inspection notes

### T3: Procedural modular arena kit

- objective: create distinctive low-poly architecture that upgrades arena silhouette while leaving physics geometry unchanged
- covered_requirements: Quake-2-level modular detail; Blender creation; low requirements; AI-maintainable assets
- owner: W3, implementation worker, `sol_high`, parallel lane B
- depends_on: CP1 accepted SHA
- owns: new `Tools/Blender/generate_arena_kit.py`; new `Assets/_Game/Models/ArenaKit.fbx`
- protected: `MovementLabBuilder.cs`; existing models/`.blend`; texture/shader paths; runtime scripts; prefabs; scene; materials; `ProjectSettings/**`
- focused_reads: `Tools/Blender/generate_fps_rocket_launcher.py` as static audit/export template; `MovementLabBuilder.BuildArena()`/`BuildGoal()` dimensions; `use-blender` mesh, preview, axis, and Unity contracts
- implementation: generator begins factory-empty; uses repository-relative output/preview paths; declares module contract before geometry; units `1m`; snap `0.25m`; connection overlap `>=0.005m`; Blender `+Z` up and directional `-Y` forward; FBX selected meshes only, `axis_forward=-Z`, `axis_up=Y`, applied units/modifiers, animation off
- implementation: output one FBX with exact unique mesh/object names and local Unity-target bounds: `ArenaGoalShell` width `38m`, height `8m`, depth `10m`, pivot opening-plane ground center, `≤1800` triangles; `ArenaRampRails` envelope `18×1×20m`, center pivot matching ramp collider, `≤1200`; `ArenaWallPylon` `1.5×8×1m`, ground-center pivot, `≤300`; `ArenaPerimeterTruss` `12×0.8×1m`, center pivot, `≤500`; `ArenaScoreboard` `8×3×0.4m`, center pivot, `≤500`
- implementation: stable material slots exact order from subset of `ArenaPrimary`, `ArenaTrim`, `ArenaHazard`, `ArenaGlow`; one `UVMap`; flat/faceted normals; one-segment bevels only; vertex color bakes cheap cavity/edge variation; no embedded textures, lights, cameras, colliders, scripts, armatures, or materials relied on by Unity
- implementation: Goal shell supplies posts, top frame, recessed side/back/floor visual panels without covering `36×7m` opening; asymmetric back detail proves imported Unity local `+Z`; ramp rails sit outside current walkable collider top; pylons/trusses/scoreboards remain decorative and outside field collision
- implementation: audit finite vertices, zero-area faces, zero-length edges, unintended loose/non-manifold geometry, outward winding, unique names, exact slots/UV, applied rotation/scale, origin/bounds tolerance `0.01m`, triangle ceilings, connection overlaps, and aggregate exported triangles `≤4300`
- implementation: render non-empty front/rear/left/right/top/three-quarter previews for every module under `Temp/BlenderPreviews/ArenaKit`; camera derives from evaluated bounds; inspect forward direction, pivots, seams, clipping, normals, scale
- done when: one deterministic command exports non-empty `ArenaKit.fbx`; every module passes contract and six-view inspection; no gameplay collider source changes
- checks: W3 runs `& 'C:/Program Files/Blender Foundation/Blender 4.5/blender.exe' --background --factory-startup --python Tools/Blender/generate_arena_kit.py`; exit `0`; audit output includes bounds/triangles/slots/output; reruns and compares semantic audit, not FBX bytes; opens all previews with image inspection; `git diff --check`
- proof: Goal opening bounds and imported-forward marker are discriminatory; ramp rail clearance proves visual cannot change walkable surface; module triangle total proves budget
- review_focus: Critical/High wrong FBX axes/pivot/scale; goal obstruction; ramp clipping; non-manifold or inverted mesh; material-slot instability; preview/export state divergence; hidden gameplay components
- review_checkpoint: CP3
- return_evidence: module contracts; Blender command/exit; audit lines; preview list/inspection; FBX path/size; residual Unity import proof deferred to T5

### T4: Basic locomotion and launcher recoil sources

- objective: add readable world locomotion states and successful-fire viewmodel feedback without touching gameplay movement or aim
- covered_requirements: basic animation; first-person feedback; low runtime cost
- owner: W4, implementation worker, `luna_max`, parallel lane C
- depends_on: CP1 accepted SHA
- owns: `Tools/Blender/generate_low_poly_character.py`; generated `Tools/Blender/LowPolyCharacter.blend`; generated `Assets/_Game/Models/LowPolyCharacter.fbx`; `Assets/_Game/Scripts/Runtime/PlayerPresentation.cs`; `Assets/_Game/Scripts/Runtime/RocketLauncher.cs`
- protected: `Assets/_Game/Scripts/Runtime/PlayerMotor.cs`; `BallKick.cs`; `PlayerCameraFeedback.cs`; `ExplosionResolver.cs`; `MovementLabBuilder.cs`; FPS kick/weapon generators and outputs; prefabs/controllers/scene/materials; `ProjectSettings/**`
- focused_reads: character action/audit/export code; FPS kick contract; `PlayerPresentation` subscription lifecycle; `RocketLauncher.LaunchRocket()` success ordering; public `PlayerMotor` presentation facts
- implementation: extend exact character actions to `Idle`, `Run`, `Jump`, `Fall`, `Land`, `Kick`; 30 FPS; ranges: Idle `1..30` loop, Run `1..20` loop, Jump `1..12`, Fall `1..15`, Land `1..10`, Kick existing `1..12` with contact frame `5`; all in-place
- implementation: Run uses opposing arms/legs and returns first/last pose exactly; Jump compresses then extends; Fall holds a readable airborne pose with small limb settling; Land compresses and returns to idle-compatible pose; Kick timing/contact remains unchanged
- implementation: audit exact action set/ranges/loop intent, unique rig/bones, normalized weights/max four influences, no Root/Pelvis translation or rotation curves, cyclic loop endpoint equality, non-loop end-pose contracts, meaningful limb displacement, original bounds/triangle budget, six static previews plus side-view contact sheets for Run/Jump/Fall/Land/Kick
- implementation: keep `CharacterRig`, bone hierarchy, mesh names, material slots, FBX export axes, Generic rig, and model GUID path stable
- implementation: add `RocketLauncher.RocketLaunched` event; invoke once only after projectile instantiate/initialize/register and cooldown assignment succeed; failed/cooldown-blocked calls emit nothing; no launch direction, cooldown, physics, cleanup, or public firing behavior change
- implementation: extend `PlayerPresentation` serialized refs with `PlayerMotor motor`, `RocketLauncher launcher`, `Transform weaponVisual`; subscribe/unsubscribe kick and launch events symmetrically; update world Animator parameters each `Update`: `Speed=motor.HorizontalSpeed`, `Grounded=motor.IsGrounded || motor.HasGroundContact`, `VerticalSpeed=motor.Velocity.y`; retain kick attempt trigger on both animators
- implementation: successful launch starts `0.16s` unscaled visual recoil. In `LateUpdate`, apply one deterministic sine envelope to cached neutral pose: maximum local offset `(0,0.025,-0.08)m`, maximum local Euler `(-6,0,1.5)°`; compose from neutral every frame, never accumulate; restart on rapid event; restore exact neutral pose on disable
- implementation: recoil modifies only `WeaponVisual`; never camera, camera feedback, head, spawn point, aim direction, player root, or projectile
- done when: Blender outputs six distinct valid clips; runtime presentation compiles by inspection; event/recoil lifecycle cannot change gameplay outcome or drift transform
- checks: W4 runs character generator with Blender; exit `0`; audits/previews pass; compares before/after gameplay methods and confirms only successful-launch event addition; `git diff --check`; no Unity run in parallel lane
- proof: animation contact sheets show distinct states; root-curve audit proves gameplay owns motion; event location proves one recoil per actual projectile; repeated recoil math proves no positional drift
- review_focus: Critical/High gameplay launch behavior change; event emitted on failure; subscription leak; null reference; root motion; clip-name/GUID churn; recoil changing aim/spawn transform; transform accumulation
- review_checkpoint: CP4
- return_evidence: action names/ranges/audits; Blender output; preview inspection; event invocation diff; recoil math/lifecycle; protected gameplay diff proof

### T5: Builder-owned bright arena integration

- objective: integrate accepted source assets into one deterministic bright scene/prefab/controller/material contract
- covered_requirements: textured scene; modular graphics; animation wiring; bright lighting; lightweight VFX; low-spec configuration; authoritative validation
- owner: W5, implementation worker, `luna_max`
- depends_on: CP2 + CP3 + CP4 accepted clean SHAs through JOIN1
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`; generated `Assets/_Game/Scenes/MovementLab.unity`; generated `Assets/_Game/Prefabs/Player.prefab`, `Ball.prefab`, `Rocket.prefab`, `ExplosionVfx.prefab`; generated `Assets/_Game/Animations/WorldCharacter.controller`, `FpsKick.controller`; generated `Assets/_Game/Materials/**`; generated `Assets/_Game/Generated/MovementLabBuildManifest.json`; new Unity `.meta` for accepted T2/T3/T6 Assets; importer `.meta` for project-owned textures/models; `ProjectSettings/EditorBuildSettings.asset`, `ProjectSettings/DynamicsManager.asset`, `ProjectSettings/TimeManager.asset` only as existing builder contract requires
- protected: runtime/gameplay paths outside accepted T4; `ProjectSettings/QualitySettings.asset`; `Assets/Settings/**`; `Packages/**`; existing model/texture GUIDs; `Assets/_Game/Shaders/RetroParticle.shader`
- focused_reads: accepted T1 manifest/no-op contract; T2 shader properties/texture dimensions; T3 mesh names/bounds/slots/axes; T4 clip/runtime reference contract; every builder importer/material/controller/prefab/arena/validator/signature function
- implementation: add paths for ArenaKit, new textures, shield shader, new materials, and manifest. Include every visual source script/shader/runtime contract plus `ArenaKit.fbx`/generated textures or their semantic source hashes in source signature. Extend fingerprint list and `GeneratedYamlAssetPaths` for every builder-owned YAML output
- implementation: configure all project-owned texture importers: sRGB, mipmaps on, bilinear, anisotropy `0`, repeat only for floor/wall/trim/hazard/ball, clamp for shield/explosion/smoke; read/write `TextureImporterSettings.ignoreMipmapLimit=true`; explicit max size equal authored dimension; no platform override above source size
- implementation: configure `ArenaKit.fbx` as static: animation none, import animation false, material import none, scale `1`; include `LowPolyRocket.fbx` in same static contract; validate exact named mesh subassets, source path, bounds, material submesh count, and no embedded gameplay components
- implementation: replace `GetOrCreateRetroMaterial` fixed values with explicit immutable material specification: shader, texture, scale, base, shadow, ambient, ambient strength, rim, rim power/strength, emission/strength. Builder reuses material assets/GUIDs and writes exact properties only
- implementation: bright palette values: floor base near white over turquoise texture; wall `(0.90,0.95,1.00)`; trim cyan `(0.10,0.75,1.00)`; hazard `(1.00,0.72,0.12)`; markings `(1.00,0.96,0.78)`; north goal blue `(0.10,0.50,1.00)`; south goal red `(1.00,0.22,0.20)`; shared lit shadow `(0.32,0.44,0.62)`; ambient color `(0.72,0.86,1.00)`, strength `0.55`; default rim strength `0.12`; emission only on trim/goal accents
- implementation: create two shield materials using `RetroShield`, team blue/red base, pale cyan/warm coral emission, alpha `0.52`, bounded pulse/scan values. Separate invisible gameplay `ShieldCollider` BoxCollider from renderer-only thin quad `ShieldVisual`; preserve returned collider reference, size, material, trigger behavior, scoring, blast occlusion, and ball collision
- implementation: keep floor/walls/ramp walkable cubes and colliders at current dimensions/transforms; apply new surface materials. Disable renderer only on old goal frame/recess solids after preserving their colliders, or create explicit collider-only goal solids; instantiate `ArenaGoalShell` visual under each goal root with exact local origin/orientation
- implementation: create `Arena/Architecture` renderer-only hierarchy. Place ramp rails with current ramp transforms, symmetric wall pylons at `8m` cadence excluding goal openings, perimeter trusses above wall height, and one scoreboard above each goal. Cap final scene at `≤80` MeshRenderers and `≤50,000` visible triangles; no decoration inside player/ball collision volume
- implementation: create mesh visuals from exact ArenaKit mesh subassets with MeshFilter/MeshRenderer; assign material slots by exact declared index, never renderer-name substring heuristic; validate source provenance, local transform, bounds, slots, no Collider/Rigidbody/runtime script, and goal/ramp clearance
- implementation: configure scene environment deterministically: `RenderSettings.skybox=null`; ambient mode Trilight; sky `(0.72,0.88,1.00)`, equator `(0.52,0.68,0.82)`, ground `(0.28,0.38,0.48)`, intensity `1`; linear fog on, color `(0.72,0.88,0.96)`, start `75m`, end `170m`; main directional light warm white, intensity `1.2`, rotation `(45,-30,0)`, shadows off; gameplay camera solid pale-blue clear, FOV unchanged `75`, far clip `180m`
- implementation: do not edit active URP/quality/renderer assets. Validator asserts quality index `0` resolves `PC_RPAsset`, render scale `0.8`, HDR off, MSAA 1, main shadows off, additional lights off, SSAO feature inactive, SRP Batcher on; fail rather than silently repair unrelated pipeline state
- implementation: configure world rig importer exact six clips/loop flags/ranges/root locks. Split controller generation/validation into world and FPS methods. FPS remains Idle/Kick with sole `Kick` trigger
- implementation: world controller parameters exact: `Speed` float, `Grounded` bool, `VerticalSpeed` float, `Kick` trigger. Base layer states exact: Idle default, Run, Jump, Fall, Land, Kick. Idle↔Run thresholds `0.30/0.20`; Idle/Run/Land route to Jump for not grounded plus vertical `>0.05` and to Fall for not grounded plus vertical `≤0.05`; Jump routes Fall below `0`, Land when grounded; Fall routes Jump above `0.05`, Land when grounded; Land routes Idle/Run after `0.65` exit using speed threshold; AnyState routes Kick on trigger with duration `0.02`, no self-transition; Kick exits at `1.0` to Idle/Run/Jump/Fall using Grounded/Speed/VerticalSpeed conditions
- implementation: controller rebuild removes stale subassets before replacement and validates exact state/transition/parameter counts, distinct imported clip object identity, conditions, defaults, root motion off, exact Avatar provenance, and no subasset growth on build 2
- implementation: wire `PlayerPresentation.motor`, `kick`, `launcher`, both animators, and exact baseline `WeaponVisual` transform in player prefab and scene instance; preserve baseline FPS kick mount; validate serialized nonzero references and prefab provenance after reload
- implementation: set explosion/smoke ParticleSystem Texture Sheet Animation to `4×4`, whole-sheet mode, one cycle across lifetime; retain four systems, total burst `37`, per-system max `≤40`, cleanup `≤1.25s`, trail max `48`/lifetime `0.45s`; adjust bright color gradients only; no new persistent particle system
- implementation: extend arena/material/importer/light/camera/VFX/prefab/controller validators from minimum counts to exact required identities, transforms, material properties, shader paths, imported mesh provenance, clip object identity, and budget ceilings
- done when: authoritative build creates one bright textured arena, imported modular architecture, correct animation controllers, viewmodel recoil refs, texture-sheet VFX, complete manifest/fingerprint, and full semantic validator coverage
- checks: W5 snapshots status/GUIDs; verifies no Unity process/lock; runs one Unity build process with `Start-Process -Wait -PassThru -WindowStyle Hidden`, captures exit `0`/log; waits for project process and lock release; runs build 2 from identical source SHA; requires reuse log and identical hashes for owned scene/prefabs/materials/controllers/manifest dependencies/importer metadata; waits; runs separate `ValidateMovementLab()` process; exit `0`, exact success marker, zero compiler/Console errors; inspects generated YAML/nonzero refs/GUIDs and `git diff --check`
- proof: build 1 stale reason plus rebuild, build 2 pre-write reuse, exact hash equality, and separate reopened validator discriminate stale refs, importer drift, controller subasset growth, missing models/materials, and non-determinism
- review_focus: Critical/High gameplay collider/goal change; baseline gameplay drift; serialized null refs; wrong FBX orientation/scale; shader/material mismatch; controller wrong motion/transition; write before no-op; fingerprint omission; unexpected URP/project-setting edit; generated GUID churn
- review_checkpoint: CP5
- return_evidence: changed/generated paths; baseline gameplay-diff proof; Unity process/exit/log paths; build 1/2 hashes; reuse marker; validation marker; prefab/controller/import provenance; expected-only status/diff; residual visual proof deferred to T6

### T6: Bright-arena visual capture and budget report

- objective: produce inspectable evidence that final scene is bright, non-blank, visually complete, and inside declared low-spec limits without mutating generated assets
- covered_requirements: drastic visible uplift proof; brightness proof; low laptop budget proof; AI-verifiable handoff
- owner: W6, implementation worker, `luna_max`
- depends_on: CP5 accepted clean SHA
- owns: new `Assets/_Game/Editor/BrightArenaVisualCapture.cs`; paired `.meta`; new `Tools/Validation/Capture-BrightArenaVisuals.ps1`
- protected: `MovementLabBuilder.cs`; all scene/prefab/material/model/texture/shader/controller/project settings; `plans/movementlab-validation-scripts-coding-plan.md`; runtime scripts
- focused_reads: `MovementLabBuilder.ValidateMovementLab()`; generated hierarchy/camera/layers; active URP settings; accepted T5 budget constants; existing pending capture-plan concepts only for process safety, never edit its planned paths
- implementation: add editor batch entry `RocketFooxball.Editor.BrightArenaVisualCapture.Capture`; output only to a new absolute directory under `Temp/BrightArenaVisuals/<UTC-id>`; fail if target exists/non-empty; never call builder build/save/refresh or scene save APIs
- implementation: call `MovementLabBuilder.ValidateMovementLab()` once, require scene clean and current manifest/fingerprint; render with graphics enabled through current URP; fixed `1280×720`, ARGB32, 24-bit depth, one sample; warm once then capture; restore camera/object/render-target state in `finally`
- implementation: capture exact ordered views: first-person spawn with weapon/crosshair; high arena overview; north goal three-quarter; south goal three-quarter; ramp/architecture detail; external character/ball detail. External views use temporary hidden camera, render world layer, hide FPS viewmodels/crosshair, then restore exact state
- implementation: per image require decodable PNG, exact dimensions, nonzero bytes, unique-color floor, mean sRGB luminance `≥0.28`, pixels below luminance `0.08` `≤35%`, pixels above `0.98` `≤25%`; thresholds detect dark/blank/clipped failure, not artistic quality
- implementation: static budget scan counts enabled scene MeshRenderers, renderer material passes, transparent renderers, unique meshes, visible mesh triangles, project-authored texture dimensions and estimated uncompressed RGBA residency; fail above `80` renderers, `100` opaque material passes, `8` transparent renderers, `50,000` visible triangles, `8MiB` authored texture residency, or any texture dimension `>512`
- implementation: write one JSON manifest after all checks: source Git SHA/dirty flag, Unity/URP/quality/GPU identifiers, scene/build-manifest fingerprint, camera poses, image SHA-256/brightness metrics, render budgets, pass status; log `BRIGHT_ARENA_CAPTURE_PASS <manifest>`
- implementation: PowerShell wrapper resolves Unity 6000.5.6f1, rejects active project process/lock, launches hidden `Start-Process -Wait -PassThru` without `-nographics`, waits for lock release, validates exit/log/manifest/image hashes, and prints evidence directory
- done when: one command yields six valid PNGs and passed budget manifest; source/generated hashes and Git status remain unchanged
- checks: W6 snapshots generated hashes/status; runs wrapper; opens all six images through `view_image`; records bright palette, texture readability, goal color distinction, architecture silhouettes, weapon/character visibility, no clipping/missing mesh/wrong normals; compares pre/post hashes/status; `git diff --check`
- proof: luminance/clipping thresholds reject dark or blown-out captures; opposing goal/detail views expose asymmetry, missing assets, wrong normals, shield overdraw, and layer mistakes; budget manifest rejects hidden cost growth
- review_focus: Critical/High accidental asset save; capture state not restored; false brightness pass; wrong layer visibility; unsupported render request; budget undercount from disabled/instanced meshes; source/evidence SHA mismatch; unsafe PowerShell process/path handling
- review_checkpoint: CP6
- return_evidence: wrapper command/exit; manifest path/hash; six PNG paths/hashes/metrics/inspection notes; render budget totals; pre/post source/generated hashes; process/lock release; residual actual-laptop feel risk

## Execution Assignments

- workers: T1 -> W1 `luna_max`, serial; T2 -> W2 `luna_max` parallel lane A; T3 -> W3 `sol_high` parallel lane B; T4 -> W4 `luna_max` parallel lane C; T5 -> W5 `luna_max` after JOIN1; T6 -> W6 `luna_max` after CP5
- review_checkpoints: CP1 -> T1/W1 -> per-worker fresh `sol_medium` Critical/High review after source-only checkpoint; accepted disposition gates all parallel lanes
- review_checkpoints: CP2 -> T2/W2 -> fresh `sol_medium` Critical/High source/output review; new Unity `.meta` intentionally deferred to T5
- review_checkpoints: CP3 -> T3/W3 -> fresh `sol_medium` Critical/High geometry/export review; new Unity `.meta` intentionally deferred to T5
- review_checkpoints: CP4 -> T4/W4 -> fresh `sol_medium` Critical/High animation/runtime review
- review_checkpoints: CP5 -> T5/W5 -> fresh `sol_medium` Critical/High integrated builder/generated-state review after CP2+CP3+CP4 join; shared builder/assets and Unity import make partial review weaker
- review_checkpoints: CP6 -> T6/W6 -> fresh `sol_medium` Critical/High capture/budget-tool review after evidence generation
- fixes: every accepted review finding goes to fresh `luna_max`; no fix worker receives re-review; rerun every invalidated check before dependency gate opens

## Final Verification

- exact head: clean committed execution SHA descending from baseline and all accepted checkpoint SHAs; no current-checkout uncommitted change copied into execution; accepted plan bytes unchanged
- Blender checks: rerun texture, arena-kit, and character generators from repository root with Blender/Python versions captured; exit `0`; audits/previews pass; generated semantics stable
- Unity checks: no interactive Editor/process/lock; hidden `Start-Process -Wait -PassThru`; build 1 exit `0`; build 2 same SHA exit `0` and pre-write reuse; owned output/import metadata hashes identical after build 1/2; separate-process `ValidateMovementLab()` exit `0` and success marker; zero compile/Console errors; process/lock released after each
- visual checks: run `Tools/Validation/Capture-BrightArenaVisuals.ps1` with graphics enabled; manifest passed; six images open and meet content/brightness requirements; generated/source hashes unchanged
- runtime contract inspect: controller has six distinct world clips and correct transitions; FPS controller remains two clips; player prefab has serialized motor/launcher/animator/weapon refs; root motion false; successful launch event changes presentation only
- gameplay preservation inspect: collider/trigger transforms, shared physic material, goal references, rocket/ball/player physics, input, spawn positions, and baseline blast/bhop/ramp/viewmodel values unchanged except planned renderer-only children/materials/camera far clip
- render budget: `≤50,000` visible triangles; `≤80` MeshRenderers; `≤100` opaque passes; `≤8` transparent renderers; `≤8MiB` estimated project-art RGBA residency; max texture dimension `≤512`; PC render scale `0.8`; HDR/MSAA-shadows/additional-lights/SSAO remain off
- Git checks: existing GUIDs preserved; new assets have unique paired `.meta`; no unrelated reserialization; `git diff --check`; expected owned paths only; final status clean in execution worktree
- user check: launch `MovementLab`, play about two minutes, inspect both goals/ramp/rocket jump/kick at native laptop resolution; report whether scene is bright/readable and motion stays smooth. This subjective check does not authorize automatic quality expansion
- invalidation: shader/texture/model/generator/importer/material/controller/builder/scene/prefab/camera/quality change -> rerun relevant generator, both builds, separate validation, capture, and image inspection; runtime presentation/launcher change -> recompile, rebuild twice, validate, and inspect recoil

## Handoff

- changed paths: `Tools/Blender/generate_retro_textures.py`, new `Tools/Blender/generate_arena_kit.py`, `Tools/Blender/generate_low_poly_character.py`, generated character `.blend`/FBX, new arena FBX, project textures, `RetroToonLit.shader`, new `RetroShield.shader`, `PlayerPresentation.cs`, `RocketLauncher.cs`, `MovementLabBuilder.cs`, generated materials/controllers/prefabs/scene/manifest/import metadata, new bright capture editor/wrapper files and `.meta`
- residual risks: actual FPS and visual taste depend on target laptop/GPU and require user playtest; transparent shield sorting needs capture inspection; generated FBX bytes may vary despite stable semantic audit; current mobile quality path remains unresolved; optional third-party packs remain unapproved/out of scope
- authority: execution orchestrator may mutate/commit only plan-owned paths in isolated worktree created from exact baseline; no external download/import without new explicit user approval; merging agent alone integrates accepted SHA into target branch; user branch, Asset Store, package, renderer-tier, and external-source changes require user authority

## Done Criteria

- every covered requirement maps to task, owner, check, and proof
- every task passes implementation design gate
- Execution Graph includes every task and checkpoint exactly once; parallel lanes have disjoint writable paths and explicit JOIN1
- every worker maps to one review checkpoint; integrated shared-asset checkpoint occurs after source lanes join
- exact baseline is recorded; uncommitted current-branch changes are excluded from execution
- core implementation requires no third-party download; optional sources have explicit approval gate
- bright art direction and low-spec ceilings are machine-checkable where possible and subjectively verified on target laptop
- execution route uses immutable accepted artifact and `$orchestrate-implementation`
- final checks bind clean committed head descended only from baseline and accepted task SHAs
