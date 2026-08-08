# Quake 2 Meets Killer7 Graphics Coding Plan

Status: proposed
Source: direct user request: ground texture, explosions, ball texture, rocket smoke, character, weapon, crosshair, FPS-visible kick; no downloaded assets
Run ID: direct
Plan ID: direct
Attempt ID: direct
Covered Requirements: direct request
Baseline: a8544c10bb77ed7a3158ff8d1f100b35a15a9266
Dependencies: None

## Objective

Add coherent low-cost retro presentation to `MovementLab`: chunky low-poly forms, low-resolution textures, hard three-band lighting, red/black/cream Killer7-like contrast, Quake-2-era effects/readability. Complete when all eight requested visuals render in FPS play, generated assets remain reproducible, current mechanics remain unchanged, builder output validates, and no third-party asset enters repository.

## Scope

- in: original procedural grass and ball textures; toon-lit surface shader; original full-body character; original FPS rocket-launcher viewmodel; dedicated FPS kick leg; successful-kick animation; center crosshair; projectile smoke; one-shot explosion; builder-owned materials/prefabs/scene/import settings; Blender and Unity validation
- in: existing flat wall, goal, marking, rocket materials moved onto same retro lighting shader for visual cohesion
- out: downloaded assets, traced/copied reference pixels, copied Quake/Killer7/Evangelion geometry or branding, audio, muzzle flash, recoil, reload, enemy/AI animation, multiplayer, new arena geometry, VFX Graph, post-processing, renderer-feature changes, gameplay tuning, collision changes, package/Unity upgrades
- protected user work: deleted `plans/basic_graphics.md`; unrelated dirty paths

## Repository Findings

- observed: `Assets/_Game/Editor/MovementLabBuilder.cs` -> sole authority for generated Player/Ball/Rocket prefabs, materials, `MovementLab.unity`, wiring, physics/build settings, and validation
- observed: baseline worktree branch `core_mechanics`; only dirty path at planning time is user deletion `plans/basic_graphics.md`
- observed: Unity `6000.5.6f1`; URP `17.5.0`; uGUI `2.5.0`; built-in Particle System module available; VFX Graph absent
- observed: `Assets/Settings/PC_RPAsset.asset` -> render scale `0.8`, HDR off, MSAA 1, depth/opaque textures off, main-light shadows unsupported, additional lights off
- observed: `MovementLabBuilder.BuildArena()` -> floor is `130 x 1 x 90 m` Unity cube with shared gameplay physics surface; markings remain separate render-only cubes
- observed: `MovementLabBuilder.BuildBallPrefab()` -> built-in UV sphere, `1.44 m` diameter, dynamic `Rigidbody`, `SphereCollider`, `BallMotor`; flat orange `Ball.mat`
- observed: `Player` hierarchy -> `Player/Head/Camera/RocketMuzzle`; no renderer, Animator, Canvas, viewmodel, crosshair, character, or weapon
- observed: `RocketLauncher.LaunchRocket()` -> camera-forward aim; logical muzzle plus forward offset; held-fire interval `0.70 s`
- observed: `RocketProjectile.TryDetonate()` -> one detonation, gameplay resolver call, immediate destroy; no smoke or VFX hook
- observed: `ExplosionResolver.ResolveExplosion()` -> central blast origin, target/occlusion/falloff/impulse/camera-shake owner; no presentation hook
- observed: `BallKick.FixedUpdate()` -> buffered attempt; `TryKickNow()` may succeed after press; `BallMotor.ApplyKick()` owns velocity; no animation signal
- observed: `PlayerCameraFeedback` -> camera local position and FOV only; aim rotation unchanged
- observed: `Tools/Blender/generate_low_poly_rocket.py` -> existing procedural FBX pattern, Blender `-Y` source nose imports as Unity local `+Z`; script lacks current Blender-skill audit/preview contract
- observed: Blender unavailable through `PATH`, found at `C:\Program Files\Blender Foundation\Blender 4.5\blender.exe`; version `4.5.10 LTS`
- observed: supplied arena references -> grass and white markings; character references -> red masked figure, eye motif, chunky armored silhouette; weapon references -> wide-muzzle angular launcher
- gap: no project-owned image textures under `Assets/_Game`
- gap: current validator checks gameplay wiring/physics but not floor/ball render textures, character/viewmodel provenance, Animator clips, UI, smoke, explosion VFX, or materials
- constraint: project forbids Computer Use; validation must use Blender background runs, Unity batch processes, logs, generated previews, and repository diffs
- constraint: one Unity process at a time; every batch run uses `Start-Process -Wait -PassThru`, records exit code, then confirms process and project-lock release
- constraint: Unity retains gameplay colliders/physics/scripts; Blender owns mesh, UV, normals, vertex colors, armatures, actions
- proposed: `Assets/_Game/Textures/` -> generated low-resolution PNGs
- proposed: `Assets/_Game/Shaders/` -> project-owned URP shaders
- proposed: `Assets/_Game/Animations/` -> builder-owned Animator controllers
- proposed: `Assets/_Game/Prefabs/ExplosionVfx.prefab` -> builder-owned one-shot effect
- proposed: `Tools/Blender/LowPolyCharacter.blend` and `Tools/Blender/FpsKickRig.blend` -> preserved rigged sources

