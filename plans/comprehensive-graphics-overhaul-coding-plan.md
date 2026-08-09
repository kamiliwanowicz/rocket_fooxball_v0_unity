# Comprehensive Graphics, VFX, Containment, and Movement Overhaul Coding Plan

Status: accepted
Source: direct user request
Run ID: direct
Plan ID: direct
Attempt ID: direct
Covered Requirements: native 1920x1080 High graphics; scalable Low fallback; Quake II-inspired industrial arena; sunny sky; PBR textures and lighting; uniform football; textured rocket; readable projectile trail; yellow fireball explosion; visible closed containment; lower normal jump; speed-responsive underfoot blast
Baseline: eddba96fb436db3fc187ba49b37285fb1cc01f7a
Dependencies: None

## Objective

Upgrade MovementLab into high-quality, readable industrial sports arena at native 1920x1080. Integrate arena, ball, rocket, projectile, explosion, containment, lighting, and render-quality changes through one graphics authority. Include explicit jump and underfoot-blast tuning needed by raised containment. Completion requires deterministic source assets, builder-owned generated state, High and Low quality levels, stable two-build output, separate validation, 1080p visual proof, gameplay proof, and target-machine performance report.

## Scope

- in: warm Quake II-inspired industrial construction language; layered panels, ribs, bevels, trims, recesses, trusses, hazard metal, aged bronze, concrete, strong edge highlights
- in: sunny blue sky, visible warm sun, sparse soft clouds, cool sky fill, clean gameplay visibility
- in: URP Lit opaque arena, character, weapon, ball, and rocket materials; base, normal, metallic-smoothness, AO, selective emission, detail-normal maps
- in: High quality with HDR, SMAA, main-light soft shadows, SSAO, baked indirect light, light probes, reflection probes, restrained bloom, ACES; Low fallback with reduced render cost
- in: 12-panel football texture, controlled rocket atlas/UVs, additive projectile glow, spaced smoke trail, contiguous yellow fireball, visible upper-wall/ceiling power grid
- in: normal-jump apex near `0.94m`; high-speed underfoot blast redirects authored forward impulse upward while preserving impulse magnitude and earned horizontal heading
- in: ceiling underside `48m`; tight upper containment walls aligned to visible arena bounds; unchanged goal apertures and backstops
- in: deterministic generators, authoritative builder integration, 1920x1080 High/Low captures, performance budgets, target-PC playtest
- out: HDRP, ray tracing, realtime GI, tessellation, runtime displacement, parallax occlusion, motion blur, depth of field, chromatic aberration, film grain, heavy vignette, external asset packs
- out: gameplay changes beyond named jump velocity, high-speed underfoot impulse direction, and containment collider placement
- out: ball radius/mass/kick behavior; rocket speed/lifetime/collider/damage; blast radius/strength/falloff/occlusion; scoring; goal planes/openings/triggers/shields/recesses; spawn positions; controls; camera FOV
- out: character topology redesign, weapon topology redesign, multiplayer architecture, new gameplay test assembly

## Repository Findings

- observed: worktree `C:/Users/iwano/Desktop/repos/rocket_fooxball_v0_unity/rocket_fooxball_v0_unity` -> branch `new_graphics`, clean at baseline SHA
- observed: `ProjectSettings/ProjectVersion.txt` -> Unity `6000.5.6f1`; `Packages/manifest.json` -> URP `17.5.0`
- observed: `graphics references/arena/qauek 2 hi res.jpg` -> readable `1920x1200` JPEG; SHA-256 `3D338708294FDFB2EDF89F132F9C917706C64E1A5050D467AA98BCA9A060A549`
- observed: reference -> reusable cues: warm gold/bronze response, repeated modules, edge highlights, deep recesses, vertical layers, catwalk forms, high surface-frequency variation; red sky excluded
- observed: `Assets/_Game/Shaders/RetroToonLit.shader` -> base-only toon lighting; no normal, metallic, smoothness, AO, reflections, or received realtime-shadow path
- observed: `Tools/Blender/generate_retro_textures.py` -> 11 outputs; most surfaces `128x128`; ball `256x128`; `generate_ball()` uses uneven latitude bands; no PBR map sets
- observed: `Tools/Blender/generate_low_poly_rocket.py` -> one joined `RocketVisual`, box fins, no controlled UV/material-region contract
- observed: `Tools/Blender/generate_arena_kit.py` -> five stable modules: `ArenaGoalShell`, `ArenaRampRails`, `ArenaWallPylon`, `ArenaPerimeterTruss`, `ArenaScoreboard`; one UV layer; exact bounds, pivots, slots, connections, and semantic audits
- observed: `Assets/_Game/Editor/MovementLabBuilder.cs` -> sole owner for generated scene, prefabs, materials, importers, render environment, manifest, fingerprint, and semantic validation
- observed: `MovementLabBuilder.BuildPlayerPrefab()` -> generated `jumpVelocity = 6.75f`; `PlayerMotor` source default `4.50f`; fixed-step gravity applies before `CharacterController.Move()`
- observed: `ExplosionResolver.ComputePlayerImpulse()` -> underfoot force always adds player-yaw forward scale `0.5625` plus upward scale `1.0`
- observed: builder rocket -> one red toon material, one point light, one dense smoke system; active renderer disables additional lights, so light does not provide reliable visible projectile core
- observed: builder explosion -> four systems and 37 burst particles; red material/gradient and `FireChunks` speed separate effect into red particles
- observed: builder containment -> ceiling center `y=14`, distant outer walls at `x=+-69`, `z=+-59`; visible arena boundaries sit near `x=+-64.5`, `z=+-44.5`
- observed: `Assets/Settings/PC_RPAsset.asset` -> HDR off, render scale `0.8`, shadows off, additional lights off; `Assets/Settings/PC_Renderer.asset` -> SSAO feature present but inactive
- observed: `ProjectSettings/QualitySettings.asset` -> one `PC` quality level, global mip limit `3`, anisotropic filtering off; `ProjectSettings/ProjectSettings.asset` -> desktop default `1920x1200`
- observed: `Assets/_Game/Editor/BrightArenaVisualCapture.cs` -> six `1280x720` views; `50,000` triangle, `80` renderer, `8MiB` texture, `512px` texture caps
- constraint: `BallMotor`, `RocketProjectile`, `ExplosionResolver`, `PlayerMotor`, and `MatchController` retain runtime state ownership from `plans/runtime-architecture.md`
- constraint: Blender owns mesh, UV, normals, vertex colors, static module design; Unity owns colliders, gameplay roots, materials, lights, probes, scene placement, render settings
- constraint: generated changes require build twice from same source SHA, build-2 no-op proof, stable owned hashes, separate-process validation, expected-only diff
- constraint: Unity mutation remains serialized; one Editor per project; short worktree path; preserved `Library/`; no concurrent Unity imports
- proposed: `Assets/_Game/Editor/GraphicsQualityConfigurator.cs` -> idempotent High/Low URP and project-quality source
- proposed: `Assets/_Game/Shaders/SunnyArenaSky.shader`, `RetroAdditiveParticle.shader`, `RetroPowerGrid.shader` -> sky, VFX, containment render paths
- proposed: `Assets/_Game/Lighting/` -> builder-owned lighting settings, lightmaps, reflection probes, lighting data

