# Rocket, Movement, Ball, Explosion, and Containment Coding Plan

Status: proposed
Scope: jump tuning, speed-responsive rocket-jump direction, football layout, textured rocket, Quake-style projectile/trail, yellow fireball explosion, visible raised containment

## Objective

Deliver following combined contract:

- normal jump apex: 50% lower than current generated build
- high-speed underfoot blast: preserve horizontal momentum heading; redirect forced forward impulse upward
- ball: 12 evenly distributed black pentagons with clean spherical sampling
- rocket mesh: dark bronze-grey body, charcoal panels, deep-red fins, hot nozzle accent inspired by supplied reference
- projectile: white/yellow additive orb, orange-red halo, spaced grey smoke puffs
- explosion: contiguous visible yellow fireball, not separated red particles
- containment: higher ceiling plus four upper walls; player and ball cannot leave arena; all five surfaces use barely visible cyan power grid

Preserve low-cost PC pipeline: HDR off, bloom off, additional lights off, render scale `0.8`, main-light shadows off, no post-process dependency.

## Decisions

- “50% shorter jump” -> 50% lower measured apex displacement, not 50% lower velocity or airtime.
- “walls so ball and player can go beyond” -> interpreted as “cannot go beyond.”
- normal jump change affects `jumpVelocity` only. Shared gravity remains unchanged.
- high-speed blast redirection preserves total underfoot impulse magnitude.
- football fix retains Unity built-in sphere. Texture layout and importer wrap are faults; custom ball mesh unnecessary.
- rocket reference provides palette and shape cues only. No pixel copying, tracing, or direct texture reuse.
- projectile/explosion brightness comes from additive sprites and yellow alpha-blended body. Realtime lights remain unused.
- containment grid uses analytic zero-texture shader. Stable, non-animated, barely visible.
- playable ceiling underside: `48m`. New blast redirection yields about `32m` ideal pure-blast apex and up to about `44m` when normal jump stacks with full redirected blast; `48m` retains useful clearance.
- no new gameplay test assembly. Existing repository policy defers test creation; builder validation plus measured play-mode checks cover change.

## Current Failures

- generated Player prefab uses `jumpVelocity = 6.75`; current 60Hz simulation reaches about `1.873m` apex.
- `ExplosionResolver.ComputePlayerImpulse()` always adds player-yaw forward component: `strength * (facing * 0.5625 + up * 1.0)`.
- `generate_ball()` creates 16 uneven pentagon centres in latitude bands, not 12 icosahedral centres.
- ball importer repeats V axis; polar sampling requires clamp.
- `LowPolyRocket.fbx` has one renderer, no material slots, overlapping primitive UVs, and one red emissive material using `RetroTrim.png`.
- `GlowLight` is Point Light while active URP disables additional lights. Point Light cannot render visible orb.
- `RetroParticle.shader` uses alpha blending; current projectile cannot create white-hot LDR glow.
- smoke begins near `0.05m` after size curve and requests about `132` live particles against cap `48`.
- explosion texture already contains yellow, but red material tint and red particle gradient multiply it toward red. `FireChunks` speed `3-7m/s` breaks fireball into separate particles.
- hidden ceiling underside sits near `13.5m`; outer containment sits well beyond visible arena and ends below raised-ceiling target.

## Protected Contracts

- `GamePhysicsSettings.GravityMagnitude = 11.8125m/s²`
- fixed timestep `1/60s`
- jump buffer `0.10s`, coyote time `0.08s`, horizontal jump boost, movement caps, air control
- rocket speed `48m/s`, lifetime `8s`, collider, Rigidbody, damage
- blast radius `5.85`, player strength `24`, ball strength `16`, falloff, occlusion, stacking, camera feedback
- ball radius `2.16m`, mass, physics material, spawn/reset position, kick behavior
- goal planes, openings, triggers, shields, recesses, scoring
- visible floor, lower walls, ramps, architecture, spawn points
- active URP, renderer, quality, fog, and volume settings

## Owned Files