## Decisions

- decision: references are mood/silhouette input only. Generate original red armored footballer, eye-like accent, original launcher, original texture patterns. Never embed or sample supplied image pixels.
- decision: target retro texture sizes: grass `128 x 128`; ball `256 x 128`; explosion/smoke sprites `32 x 32`. sRGB, mipmaps on, repeat for grass/ball, clamp for VFX, bilinear filter, anisotropic level `0`.
- decision: floor keeps Unity cube and collider. `Floor.mat` repeats grass every `4 m`: texture scale `(32.5, 22.5)`.
- decision: ball keeps Unity sphere/collider/rigidbody. Texture uses seam-aware orange/cream/black asymmetric eye-panel pattern for visible spin.
- decision: add `RocketFooxball/RetroToonLit` URP shader: base texture/color * vertex color, main-light direction only, three quantized diffuse bands, fixed cool shadow tint, no screen/depth/opaque texture dependency, no normal/metallic maps.
- decision: add one transparent unlit `RocketFooxball/RetroParticle` shader: texture/color, alpha blend, `ZWrite Off`, `Cull Off`; no soft-particle/depth dependency.
- decision: full-body character and camera-space kick use separate rigs. Full body provides player silhouette/body-awareness; FPS rig guarantees right boot crosses lower-center view during successful kick.
- decision: character model splits stable renderer objects `CharacterBody`, `CharacterArmor`, `CharacterHead`, `CharacterEye`. Camera hides only `CharacterHead` through builder-provisioned `LocalPlayerHidden` layer; lower body remains visible when looking down. FPS kick remains on visible layer.
- decision: world and FPS clips trigger only when `BallMotor.ApplyKick()` returns true. Buffered miss/expiry plays no contact animation. Animation never applies force or root motion.
- decision: weapon visual mounts below/right of camera. Existing logical `RocketMuzzle` stays camera-centered and authoritative; visual barrel never changes aim, spawn, collision, or projectile velocity.
- decision: crosshair uses uGUI rectangles, no sprite asset: screen-space camera Canvas, ivory/red bars with dark backing, centered anchors, no raycast target. It remains screen-centered during positional camera shake.
- decision: explosion uses built-in Particle Systems, not VFX Graph: flash/fire chunks/sparks/smoke. Smoke trail uses world-space particles and detaches/fades only on detonation; score/reset cleanup destroys trail with rocket.
- decision: ParticleSystem maximums remain POC-low: rocket trail <= `48` live particles; explosion <= `40` emitted particles; visual lifetime <= `1.25 s`.
- assumption: requested "smoke behind rocket" means projectile trail, not player movement trail.
- assumption: full-body world kick and FPS kick use same successful-kick signal; exact pose differs for camera readability.
- question: None

## Tasks

### T1: Generate original Blender visual asset pack