## Decisions

- decision: one graphics authority -> High/Low render contract, PBR maps, ball/rocket materials, projectile/explosion shaders, containment grid, lighting, budgets, and captures form one pipeline. No retained base-only rocket/ball path
- decision: High default -> Standalone `1920x1080`; render scale `1.0`; HDR on; SMAA; main-light `2048` soft shadows; two cascades; `80m` shadow distance; Forward+ additional lights on; no additional-light shadows; half-resolution medium SSAO; forced anisotropic filtering; full mip quality; SRP Batcher on
- decision: Low fallback -> render scale `0.8`; HDR off; main shadows off; SSAO off; additional lights off; one mip reduction; same scene, meshes, PBR materials, transparent VFX, gameplay, and native output size
- decision: quality assets -> existing `Assets/Settings/PC_RPAsset.asset` and `PC_Renderer.asset` become `High` while preserving GUIDs; add `Assets/Settings/PC_Low_RPAsset.asset` and `PC_Low_Renderer.asset`; quality names `High`, `Low`; High index/default `0`, Low index `1`
- decision: quality switching -> `Assets/_Game/Scripts/Runtime/GraphicsQualityRuntime.cs` watches `QualitySettings.GetQualityLevel()` and applies camera-only state. High: HDR on, SMAA High, post on. Low: HDR off, FXAA, post off. Pipeline asset, mip limit, shadows, lights, and SSAO come from selected quality asset/config. Capture switches levels through same runtime path and restores prior level
- decision: performance goal -> `60 FPS` on Intel Core Ultra 5 135U integrated graphics at native `1920x1080`. Miss order: SSAO samples -> shadow distance -> shadow resolution -> accent-light count -> reflection resolution -> bloom. Preserve native resolution, PBR bindings, and High base resolution until these costs exhaust
- decision: lighting -> one warm mixed sun with realtime direct shadows and baked indirect; sparse non-shadowed goal/accent lights; static arena lightmapped; dynamic player/ball/rocket/VFX use light probes; three baked box-projected reflection probes cover center and goals
- decision: sun -> rotation `(50,-30,0)`, color `#FFD6A3`, intensity `1.1`, soft shadows, strength `1`, bias `0.05`, normal bias `0.4`; no other shadow-casting realtime light
- decision: accents -> four non-shadowed Point lights at `(-58,5,-12)`, `(-58,5,12)`, `(58,5,-12)`, `(58,5,12)`; west blue/east red team tint; intensity `500` lumens; range `14m`
- decision: SSAO High -> BlueNoise, Downsample on, AfterOpaque off, DepthNormals source, Medium samples, Medium blur, intensity `1.2`, direct-light strength `0.25`, radius `0.035`, falloff `100`; Low renderer has SSAO inactive
- decision: probes -> reflection probes: center `(0,12,0)` size `(100,28,70)` plus goals `(+-58,6,0)` size `(20,12,38)`, baked, box projection, `128` resolution, HDR; light probes use bounded lattice across `x=-56..56` step `16`, `z=-36..36` step `18`, `y={1.5,8,20,36,46}`, removing points inside solid colliders
- decision: bake -> Progressive CPU, Baked GI on, Realtime GI off, Mixed Lighting Shadowmask, directional lightmaps, `10` texels/m, atlas `1024`, padding `2`, two indirect bounces, compressed lightmaps, automatic filtering; outputs stay under `Assets/_Game/Lighting/` and manifest records actual stable filenames
- decision: post -> ACES; Bloom threshold `1.1`, intensity `0.20`, scatter `0.60`, clamp `10`, high-quality filtering off; Color Adjustments post-exposure `0`, contrast `+5`, saturation `+4`; no other Volume overrides. Readable whites, team colors, ball panels, and dark recesses remain unclipped
- decision: sky -> horizon `#B9DCF2`, zenith `#4C91D8`, cloud tint `#F5F3E8`, cloud coverage `0.22`, softness `0.65`, sun angular radius `0.012`, sun HDR intensity `3.0`; sky source carries clouds only and shader sun direction follows directional light
- decision: opaque material path -> `Universal Render Pipeline/Lit`; remove `RetroToonLit` assignments from opaque world, character, weapon, ball, rocket. Keep custom transparent shield/particle/grid paths. Retain obsolete shader asset until validator proves zero material references
- decision: PBR channels -> `_BaseMap` sRGB; `_BumpMap` NormalMap; `_MetallicGlossMap` linear with metallic R and smoothness A; `_OcclusionMap` linear with AO G; `_EmissionMap` sRGB only where authored
- decision: Lit material multipliers -> metallic/smoothness map multipliers `1`; floor normal `0.65`, AO `0.75`; wall `0.80/0.80`; trim `1.0/0.80`; hazard `0.75/0.80`; weapon `1.0/0.90`; ball `0.45/0.65`; rocket `0.80/0.85`. Emission intensities: arena glow `2.0`, weapon accent `1.5`, rocket hot `3.0`; other opaque emission off
- decision: resolutions -> tiling arena sets `1024x1024`; shared detail normal `512x512`; weapon sets `2048x2048`; ball sets `1024x512`; rocket atlas set `1024x1024`; sky source `2048x1024`; projectile/explosion/smoke sprites `128x128`
- decision: texture import -> High uses authored mip chain, trilinear where oblique world/weapon sampling benefits, anisotropy `8`; Low uses global one-level mip reduction. VFX uses bilinear, clamp, anisotropy `0`. Existing base-map GUIDs remain stable
- decision: texture memory budget -> `96MiB` measures imported runtime-compressed mip residency from platform texture formats and mip counts, not raw source RGBA bytes; capture manifest records both compressed estimate and source-file bytes
- decision: ball geometry -> keep Unity built-in sphere, radius `2.16m`. Replace texture centers with normalized icosahedron vertices: `(0,+-1,+-phi)`, `(+-1,+-phi,0)`, `(+-phi,0,+-1)`, `phi=(1+sqrt(5))/2`; deterministic tangent orientation; U Repeat, V Clamp
- decision: ball PBR -> synthetic leather, non-metallic, low-to-moderate smoothness variation, shallow panel normal, seam AO. Twelve black pentagons remain equal-size, evenly spaced, seam-safe, pole-safe
- decision: rocket atlas -> normalized regions: body/nose `U 0..0.5,V 0..1`; hot `U 0.5..1,V 0..0.5`; fins `U 0.5..0.75,V 0.5..1`; nozzle `U 0.75..1,V 0.5..1`; `32px` gutters. Base palette: dark bronze-grey body, charcoal panels, deep-red fins, blackened nozzle, yellow-orange throat. Normal, metallic-smoothness, AO, emission maps follow same regions
- decision: rocket mesh -> exact `RocketSurface` and `RocketHot` objects; 12-sided body; pointed nose; triangular wedge fins; closed nozzle/throat; short tapered exhaust; `120-300` triangles; Blender `-Y` nose imports as Unity local `+Z`
- decision: projectile readability -> additive billboard orb plus alpha smoke. High HDR intensity feeds restrained bloom; Low LDR output remains white/yellow without bloom. No projectile Point Light dependency
- decision: smoke -> one world-space system; `1.5` particles/m, lifetime about `0.55s`, size about `0.70m`, `0.55->1.25` size multiplier, warm grey to charcoal, alpha fade, max `48`; about `40` live particles at `48m/s`
- decision: explosion -> four systems, total burst `37`, cleanup `<=1.25s`; stationary/slow yellow `FireballBody`, additive `Flash` and `Sparks`, subdued trailing `Smoke`; no physics or lights. Authored sheet and Low capture yellow:red `>=3:1`; High tone-mapped/bloom capture `>=2:1`
- decision: additive shader -> one URP unlit transparent pass, `Blend SrcAlpha One`, `ZWrite Off`, `ZTest LEqual`, `Cull Off`, one texture sample, particle color, bounded intensity property, instancing; no distortion, depth texture, lighting, or extra pass
- decision: containment -> ceiling underside `48m`; upper walls align to `x=+-64.5`, `z=+-44.5`, overlap lower walls and ceiling by `1m`; eight collider-only containment objects remain exact; goal apertures/backstops remain open and unchanged
- decision: grid -> five renderer-only inward-facing surfaces, analytic anti-aliased cyan grid, stable and non-animated, low alpha, zero texture samples, no physics, shadows, probes, or bloom-driving HDR intensity
- decision: arena-kit placement -> generate all five modules and place all five families. Add renderer-only `ArenaRampRails` at exact existing ramp transforms. Add renderer-only `ArenaWallPylon` five times per long wall at `x={-48,-24,0,24,48}`, `y=0`, `z=+-44`; north rotation identity, south rotation `(0,180,0)`. Remove stale validator rejection; require zero Collider/Rigidbody on every architecture visual
- decision: jump -> `jumpVelocity=4.80f`; measured fixed-step apex about `0.936m`, target `0.94m +-0.05m`; shared gravity, jump buffer, coyote time, forward boost, movement caps, and air control unchanged
- decision: underfoot blast -> speed blend from `BaseSpeed=10` to `SoftCap=25`; forward scale falls `0.5625->0`; upward scale rises to preserve squared impulse-scale magnitude; full-speed force adds no yaw-forward component. Side and upper-body radial impulses unchanged
- decision: validation -> no new gameplay test assembly. Existing policy uses Unity compile, builder semantic checks, deterministic builds/captures, measured play-mode scenarios, and target-machine standalone run
- assumption: reference file remains readable and hash-matched during implementation
- question: None