- runtime: `Assets/_Game/Scripts/Runtime/PlayerMotor.cs`, `Assets/_Game/Scripts/Runtime/ExplosionResolver.cs`
- generators: `Tools/Blender/generate_retro_textures.py`, `Tools/Blender/generate_low_poly_rocket.py`
- generated model: `Assets/_Game/Models/LowPolyRocket.fbx`
- textures: `RetroBall.png`, `RetroExplosion.png`, `RetroSmoke.png`, new `RetroRocket.png`, new `RetroRocketGlow.png`
- shaders: new `Assets/_Game/Shaders/RetroAdditiveParticle.shader`, new `Assets/_Game/Shaders/RetroPowerGrid.shader`
- authority: `Assets/_Game/Editor/MovementLabBuilder.cs`
- generated outputs: Player/Rocket/Explosion prefabs, MovementLab scene, affected materials/importers, generated manifest
- capture: `Assets/_Game/Editor/BrightArenaVisualCapture.cs`, `Tools/Validation/Capture-BrightArenaVisuals.ps1`

Preserve existing `.meta` GUIDs for modified assets. Add paired `.meta` files for every new asset.

## Implementation Assignment

- Step 5 rocket mesh/UV design owner profile: exact `sol_high`
- remaining steps: one implementation pass; no orchestration skill required
- Step 5 owns only rocket generator, generated FBX, rocket previews; builder remains single-owner integration path

## Implementation

### 1. Halve normal-jump apex

Current integration applies gravity before `CharacterController.Move()`. Use measured fixed-step value, not continuous approximation:

- current `6.75m/s` -> apex about `1.872656m`
- target apex -> about `0.936328m`
- new `jumpVelocity = 4.80f` -> apex about `0.935625m`, 50.04% reduction
- expected airtime -> about `0.80s`; airtime reduction about 29%, not 50%

Edits:

- `PlayerMotor.jumpVelocity` field default: `4.50f -> 4.80f`
- builder Player prefab assignment: `6.75f -> 4.80f`
- builder persisted-prefab validator: expect `4.80f`
- rebuild Player prefab through builder; never edit prefab YAML manually

Do not halve velocity. `3.375m/s` would cut height by about 75%. Do not change gravity; shared gravity affects ball and every Rigidbody.

Completion:

- flat-floor jump root apex: `0.94m ±0.05m`
- airtime: about `0.80s`, fixed-step tolerance
- scene Player remains prefab-backed with no jump override
- held jump, fresh press, coyote time, buffer, and horizontal boost remain unchanged

Risks requiring playtest: large-ball clearance, ramp clearance, faster bunny-hop cadence, reduced air distance, jump animation timing.

### 2. Redirect high-speed underfoot blast upward

Modify `ExplosionResolver.ComputePlayerImpulse()` underfoot branch only.

Add tunable:

```csharp
[SerializeField, Range(0f, 1f)]
private float underfootHighSpeedVerticalRedirect = 1f;
```

Use player horizontal speed between `BaseSpeed` and `SoftCap`:

```csharp
speedT = Clamp01(
    (target.HorizontalSpeed - target.BaseSpeed) /
    Max(target.SoftCap - target.BaseSpeed, epsilon));

redirectT = speedT * underfootHighSpeedVerticalRedirect;
forwardScale = underfootForwardImpulseScale * (1f - redirectT);

impulseScaleSqr =
    underfootForwardImpulseScale * underfootForwardImpulseScale +
    underfootUpwardImpulseScale * underfootUpwardImpulseScale;

upwardScale = Sqrt(Max(
    impulseScaleSqr - forwardScale * forwardScale,
    0f));

impulse = strength *
    (facing * forwardScale + Vector3.up * upwardScale);
```

Starting contract with clear full-strength blast:

- speed `<=10m/s`: forward `13.5`, upward `24`
- speed `17.5m/s`: forward `6.75`, upward about `26.7`
- speed `>=25m/s`: forward `0`, upward `27.536`
- total impulse magnitude: `27.536` throughout

Builder:

- write `underfootHighSpeedVerticalRedirect = 1f`
- promote forward scale, upward scale, and redirect to shared builder constants
- validate all three fields after scene reload
- `ExplosionResolver.cs` already belongs to builder source signature; retain that coverage

Preserve:

- low-speed rocket-jump behavior
- side/upper-body radial impulses
- existing player momentum and heading
- additive multi-blast behavior
- falloff, occlusion, hard cap, air steering