- objective: create reproducible textures, character, FPS leg, and weapon meeting declared scale/orientation/animation contracts
- covered_requirements: ground texture; ball texture; character model; weapon model; FPS kick source animation; no asset downloads
- owner: W1 texture worker, W2 character-model worker, W3 FPS-kick-model worker, and W4 launcher-model worker; each exact `sol_medium` Blender profile
- depends_on: None
- owns: `Tools/Blender/generate_retro_textures.py`, `Tools/Blender/generate_low_poly_character.py`, `Tools/Blender/generate_fps_kick_rig.py`, `Tools/Blender/generate_fps_rocket_launcher.py`, `Tools/Blender/LowPolyCharacter.blend`, `Tools/Blender/FpsKickRig.blend`, `Assets/_Game/Textures/RetroGrass.png`, `Assets/_Game/Textures/RetroBall.png`, `Assets/_Game/Textures/RetroExplosion.png`, `Assets/_Game/Textures/RetroSmoke.png`, `Assets/_Game/Models/LowPolyCharacter.fbx`, `Assets/_Game/Models/FpsKickRig.fbx`, `Assets/_Game/Models/FpsRocketLauncher.fbx`, paired new `.meta` files after Unity import
- protected: `Tools/Blender/generate_low_poly_rocket.py`, `Assets/_Game/Models/LowPolyRocket.fbx`, existing `.meta` GUIDs, all gameplay/source/generated Unity assets
- focused_reads: `$use-blender`; `Tools/Blender/generate_low_poly_rocket.py`; `MovementLabBuilder.BuildPlayerPrefab()`, `BuildBallPrefab()`, `BuildRocketPrefab()`; supplied `graphics references/` images; `CharacterController` and camera dimensions
- implementation: dispatch four bounded Blender workers in parallel with disjoint writable paths. W1 owns `generate_retro_textures.py` plus four PNG outputs. W2 owns `generate_low_poly_character.py`, `LowPolyCharacter.blend`, and `LowPolyCharacter.fbx`. W3 owns `generate_fps_kick_rig.py`, `FpsKickRig.blend`, and `FpsKickRig.fbx`. W4 owns `generate_fps_rocket_launcher.py` and `FpsRocketLauncher.fbx`. One model -> one dedicated worker; no worker creates or edits another model.
- implementation: each script starts `bpy.ops.wm.read_factory_settings(use_empty=True)`, derives repository root from `__file__`, uses metres, explicit names, deterministic inputs, declared outputs, selected-object export only
- implementation: `generate_retro_textures.py` writes PNGs through Blender image pixel buffers; grass uses seamless seeded green noise/blade flecks; ball wraps U seam and uses pole-safe asymmetric panels; VFX sprites use original irregular radial alpha, with no reference-image sampling
- implementation: `LowPolyCharacter` bounds target `0.75 W x 1.75 H x 0.45 D m`, origin between soles at ground, faces Blender `-Y`; `2,000-4,000` triangles, hard cap `5,000`; flat/faceted normals; separate named skinned renderer objects `CharacterBody`, `CharacterArmor`, `CharacterHead`, `CharacterEye`
- implementation: one `CharacterRig` Generic armature; stable `Root -> Pelvis`; right leg chain `Thigh.R -> Shin.R -> Foot.R`; coherent spine/head/arms/left leg; normalized weights; max four influences; no unweighted vertices
- implementation: export declared in-place actions only: `Idle` looping and `Kick` non-looping. `Kick` duration `0.30-0.38 s`, right-foot contact at `35-45%`, root/pelvis translation and rotation within `0.005 m`/`0.5 degrees`, exact idle-compatible final pose
- implementation: preserve deterministic rigged scene to `Tools/Blender/LowPolyCharacter.blend` before FBX export
- implementation: `FpsKickRig` contains original right shin/boot camera-view mesh, `500-900` triangles, one `FpsKickRig` armature, same `Idle`/`Kick` timing; idle pose below frame; Kick reaches lower-center when mounted at builder contract transform; preserve `Tools/Blender/FpsKickRig.blend`
- implementation: `FpsRocketLauncher` static model, original angular wide-muzzle silhouette, `0.32 W x 0.25 H x 0.75 L m` target, hard envelope `0.40 x 0.35 x 0.90 m`, `700-1,500` triangles, hard cap `2,000`, origin at grip/mount, faces Blender `-Y`; named mesh objects `WeaponMetal`, `WeaponDark`, `WeaponAccent`; no collider/rig/animation
- implementation: multipart generators declare connection maps before geometry; derive spans from measured endpoints; require >= `0.005 m` joint overlap; apply transforms; stable material slots/object/mesh/bone/action names
- implementation: audit finite vertices, positive face/edge areas, no loose/unintended non-manifold geometry, normals/winding, object rotation near zero, scale near one, dimensions/origin, UV layer `UVMap`, triangle budgets, connection overlaps, rig weights/actions/root motion. Raise `RuntimeError` on violation.
- implementation: render front/rear/left/right/top/three-quarter previews from evaluated final geometry to `Temp/BlenderPreviews/<asset>/`; verify six non-empty files. Print bounds, vertex/triangle counts, actions, output path.
- implementation: FBX export uses selected declared meshes/armature only, `axis_forward="-Z"`, `axis_up="Y"`, unit scale and modifiers applied; rigged files export declared actions only; static weapon uses `bake_anim=False`
- done when: four PNGs and three FBXs exist/non-empty; two `.blend` sources preserved; audits pass; previews show original cohesive silhouettes, correct joints, visible forward direction, no clipping/inverted faces; imported Unity geometry faces local `+Z`
- checks: W1-W4 each resolve Blender executable, record `4.5.10 LTS`, run only their assigned generator from repository root using background/factory-startup command, and inspect assigned preview images. After all four return, execution orchestrator closes writer barrier and performs one Unity batch import with exit `0`; evidence = per-worker logs/audit output/preview paths plus joined import log and imported asset/meta paths; invalidation = generator/source/mesh/texture edit reruns owning Blender generator/audit/previews and joined Unity import
- proof: regenerated grass tiles without seam; ball U seam/poles remain continuous and asymmetric mark rotates visibly; character neutral/foot-contact preview proves scale and kick extension; FPS rig contact preview crosses intended lower-center envelope; launcher front/rear views prove Unity-forward barrel
- review_focus: High only: copied reference content, nondeterministic/machine-specific outputs, invalid rig/root motion, wrong Unity orientation/scale, geometry defects, missing source preservation, gameplay assets modified
- review_checkpoint: RC1
- return_evidence: W1-W4 assigned generator/source/output paths; per-worker Blender version/exit; audit counts/bounds/actions; six-view preview inventory and inspection notes; joined Unity import log; residual visual risks

