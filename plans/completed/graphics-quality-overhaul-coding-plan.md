# Graphics Quality Overhaul Coding Plan

Status: accepted
Source: direct user request
Run ID: direct
Plan ID: direct
Attempt ID: direct
Covered Requirements: 1920x1080 target; Quake II industrial reference; sunny clouded sky; modern lighting; detailed textures; normal/bump maps; metallic/smoothness and AO maps; scalable performance
Baseline: 4bb8b739a9e93f240ad0f0ea63bcd31f0464b90b
Dependencies: None

## Objective

Upgrade current bright-retro arena into high-quality Quake II-inspired industrial sports space at native 1920x1080. Preserve fast gameplay readability while adding convincing surface depth, metal response, shadowing, reflections, indirect light, sky illumination, and restrained post-processing. Completion requires deterministic source assets, builder-owned Unity integration, High and Low quality levels, 1080p evidence captures, stable generated outputs, and target-machine performance check.

## Scope

- in: main reference `graphics references/arena/qauek 2 hi res.jpg`; warm industrial metal/concrete language; repeated panels, ribs, bevels, trims, recesses, and layered vertical architecture
- in: sunny blue daytime sky, visible sun, sparse soft clouds, clean outdoor visibility, warm sunlight, cool sky fill
- in: URP Lit opaque materials; 1K tiling environment sets; selective 2K hero weapon set; normal/bump, metallic/smoothness, AO, emission, detail-normal maps; anisotropic filtering and full mip quality
- in: shadowed sun; baked indirect lighting; reflection probes; light probes; SSAO; HDR rendering; ACES tonemapping; restrained bloom; 1080p SMAA
- in: modest arena-kit geometry expansion where silhouette, bevel, panel depth, or light-catching edges need real geometry
- in: High default quality and Low fallback; visual capture, render budgets, Unity validation, target-PC playtest
- out: gameplay layout, arena collision, goal dimensions, spawn positions, physics, controls, camera FOV, scoring, or movement tuning
- out: red/space/horror sky from reference; brown-black visibility; heavy fog; excessive bloom; wet-looking concrete; mirror-like rough materials
- out: HDRP, ray tracing, realtime GI, tessellation, runtime displacement, parallax occlusion, 4K-everywhere textures, external asset packs, character redesign, weapon topology redesign

## Repository Findings

- observed: `graphics references/arena/qauek 2 hi res.jpg` -> 1920x1200 JPEG; SHA-256 `3D338708294FDFB2EDF89F132F9C917706C64E1A5050D467AA98BCA9A060A549`; currently untracked user-owned reference
- observed: reference -> strongest reusable cues: warm gold/bronze material response, repeated panel modules, strong edge highlights, deep recesses, vertical layering, catwalk forms, high surface-frequency variation
- observed: reference -> red sky excluded by request; arena sky must become sunny blue with few clouds
- observed: `plans/bright-retro-graphics-coding-plan.md` -> implemented low-cost baseline intentionally excluded PBR maps, HDR, bloom, SSAO, shadows, reflections, and baked lighting
- observed: `Assets/_Game/Shaders/RetroToonLit.shader` -> one base sample plus toon bands, ambient, rim, and emission; no normal, metallic, smoothness, AO, reflection, or received realtime-shadow path
- observed: `Tools/Blender/generate_retro_textures.py` -> most surface textures 128x128; ball 256x128; no normal, metallic/smoothness, or AO outputs
- observed: `Tools/Blender/generate_arena_kit.py` -> five named modules, one UV layer, stable material slots, faceted normals, strict audits, deterministic FBX export
- observed: `Assets/_Game/Models/ArenaKit.fbx` -> existing modular architecture integrates through exact builder mesh provenance and material-slot contracts
- observed: `Assets/_Game/Editor/MovementLabBuilder.cs` -> authoritative scene, prefab, material, importer, render-environment, build-manifest, and validator owner
- observed: current scene -> solid-color camera, no skybox, Trilight ambient, linear fog, one directional light with shadows off, no lightmap/probe contract
- observed: `Assets/Settings/PC_RPAsset.asset` -> HDR off, render scale `0.8`, MSAA 1x, main shadows off, additional lights off, shadow map 256
- observed: `Assets/Settings/PC_Renderer.asset` -> Forward+; SSAO renderer feature exists but inactive
- observed: `ProjectSettings/QualitySettings.asset` -> one `PC` level; global mip limit `3`; anisotropic filtering off; vSync on
- observed: `ProjectSettings/ProjectSettings.asset` -> desktop default 1920x1200, not requested 1920x1080
- observed: `Assets/_Game/Editor/BrightArenaVisualCapture.cs` -> six-view capture at 1280x720; caps 50,000 triangles, 80 renderers, 8 MiB authored RGBA, and 512px texture dimension
- constraint: Blender owns mesh, UV, normals, vertex color, and static module design; Unity owns colliders, gameplay roots, materials, lights, probes, scene placement, and render settings
- constraint: builder-generated change requires build twice, build-2 no-op proof, stable owned-output hashes, separate-process validation, and expected-only diff
- constraint: existing dirty character-reference deletions and untracked `plans/quake-style-rocket-vfx-coding-plan.md` remain user-owned and protected
- proposed: new PBR texture outputs under `Assets/_Game/Textures/`; preserve existing base-map paths/GUIDs where semantic surface remains same
- proposed: `Assets/_Game/Shaders/SunnyArenaSky.shader` -> project-owned daytime sky with sun disc and sparse cloud layer
- proposed: `Assets/_Game/Editor/GraphicsQualityConfigurator.cs` -> authoritative High/Low URP and project-quality configuration called by builder
- proposed: `Assets/_Game/Lighting/` -> builder-owned lightmap, reflection-probe, lighting-settings, and lighting-data outputs