Completion: at `>=25m/s`, yaw aligned/opposite/perpendicular to velocity produces no new yaw-forward velocity; horizontal heading remains player-earned.

### 3. Make football layout uniform

Replace latitude-band centres in `generate_ball()` with normalized icosahedron vertices:

- `(0, ±1, ±φ)`
- `(±1, ±φ, 0)`
- `(±φ, 0, ±1)`
- `φ = (1 + sqrt(5)) / 2`

Result: 12 centres; each centre has five nearest neighbours at `63.434949°`.

Generator changes:

- add `build_ball_panel_centres()`
- keep spherical angular-distance evaluation, `regular_polygon_boundary()`, `256×128`, exact U endpoint duplication, uniform pole rows
- derive each pentagon orientation from deterministic nearest-neighbour tangent; remove random orientation
- retain `0.27rad` panel radius only after separation audit passes
- add `audit_ball_layout()`:
  - exact 12 unique finite unit centres
  - near-zero centroid
  - antipodal symmetry
  - exact five common-distance neighbours per centre
  - nearest separation greater than twice seam radius
  - every centre pixel black
  - bounded total dark-area ratio
- add six-cardinal-view orthographic sphere preview; retain flat repeat/seam preview

Builder importer changes:

- ball U wrap: Repeat
- ball V wrap: Clamp
- exact imported dimensions: `256×128`
- retain sRGB, mipmaps, bilinear, anisotropy `0`, max size `256`, ignore global mip limit
- other repeating textures retain repeat on both axes

Ball prefab validator:

- require built-in Sphere MeshFilter/shared mesh
- require non-empty finite UVs matching vertex count
- retain uniform scale, collider radius, material, neutral tint, zero emission checks

Completion: actual Unity sphere shows 12 equal-size/equal-spacing pentagons with no polar smear, seam stripe, merged panels, or oval scaling.

### 4. Generate rocket texture atlas

Add deterministic opaque `128×128` `RetroRocket.png`. Reference cues: dark bronze-grey shell, charcoal inset panels, deep-red fins, black nozzle, yellow-orange hot accent.

Atlas regions with four-pixel gutters:

- body/nose: `x=0..63, y=0..127`; usable `4..59,4..123`
- hot: `x=64..127, y=0..63`; usable `68..123,4..59`
- fins: `x=64..95, y=64..127`; usable `68..91,68..123`
- nozzle shell: `x=96..127, y=64..127`; usable `100..123,68..123`

Palette:

- body: `#171411 -> #69533B`
- panels: `#090B0D -> #252729`
- fins: `#250306 -> #8F1718`
- nozzle: blackened bronze/graphite
- hot: dark red -> orange -> pale yellow; near-white limited to small throat/tip pixels

Texture design:

- broad charcoal longitudinal panels separated by bronze ribs
- two restrained cross seams
- chunky `2-4px` features
- deterministic quantized wear; no photographic grain
- no alpha, normal map, metallic map, or emission map

Audit:

- exact dimensions and opaque alpha
- every atlas region non-flat
- body luminance greater than panel luminance
- fin red channel dominant
- hot region includes red/orange/yellow ranges
- gutters prevent mip bleeding
- add atlas preview

Completion: two generator runs produce byte-identical atlas; region audits and atlas preview pass.

### 5. Rebuild low-poly rocket visual for controlled UVs

Modify `generate_low_poly_rocket.py`:

- retain 12-sided body, pointed nose, origin, root scale, Blender `-Y` nose -> Unity local `+Z`
- replace four box fins with closed triangular wedge prisms; overlap body by `>=0.005m`
- add closed nozzle shell
- add small closed throat and `0.25-0.35` Blender-unit tapered exhaust cone
- keep combined bounds near X/Z `±0.72`, nose Y `-1.50`, exhaust Y no farther than `+1.25`
- target `120-300` triangles

UVs:

- unwrap each part before join
- body/nose -> body region
- fins -> fin region
- nozzle -> nozzle region
- throat/exhaust -> hot region
- half-pixel inset inside usable atlas rectangles
- reject UV outside assigned rectangle and zero-area UV triangles

Export exact two objects:

- `RocketSurface`: body, nose, fins, nozzle; one `RocketSurface` slot
- `RocketHot`: throat and short exhaust; one `RocketHot` slot

Audits:

- finite vertices/UVs
- no zero-length edges or zero-area faces
- closed parts manifold with positive volume
- exact object names, mesh names, one slot/submesh each, one UV map
- rotation zero, scale one
- connection overlaps, bounds, triangle budget, forward convention
- render front/rear/left/right/top/three-quarter previews with generated atlas

Preserve `LowPolyRocket.fbx.meta` GUID.

Completion: exact two-object FBX passes geometry/UV/material audits; every preview shows connected fins/nozzle, correct normals, readable panels, no atlas bleed.

### 6. Add projectile and smoke textures

Add deterministic `RetroRocketGlow.png`, `64×64` or `128×128`:

- white center
- pale-yellow inner ring
- orange/red halo
- fully transparent corners
- smooth edge without visible square

Update `RetroSmoke.png` while retaining `128×128`, `4×4`, `32×32` cells:

- round cloudy clustered silhouettes
- mild frame variation
- no radial-star shape
- soft alpha edge
- no frame-to-frame silhouette popping

Extend generator contracts, previews, alpha audits, and deterministic hash coverage. Expected generator total after additions: 13 textures. Derive printed preview/output counts from actual collections rather than hardcoded literals.

Completion: two generator runs produce byte-identical glow/smoke outputs; glow corners remain transparent; all smoke frames remain round and distinct.

### 7. Add low-cost additive shader

Create `RetroAdditiveParticle.shader` from existing particle shader contract:

- shader name `RocketFooxball/RetroAdditiveParticle`
- URP transparent queue, one unlit pass
- `Blend SrcAlpha One`
- `ZWrite Off`
- `ZTest LEqual`
- `Cull Off`
- one texture sample multiplied by material and particle color
- target `2.0`, instancing
- no lighting, HDR, bloom, noise, distortion, depth texture, or extra pass

Use same shader for projectile glow plus explosion Flash/Sparks. Additive LDR output saturates toward white; normal depth testing preserves world occlusion.

Completion: Unity imports shader with zero shader errors; ProjectileGlow and ExplosionAdditive materials resolve exact shader after reload.

### 8. Build textured glowing projectile

Builder materials:

- preserve `Rocket.mat` GUID
  - shader: `RetroToonLit`
  - texture: `RetroRocket.png`
  - neutral white base tint
  - zero emission
- add `RocketHot.mat`
  - same atlas
  - neutral base tint
  - orange-red emission strength `0.25-0.40`
- add `ProjectileGlow.mat`
  - shader: `RetroAdditiveParticle`
  - texture: `RetroRocketGlow.png`
  - neutral white tint

`BuildRocketPrefab()`:

- accept surface/hot/glow materials
- route exact renderer names:
  - `RocketSurface` -> `Rocket.mat`
  - `RocketHot` -> `RocketHot.mat`
- reject unknown, duplicate, missing, or multi-slot renderer
- remove `GlowLight` and every Point Light constant/validator
- add `ProjectileGlow` child near rocket visual center
- one local-space, camera-facing billboard ParticleSystem
- stationary particles, zero gravity, no shape emission
- stable overlapping refresh so orb never disappears between lifetimes
- size `0.7-1.0m`; orb wider than rocket mesh
- cap `2` particles; second glow system permitted only if one sprite cannot preserve white core, total glow cap then `4`
- retain root scale `0.24`, body, collider, Rigidbody, speed, lifetime, trail reference

Completion: bronze-grey rocket, dark panels, red fins, and hot nozzle remain visible close-up while white/yellow orb dominates in flight.

### 9. Retune Quake-style smoke trail

Keep one world-space `SmokeTrail` system:

- `rateOverDistance = 1.5` particles/metre
- lifetime about `0.55s`
- base size about `0.70m`
- size multiplier about `0.55 -> 1.25`
- warm medium grey -> charcoal-grey
- alpha about `0.75 -> 0`
- start speed `0`
- max particles `48`
- billboard, alpha-blended `Smoke.mat`
- texture-sheet animation `4×4`, whole sheet, one cycle