### T2: Add URP-safe retro shaders

- objective: provide deterministic low-cost toon surface and particle rendering without changing URP/project/package configuration
- covered_requirements: coherent Quake-2/Killer7 treatment for ground, ball, character, weapon, explosion, smoke
- owner: W5 shader implementation worker
- depends_on: RC1 accepted
- owns: `Assets/_Game/Shaders/RetroToonLit.shader`, `Assets/_Game/Shaders/RetroParticle.shader`, paired `.meta` files
- protected: `Assets/Settings/PC_RPAsset.asset`, `Assets/Settings/PC_Renderer.asset`, `Packages/`, `ProjectSettings/`, existing materials/prefabs/scene
- focused_reads: URP `17.5.0` package shader includes/templates from local `Library/PackageCache`; PC render-pipeline settings; current generated materials
- implementation: `RetroToonLit` defines `_BaseMap`, `_BaseColor`, `_ShadowColor`, `_LightSteps`; Forward pass transforms position/normal/UV, samples base map, multiplies vertex color, fetches main light, computes `saturate(dot(normalWS, light.direction))`, quantizes into clamped three bands, blends fixed shadow tint to main-light color, outputs opaque alpha; no normal/metallic/specular/screen/depth texture path
- implementation: add URP-compatible `DepthOnly` and `ShadowCaster` passes using current URP local package templates as compatibility source. Do not enable shadows, depth texture, or renderer features; passes preserve future renderer compatibility only.
- implementation: `RetroParticle` defines `_BaseMap`, `_BaseColor`; transparent queue/tags, alpha blend, `ZWrite Off`, `Cull Off`; samples sprite and vertex color; no soft-particle depth read
- implementation: preserve SRP Batcher-compatible UnityPerMaterial CBUFFER layout; avoid per-frame material instances and shader keywords
- done when: Unity imports both shaders without compile errors; target platform SubShaders/pass tags resolve under active URP; no pipeline/settings diff
- checks: W5 runs one Unity batch import/compile process through required `Start-Process -Wait -PassThru`; exit `0`; log has no `Shader error`, compile error, or fallback shader; inspect `git status` and verify no pipeline/package/project-setting mutation; invalidation = shader, URP version, renderer, or material-property contract edit
- proof: temporary material inspection or final builder material shows three discrete light bands; transparent sprite preview preserves alpha and does not require depth/opaque textures
- review_focus: High only: magenta/fallback shader, incorrect URP pass/tag/include, SRP Batcher break, opaque particle output, hidden pipeline dependency, render-setting mutation
- review_checkpoint: RC2
- return_evidence: shader paths/properties/passes; Unity exit/log excerpts; active shader names; pipeline diff proof; residual platform risk

### T3: Add explosion and rocket-smoke runtime presentation hooks