## Decisions

- decision: style target -> Quake II industrial construction language, not asset copy. Use chunky modular proportions, recessed panels, ribbed trims, bolts, hazard bands, catwalk-like layers, warm aged metals, and strong shadow shapes. Preserve blue/red goal coding and bright sports readability
- decision: sky target -> clear sunny day with pale-blue horizon, deeper blue zenith, warm sun disc, and few broad soft white clouds. No red, alien, night, storm, or space sky
- decision: opaque material path -> Unity `Universal Render Pipeline/Lit`. Retain custom transparent shield and particle shaders. Retire `RetroToonLit` only from opaque world, ball, character, rocket, and weapon assignments; keep shader asset until no validator or material references remain
- decision: map contract -> `_BaseMap` sRGB; `_BumpMap` imported as NormalMap; `_MetallicGlossMap` linear with metallic in R and smoothness in A; `_OcclusionMap` linear with AO in G; `_EmissionMap` only for authored lights/signs. Normal maps provide requested bump detail; runtime displacement and parallax remain excluded
- decision: authored resolution -> 1024x1024 tiling floor/wall/trim/hazard sets; shared 512x512 detail normal; 2048x2048 weapon hero maps; 1024x512 ball maps; 2048x1024 sky/cloud source. Keep VFX sheets at existing sizes unless capture proves visible pixelation
- decision: texture filtering -> mipmaps on; bilinear or trilinear by map role; anisotropic level 8 for world/weapon maps; no global mip reduction on High; Low uses one-level mip reduction before changing render scale
- decision: geometry -> real bevels, panel steps, rail profiles, and silhouette breaks where lighting must catch edges. Micro-scratches, pitting, stamped patterns, seams, and small fasteners stay in texture maps. High visible budget: 150,000 triangles, 140 MeshRenderers, 180 opaque draw-equivalent bindings, 96 MiB authored mip residency
- decision: High quality -> default Standalone quality; 1.0 render scale; 1920x1080 default window; HDR on; SMAA; main-light 2048 soft shadows; two cascades; 80m shadow distance; Forward+ additional lights enabled with no additional-light shadows; half-resolution medium SSAO; anisotropic filtering forced on; SRP Batcher retained
- decision: Low fallback -> current 0.8 render scale; HDR off; main shadows off; SSAO off; additional lights disabled; one mip reduction; same PBR materials and gameplay scene. Low exists for recovery, not visual acceptance
- decision: lighting -> one warm mixed directional sun with realtime direct shadows and baked indirect contribution; sparse non-shadowed goal/accent lights; static arena lightmapped; dynamic player, ball, rocket, and VFX sample light probes; three baked box-projected reflection probes cover arena center and both goal recesses
- decision: post -> global Volume with ACES tonemapping, restrained bloom limited to sun/emissive accents, mild contrast and saturation correction, no depth of field, motion blur, chromatic aberration, film grain, or strong vignette
- decision: lighting bake -> builder stale path creates or updates lighting settings, bakes lightmaps and reflection probes once, saves lighting outputs, then writes output fingerprint. Build 2 must reuse baked outputs without rebake or save
- decision: performance target -> 1920x1080, 60 FPS goal on current Intel Core Ultra 5 135U integrated graphics. If target misses, reduce SSAO samples, shadow distance/resolution, accent lights, reflection resolution, then bloom. Preserve native resolution, PBR maps, and High base texture resolution until those costs are exhausted
- assumption: bright-retro implementation at baseline SHA is complete and authoritative
- assumption: user-owned reference remains readable at recorded absolute path during implementation; execution verifies hash before visual work
- question: None