## Execution Graph

`START -> {T1 -> CP1 || T2 -> CP2 -> T5 -> CP5 || T3 -> CP3 || T4 -> CP4} -> JOIN1 -> T6 -> CP6 -> T7 -> CP7 -> T8 -> CP8 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` fan-out/fan-in
- gates: `START` -> clean isolated short-path worktree at exact baseline; reference hash matched; Blender `4.5.10` and Unity `6000.5.6f1` available; no Unity process or project lock
- gates: `JOIN1` -> CP1, CP3, CP4, CP5 accepted; unified textures, both FBX outputs, runtime tuning, quality configurator, and shader sources present; no Unity import occurred in parallel lanes
- gates: `FINAL` -> build 2 no-op, separate validator pass, High/Low 1080p captures accepted, manual gameplay checks recorded, target-PC performance reported
- rule: T1/T2/T3/T4 launch together from same head with disjoint paths and no Unity mutation
- rule: T5 starts only after CP2 because rocket UV audits and previews consume accepted atlas regions/textures
- rule: T6/T7 alone own shared builder, generated assets, importer metadata, project settings, lighting, and Unity processes; both remain sequential

## Tasks

### T1: Jump and underfoot-blast runtime tuning

- objective: implement named movement changes without altering unrelated movement or blast behavior
- covered_requirements: lower normal jump; speed-responsive high-speed underfoot blast
- owner: W1 implementation worker, `luna_max`, parallel lane A
- depends_on: None
- owns: `Assets/_Game/Scripts/Runtime/PlayerMotor.cs`; `Assets/_Game/Scripts/Runtime/ExplosionResolver.cs`
- protected: all other runtime scripts; gravity; fixed timestep; ball impulse; rocket flight; blast falloff/occlusion; side/upper-body radial path; builder serialization deferred to T6
- focused_reads: `PlayerMotor.FixedUpdate()`, `TryConsumeJump()`, `ExplosionResolver.ComputePlayerImpulse()`, `TryGetUnderfootFacing()`, `PlayerMotor.BaseSpeed`, `SoftCap`, `HorizontalSpeed`
- implementation: set `PlayerMotor.jumpVelocity` source default `4.80f`; change no gravity, timing, boost, acceleration, cap, or steering field
- implementation: add `[SerializeField, Range(0f,1f)] underfootHighSpeedVerticalRedirect=1f`. In underfoot branch compute `speedT=Clamp01((HorizontalSpeed-BaseSpeed)/Max(SoftCap-BaseSpeed,epsilon))`, `redirectT=speedT*redirect`, `forwardScale=underfootForwardImpulseScale*(1-redirectT)`, `scaleSqr=forwardBase^2+upBase^2`, `upScale=Sqrt(Max(scaleSqr-forwardScale^2,0))`, then `strength*(facing*forwardScale+up*upScale)`
- implementation: preserve zero-vector fallback and finite math. Expected full-strength components: speed `<=10`: forward `13.5`, up `24`; speed `17.5`: forward `6.75`, up about `26.7`; speed `>=25`: forward `0`, up `27.536`
- done when: source exposes one bounded redirect tunable; total underfoot impulse magnitude remains constant across blend; SoftCap+ force contains no yaw-forward component; unrelated runtime diff absent
- checks: worker -> static source inspection, deterministic numeric sweep over speeds `0,10,17.5,25,30`, `git diff --check`; authoritative Unity compile deferred to T6; invalidated by runtime formula/default edits
- proof: sweep records forward/up/magnitude; yaw aligned/opposite/perpendicular at `>=25m/s` all produce zero added horizontal component
- review_focus: Critical/High NaN/divide-by-zero; changed low-speed force; magnitude drift; radial-path regression; unrelated movement tuning
- review_checkpoint: CP1
- return_evidence: changed fields/formula, numeric sweep, diff check, preserved-value list, residual play-feel risk