At `48m/s`, requested live count becomes about `40`, below cap. Completion: separated expanding puffs, not ribbon, starbursts, or tiny specks.

### 10. Build visible yellow fireball explosion

Retain four systems and total burst count `37`. Rename `FireChunks` -> `FireballBody`.

`Flash`:

- additive material
- count `1`, lifetime `0.13s`, size `2.80`, speed `0`
- size `0.75 -> 1.25`
- color `(1,1,0.78) -> (1,0.78,0.12)`, alpha `1 -> 0`
- sorting order `1`, seed `0xF001`

`FireballBody`:

- alpha material, count `20`
- lifetime `0.40-0.56s`, size `1.35`, speed `0.65-2.10m/s`
- sphere emission radius `0.06`
- size keys: `0.70@0`, `1.00@0.18`, `1.18@0.65`, `0.75@1`
- colors: near-white yellow -> bright yellow -> gold-orange
- alpha `1@0`, `0.95@0.65`, `0@1`
- sorting order `0`, seed `0xF002`

`Sparks`:

- additive material, count `10`
- lifetime `0.20-0.32s`, size `0.10`, speed `7-12m/s`
- pale yellow -> gold, alpha `1 -> 0`
- sorting order `2`, seed `0xF003`

`Smoke`:

- alpha material, count `6`, burst time `0.10s`
- lifetime `0.62-0.86s`, size `0.82`, speed `0.5-1.6m/s`
- size `0.55 -> 1.40`, alpha `0.30 -> 0`
- sorting order `-1`, seed `0xF004`

Texture:

- retain `RetroExplosion.png` path/GUID, `128×128`, `4×4`
- core `(1.00,0.99,0.82)`
- yellow `(1.00,0.88,0.16)`
- gold `(1.00,0.62,0.04)`
- orange edge `(1.00,0.32,0.015)`
- remove dark red band and tongue alpha cuts
- retain irregular outer edge
- audit opaque bright centers, transparent corners, and majority-green/yellow high-alpha pixels

Materials:

- preserve `Explosion.mat` GUID; alpha shader, neutral white tint, FireballBody only
- add `ExplosionAdditive.mat`; additive shader, neutral white tint, Flash/Sparks
- retain `Smoke.mat` for Smoke

Preserve root visual scale `1.30`, cleanup `<=1.25s`, gameplay blast settings, and `ExplosionVfx` runtime behavior.

Completion: `0.30s` frame reads as one yellow ball of fire on pale and dark backgrounds; yellow pixels outnumber red pixels by at least `3:1`.

### 11. Raise and close arena containment

Interpret visible field bounds from existing geometry:

- X boundary: `±64.5m`
- Z boundary: `±44.5m`
- visible lower wall top: `8m`
- new ceiling underside: `48m`

Keep visible lower-wall colliders and goal contracts unchanged. Replace oversized upper containment with tight collider-only surfaces:

- `CeilingContainment`: center `(0,48.5,0)`, size `(130,1,90)` -> underside `48m`
- `NorthContainment`: center `(0,28,-44.5)`, size `(130,42,1)` -> Y `7..49m`
- `SouthContainment`: center `(0,28,44.5)`, size `(130,42,1)`
- `WestContainment`: center `(-64.5,28,0)`, size `(1,42,90)`
- `EastContainment`: center `(64.5,28,0)`, size `(1,42,90)`
- retain `FloorContainment`
- retain West/East goal backstops at `x=±67`, center `y=3.5`, size `(1,8,38)`

Upper walls overlap lower walls by `1m` and ceiling collider by `1m`. Corner boxes overlap by thickness. No gap permits diagonal escape.

All containment colliders:

- non-trigger BoxCollider
- shared `BallSurface`
- default collision layer
- no renderer, Rigidbody, or runtime script

Do not place containment across goal aperture below `8m`. Goal shields keep blocking player/rockets; ball crosses scoring plane at `x=±64` and reaches existing backstop at `x=±67`.

Completion: exact eight collider-only containment objects form closed overlapping volume; goal aperture/backstop path remains unchanged.

### 12. Add barely visible power-grid surfaces

Create `RetroPowerGrid.shader`:

- shader name `RocketFooxball/RetroPowerGrid`
- analytic UV grid; zero texture samples
- URP unlit transparent, one pass, target `2.0`
- `Blend SrcAlpha OneMinusSrcAlpha`
- `ZWrite Off`, `ZTest LEqual`, `Cull Back`
- `fwidth` anti-aliased minor/major lines
- optional standard URP fog application
- no animation, emission, shadow/depth pass, or post-process dependency

Material values:

- color `(0.12,0.50,0.72,1)`
- minor alpha `0.055`
- major alpha `0.11`
- minor width `0.025` cell units
- major width `0.045`
- major interval every `5` cells
- cell size `4m`

Create three generated materials using same shader:

- `ContainmentGridCeiling.mat`: `_GridScale = (32.5,22.5)`
- `ContainmentGridLongWall.mat`: `_GridScale = (32.5,10)`
- `ContainmentGridEndWall.mat`: `_GridScale = (22.5,10)`

Create `Arena/Containment/GridVisuals` with five renderer-only Quads, offset `0.02m` inside colliders:

- `CeilingGrid`: `(0,47.98,0)`, rotation `(90,0,0)`, scale `(130,90,1)`
- `NorthUpperGrid`: `(0,28,-43.98)`, rotation identity, scale `(130,40,1)`
- `SouthUpperGrid`: `(0,28,43.98)`, rotation `(0,180,0)`, scale `(130,40,1)`
- `WestUpperGrid`: `(-63.98,28,0)`, rotation `(0,90,0)`, scale `(90,40,1)`
- `EastUpperGrid`: `(63.98,28,0)`, rotation `(0,-90,0)`, scale `(90,40,1)`

Visual settings:

- static
- no Collider/Rigidbody/runtime script
- shadow casting off, receive shadows false
- light/reflection probes off
- no grid across goal apertures or recesses

Budget after five surfaces: expected scene MeshRenderers `56`, transparent renderers `7`, added triangles `10`, texture bytes `0`; caps remain `80`, `8`, `50,000`, `8MiB`.

Completion: exact five grid renderers cover ceiling/upper walls, remain inside budgets, and have no physics components.

### 13. Extend builder authority and validators

Builder path/material contracts:

- add rocket/glow textures, additive/grid shaders, RocketHot/ProjectileGlow/ExplosionAdditive/three containment materials
- add every generated YAML material to `GeneratedYamlAssetPaths`
- add new texture importer metadata to `GeneratedImporterMetadataPaths`
- add new textures, model output, generator scripts, and shaders to source signature/fingerprint inputs
- bump `ManifestSchemaVersion` because fingerprint path contract expands
- require every source asset before build
- preserve valid asset GUIDs and material assets; no delete/recreate churn

Texture importer contracts:

- Ball: U Repeat, V Clamp, exact `256×128`
- Rocket atlas: Clamp, exact `128×128`, opaque sRGB
- Rocket glow, explosion, smoke: Clamp, exact authored sizes, sRGB
- all: mipmaps on, bilinear, anisotropy `0`, authored max size, no platform upsize, ignore global mip limit

Rocket validator:

- exact two imported renderers, mesh provenance, object names, one material/submesh each
- finite UVs inside assigned atlas regions
- exact material asset identities and emission contracts
- exact ProjectileGlow system and additive shader
- zero Light components
- exact one world-space smoke system and tuning
- unchanged projectile physics/gameplay serialization after prefab reload

Explosion validator:

- exact system names/counts/tunings/materials/sorting/seeds
- exact `4×4` whole-sheet animation
- exact serialized `ExplosionVfx.particleSystems` identities
- total burst `37`, cleanup `<=1.25s`
- no Collider, Rigidbody, or Light

Containment validator:

- exact eight direct containment colliders by name, position, size, material, non-trigger state
- no renderer on collider objects
- exact five grid children/transforms/materials
- no physics/runtime components on grid visuals
- exact shader properties, queue/tag, shadow/probe state
- transparent renderer total `<=8`

Movement validator:

- exact `jumpVelocity = 4.80f` on saved/reloaded Player prefab
- exact underfoot forward/up/redirect fields in saved/reloaded scene
- protected movement and blast values unchanged

Completion: semantic validator rejects old Point Light, old jump value, yaw-forced high-speed blast, uneven ball importer, untextured rocket, red explosion, low/gapped containment, missing grid, or gameplay drift.