## Execution Graph

`START -> {T1 -> CP1 || T2 -> CP2 || T3 -> CP3} -> JOIN1 -> T4 -> CP4 -> T5 -> CP5 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in
- gates: `START` -> clean isolated worktree at baseline; main reference readable and hash-matched; Blender 4.5.10 and Unity 6000.5.6f1 available; no Unity process or project lock
- gates: `JOIN1` -> texture outputs, arena FBX, and quality/sky source accepted; no parallel Unity import; shared `.meta`, settings, materials, scene, and builder integration deferred to T4
- gates: `FINAL` -> builder build 2 no-op; separate validator pass; 1080p captures accepted; High/Low switch validated; target-PC playtest reported
- rule: T1/T2/T3 launch together from same accepted head, own disjoint paths, and avoid Unity mutation
- rule: T4 alone owns Unity import, serialized assets, builder, scene, project settings, quality assets, and lighting bake

## Tasks

### T1: PBR texture-set pipeline

- objective: replace low-resolution base-only surfaces with deterministic PBR sets matching reference material character
- covered_requirements: detailed textures; normal/bump maps; metallic/smoothness; AO; emission; sunny sky texture source
- owner: W1 implementation worker, `luna_max`, parallel lane A
- depends_on: None
- owns: `Tools/Blender/generate_retro_textures.py`; generated files under `Assets/_Game/Textures/` except paired Unity `.meta`
- protected: Blender model generators; FBX files; shaders; builder; materials; scene; settings; gameplay code
- focused_reads: current texture contracts, seam audits, preview generation, existing material UV scales, weapon and ball UV layouts
- implementation: preserve deterministic seed and existing base-map paths; raise approved base resolutions; add exact normal, metallic-smoothness, occlusion, detail-normal, emission, and sunny-sky outputs; encode channel contract from Decisions
- implementation: author four coherent families: weathered warm wall/concrete, brushed/aged bronze trim, grippy sports floor with industrial inset panels, painted hazard metal. Add restrained wear at edges and recesses without muddy random noise
- implementation: derive normals from authored height fields; keep believable scale and moderate strength. Metallic masks separate exposed metal from paint/concrete. Smoothness varies by material and wear. AO reinforces panel seams without painting direct-light shadows into albedo
- done when: every opaque target material has required maps, exact dimensions/channels, seamless tiling where required, deterministic two-run hashes, and material-ball/tiled previews
- checks: run generator twice; compare output hashes; audit dimensions, color space intent, alpha/channel ranges, seams, and finite pixels; inspect base/normal/mask composites and sky panorama; run `git diff --check`
- proof: preview lighting rotated across generated normal/metallic maps must show relief and metal response absent from base-only preview while albedo remains readable alone
- review_focus: Critical/High wrong map channels; broken seams; inverted normals; false metallic concrete; baked lighting in albedo; nondeterministic output; excessive texture memory
- review_checkpoint: CP1
- return_evidence: output manifest, hashes, channel audit, preview paths, texture-memory estimate, residual visual risks

### T2: Industrial arena-kit refinement

- objective: add light-catching geometry and stronger Quake-style industrial structure without changing gameplay collision or arena dimensions
- covered_requirements: reference silhouette and detail; panel depth; bevel response; modern material presentation
- owner: W2 implementation worker, exact `sol_high`, parallel lane B
- depends_on: None
- owns: `Tools/Blender/generate_arena_kit.py`; `Assets/_Game/Models/ArenaKit.fbx` except paired Unity `.meta`; Blender previews/audit output under `Temp/`
- protected: gameplay colliders; builder placement; scene; materials; texture generator; render settings; character/weapon/rocket models
- focused_reads: current five module contracts, connection map, dimensions, pivots, UV/material-slot mapping, Unity imported-bounds validation, reference image
- implementation: retain exact module names, dimensions, pivots, forward convention, openings, snap positions, and four material-slot semantics. Refine goal shell, ramp rails, perimeter truss, pylon, and scoreboard with bevels, recessed panels, ribbed trims, vent/bolt forms, and layered profiles
- implementation: use geometry for silhouette and edges wider than roughly 2cm at module scale; use PBR maps for smaller detail. Replace generic cube feel while preserving low repeated-module count and static-renderer ownership
- implementation: preserve one non-overlapping UV0 per module for base materials; add lightmap UV1 with padding and no overlap; keep outward normals, manifold solids, applied transforms, and audited connections
- done when: five modules remain drop-in compatible, imported aggregate stays within 75,000 triangles, UV0/UV1 and material slots validate, and six-view previews show richer industrial depth
- checks: run Blender generator from repository root; require exit 0, semantic audit pass, connection overlap pass, FBX non-empty, six previews per asset family; inspect normals, pivots, scale, UV density, bevel consistency, and reference match
- proof: side-lit previews must show visible edge highlights and recessed shadow lines with flat test materials; collider/layout overlay remains unchanged in Unity after T4
- review_focus: Critical/High module bounds or orientation drift; goal/ramp clearance change; missing UV1; bad normals; non-manifold geometry; unstable slots/names; excessive geometry
- review_checkpoint: CP2
- return_evidence: Blender version, commands, audit counts/bounds, connection results, preview paths, output path, residual import risks

### T3: High/Low render contract and sunny sky source

- objective: define authoritative scalable URP configuration and daytime sky rendering before Unity integration
- covered_requirements: native 1080p; sunny clouds; modern shadow/SSAO/HDR/post stack; low fallback
- owner: W3 implementation worker, `luna_max`, parallel lane C
- depends_on: None
- owns: new `Assets/_Game/Shaders/SunnyArenaSky.shader`; new `Assets/_Game/Editor/GraphicsQualityConfigurator.cs`; paired source `.meta` only if already present
- protected: existing URP assets; `ProjectSettings/**`; builder; generated scene/materials/lighting; texture and Blender sources
- focused_reads: Unity 6 URP asset schema/API, existing Forward+ renderer and SSAO feature, camera URP data, QualitySettings, current builder environment validator
- implementation: create sky shader using one generated panorama/cloud source, blue horizon-to-zenith gradient, synchronized warm sun disc, and sparse soft cloud coverage. Support fog-compatible horizon and HDR output without animated storm motion
- implementation: encode High/Low values from Decisions in one editor configurator. Configurator creates/updates assets through Unity serialized APIs, preserves GUIDs, rejects unknown schema/state, and exposes read-only validation used by builder
- implementation: define volume settings, directional light, additional-light limits, SSAO settings, camera SMAA/HDR, texture quality, 1920x1080 desktop default, and fallback quality selection as exact data contracts
- done when: source compiles by inspection against installed URP APIs; configurator has idempotent create/update/validate paths and contains no gameplay or scene-layout ownership
- checks: `git diff --check`; static API/reference check against installed URP package; Unity compile/import deferred to sole T4 process
- proof: configurator validation must distinguish every High/Low field and fail on stale renderer, quality, camera, or volume state
- review_focus: Critical/High destructive settings rewrite; GUID churn; wrong renderer asset; unsupported serialized field; HDR/SMAA mismatch; sky/sun direction mismatch; Low fallback unable to load
- review_checkpoint: CP3
- return_evidence: exact owned paths, configuration matrix, source inspection result, expected generated paths, residual Unity-import risks

### T4: Builder integration, materials, and lighting bake

- objective: assemble accepted model, textures, sky, render settings, PBR materials, lighting, and probes into authoritative MovementLab outputs
- covered_requirements: complete graphics overhaul; deterministic Unity ownership; High default and Low fallback; gameplay preservation
- owner: W4 implementation worker, `luna_max`
- depends_on: CP1 + CP2 + CP3 through JOIN1
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`; `Assets/_Game/Editor/GraphicsQualityConfigurator.cs`; `Assets/Settings/PC_RPAsset.asset`; `Assets/Settings/PC_Renderer.asset`; new Low URP assets and `.meta`; `ProjectSettings/QualitySettings.asset`; `ProjectSettings/ProjectSettings.asset`; `Assets/_Game/Materials/**`; `Assets/_Game/Lighting/**`; generated scene/prefabs/manifest; importer `.meta` for T1/T2/T3 outputs
- protected: gameplay scripts and values; collider/trigger transforms; input; physics settings; character/weapon/rocket topology; user reference/deletions/untracked plan
- focused_reads: accepted map channels, arena module/UV contracts, quality configurator API, every builder importer/material/arena/environment/validator/fingerprint function
- implementation: import color, normal, mask, AO, emission, and sky maps with exact map-specific color space, normal type, mip, wrap, compression, max size, filter, and anisotropy contracts. Preserve existing base-map GUIDs
- implementation: replace opaque material creation with URP Lit material specifications. Assign physically plausible metallic/smoothness values, normal strengths, AO strengths, and restrained emission per surface. Keep team colors readable and avoid uniform gold tint
- implementation: integrate refined ArenaKit without changing renderer placement or gameplay collision. Mark static render geometry for batching, lightmapping, occlusion, and reflection-probe use; validate UV1 and imported bounds
- implementation: configure High and Low quality assets through configurator; set High default; set player desktop default 1920x1080; configure camera skybox, HDR, SMAA, and existing FOV/far clip contract
- implementation: build sunny sky material and synchronized sun. Add mixed sun, sparse accent lights, baked reflection probes, light-probe volume, global Volume, and lighting settings. Bake static indirect light and probes only on stale authoritative build
- implementation: extend builder signature/fingerprint and semantic validator across all new sources, import settings, material maps/channels, quality assets, volume, sky, lights, probes, lighting outputs, module UV1, static flags, and budgets
- implementation: build 2 must pass early no-op before importer, settings, material, scene, or lighting writes; no rebake on valid manifest/fingerprint
- done when: High scene visibly matches style target with sunny sky and PBR response; Low loads same scene safely; all gameplay ownership and serialized references remain unchanged
- checks: snapshot GUIDs/status; ensure Unity process/lock absent; run hidden Unity build 1; require exit 0 and lighting completion; wait for release; run build 2 from same source; require reuse marker and identical owned hashes; wait; run separate `ValidateMovementLab()`; require exit 0, success marker, zero compile/Console errors; inspect generated YAML, materials, lighting refs, GUIDs, and diff
- proof: controlled material test objects plus arena detail capture show base-only, normal-only, metallic/smoothness, AO, reflection, and final combined stages; gameplay collider overlay proves visual-only geometry change
- review_focus: Critical/High gameplay/collider drift; serialized nulls; lightmap/probe loss; wrong map import/channel; PBR material mismatch; build-2 rebake/write; quality-level breakage; GUID churn; unbounded shadow/post cost
- review_checkpoint: CP4
- return_evidence: generated paths; Unity commands/exits/logs; build hashes; reuse marker; validator marker; settings/material/light contracts; gameplay-diff proof; residual performance risk

### T5: 1080p visual and performance acceptance

- objective: prove overhaul improves reference-aligned lighting/material quality at native target resolution without hiding gameplay or exceeding bounded costs
- covered_requirements: 1080p target; visible PBR improvement; sunny sky; stable performance; scalable fallback
- owner: W5 implementation worker, `luna_max`
- depends_on: CP4
- owns: `Assets/_Game/Editor/BrightArenaVisualCapture.cs`; `Tools/Validation/Capture-BrightArenaVisuals.ps1`; paired existing `.meta`
- protected: builder; scene; materials; models; textures; shaders; settings; lighting outputs; gameplay scripts
- focused_reads: current six-view capture, budget accounting, source-provenance checks, accepted High/Low contracts, reference image
- implementation: raise captures to exact 1920x1080 and record active quality, HDR, render scale, antialiasing, shadow, SSAO, lightmap, reflection-probe, texture, triangle, renderer, and authored-memory evidence
- implementation: capture High and Low from identical six camera poses. Add close wall/trim/weapon crops where normal and metallic response are visible. Keep source/generated assets byte-identical before and after capture
- implementation: update caps to accepted High budgets and add checks for required PBR map bindings, normal import type, linear masks, valid lightmaps/probes, sunny-sky color/coverage, shadow presence, highlight clipping, and team-color readability
- implementation: capture one staged material proof set from T4 and include hashes/labels in manifest. Require visual inspection against reference cues, not pixel similarity
- done when: High captures show obvious relief, varied roughness/metal response, contact grounding, sun shadows, reflections, industrial depth, and sunny clouds; Low captures remain playable and readable; capture is non-mutating
- checks: run graphics-enabled wrapper; open every image; inspect missing maps, flat normals, over-metallic paint, crushed shadows, blown highlights, cloudy overcast drift, seams, incorrect goal colors, and weapon readability; compare pre/post hashes/status; run `git diff --check`
- proof: High/Low A/B plus staged-material captures discriminate real map/lighting contribution from higher resolution alone
- review_focus: Critical/High false-positive capture; hidden asset mutation; incorrect active quality; budget undercount; missing PBR bindings; dark gameplay areas; clipped HDR output; invalid source provenance
- review_checkpoint: CP5
- return_evidence: manifest/hash; all image paths and inspection notes; High/Low settings; budgets; pre/post hashes; residual target-PC frame-time risk

## Execution Assignments

- workers: T1 -> W1 `luna_max`; T2 -> W2 exact `sol_high`; T3 -> W3 `luna_max`; T4 -> W4 `luna_max` after JOIN1; T5 -> W5 `luna_max` after CP4
- review_checkpoints: CP1 -> fresh `sol_medium` review of T1 map correctness/determinism; CP2 -> fresh `sol_medium` review of T2 geometry/UV/export; CP3 -> fresh `sol_medium` review of T3 settings/sky source; CP4 -> fresh `sol_medium` integrated Unity/generated-state review after JOIN1; CP5 -> fresh `sol_medium` evidence-tool review
- fixes: every Critical/High finding -> fresh `luna_max`; rerun invalidated checks; no fix re-review

## Final Verification

- exact head: clean committed execution SHA descending from baseline and accepted checkpoint SHAs; accepted plan bytes unchanged
- source reference: exact path readable; SHA-256 matches recorded value; implementation notes map each adopted cue and excluded sky cue
- Blender: generator exit 0; semantic and connection audits pass; preview sets inspected; Unity-imported bounds, orientation, slots, UV0, and UV1 validate
- textures: generator repeat hashes match; dimensions/channels/seams/import settings validate; material previews prove normal, metallic/smoothness, AO, and emission contribution
- Unity: build 1 exit 0; build 2 same SHA reuses outputs before writes; owned hashes stable; separate validator exit 0; zero compile/Console errors; no process/lock remains
- visuals: High and Low six-view 1920x1080 capture sets plus staged-material proof open successfully; High meets sunny-sky, reference-style, lighting, PBR, readability, and budget criteria
- gameplay: collider/trigger transforms, physic materials, scoring, rocket/ball/player physics, input, spawn positions, camera FOV, and runtime tuning unchanged
- quality: High default and Low fallback both load; High values and Low degradation order match Decisions; switching quality never changes gameplay state
- target PC: user runs native 1920x1080 standalone build for two minutes across goals, ramps, rocket jumps, explosions, and fast turns; goal 60 FPS with no persistent stutter. Miss -> tune in recorded degradation order and rerun capture/validation
- Git: existing GUIDs preserved; new assets have paired unique `.meta`; no unrelated reserialization; dirty user-owned paths unchanged; `git diff --check`; final execution worktree clean
- invalidation: texture/generator/model/shader/import/material/quality/light/volume/probe/builder/scene change -> rerun affected source audit, both builds, separate validation, High/Low captures, and visual inspection

## Handoff

- changed paths: PBR texture generator and outputs; arena-kit generator/FBX; sunny-sky shader; graphics-quality configurator; PC High/Low URP assets; quality/player settings; builder; materials; lighting/probe outputs; generated scene/prefabs/manifest; 1080p capture tool
- residual risks: exact 60 FPS requires target-PC standalone test; progressive light bake time and cross-machine variation need controlled Unity version; integrated GPU may require SSAO/shadow tuning; procedural sunny sky requires visual taste check
- authority: execution orchestrator mutates and commits only plan-owned paths in isolated worktree; no external download/import; merging agent alone integrates accepted SHA; user authorizes final branch integration and target-PC taste/performance acceptance

## Done Criteria

- every requirement maps to task, owner, check, and proof
- every task passes implementation design gate
- execution graph includes every task/checkpoint once and keeps Unity/generated ownership serialized
- PBR maps and lighting create visible improvement beyond resolution change
- sunny sky replaces reference sky while industrial material/architecture cues remain recognizable
- High default targets native 1920x1080; Low fallback remains functional
- gameplay behavior and collision remain unchanged
- execution route uses immutable accepted artifact and `$orchestrate-implementation`
- final checks bind clean committed head or name exact blocker/action