### T2: Unified PBR, ball, rocket, VFX, and sky texture pipeline

- objective: make one deterministic generator own every new surface and VFX texture contract
- covered_requirements: detailed PBR surfaces; uniform football; rocket atlas; projectile glow; yellow explosion; smoke; sunny sky
- owner: W2 implementation worker, `luna_max`, parallel lane B
- depends_on: None
- owns: `Tools/Blender/generate_retro_textures.py`; generated PNG files under `Assets/_Game/Textures/`; preview/audit output under `Temp/BlenderPreviews/RetroTextures/`
- protected: generated PNG `.meta`; Blender model generators; FBX; shaders; builder; materials; scene; settings
- focused_reads: current texture functions, repeat-edge logic, spherical helpers, `generate_ball()`, sprite-sheet generator, audit matrix, previews, existing material UV scale and weapon slot contracts
- implementation: preserve semantic base paths/GUID targets. Generate arena families `RetroGrass`, `RetroWall`, `RetroTrim`, `RetroHazard` at `1024x1024`, each with base, `_Normal`, `_MetallicSmoothness`, `_Occlusion`; add shared `RetroDetailNormal` at `512x512`. Build coherent sports-floor, warm concrete, aged bronze, painted-hazard height/material fields; derive normals; keep tiling seams exact
- implementation: generate `RetroWeaponMetal`, `RetroWeaponDark`, `RetroWeaponAccent` families at `2048x2048`, each base plus normal, metallic-smoothness, AO; emission only for accent. Preserve existing base filenames
- implementation: replace ball centers with 12 normalized icosahedron vertices. Use deterministic nearest-neighbor tangent orientation, spherical angular distance, pentagon boundary, U endpoint duplication, uniform pole rows. Generate `RetroBall` base, normal, metallic-smoothness, AO at `1024x512`; metallic R zero; smoothness and AO carry leather/panel response
- implementation: ball audit requires 12 finite unique unit centers, near-zero centroid, antipodal symmetry, five equidistant nearest neighbors per center, separation greater than twice seam radius, black center pixels, bounded dark-area ratio, clean U seam/poles, six cardinal sphere previews
- implementation: generate rocket `RetroRocket`, `_Normal`, `_MetallicSmoothness`, `_Occlusion`, `_Emission` at `1024x1024` using atlas regions and `32px` gutters from Decisions. Require non-flat regions, bronze body above charcoal panel luminance, red-dominant fins, metallic shell/nozzle separation, hot red/orange/yellow emission, mip-safe gutters
- implementation: add `RetroRocketGlow` `128x128`: white core, pale-yellow ring, orange-red halo, transparent corners. Retain `RetroSmoke` and `RetroExplosion` `128x128`, `4x4`, `32x32` cells. Smoke frames round, clustered, soft-edged, mildly varied. Explosion frames use near-white yellow core, yellow body, gold/orange edge, irregular silhouette, no dark-red band or separated tongue cuts
- implementation: add `RetroSunnySky` `2048x1024`: pale-blue horizon, deeper zenith, sparse broad soft clouds; no baked sun disc, direct shadows, red/space/storm motifs. Sky shader owns sun direction/disc
- implementation: derive output/preview counts from collections. Add exact dimension/channel/finite/seam/alpha/map-range audits, PBR material-ball previews, atlas preview, sphere views, sprite previews, sky panorama. Two runs from same source must produce identical PNG hashes
- done when: every named file and audit exists; actual previews prove PBR response, 12-panel ball, controlled rocket atlas, smooth projectile orb, round smoke, yellow explosion, sunny sky
- checks: worker -> run generator twice; compare SHA-256 for every PNG; require audits pass; inspect previews; calculate imported compressed-memory forecast; `git diff --check`; invalidated by generator/output edits
- proof: rotated-light PBR previews show normal, metal/smoothness, AO contributions beyond base resolution; ball sphere views show 12 equal panels; explosion sheet high-alpha pixels remain yellow-majority
- review_focus: Critical/High wrong PBR channels; inverted normals; false metallic concrete/ball; broken seams/poles; atlas bleed; red explosion; nondeterminism; memory-budget miss
- review_checkpoint: CP2
- return_evidence: output manifest, repeat hashes, channel/seam audits, preview paths, memory forecast, residual visual risks

### T3: Industrial arena-kit refinement