- objective: render one-shot blast and persistent projectile smoke without affecting detonation, flight, targeting, impulses, or cleanup authority
- covered_requirements: explosions; smoke behind rocket
- owner: W6 VFX runtime implementation worker
- depends_on: RC2 accepted
- owns: `Assets/_Game/Scripts/Runtime/ExplosionVfx.cs`, `Assets/_Game/Scripts/Runtime/RocketTrailVfx.cs`, `Assets/_Game/Scripts/Runtime/ExplosionResolver.cs`, `Assets/_Game/Scripts/Runtime/RocketProjectile.cs`, paired new `.meta` files
- protected: explosion falloff/occlusion/impulse loops; rocket movement/raycast/collision filters; launcher cooldown/tracking; `BallMotor`; `PlayerMotor`; input; builder/generated assets until T5
- focused_reads: `ExplosionResolver.ResolveExplosion()`; `RocketProjectile.FixedUpdate()`, `TryDetonate()`, cleanup flow; `RocketLauncher.DestroyAllProjectiles()`; match score cleanup
- implementation: add serialized `ExplosionVfx explosionVfxPrefab` to `ExplosionResolver`; at start of accepted `ResolveExplosion()` call, instantiate at exact origin and call idempotent `Play()`. Missing prefab is safe no-op; gameplay loop/order/math remain byte-for-byte equivalent apart from visual call.
- implementation: `ExplosionVfx` owns serialized ParticleSystem array; `Play()` plays each once, computes maximum configured duration/lifetime, destroys root after <= `1.25 s`; repeated `Play()` does not duplicate emission
- implementation: add serialized `RocketTrailVfx trailVfx` to `RocketProjectile`; cache from child if null. On accepted `TryDetonate()` after one-shot guard and before resolver/destroy, call `DetachAndFade()` once.
- implementation: `RocketTrailVfx.DetachAndFade()` detaches its own effect root preserving world transform, stops with `StopEmitting`, destroys after configured maximum particle lifetime plus `0.1 s`; idempotent. Ordinary score/reset `Destroy(gameObject)` without detonation does not detach, preventing orphan smoke after cleanup.
- implementation: no VFX object applies force, owns collider/Rigidbody, changes time scale, or changes aim/camera rotation
- done when: accepted detonation spawns exactly one explosion and leaves a fading smoke tail; non-detonation cleanup leaves no orphan; existing gameplay values/control flow remain unchanged
- checks: W6 Unity batch compile exit `0`; inspect serialized field defaults/null safety; final T5 builder/validator owns populated-reference proof; invalidation = resolver/projectile lifecycle or VFX API edit
- proof: call-flow trace proves one VFX spawn per `detonated` transition and no visual call before ignored collision; cleanup trace proves goal reset destroys trail with projectile
- review_focus: High only: duplicate explosion, leaked detached objects, visual exception blocks gameplay, altered blast/flight math, match cleanup leaves particles, collider/Rigidbody added to VFX
- review_checkpoint: RC3
- return_evidence: changed symbols/lines; compile log; lifecycle proof for hit/lifetime/ignored collision/goal cleanup; residual particle-lifetime risk

### T4: Add successful-kick presentation controller

- objective: trigger world and FPS kick animations exactly once from successful gameplay contact
- covered_requirements: character kick animation; kick animation visible in FPS view
- owner: W7 player-presentation runtime implementation worker
- depends_on: RC3 accepted
- owns: `Assets/_Game/Scripts/Runtime/PlayerPresentation.cs`, `Assets/_Game/Scripts/Runtime/BallKick.cs`, paired new `.meta` file
- protected: kick input consumption, `0.50 s` buffer, `0.40 s` cooldown, reach/cone/raycast gates, `BallMotor.ApplyKick()` math/ownership, player/camera transforms, match reset
- focused_reads: `BallKick.FixedUpdate()`, `TryKickNow()`, `ResetState()`; `BallMotor.ApplyKick()`; `MatchController` freeze/reset; generated Player hierarchy
- implementation: add `using System;` and `public event Action KickSucceeded` to `BallKick`; store `ball.ApplyKick(...)` result; invoke event only when result is true; return same result. No event on press, failed gate, failed apply, buffer expiry, freeze, or reset.
- implementation: new `PlayerPresentation` serializes `BallKick kick`, `Animator worldAnimator`, `Animator fpsKickAnimator`; resolves local `BallKick` fallback; subscribes in `OnEnable`, unsubscribes in `OnDisable`; cached `Animator.StringToHash("Kick")`; successful event calls `SetTrigger` on each non-null active Animator once
- implementation: presentation tolerates absent visual references during staged compile. It never reads Input System, moves player/camera, applies root motion, or calls ball/gameplay methods.
- implementation: Animators use normal scaled time. Five-second score freeze outlasts `0.38 s` clip; reset reaches Idle before gameplay re-enables, so no new MatchController contract needed.
- done when: each successful `ApplyKick` causes one world and one FPS trigger; failed/buffer-expired attempt causes zero; gameplay return values/cooldown/impulse remain unchanged
- checks: W7 Unity batch compile exit `0`; source trace covers subscribe/unsubscribe and null paths; final T5 validator checks references/controllers/root motion; invalidation = `BallKick` success timing, Animator parameter, or player-prefab wiring edit
- proof: successful buffered kick -> `ApplyKick true` -> one event -> two triggers; miss -> no event; no animation event applies physics
- review_focus: High only: duplicate subscription/trigger, animation on miss, gameplay timing/math change, memory/leak lifecycle, Animator root motion moves player
- review_checkpoint: RC4
- return_evidence: changed symbols; compile log; success/miss/reset trace; protected gameplay diff proof; residual animation-timing risk