### 14. Extend visual capture

Retain six existing images. Add four transient non-saving views:

- `RocketShowcase`: side/rear three-quarter textured projectile with deterministic smoke path
- `ExplosionIgnition`: seeded explosion at `0.08s`
- `ExplosionFireball`: seeded explosion at `0.30s` against split pale/dark backdrop
- `ContainmentGridFromField`: camera `(0,2,-24)`, target near `(0,35,0)`, FOV about `65`; show ceiling plus upper boundaries

Capture requirements:

- temporary objects/cameras/backdrops destroyed and state restored in `finally`
- no builder/save/refresh calls; source/generated hashes unchanged
- wrapper expected image count: `6 -> 10`
- manifest includes image hashes, source SHA, build fingerprint, budgets
- rocket frame: visible bronze body/red fins inside stronger orb; at least six separated smoke puffs; no atlas bleeding
- explosion frames: effect ROI `>=500` changed pixels; mean changed-pixel `G/R >=0.60`; yellow:red pixel count at least `3:1`
- containment frame: grid covers ceiling/walls, remains translucent, no opaque sky veil, missing seam, or pink shader
- existing ball detail: uniform panel size/spacing, clean poles/seam

Completion: capture wrapper emits 10 valid PNGs plus passed manifest while source/generated hashes and scene dirty state remain unchanged.

## Verification Sequence

1. Snapshot Git status and GUIDs. Preserve unrelated `graphics references/` changes.
2. Run texture generator twice -> all 13 PNG hashes identical; audits/previews pass.
3. Run rocket generator -> semantic audits pass; inspect six rocket previews.
4. Run Unity compile from short worktree path -> zero C#/shader Console errors.
5. Run `MovementLabBuilder.BuildMovementLab()`.
6. Run same build from unchanged source SHA -> explicit reuse marker; owned scene/prefab/material/importer/manifest hashes identical.
7. Run `MovementLabBuilder.ValidateMovementLab()` in separate Unity process -> explicit success marker.
8. Run graphics-enabled bright-arena capture -> 10 valid images and passed budget manifest.
9. Inspect ball, rocket, explosion, ceiling, wall, goal, and overview images.
10. Inspect generated YAML references/GUID provenance and expected-only Git diff.
11. Run `git diff --check`.

## Manual Acceptance

- flat-floor normal jump: apex near `0.94m`; responsive buffer/coyote behavior
- normal jump: verify intended ball/ramp clearance and bunny-hop rhythm
- underfoot blast at `<=10`, `17.5`, and `>=25m/s`; test yaw aligned/opposite/perpendicular to velocity
- high-speed blast: horizontal heading preserved; launch becomes primarily vertical
- side/upper-body blast: radial response unchanged
- projectile: orb readable against pale and dark areas; smoke separated and visible
- rocket close-up: bronze-grey shell, charcoal panels, deep-red fins, hot nozzle; glow remains dominant in flight
- explosion: contiguous yellow fireball, not red dots
- containment: rocket-jump into every upper wall/ceiling; kick/blast ball into upper corners; player/ball never enter exterior lanes
- scoring: both goals, center/near-post/top shots; no containment grid across aperture
- grid: barely visible from field, continuous at corners, no distracting cyan veil

## Done Criteria

- every objective above passes semantic and visual validation
- normal jump measured apex reduced by 50% within tolerance
- high-speed underfoot blast adds no forced yaw-forward component at SoftCap+
- ball uses 12 uniform spherical pentagon centres and U-repeat/V-clamp sampling
- rocket uses controlled atlas UVs and exact surface/hot material routing
- projectile uses additive orb, no Point Light, one smoke system, `<=48` smoke particles, `<=4` glow particles
- explosion uses visible yellow FireballBody plus additive flash/sparks; four systems, 37 particles, cleanup `<=1.25s`
- ceiling underside `48m`; four tight upper walls reach and overlap ceiling; goal contracts unchanged
- five power-grid surfaces stay inside renderer/triangle/texture budgets
- gameplay values outside explicit jump/blast-direction changes remain unchanged
- generators deterministic; builder build 2 no-op; separate validator passes; 10 captures pass inspection