- objective: add light-catching geometry and Quake-style industrial depth without changing gameplay collision or module placement contracts
- covered_requirements: industrial silhouette; bevel response; layered panels; UV1 lightmapping
- owner: W3 implementation worker, exact `sol_high`, parallel lane C
- depends_on: None
- owns: `Tools/Blender/generate_arena_kit.py`; `Assets/_Game/Models/ArenaKit.fbx`; `Temp/BlenderPreviews/ArenaKit/`
- protected: `Assets/_Game/Models/ArenaKit.fbx.meta`; builder placement; colliders; scene; materials; texture generator; rocket/character/weapon models
- focused_reads: `MODULE_CONTRACTS`, `CONNECTION_MAP`, five create functions, UV/export/audit paths, builder imported-bounds and material-slot validation
- implementation: retain exact five module names, bounds, pivots, forward convention, openings, snap positions, and material-slot order. Add bevels, recessed panels, ribbed trims, rails, vents, bolts, layered profiles, truss/catwalk cues. Geometry handles silhouette and edges wider than about `2cm`; textures handle microdetail
- implementation: preserve non-overlapping UV0 and add padded non-overlapping UV1 for lightmaps. Keep outward normals, manifold solids, positive volumes, applied transforms, audited part overlaps, goal opening, ramp clearance
- implementation: raise aggregate imported budget to `75,000` triangles while final scene remains within `150,000`; render six views per module family with side lighting and flat test materials
- done when: five modules remain drop-in compatible; semantic, connection, UV0/UV1, bounds, slot, and triangle audits pass; previews show clear edge highlights and recess shadows
- checks: worker -> resolve Blender `4.5.10`; run factory-startup background generator; require exit `0`, non-empty FBX, audit JSON, previews; inspect normals/pivots/scale/UV/bevels; `git diff --check`; invalidated by generator/FBX edits
- proof: side-lit preview shows geometry-driven depth while recorded contract snapshot matches baseline bounds/openings/pivots
- review_focus: Critical/High bounds/orientation drift; goal/ramp clearance change; missing/overlapping UV1; bad normals; non-manifold geometry; slot/name drift; excessive triangles
- review_checkpoint: CP3
- return_evidence: Blender command/version, semantic counts/bounds, connection results, UV proof, preview paths, residual import risk

### T4: High/Low render sources and transparent shaders

- objective: define authoritative quality configuration, sunny sky, additive VFX, and containment-grid sources before Unity integration
- covered_requirements: native 1080p High; Low fallback; HDR/post/lighting contract; projectile/explosion shader; grid shader
- owner: W4 implementation worker, `luna_max`, parallel lane D
- depends_on: None
- owns: `Assets/_Game/Editor/GraphicsQualityConfigurator.cs`; `Assets/_Game/Scripts/Runtime/GraphicsQualityRuntime.cs`; `Assets/_Game/Shaders/SunnyArenaSky.shader`; `Assets/_Game/Shaders/RetroAdditiveParticle.shader`; `Assets/_Game/Shaders/RetroPowerGrid.shader`
- protected: existing URP assets; `ProjectSettings/**`; builder; scene/materials/lighting; texture/model sources; existing shader `.meta`
- focused_reads: installed URP `17.5.0` APIs/schema, `PC_RPAsset`, `PC_Renderer`, SSAO feature, camera URP data, `QualitySettings`, builder render validator, current transparent shader conventions
- implementation: configurator creates/updates/validates exact High/Low values from Decisions through Unity serialized/editor APIs; preserves valid GUIDs; rejects unknown schema; owns no gameplay or arena layout
- implementation: runtime controller observes quality-index changes, configures attached camera/URP camera data exactly, exposes immediate `ApplyCurrentQuality()`, performs no project/asset writes, and changes no gameplay state
- implementation: sky shader samples one panorama/cloud source, adds synchronized warm sun disc, fog-compatible horizon, HDR output, no storm animation
- implementation: additive shader follows Decisions; `_Intensity` supports High bloom without making Low dependent on HDR. Grid shader uses analytic UV minor/major lines with `fwidth`, alpha blend, fog support, one pass, zero texture samples, no animation/emission spike/shadow/depth pass
- done when: source has idempotent configure and read-only validate paths; shaders match URP pass/tag/blend contracts; every High/Low field has explicit validation
- checks: worker -> static API check against installed URP package; shader/source inspection; `git diff --check`; Unity compile/import deferred to T6; invalidated by source/API edits
- proof: validator paths distinguish stale quality asset, renderer feature, camera HDR/SMAA, volume, shadow, mip, anisotropy, resolution, sky, and Low fallback fields
- review_focus: Critical/High destructive settings rewrite; GUID churn; unsupported URP fields; wrong renderer; shader blend/depth bug; HDR-only VFX; Low cannot load
- review_checkpoint: CP4
- return_evidence: source paths, exact quality matrix, API evidence, shader contracts, residual Unity-import risk

### T5: PBR rocket model and controlled UVs

- objective: rebuild projectile mesh around accepted PBR atlas contract
- covered_requirements: bronze/charcoal/red rocket; hot nozzle; stable material routing; close-up quality
- owner: W5 implementation worker, exact `sol_high`, parallel lane B continuation
- depends_on: accepted CP2 checkpoint SHA
- owns: `Tools/Blender/generate_low_poly_rocket.py`; `Assets/_Game/Models/LowPolyRocket.fbx`; `Temp/BlenderPreviews/LowPolyRocket/`
- protected: `Assets/_Game/Models/LowPolyRocket.fbx.meta`; accepted texture generator/PNG outputs; builder; prefab; materials; scene
- focused_reads: accepted atlas/map manifest and previews; current generator/export convention; builder `ValidateRocketVisualForward()` and mesh provenance checks
- implementation: retain origin, root scale contract, 12-sided body, pointed nose, Blender `-Y` -> Unity local `+Z`. Replace box fins with closed triangular wedge prisms overlapping body `>=0.005m`; add closed nozzle shell, small closed throat, `0.25-0.35` unit tapered exhaust; bounds near X/Z `+-0.72`, nose Y `-1.50`, exhaust Y no farther than `+1.25`
- implementation: export exact `RocketSurface` and `RocketHot` objects, same-named meshes, one slot/submesh and one UV0 each. Route body/nose, fins, nozzle, throat/exhaust into accepted atlas regions with half-pixel inset beyond gutters. Add valid non-overlapping UV1 only when Unity lightmapping marks projectile static; default dynamic contract requires no UV1
- implementation: audit finite vertices/UVs, zero-area triangles/UVs, zero-length edges, manifold positive-volume parts, overlap, exact objects/slots, applied transforms, bounds, triangle budget, forward convention, atlas containment
- done when: FBX passes semantic and visual audits; six views show connected fins/nozzle, correct normals, readable PBR regions, no bleed
- checks: worker -> Blender factory-startup run; exit `0`; non-empty FBX; six previews using accepted atlas/PBR lighting; semantic snapshot; `git diff --check`; invalidated by atlas or model edits
- proof: front/rear/left/right/top/three-quarter previews show distinct bronze body, charcoal panels, red fins, hot throat under rotated lighting
- review_focus: Critical/High forward/bounds drift; disconnected/non-manifold parts; UV escape/bleed; object/slot mismatch; invalid normals; triangle-budget miss
- review_checkpoint: CP5
- return_evidence: Blender version/command, object/bounds/triangle/UV audits, preview paths, residual Unity import risk

### T6: Builder integration for PBR assets, gameplay tuning, VFX, and containment