### T5: Integrate all visuals through authoritative builder and validator

- objective: generate/import/wire every requested visual into stable Player/Ball/Rocket/Explosion prefabs and MovementLab scene
- covered_requirements: all direct request requirements
- owner: W8 Unity integration implementation worker
- depends_on: RC1, RC2, RC3, RC4 accepted
- owns: `Assets/_Game/Editor/MovementLabBuilder.cs`, `Assets/_Game/Editor/RocketFooxball.Editor.asmdef`, `ProjectSettings/TagManager.asset`, new/updated project-owned material `.mat` files, `Assets/_Game/Animations/WorldCharacter.controller`, `Assets/_Game/Animations/FpsKick.controller`, `Assets/_Game/Prefabs/ExplosionVfx.prefab`, `Assets/_Game/Prefabs/Player.prefab`, `Assets/_Game/Prefabs/Ball.prefab`, `Assets/_Game/Prefabs/Rocket.prefab`, `Assets/_Game/Scenes/MovementLab.unity`, all paired new `.meta` files
- protected: existing prefab/scene/meta GUIDs; `CharacterController`, Rigidbody/collider/physics values; runtime tuning; input actions; build scene; project/package versions; render-pipeline assets; user deletion `plans/basic_graphics.md`
- focused_reads: entire `MovementLabBuilder`; generated prefabs/materials/scene; runtime visual fields; imported FBX hierarchies/clips/avatars; shader properties; `UnityEngine.UI` assembly name from installed uGUI package
- implementation: add exact constants for texture/model/shader/controller/VFX paths; extend `GeneratedYamlAssetPaths` with every builder-created material/controller/prefab; extend `EnsureFolders()` for `Textures`, `Shaders`, `Animations`
- implementation: add `UnityEngine.UI` asmdef reference and builder namespace import; do not change packages
- implementation: before loading visual assets, configure TextureImporter only when values differ: grass/ball sRGB+mipmaps+bilinear+aniso0+repeat; VFX sRGB+mipmaps+bilinear+aniso0+clamp. `SaveAndReimport()` only changed importer.
- implementation: configure character/FPS-rig ModelImporters as Generic, scale `1`, materials not imported, declared clips only. `Idle.loopTime=true`; `Kick.loopTime=false`; bake root transform rotation/position into pose; no root motion. Configure weapon as static/no animation/material import.
- implementation: add/update deterministic Animator controllers through `UnityEditor.Animations`: default `Idle`; trigger parameter `Kick`; AnyState -> `Kick` with no self-transition and <= `0.03 s` blend; `Kick` -> `Idle` at exit time `1` with <= `0.03 s` blend. World controller uses character clips; FPS controller uses FPS-rig clips.
- implementation: replace color-only `GetOrCreateMaterial()` contract with explicit builder-owned retro material configuration. Assign `RetroToonLit` to Floor/Wall/Marking/Ball/Rocket/GoalFrame/Shield/character/weapon materials; set complete shader properties every build. Floor and Ball reference exact textures; Floor scale `(32.5,22.5)`; other base maps use white/default.
- implementation: create Explosion/Smoke materials using `RetroParticle`, exact sprite textures, complete color/alpha properties. No runtime-created material instances.
- implementation: `BuildExplosionVfxPrefab()` creates root `ExplosionVfx` plus exact one-shot systems: `Flash` burst `1`, lifetime `0.10 s`, size `2.4 m`, speed `0`; `FireChunks` burst `10`, lifetime `0.25-0.40 s`, size `0.35-0.80 m`, radial speed `3-7 m/s`; `Sparks` burst `18`, lifetime `0.18-0.35 s`, size `0.08 m`, radial speed `7-13 m/s`; `Smoke` burst `8`, lifetime `0.55-1.10 s`, size over lifetime `0.60 -> 2.20 m`, radial speed `0.5-2 m/s`. Total `37`; controller cleanup `1.25 s`; no collider/Rigidbody/audio. Wire system array into `ExplosionVfx`.
- implementation: `BuildRocketPrefab()` preserves root scale/collider/body/projectile/speed/lifetime/imported `Visual`; add child `SmokeTrail` with world simulation, rate-over-time `0`, rate-over-distance `2 particles/m`, lifetime `0.45 s`, size over lifetime `0.12 -> 0.38 m`, start speed `0`, gray alpha fade, `maxParticles=48`; add/wire `RocketTrailVfx` and `RocketProjectile.trailVfx`
- implementation: `BuildMovementLab()` builds explosion prefab before scene; wires `ExplosionResolver.explosionVfxPrefab`; keeps blast fields unchanged
- implementation: ensure project layer `LocalPlayerHidden` using existing named layer or first free user layer; never overwrite occupied layer. Assign only imported `CharacterHead` recursively. Clear that layer bit from player camera culling mask; validator resolves name dynamically, not fixed index.
- implementation: `BuildPlayerPrefab()` preserves root/controller/runtime/camera/muzzle values. Add `WorldVisual` from `LowPolyCharacter.fbx` at root origin, scale one, imported local `+Z`; assign named renderer materials; Animator/controller/avatar; `applyRootMotion=false`.
- implementation: add `Head/Camera/Viewmodels/WeaponVisual` from `FpsRocketLauncher.fbx`, local position `(0.28,-0.22,0.55)`, identity rotation, scale one; imported barrel remains local `+Z`; renderer-only, no collider/Rigidbody/gameplay script. Keep logical `RocketMuzzle` exact local `(0,-0.05,0.45)`.
- implementation: add `Head/Camera/Viewmodels/FpsKickVisual` from `FpsKickRig.fbx`, local position `(0.12,-0.42,0.30)`, identity rotation, scale one, Animator/controller/avatar, `applyRootMotion=false`; generator authors Idle below view and Kick contact at lower-center for this mount. Viewmodel hierarchy contains no collider/Rigidbody.
- implementation: add/wire `PlayerPresentation` on Player root to `BallKick`, world Animator, FPS Animator
- implementation: add `CrosshairCanvas` as `ScreenSpaceCamera`, target player camera, plane distance `0.10 m`, sorting order `100`. `CanvasScaler.ScaleWithScreenSize`, reference `1920 x 1080`, match `0.5`. Build centered Crosshair: four dark backing arms `14 x 4 px`, four ivory foreground arms `10 x 2 px`, `5 px` clear center gap, dark center backing `5 x 5 px`, red center dot `3 x 3 px`; dark `(0.05,0.03,0.04,0.85)`, ivory `(0.95,0.88,0.72,1)`, red `(0.85,0.08,0.12,1)`. All anchors/pivots center; all `Image.raycastTarget=false`; omit `GraphicRaycaster`.
- implementation: `BuildBallPrefab()` preserves primitive mesh, diameter, collider/body/motor fields; assign textured `Ball.mat`
- implementation: keep existing directional light intensity `1.1` and rotation `(45,-30,0)` unchanged; retro shader colors own palette contrast. Do not require shadow/depth/HDR.
- implementation: extend validation: required source/output assets and shaders exist; texture importer contracts; exact material shader/texture/scale; floor and ball renderer material; character/weapon/FPS mesh provenance; renderer names/materials; imported bounds/forward; expected avatar/bones/clips/loop flags; controller states/trigger/transitions; root motion off; camera culling layer; weapon/muzzle hierarchy and no physics; presentation references; crosshair Canvas/camera/anchors/colors/no raycasts; particle systems/materials/limits/simulation space; resolver/trail references; no missing components
- implementation: preserve existing rocket local `+Z` validator; add reusable imported-mesh bounds/forward/provenance helper for new assets
- implementation: replace single-file builder signature with SHA-256 over UTF-8 path name plus file bytes for a stable ordinal-sorted list containing builder source, both shaders, `ExplosionVfx.cs`, `RocketTrailVfx.cs`, `PlayerPresentation.cs`, `BallKick.cs`, `ExplosionResolver.cs`, `RocketProjectile.cs`, and all four new Blender generators. Missing listed source throws. Any listed source edit forces scene rebuild.
- done when: fresh and second build produce correct stable generated hierarchy/assets; validator proves every visual contract and all prior physics/wiring contracts; scene opens with textured field/ball, retro character/weapon/crosshair, projectile smoke, explosion, and successful FPS kick
- checks: W8 records pre-run status; confirms no Unity process/lock; runs Build via one `Start-Process -Wait -PassThru` Unity process and captures exit/log; waits for process/lock release; hashes builder-owned YAML; runs second Build in new process and proves stable hashes; waits; runs `ValidateMovementLab()` in separate process; exit `0` and marker `Rocket Fooxball Movement Lab validation succeeded`; logs contain zero Console/shader errors; inspect generated prefab/scene/material/importer diff; run `git diff --check`; remove only newly generated untracked IDE churn; invalidation = builder/runtime/shader/generator/importer/controller/prefab/scene edit reruns build twice plus validator
- proof: Play-mode acceptance at generated scene: ground texture tiles; ball texture spin reads; launcher/crosshair visible and aim remains camera-center; fired rocket leaves smoke; impact shows one explosion; successful close-range right-click shows lower-center boot kick once; miss shows no contact kick; looking down shows character body without head clipping. If agent cannot interact due Computer Use prohibition, return batch validation plus captured Blender previews and flag one short user Play check as residual UX proof, not code blocker.
- review_focus: Critical/High only: gameplay/physics drift, stale builder ownership, missing/broken GUID, wrong model orientation/scale, camera clipping/culling, crosshair off mechanical aim, VFX leaks, animation root motion, non-deterministic generated YAML, unrelated user changes
- review_checkpoint: RC5
- return_evidence: exact changed/generated paths; first/second build/validator exit and log markers; stable hash comparison; validator coverage; visual proof notes; full status/diff; residual user Play check

## Execution Graph

`START -> T1 -> {W1 || W2 || W3 || W4} -> JOIN1 -> RC1 -> T2 -> RC2 -> T3 -> RC3 -> T4 -> RC4 -> T5 -> RC5 -> FINAL`

- `JOIN1`: W1-W4 outputs and checks complete; joined Unity import passes; RC1 reviews combined Blender asset pack

## Final Verification

- exact head: clean committed implementation SHA; record full 40-character SHA; validation logs and diff must correspond to this SHA with no later edits
- checks: Blender generators exit `0`; audits and six-view previews exist/inspected; Unity first Build exit `0`; second Build exit `0` with stable builder-owned YAML hashes; separate Validate exit `0`; zero C#/shader/Console errors; no Unity process/lock remains
- checks: `git status --short`, `git diff --check`, changed-path allowlist, duplicate/missing GUID scan, generated asset provenance, package/project version comparison
- inspect: original visual identity; grass seams; ball seam/poles/spin mark; character/weapon silhouette; forward axes/bounds; named materials; viewmodel clipping; crosshair center; smoke spacing/fade; explosion count/lifetime; kick contact frame/root motion; all prior gameplay serialized values
- invalidation: any post-check edit to Blender source/output, shaders, runtime scripts, builder, import metadata, materials/controllers/prefabs/scene, asmdef, or TagManager reruns owning task checks plus first/second Build and separate Validate

## Handoff

- changed paths: T1-T5 owned paths only; existing builder-owned materials/prefabs/scene expected regenerated; exact final list comes from execution diff
- residual risks: user taste may require mount/palette/particle tuning; one short human Play check may remain because Computer Use is prohibited; current URP lacks shadows, so character depth comes from toon bands/vertex colors rather than dynamic shadows
- authority: merging agent may integrate only clean committed execution SHA after all checkpoints/final validation. User-branch merge requires explicit integration authority. Preserve `plans/basic_graphics.md` deletion and every unrelated user change.

## Done Criteria

- every requested visual maps to task, owner, check, and proof
- all art original and procedurally reproducible; no downloads/reference pixels
- every task passes implementation design gate
- every T maps to exactly one end-of-task review checkpoint; T1 W1-W4 map only to grouped RC1
- exact baseline and dependencies remain factual
- execution route uses immutable accepted artifact and `$orchestrate-implementation`
- final checks bind clean committed head or blocker names required action