- objective: integrate accepted source outputs into authoritative prefabs, materials, scene content, importers, and semantic validation before render-environment build
- covered_requirements: ball/rocket/explosion graphics; projectile trail; named movement changes; closed visible containment; PBR material routing
- owner: W6 implementation worker, `luna_max`
- depends_on: accepted CP1 + CP3 + CP4 + CP5 checkpoint SHAs through JOIN1
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`; `Assets/_Game/Materials/`; `Assets/_Game/Prefabs/Player.prefab`; `Assets/_Game/Prefabs/Ball.prefab`; `Assets/_Game/Prefabs/Rocket.prefab`; `Assets/_Game/Prefabs/ExplosionVfx.prefab`; `Assets/_Game/Scenes/MovementLab.unity`; new source/texture/model `.meta`; generated manifest paths
- protected: High/Low URP assets and `ProjectSettings/**` until T7; runtime values outside T1; input; goals; spawn positions; ball/rocket physics; accepted generator/FBX/shader sources
- focused_reads: every builder asset-path list, source signature, importer, material factory, `BuildPlayerPrefab()`, `BuildBallPrefab()`, `BuildRocketPrefab()`, `BuildExplosionVfxPrefab()`, `BuildArena()`, containment helpers, prefab/scene validators, fingerprint/no-op logic
- implementation: replace opaque toon material factory use with URP Lit specifications and exact accepted PBR maps/channels. Preserve existing base/material GUIDs where semantics remain. Route character solid materials through Lit scalar values; route arena/weapon/ball/rocket through full map sets
- implementation: import base/emission as sRGB; normal as NormalMap; packed metallic-smoothness/AO as linear; correct max size, wrap, mip, filter, compression, aniso. Ball U Repeat/V Clamp. Rocket/PBR atlases Clamp. Repeating arena maps Repeat. VFX Clamp/bilinear/aniso `0`
- implementation: configure ArenaKit and rocket import normals/tangents for PBR: import authored normals; calculate MikkTSpace tangents when missing; validate finite normals/tangents and UV channel counts. ArenaKit requires exact UV0+UV1; dynamic rocket requires exact UV0
- implementation: serialize `jumpVelocity=4.80`, `underfootForwardImpulseScale=0.5625`, `underfootUpwardImpulseScale=1`, `underfootHighSpeedVerticalRedirect=1`; validate saved/reloaded prefab/scene fields and all protected movement/blast values
- implementation: build ball from built-in Sphere mesh; require finite UVs matching vertices, exact material identity, uniform scale, collider radius, spawn/reset, neutral tint. Validate imported ball maps and 12-panel generator provenance
- implementation: build rocket with exact `RocketSurface -> Rocket.mat`, `RocketHot -> RocketHot.mat`; reject missing/unknown/duplicate/multi-slot renderers. Add one looping/prewarmed local camera-facing `ProjectileGlow` system: lifetime `0.22s`, rate `10/s`, speed/gravity `0`, constant size `0.85m`, constant white particle color, max `2`, fixed seed; additive material intensity `2.5`. Remove Point Light and point-light validators
- implementation: retune one world-space `SmokeTrail` to `1.5` particles/m, lifetime about `0.55s`, base size about `0.70m`, size `0.55->1.25`, warm grey->charcoal, alpha fade, speed `0`, max `48`, `4x4` whole-sheet one cycle
- implementation: keep four explosion systems and total `37`: `Flash` count `1`, additive intensity `3.0`, lifetime `0.13s`, size `2.80`, speed `0`, size `0.75->1.25`, near-white yellow -> gold alpha fade; `FireballBody` count `20`, alpha, lifetime `0.40-0.56s`, size `1.35`, speed `0.65-2.10`, radius `0.06`, size keys `0.70@0,1.00@0.18,1.18@0.65,0.75@1`, near-white yellow -> bright yellow -> gold-orange, alpha `1@0,0.95@0.65,0@1`; `Sparks` count `10`, additive intensity `2.0`, lifetime `0.20-0.32s`, size `0.10`, speed `7-12`, pale yellow -> gold fade; `Smoke` count `6`, alpha, burst `0.10s`, lifetime `0.62-0.86s`, size `0.82`, speed `0.5-1.6`, size `0.55->1.40`, warm grey alpha `0.30->0`. Use deterministic seeds `0xF001..0xF004`, sorting `1,0,2,-1`; root scale `1.30`; cleanup `<=1.25s`; no physics/lights
- implementation: replace containment with `FloorContainment` center `(0,-4,0)`, size `(140,1,120)`; ceiling center `(0,48.5,0)`, size `(130,1,90)`; north/south center `(0,28,+-44.5)`, size `(130,42,1)`; west/east center `(+-64.5,28,0)`, size `(1,42,90)`; unchanged west/east backstops `(+-67,3.5,0)`, size `(1,8,38)`. Eight collider objects: non-trigger BoxCollider, `BallSurface`, default layer, no renderer/Rigidbody/script
- implementation: add `Arena/Containment/GridVisuals` with five inward-facing Quads offset `0.02m`: ceiling `(0,47.98,0)`, rotation `(90,0,0)`, size `130x90`; north `(0,28,-43.98)`, identity, size `130x40`; south `(0,28,43.98)`, rotation `(0,180,0)`; west `(-63.98,28,0)`, rotation `(0,90,0)`, size `90x40`; east `(63.98,28,0)`, rotation `(0,-90,0)`. Use three generated grid materials with `4m` cell scale, minor alpha `0.055`, major alpha `0.11`, major interval `5`; static render-only; shadows/probes off; no grid across goals
- implementation: place `RampWestRails` and `RampEastRails` from `ArenaRampRails` at primitive-ramp position/rotation exactly: `(-22,2.1,2)` / `(-15,-90,0)` and `(22,2.1,-2)` / `(-15,90,0)`. Place 10 `ArenaWallPylon` visuals at positions/rotations from Decisions. Validate every architecture visual against frozen transforms and zero physics components
- implementation: expand generated YAML/importer/source/fingerprint path sets; bump manifest schema; preserve GUIDs; semantic validator rejects stale toon opaque assignments, point light, old jump/blast fields, old ball wrap/resolution, wrong rocket routing, red/scattered explosion, old/gapped containment, missing grid, physics drift
- done when: Unity source compiles; generated ownership and validators fully describe intended prefabs/materials/containment; no render-quality setting is mutated before T7
- checks: worker -> snapshot status/GUIDs; run short-path Unity compile-only once after all source edits; require exit `0`, zero C#/shader Console errors, released process/lock; inspect source/YAML diff; `git diff --check`; invalidated by runtime/builder/shader/model/texture edits
- proof: serialized-contract inspection maps every new field/reference/material/importer/transform to one validator and preserves every excluded gameplay field
- review_focus: Critical/High serialized null/GUID churn; wrong PBR import/channel; gameplay drift; ball/rocket physics change; explosion lifetime/physics; containment goal blockage/gap; missing validator/fingerprint path
- review_checkpoint: CP6
- return_evidence: changed symbols/assets, compile command/exit/log, GUID snapshot, serialized-contract map, diff, residual integration risk

### T7: High/Low environment, lighting, deterministic build, and validation

- objective: make High/Low render configuration and baked environment authoritative, then prove persisted generated state
- covered_requirements: native 1080p High; Low fallback; sunny sky; modern lighting; probes; post; deterministic builder
- owner: W7 implementation worker, `luna_max`
- depends_on: accepted CP6 checkpoint SHA
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`; `Assets/_Game/Editor/GraphicsQualityConfigurator.cs`; `Assets/Settings/PC_RPAsset.asset`; `Assets/Settings/PC_Renderer.asset`; new Low URP assets and `.meta`; `ProjectSettings/QualitySettings.asset`; `ProjectSettings/ProjectSettings.asset`; `Assets/_Game/Lighting/`; generated sky/volume/material/scene/manifest outputs
- protected: runtime code; gameplay values beyond accepted T1; collider/trigger state beyond accepted containment; generator/FBX outputs; capture tooling
- focused_reads: accepted configurator contract; builder render-environment and no-op flow; camera setup; current directional light; renderer feature; URP volume APIs; lighting bake/probe APIs; generated fingerprint paths
- implementation: invoke configurator before material/scene validation on stale build; create High and Low assets, set High default, desktop width/height `1920x1080`, camera skybox/HDR/SMAA, existing FOV/far clip
- implementation: build sky material from `RetroSunnySky`, synchronize shader sun disc with warm mixed directional sun. Add sparse non-shadowed goal/accent lights, global Volume, mixed/baked lighting settings, light-probe volume, three box-projected reflection probes. Mark static arena renderers for batching/lightmapping/occlusion/probes; validate ArenaKit UV1 and imported bounds
- implementation: High values and Low values match Decisions exactly. Volume uses ACES, restrained bloom, mild color correction only. Set budgets: `150,000` visible triangles, `140` MeshRenderers, `180` opaque draw-equivalent bindings, `8` static transparent MeshRenderers, `96MiB` imported compressed authored mip residency
- implementation: wire `GraphicsQualityRuntime` to gameplay camera and URP camera data. Build one High post Volume active only through runtime High state; Low disables post. Validator switches High->Low->High and proves camera state, pipeline asset, mip limit, renderer features, and scene identity restore correctly
- implementation: record two triangle budgets: unique imported mesh triangles and placed-instance-weighted visible scene triangles. Enforce `75,000` ArenaKit import aggregate and `150,000` complete visible scene cap
- implementation: stale build updates lighting settings, bakes lightmaps and reflection probes once, saves outputs, then writes fingerprint. Valid build enters early no-op before importer, settings, material, scene, lighting, bake, or save writes
- implementation: extend semantic validation across quality assets, renderer/SSAO, camera, sky, sun, Volume, lights, lightmaps, probes, static flags, UV1, PBR bindings, imported memory, budgets, generated paths, manifest
- done when: build 1 completes/bakes; build 2 from unchanged source SHA emits explicit reuse marker and changes no owned hash; separate validator reopens persisted assets and succeeds; High and Low both load
- checks: worker -> confirm no Unity process/lock; hidden `Start-Process -Wait -PassThru` build 1; exit `0`; wait release; hash owned outputs; build 2 same SHA; exit `0`, reuse marker, identical hashes; wait release; separate `MovementLabBuilder.ValidateMovementLab()`; exit `0`, success marker, zero Console errors; inspect settings/YAML/GUID/diff; invalidated by any builder/config/settings/material/lighting/scene/import edit
- proof: build-2 log proves no importer/settings/material/scene/light-bake writes; separate process proves persisted non-null references, High/Low matrix, PBR maps, UV1, lightmaps/probes, containment, gameplay serialization
- review_focus: Critical/High gameplay/collider drift; wrong default quality; unsupported URP state; missing lightmap/probe; rebake/write on build 2; stale fingerprint; over-budget render state; GUID churn
- review_checkpoint: CP7
- return_evidence: exact Unity commands/version/exits/logs, build hashes/reuse marker, validator marker, quality/material/lighting contracts, residual performance risk

### T8: 1080p visual, gameplay, and performance acceptance

- objective: prove integrated overhaul visibly works at High and remains readable/playable at Low without mutating assets
- covered_requirements: 1080p evidence; ball/rocket/explosion/grid proof; PBR proof; movement/containment proof; performance fallback
- owner: W8 implementation worker, `luna_max`
- depends_on: accepted CP7 checkpoint SHA
- owns: `Assets/_Game/Editor/BrightArenaVisualCapture.cs`; `Tools/Validation/Capture-BrightArenaVisuals.ps1`; evidence under task-local `Temp/`
- protected: builder; scene; prefabs; materials; models; textures; shaders; settings; lighting; runtime scripts
- focused_reads: current capture state restoration, six view definitions, image analysis, source hashing, budget scan, wrapper process checks, accepted High/Low/VFX/containment contracts
- implementation: capture exact `1920x1080`; record quality name, HDR, render scale, SMAA, shadows, SSAO, lightmaps, reflection/light probes, texture/import memory, renderer/draw/triangle budgets, graphics device
- implementation: capture 10 identical High/Low views: first person, arena overview, north goal, south goal, ramp detail, character/ball detail, rocket showcase with deterministic smoke path, explosion at `0.08s`, explosion at `0.30s` against pale/dark split backdrop, containment grid from field. Add three High closeups: wall, trim, weapon. Add one High staged-material composite showing base-only, normal, metallic-smoothness/AO, reflection/lighting, final combined. Expected total `24` PNGs plus manifest
- implementation: temporary cameras, backdrops, rockets, effects, quality switches, and material overrides restore/destroy in `finally`; no builder/save/refresh; source/generated hashes and scene dirty state unchanged
- implementation: image gates -> valid dimensions/non-flat content; High PBR relief/roughness/reflections/shadows visible; Low readable; rocket atlas clean and orb dominant; at least six spaced smoke puffs; fireball ROI `>=500` changed pixels; High mean changed-pixel `G/R>=0.55` and yellow:red `>=2:1`; Low `G/R>=0.60` and yellow:red `>=3:1`; grid translucent and continuous; ball panels equal with clean poles/seam; sky blue with sparse clouds; team colors readable; quality-specific clipped/dark ratios bounded
- implementation: external capture cameras use Skybox clear flags. Budget scan includes MeshRenderer, SkinnedMeshRenderer, and ParticleSystemRenderer material passes. Wrapper checks exact 24 filenames, hashes, manifest source SHA/fingerprint, active quality per view, pre/post scoped hashes, process/lock release, budgets
- done when: all images open and pass visual inspection; High/Low comparison and staged proof discriminate real graphics contributions; manual gameplay scenarios pass; target-machine result reported
- checks: worker -> run graphics-enabled wrapper; inspect every PNG; compare pre/post hashes/status; run `git diff --check`; user -> native `1920x1080` standalone two-minute run across goals, ramps, jump, rocket jumps, explosions, fast turns; invalidated by capture or any rendered/runtime source change
- proof: record flat-floor apex `0.94m +-0.05m`; underfoot blasts at `<=10`, `17.5`, `>=25m/s` with yaw aligned/opposite/perpendicular; side blast unchanged; player/ball cannot escape upper walls/corners/ceiling; both goals still score center/near-post/top shots; High/Low A/B and staged composite show intended render contribution
- review_focus: Critical/High false-positive capture; hidden mutation; wrong quality/resolution; budget undercount; missing PBR bindings; dark/clipped gameplay; red/scattered explosion; containment/goal regression; unreported performance miss
- review_checkpoint: CP8
- return_evidence: manifest/hash, 24 image paths and inspection notes, manual gameplay record, High/Low settings/budgets, target-PC FPS/stutter report, pre/post hashes, residual taste/performance risk

## Execution Assignments

- workers: T1 -> W1 `luna_max`; T2 -> W2 `luna_max`; T3 -> W3 exact `sol_high`; T4 -> W4 `luna_max`; T5 -> W5 exact `sol_high` after CP2; T6 -> W6 `luna_max` after JOIN1; T7 -> W7 `luna_max` after CP6; T8 -> W8 `luna_max` after CP7
- review_checkpoints: CP1 -> fresh `sol_medium` runtime tuning review; CP2 -> fresh `sol_medium` texture/map/determinism review; CP3 -> fresh `sol_medium` arena geometry/UV/export review; CP4 -> fresh `sol_medium` URP/config/shader review; CP5 -> fresh `sol_medium` rocket geometry/UV/export review; CP6 -> fresh `sol_medium` builder/gameplay/VFX/containment serialization review; CP7 -> fresh `sol_medium` integrated generated-state/settings/lighting review; CP8 -> fresh `sol_medium` evidence-tool and acceptance review
- fixes: every Critical/High finding -> fresh `luna_max`; rerun invalidated checks; no fix re-review
- execution contract: exact `sol_high` execution orchestrator uses `$orchestrate-implementation`; each worker receives only assigned slice and accepted predecessor evidence

## Final Verification

- exact head: clean committed execution SHA descending from baseline and every accepted checkpoint; plan bytes unchanged
- reference: exact repository path readable; SHA-256 matches recorded value; implemented cue list excludes red sky and direct asset copying
- runtime: Unity compile passes; jump and underfoot formula/serialization match Decisions; protected gameplay values unchanged
- Blender: both generators exit `0`; semantic/connection/UV/bounds/slot audits pass; previews inspected; Unity imported orientation, materials, UV0/UV1, and provenance validate
- textures: two generator runs produce identical PNG hashes; dimensions/channels/seams/imports pass; PBR, ball, rocket, VFX, and sky previews pass
- Unity: build 1 exit `0`; build 2 same SHA reuses outputs before writes; owned hashes stable; separate validator exit `0`; zero compile/shader/Console errors; no process/lock remains
- visuals: 10 High plus 10 Low `1920x1080` views, three High PBR closeups, and one staged-material composite open successfully; High meets sky/PBR/lighting/VFX/grid/readability/budget criteria; Low remains readable
- gameplay: named jump/blast/containment changes pass; ball/rocket physics, input, scoring, goal/spawn geometry, camera FOV, and other runtime tuning unchanged
- quality: High default and Low fallback load same gameplay scene; switching quality never changes gameplay state; degradation order matches Decisions
- target PC: native `1920x1080` standalone run targets `60 FPS` with no persistent stutter; miss triggers recorded degradation order plus repeated validation/capture
- Git: existing GUIDs preserved; new assets have paired unique `.meta`; no unrelated reserialization; `git diff --check`; final worktree clean
- invalidation: runtime/generator/model/shader/import/material/builder/quality/light/volume/probe/scene/capture edit -> rerun affected source audit plus downstream compile, two builds, separate validation, High/Low captures, visual/gameplay proof

## Handoff

- changed paths: runtime jump/blast sources; unified texture generator and PBR/VFX/sky outputs; arena and rocket generators/FBX; quality configurator; sky/additive/grid shaders; URP High/Low assets; project quality/resolution; builder; materials; prefabs; lighting/probes; generated scene/manifest; 1080p capture tool
- residual risks: exact 60 FPS requires target-PC standalone test; baked-light cross-machine variation needs fixed Unity version; integrated GPU may need ordered SSAO/shadow/probe/bloom tuning; sky/material/VFX taste needs visual acceptance; lower jump changes traversal rhythm by design
- authority: execution orchestrator mutates and commits only plan-owned paths in isolated worktree; no external download/import; merging agent alone integrates accepted SHA; user authorizes branch integration and final taste/performance acceptance

## Done Criteria

- every requirement maps to task, owner, check, and proof
- every task passes implementation design gate
- execution graph includes every task/checkpoint once and serializes shared Unity/generated ownership
- all arena, ball, rocket, explosion, projectile, smoke, sky, and containment work uses unified High/Low graphics contract
- opaque materials use PBR path; High improvement exceeds resolution-only change; Low fallback remains functional
- ball shows 12 uniform panels; rocket uses controlled PBR atlas; projectile stays readable without Point Light/HDR dependency; explosion reads as contiguous yellow fireball
- normal jump and high-speed underfoot blast meet exact measured contracts; containment closes upper arena without blocking goals
- generators deterministic; build 2 no-op; separate validator passes; 24 captures pass; manual gameplay and target-PC checks recorded
- execution route uses immutable accepted artifact and `$orchestrate-implementation`
- final checks bind clean committed head or name exact blocker/action
