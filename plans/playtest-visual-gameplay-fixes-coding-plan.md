# Playtest Visual and Gameplay Fixes Coding Plan

Status: accepted
Source: direct user request with current-state and Quake II scorch-mark screenshots as visual references only
Run ID: direct
Plan ID: direct
Attempt ID: direct
Covered Requirements: direct request — 60% character visuals; equal-size health/shotgun HUD widgets; 3x shotgun pickup model; clearer arena-wide pellets; dark static flat impact marks; taller aligned goal/concrete; lateral ramp movement; travel-directed upward rocket jumps; look-directed ball kicks
Baseline: cda26613b36b86406994f2579f0ad90099a70aba
Dependencies: None

## Objective

Apply the requested playtest corrections through runtime code, the authoritative Blender arena generator, and the MovementLab builder. Completion means exact gameplay and presentation contracts are implemented, raw and generated assets retain provenance/GUID integrity, every implementation slice passes independent Critical/High review, persisted Unity state validates in a separate process, and the final committed SHA is clean and fully proven.

## Scope

- in: third-person character visual scale; live health/shotgun HUD layout; shotgun pickup display model scale; shotgun trace reach and visibility; shotgun/rocket surface marks; grounded underfoot rocket-jump impulse direction; ball dash-kick aim; ArenaKit goal geometry; goal, ramp, bot-node, prefab, material, shader, scene, validator, manifest, and lighting integration
- out: participant collider/camera/head/cue/shield/nameplate scaling; shotgun damage or ball-impulse falloff changes; projectile or spread changes; marks on moving Rigidbody targets; hand-edited Inspector/scene/prefab/YAML state; unrelated arena, bot, match, weapon-model, or explosion-VFX redesign; new texture assets; production abstractions; automated subjective-feel approval

## Decisions

- assumption: “characters are too big” targets the visible third-person character mesh. `WorldVisualScale` changes from `2f` to `1.2f`, exactly 60%; the participant root and gameplay collider remain unchanged.
- assumption: “ramps more to the side” means the arena lateral Z axis. West/east X, Y, rotations, and mesh stay fixed while Z changes from `2/-2` to `6/-6`.
- assumption: “longer shotgun range” targets visible pellet travel and static impact reach. Visual ray distance becomes 180 m, covering the arena, while existing 6/16/30 m damage and ball-impulse falloff remain unchanged.
- decision: both live HUD panels are exactly `530x112` on the 1920x1080 reference canvas, bottom-aligned at Y `930`, with mirrored 36 px screen margins.
- decision: only `ShotgunPickup/VisualRoot/ShotgunModel` scales to `(3,3,3)`; trigger, root, team cues, grant, and respawn remain unchanged.
- decision: the gameplay goal and Blender concrete recess use a `36x11` m clear opening under the existing 12 m arena top, leaving one metre of lintel.
- decision: surface marks remain pooled world-space particles on static non-trigger hits, use the existing circle mesh, and gain correct 3D surface-normal rotation plus a dedicated dark procedural scorch shader variant. No per-hit GameObject or decal allocation is added.
- decision: a fast underfoot blast uses actual planar velocity, including backward travel, and converts a strength-limited share of speed above base speed into lift without reversing horizontal travel.
- decision: ball contact detection and dash motion continue using `DashDirection`; only the ball impulse uses retained/current look aim.
- decision: `WeaponImpactRules` is pure runtime math and intentionally stays outside MovementLab stage digests; every source that changes serialized/generated contracts is already an explicit stage input.
- question: None

## Execution Graph

`START -> {T1 -> CP1 -> T2 -> CP2 || T3 -> CP3 || T4 -> CP4 -> T5 -> CP5 || T6 -> CP6 || T7 -> CP7} -> JOIN1 -> T8 -> CP8 -> {T9 -> CP9 || T10 -> CP10} -> JOIN2 -> T11 -> CP11 -> JOIN3 -> T12 -> CP12 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in; `+` requires every named predecessor
- gates: `START` -> execution `start_sha == cda26613b36b86406994f2579f0ad90099a70aba`; `JOIN1` -> CP1-CP7 accepted and T2/T5 have consumed their pure producer APIs; `JOIN2` -> CP8-CP10 accepted; `JOIN3` -> CP1-CP11 accepted, all fixes accepted, all source/raw-asset work committed as clean `sourceFreezeSha`; `FINAL` -> CP12 accepted, final proof green at clean committed `finalSha`, no running children

## Tasks

### T1: Pure travel-directed rocket-jump math

- objective: make the underfoot blast rule convert fast actual travel into a materially steeper launch without camera-direction coupling
- covered_requirements: fast forward/backward grounded rocket jumps become more upward and preserve travel direction
- owner: worker-T1 (luna_max)
- dependencies: None
- parallel_contract: produces `BlastMath.ResolvePlanarTravelDirection` and the revised `ComputePlayerImpulse` contract for T2; owns no component state
- owns: `Assets/_Game/Scripts/Runtime/Weapons/BlastMath.cs`, `Assets/_Game/Scripts/Tests/EditMode/BlastMathTests.cs`
- protected: `Assets/_Game/Scripts/Runtime/Weapons/ExplosionResolver.cs`, `Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs`, builder/generated assets
- read_paths: `Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs/Velocity,BaseSpeed,SoftCap,HardCap` -> post-impulse trajectory inputs; `Assets/_Game/Scripts/Runtime/Weapons/ExplosionResolver.cs/ComputePlayerImpulse` -> consumer; `plans/gameplay-feel-polish-coding-plan.md/T7` -> prior intended redirect contract
- validation_environment: source-only worker in orchestrator worktree; no Unity mutation
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_execution: None
- implementation: replace `ResolvePlanarDirection(Vector3 playerForward, Vector2 moveIntent)` with `ResolvePlanarTravelDirection(Vector3 playerVelocity, Vector3 fallbackForward)`. Project velocity onto XZ; finite squared magnitude above `Epsilon` returns its normalized direction. Otherwise project fallback onto XZ and normalize when valid; otherwise return `Vector3.forward`. In the underfoot branch of `ComputePlayerImpulse`, retain existing serialized constants and compute `speedT = Clamp01((horizontalSpeed - baseSpeed) / Max(softCap - baseSpeed, Epsilon))`, `redirectT = speedT * Clamp01(underfootHighSpeedVerticalRedirect)`, `forwardBoost = strength * underfootForwardImpulseScale * (1 - redirectT)`, `redirectBudget = Max(strength, 0) * underfootForwardImpulseScale * redirectT`, `excessSpeed = Max(horizontalSpeed - baseSpeed, 0)`, `brake = Min(excessSpeed, redirectBudget)`, `planarImpulse = normalizedTravel * (forwardBoost - brake)`, and `upwardImpulse = Vector3.up * strength * (underfootUpwardImpulseScale + underfootForwardImpulseScale * redirectT)`. Return their sum. Preserve non-underfoot radial/up-bias behavior and invalid-input rejection. Update tests to assert full-strength 24 results after adding impulse to starting velocity: speed 10 -> planar `23.5`, up `24`; speed 17.5 -> planar `17.5`, up `30.75`; speed 25 -> planar `11.5`, up `37.5`; speed 30 -> planar `16.5`, up `37.5`; mirror backward signs; cover diagonal travel, zero/invalid fallback, half-strength braking, no reversal, and unchanged low-speed behavior.
- done when: tests discriminate current camera/intent behavior and current shallow post-blast trajectory from the new exact contract
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Weapons/BlastMath.cs Assets/_Game/Scripts/Tests/EditMode/BlastMathTests.cs` -> exit 0 and only owned paths selected
- review_focus: braking not scaled by blast strength -> weak blasts erase speed; wrong sign -> travel reverses; input/fallback direction -> backward/coasting regression; non-underfoot change -> unrelated explosion physics regression
- review_checkpoint: CP1

### T2: Rocket-jump component integration

- objective: feed actual velocity into T1 and remove the obsolete camera-relative effective-intent state without changing fixed-step impulse ownership
- covered_requirements: rocket jump follows forward/backward player travel rather than camera facing
- owner: worker-T2 (luna_max)
- dependencies: `CP1 -> accepted travel-direction and impulse API required by both edited components`
- parallel_contract: consumes T1 API; disjoint from every sibling lane
- owns: `Assets/_Game/Scripts/Runtime/Weapons/ExplosionResolver.cs`, `Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs`
- protected: `BlastMath.cs`, ball-kick files, serialized constants, builder/generated assets
- read_paths: `Assets/_Game/Scripts/Runtime/Weapons/BlastMath.cs/ResolvePlanarTravelDirection,TryGetUnderfootFacing,ComputePlayerImpulse` -> exact producer; `Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs/FixedUpdate,ApplyQueuedExternalImpulse` -> ordering to preserve
- validation_environment: source-only worker in orchestrator worktree; no Unity mutation
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: in `ExplosionResolver.ComputePlayerImpulse`, resolve underfoot travel from `target.Velocity` with `target.transform.forward` fallback, then pass that direction through unchanged geometric `TryGetUnderfootFacing` and T1 impulse math. Keep the geometric feet test instead of adding a current-grounded bit, because detonation may follow jump state. Remove `PlayerMotor.currentEffectiveMoveIntent`, its public property, assignments, clear method, and reset/disable calls; keep local sanitized move intent and every movement calculation unchanged. Preserve queued additive impulse, fixed-step application, pause freeze, simulation-disable clearing, reset behavior, caps, and all serialized fields.
- done when: no camera/move-intent state feeds rocket impulses; normal movement and non-underfoot explosions remain unchanged
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Weapons/ExplosionResolver.cs Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs` -> exit 0 and only owned paths selected
- review_focus: grounded-bit gate -> missed ground launch; retained obsolete state -> ambiguous owner; direct velocity mutation in resolver -> frame-ownership regression; movement-input deletion beyond dead state -> locomotion regression
- review_checkpoint: CP2

### T3: Look-directed ball dash kick

- objective: separate the ball’s kick aim from planar dash motion while preserving contact, enemy shove, cooldown, pause, and reset contracts
- covered_requirements: ball follows player look direction instead of shooting upward or following only dash motion
- owner: worker-T3 (luna_max)
- dependencies: None
- parallel_contract: self-contained ball-kick state/API; no PlayerMotor or BallMotor writes
- owns: `Assets/_Game/Scripts/Runtime/Ball/BallKick.cs`, `Assets/_Game/Scripts/Runtime/Ball/DashKickRules.cs`, `Assets/_Game/Scripts/Tests/EditMode/DashKickRulesTests.cs`, `Assets/_Game/Scripts/Tests/EditMode/BallMotionRulesTests.cs`
- protected: `PlayerMotor.cs`, `BallMotor.cs`, `BallMotionRules.cs`, bot controller, builder/generated assets
- read_paths: `Assets/_Game/Scripts/Runtime/Ball/BallMotor.cs/ApplyKick` -> sole Rigidbody writer; `Assets/_Game/Scripts/Runtime/Ball/BallMotionRules.cs/ApplyKick` -> unchanged aimed-velocity math; `Assets/_Game/Scripts/Runtime/Bots/BotController.cs/TranslateCombatResult` -> programmatic one-slot aim caller
- validation_environment: source-only worker in orchestrator worktree; no Unity mutation
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: add pure `DashKickRules.ResolveKickDirection(Vector3 lookDirection, Vector3 fallbackDashDirection)`: return normalized finite look above epsilon, else normalized finite fallback, else zero. Add private `activeKickAim`. On accepted dash, store the resolved activation aim. During a dash, an active local `PlayerInputReader` refreshes it from current camera aim every fixed step; a valid programmatic request refreshes it for bots, and absence of a later programmatic request retains the accepted aim. Pause preserves it; dash end, disable, simulation reset, and coordinated reset clear it. Change `TryProcessContacts` to accept the retained/current look. Keep `player.DashDirection` for overlap capsule, forward filtering, enemy shove, dash termination, and steering; pass only `ResolveKickDirection(activeKickAim, dashDirection)` to `BallMotor.ApplyKick`. Preserve `BallMotor`/`BallMotionRules` signatures, contact dedupe, cooldown, fixed-step order, and external impulse retention. Add pure tests for pitched/horizontal look, invalid look fallback, zero rejection, programmatic retention policy, and `BallMotionRules.ApplyKick` producing zero vertical kick from horizontal look even when player velocity has an upward component.
- done when: ball velocity direction follows full valid look; dash movement/contact shape remains grounded/planar where it was before
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Ball/BallKick.cs Assets/_Game/Scripts/Runtime/Ball/DashKickRules.cs Assets/_Game/Scripts/Tests/EditMode/DashKickRulesTests.cs Assets/_Game/Scripts/Tests/EditMode/BallMotionRulesTests.cs` -> exit 0 and only owned paths selected
- review_focus: using look for overlap/shove -> dash gameplay expansion; clearing aim on pause -> resume defect; bot aim overwritten by inactive camera -> programmatic regression; Rigidbody write outside BallMotor -> state-owner violation
- review_checkpoint: CP3

### T4: Pure impact-mark orientation rules

- objective: provide deterministic, testable surface-normal rotation without particle or weapon integration
- covered_requirements: marks lie flat and remain statically oriented on hit surfaces
- owner: worker-T4 (luna_max)
- dependencies: None
- parallel_contract: produces `WeaponImpactRules.TryResolveMarkRotation` for T5; pure source only
- owns: `Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactRules.cs`, `Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactRules.cs.meta`, `Assets/_Game/Scripts/Tests/EditMode/WeaponImpactRulesTests.cs`, `Assets/_Game/Scripts/Tests/EditMode/WeaponImpactRulesTests.cs.meta`
- protected: `WeaponImpactFeedback.cs`, weapon call sites, editor/shader/generated assets
- read_paths: `Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactFeedback.cs/EmitMark` -> consumer and current inverse-look defect; `Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs/CreateWeaponImpactMarkSystem` -> mesh forward-axis contract
- validation_environment: source-only worker in orchestrator worktree; no Unity mutation
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_execution: None
- implementation: add public static `WeaponImpactRules.TryResolveMarkRotation(Vector3 normal, out Quaternion rotation)` in `RocketFooxball.Runtime.Feedback`. Reject non-finite and squared magnitude at or below `0.000001`, returning identity and false. Otherwise return `Quaternion.FromToRotation(Vector3.forward, normal.normalized)` and true. Tests require `rotation * Vector3.forward` to match normalized normals for forward/back, left/right, floor/ceiling, and a non-axis ramp normal within `0.0001`; NaN, infinity, and zero reject.
- done when: the API fully specifies mark plane orientation and invalid handling without Unity component state
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactRules.cs Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactRules.cs.meta Assets/_Game/Scripts/Tests/EditMode/WeaponImpactRulesTests.cs Assets/_Game/Scripts/Tests/EditMode/WeaponImpactRulesTests.cs.meta` -> exit 0 and new asset/meta pairs are complete
- review_focus: wrong mesh forward axis -> perpendicular/half-visible marks; accepted invalid normal -> NaN particle rotation; non-deterministic roll -> unstable appearance
- review_checkpoint: CP4

### T5: Arena-wide shotgun traces and static scorch integration

- objective: integrate T4 into existing pooled weapon feedback and extend visual pellet reach/readability without changing shotgun damage
- covered_requirements: visible pellets across arena; distant marks; dark static flat shotgun/rocket marks; no camera-rotating white half-circles
- owner: worker-T5 (luna_max)
- dependencies: `CP4 -> accepted mark-rotation API`
- parallel_contract: produces exact runtime constants/call behavior consumed by T8/T9/T11; no shader or builder writes
- owns: `Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactFeedback.cs`, `Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs`
- protected: `WeaponImpactRules.cs`, shotgun damage/spread rules, rocket call sites, editor/shader/generated assets
- read_paths: `Assets/_Game/Scripts/Runtime/Weapons/ShotgunDamageRules.cs` -> unchanged falloff; `Assets/_Game/Scripts/Runtime/Weapons/RocketProjectile.cs/TryDetonate` -> existing rocket mark caller/static filter; `Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs/ShouldEmitShotgunMark` -> existing static filter
- validation_environment: source-only worker in orchestrator worktree; no Unity mutation
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: add private `ShotgunWeapon.VisualTraceRange = 180f`. In `ResolveShot`, compute `traceRange = Max(VisualTraceRange, Max(maxRange,0))`; use it for the pellet raycast and miss endpoint. Continue passing serialized `maxRange` to `ShotgunDamageRules.EvaluateFalloff`, so hits beyond 30 m emit traces and qualifying static marks but apply zero participant damage and zero ball impulse. Preserve spread, participant blocking, damage aggregation, hit confirmation, and fire timing. In `WeaponImpactFeedback`, keep world simulation and pooling; change private constants to surface offset `0.005f`, shotgun size `0.30f`, rocket size `0.90f`, and retain 30 s lifetime. `EmitMark` calls T4, emits zero velocity with `rotation3D = rotation.eulerAngles`, and performs no parenting or allocation. Keep static non-trigger filters in shotgun/rocket callers; moving Rigidbody, player, ball, pickup, trigger, and miss never mark. Tracer speed stays 120, minimum lifetime `0.04`, and exact endpoint motion remains unchanged; builder-owned start size changes in T9.
- done when: eight visual rays reach up to 180 m, damage remains zero beyond the existing max knot, and accepted static impacts emit one correctly oriented persistent mark
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactFeedback.cs Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs` -> exit 0 and only owned paths selected
- review_focus: 180 m passed into falloff -> long-range damage regression; mark before hit/static guard -> floating artifacts; local simulation/parenting -> moving marks; changed spread/order -> weapon behavior regression
- review_checkpoint: CP5

### T6: Equal-size live shotgun HUD

- objective: make the right shotgun widget match the left health widget while retaining state, scaling, and GUI hygiene
- covered_requirements: shotgun HUD on the right is the same size as the left HUD
- owner: worker-T6 (luna_max)
- dependencies: None
- parallel_contract: runtime OnGUI layout facts consumed read-only by T11; no builder asset mutation
- owns: `Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs`
- protected: HUD state/rules, MatchController, scene builder, generated scene
- read_paths: `Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs/ApplyReferenceCanvas,DrawHealth,DrawShotgunWidget,DrawShotgunSilhouette` -> scaling/state contracts; `Assets/_Game/Editor/MovementLab/MovementLabValidator.cs` -> future consumer
- validation_environment: source-only worker in orchestrator worktree; no Unity mutation
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: expose runtime-used public static read-only Rect facts: health panel `(36,930,530,112)`, shotgun panel `(1354,930,530,112)`, shotgun icon `(1376,938,192,96)`, title `(1588,946,274,30)`, and shells `(1588,988,274,26)`. Draw methods consume these facts. Define silhouette parts as normalized fractions of icon Rect: stock `(0,.421875,.21875,.1875)`, body `(.171875,.25,.3671875,.453125)`, barrel `(.5078125,.328125,.4921875,.171875)`, trigger `(.3671875,.65625,.0859375,.25)`. Preserve conditional visibility, colors, shell text, reference-canvas matrix, cached texture, and restoration of GUI color/matrix.
- done when: panels share exact size/Y, retain mirrored 36 px margins, and all right-widget content fits without overlap
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs` -> exit 0 and only owned path selected
- review_focus: hard-coded draw Rect diverges from exposed fact -> validator blind spot; content overflow -> request not met; lost GUI restoration/caching -> frame regression; state condition change -> HUD behavior regression
- review_checkpoint: CP6

### T7: Author the 11 m concrete goal in Blender

- objective: regenerate the authoritative ArenaKit goal recess with an 11 m clear opening and unchanged modular/provenance contracts
- covered_requirements: concrete surround matches a goal that is higher than the current 9 m opening
- owner: worker-T7 (sol_high)
- dependencies: None
- parallel_contract: produces exact FBX bounds and semantic audit consumed by T8/T10; owns raw generator/output only
- owns: `Tools/Blender/generate_arena_kit.py`, `Assets/_Game/Models/ArenaKit.fbx`
- protected: `Assets/_Game/Models/ArenaKit.fbx.meta`, every Unity editor/generated path, other Blender assets
- read_paths: `.agents/skills/use-blender/SKILL.md` -> mandatory workflow; `Assets/_Game/Editor/MovementLab/MovementLabContract.cs/ArenaGoal*` -> baseline/import mapping; `Assets/_Game/Editor/MovementLab/MovementLabImportPipeline.cs/ValidateArenaKitModel` -> Unity consumer
- validation_environment: orchestrator short worktree under `C:\wt\<id>`; Blender background process; previews remain uncommitted under `Temp/BlenderPreviews/ArenaKit`; no Unity process
- unity_mutation: false
- expensive_proof_owner: worker-T7 (sol_high)
- expensive_proof_execution: same_dispatch
- implementation: change `MODULE_CONTRACTS["ArenaGoalRecess"]` opening to half-width 18/height 11, outer maximum Z 12, and keep minimum, 10 m render depth, pivot, axes, names, UV layers, and four material slots. Build cheeks at Z `5.505`, dimensions `3x10x11.01`; mirrored shoulders span Z `11..12`; lintel center Z `11.5`, dimensions `36.02x10x1`; back wall center Z `5.505`, dimensions `36.02x0.4x11.01`; glow strip center Z `10`; floor, rear marker, low hazard stripe, material order, connection map, topology, and recess depth remain unchanged. Make obstruction and success text derive/emit `36x11`, and semantic audit report module bounds `42x12x10`. Resolve Blender executable/version, then run `& $blenderExe --background --factory-startup --python Tools/Blender/generate_arena_kit.py` from repository root. Require exit 0, non-empty FBX, semantic JSON, finite/manifold/transform/bounds/material/UV/connection audits, and all 12 non-empty preview PNGs (six views for each module). Inspect every preview for opening clearance, one-metre lintel, back-wall height, normals, clipping, scale, and unchanged sconce. Hash `ArenaKit.fbx.meta` before/after and require exact identity/GUID `5979bda68030bdf4e9475ce7c370ffb4`. Do not send FBX to the YAML comparator.
- done when: generator, semantic audit, FBX, and previews prove a `36x11` clear opening under a 12 m top with stable source contracts/meta
- checks: proof: `& $blenderExe --background --factory-startup --python Tools/Blender/generate_arena_kit.py` -> exit 0; exact bounds/opening audits; non-empty FBX/semantic JSON/12 previews; `.meta` SHA-256 unchanged
- review_focus: source/FBX bounds mismatch -> Unity import failure; missing preview inspection -> hidden geometry defect; slot/name/pivot/axis drift -> scene provenance failure; meta change -> GUID break
- review_checkpoint: CP7

### T8: Publish shared builder contracts

- objective: make one source owner publish every exact constant/version consumed by the parallel prefab and arena integrations
- covered_requirements: all direct-request slices that persist through builder contracts
- owner: worker-T8 (luna_max)
- dependencies: `JOIN1 -> CP1-CP7 accepted; exact runtime APIs, visual layout, and Blender bounds are stable inputs`
- parallel_contract: sole owner of shared contract/catalog; T9 and T10 begin only after CP8
- owns: `Assets/_Game/Editor/MovementLab/MovementLabContract.cs`, `Assets/_Game/Editor/MovementLab/MovementLabContractCatalog.cs`
- protected: runtime, Blender, pipeline, validator, generated assets
- read_paths: T1-T7 accepted slices -> exact producer facts; `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs/Definitions` -> version consumers; `Assets/_Game/Editor/MovementLab/MovementLabArenaPipeline.cs` and `MovementLabPrefabPipeline.cs` -> consumers
- validation_environment: source-only worker in orchestrator worktree; no Unity mutation
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: bump `SerializedContractVersion 14->15`, `MaterialPrefabStageContractVersion 16->17`, and `GameplaySceneStageContractVersion 18->19`. Set `WorldVisualScale=1.2f`; leave controller radius/height/center/skin, head, cues, shield, and nameplate exact. Add `ShotgunPickupModelScale=3f`, `WeaponImpactVisualTraceRange=180f`, `WeaponImpactTracerSize=0.10f`; change mark offset to `0.005f`, shotgun size `0.30f`, rocket size `0.90f`, and publish mark color `(0.055,0.04,0.03,0.88)`. Set `ArenaGoalOpeningHeight=11f`; retain derived center/lintel/containment formulas, yielding 5.5/11.5/12. Change goal generator/imported max bounds to `(21,0,12)` and `(21,12,10)` with existing mins. Rename lintel specs to `WestUpperLintel`/`EastUpperLintel`, position Y `11.5`, scale Y `1`, preserving X/Z and widths. Set ramp centers to `(-30,0,6)` and `(30,0,-6)` with rotations unchanged. Catalog re-exports every value used across assembly/validator code. Do not add pure `WeaponImpactRules` to stage inputs.
- done when: consumers can implement without duplicated product values, and every stage-changing value has one contract owner
- checks: proof: `git diff --check -- Assets/_Game/Editor/MovementLab/MovementLabContract.cs Assets/_Game/Editor/MovementLab/MovementLabContractCatalog.cs` -> exit 0 and only owned paths selected
- review_focus: physical character constants scaled -> gameplay regression; version omitted -> stale no-op; Blender/Unity axis bounds swapped -> import failure; ramp X changed -> goal-axis request violation
- review_checkpoint: CP8

### T9: Compose character, pickup, tracer, and scorch presentation

- objective: persist T5/T8 presentation through one material-prefab pipeline without touching arena composition
- covered_requirements: 60% character visuals; 3x pickup model; visible pellets; dark flat static marks
- owner: worker-T9 (luna_max)
- dependencies: `CP5 -> accepted runtime particle/trace constants`; `CP8 -> shared builder values and versions`
- parallel_contract: disjoint from T10 arena/import/bot files; both feed T11 validator
- owns: `Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs`, `Assets/_Game/Editor/MovementLab/MovementLabMaterialPipeline.cs`, `Assets/_Game/Shaders/RetroParticle.shader`
- protected: arena/import/bot pipelines, runtime, contract/catalog, validator, generated assets
- read_paths: `Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactFeedback.cs` and `WeaponImpactRules.cs` -> runtime consumer; `Assets/_Game/Generated/BlueCircleCueMesh.asset` -> unchanged mark mesh; `Assets/_Game/Materials/WeaponImpactMark.mat` -> generated target
- validation_environment: source-only worker in orchestrator worktree; no Unity mutation
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: player prefab continues scaling only `WorldVisual` from contract, so world shotgun/corpse inherit `1.2` while root/collider/camera/cues/shield/nameplate stay exact. Shotgun pickup sets only `VisualRoot/ShotgunModel.localPosition=zero`, rotation identity, scale `(3,3,3)`; root, trigger, cues, grant, respawn stay exact. Pellet ParticleSystem start size becomes contract `0.10`, retaining Stretch/View, speed 120, world simulation, lifetime rule, and cap 32. Mark ParticleSystem keeps existing circle mesh, sets `main.startRotation3D=true`, world simulation, zero speed, lifetime 30, cap 512, and renderer alignment `World`. Add dedicated weapon-impact material creation: white texture, normal alpha blend, color `(0.055,0.04,0.03,0.88)`, local `_SCORCH_MARK` enabled; generic particle material creation explicitly disables this keyword. In `RetroParticle.shader`, declare local shader-feature `_SCORCH_MARK`; generic path stays byte/behavior equivalent. Scorch branch computes `p=uv*2-1`, `r=length(p)`, `a=atan2(p.y,p.x)`, `irregularRadius=.78+.08*sin(7*a+.4)+.05*sin(13*a-1.1)`, `edge=1-smoothstep(irregularRadius-.18,irregularRadius,r)`, `mottle=saturate(.65+.20*sin(31*p.x+17*p.y)+.15*sin(19*p.x-29*p.y))`, and multiplies output alpha by `edge*mottle`; RGB remains material dark color. Preserve shader depth test/no depth write and all other material routing.
- done when: source composition exactly expresses smaller characters, larger pickup model, thicker pellets, and dark irregular surface-conforming marks without new assets
- checks: proof: `git diff --check -- Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs Assets/_Game/Editor/MovementLab/MovementLabMaterialPipeline.cs Assets/_Game/Shaders/RetroParticle.shader` -> exit 0 and only owned paths selected
- review_focus: shared circle mesh edited -> cue regression; keyword leaks to generic particles -> arena/explosion regression; missing 3D rotation/world alignment -> original camera-rotation defect; pickup root/cue scale -> gameplay/readability regression
- review_checkpoint: CP9

### T10: Compose taller goals and lateral ramps

- objective: integrate the accepted raw FBX and shared geometry values into arena collision, visuals, import validation, and bot navigation
- covered_requirements: concrete matches taller goal; both ramps move laterally only
- owner: worker-T10 (luna_max)
- dependencies: `CP7 -> accepted ArenaKit FBX/bounds`; `CP8 -> exact Unity goal/ramp contracts`
- parallel_contract: disjoint from T9 presentation files; both feed T11 validator
- owns: `Assets/_Game/Editor/MovementLab/MovementLabArenaPipeline.cs`, `Assets/_Game/Editor/MovementLab/MovementLabImportPipeline.cs`, `Assets/_Game/Editor/MovementLab/MovementLabBotPipeline.cs`
- protected: prefab/material/shader pipeline, runtime, contract/catalog, validator, raw FBX/meta, generated assets
- read_paths: `Assets/_Game/Models/ArenaKit.fbx` and T7 semantic audit -> imported source; `MovementLabContract.cs/ArenaGoal*,ArenaRamp*` -> exact values; `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs` -> existing source coverage
- validation_environment: source-only worker in orchestrator worktree; no Unity mutation
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: retain one goal-height contract for trigger, shield collider/visual, team cue center, posts, lintel, recess side/back/floor colliders, goal crossing, and containment; all consume the new derived 11 m values. Replace hard-coded upper-wall/lintel creation and validation with existing contract specification arrays; use renamed `WestUpperLintel`/`EastUpperLintel` at Y `11.5`, height `1`, so no architecture solid blocks the 11 m opening. Import validation keeps exact two-mesh set, unit scale, provenance, renderer-only hierarchy, UVs, submesh/material order, and now requires ArenaGoalRecess bounds min `(-21,0,0)`, max `(21,12,10)`. Ramp build/validation consumes centers Z `6/-6` with X/Y/rotation/mesh unchanged. Bot ramp nodes 24-27 remain transform-derived; replace hard-coded drop-node Z for 28/29 with matching ramp-center Z while preserving node IDs, X/Y, edges, costs, target rules, and the 3 m geometric clearance from Z `18/-18` ammo pickups.
- done when: gameplay opening, concrete mesh, collision shell, containment, and bot graph agree; ramp movement is purely lateral
- checks: proof: `git diff --check -- Assets/_Game/Editor/MovementLab/MovementLabArenaPipeline.cs Assets/_Game/Editor/MovementLab/MovementLabImportPipeline.cs Assets/_Game/Editor/MovementLab/MovementLabBotPipeline.cs` -> exit 0 and only owned paths selected
- review_focus: remaining hard-coded 8 m lintel -> goal obstruction; imported bound mismatch -> build failure; drop node left at ±2 -> bot route defect; pickup overlap or X movement -> scope/request regression
- review_checkpoint: CP10

### T11: Persisted contract and stage validation fan-in

- objective: make the authoritative validator prove every new runtime, raw-asset, prefab, material, HUD, arena, and bot contract without changing stage ownership
- covered_requirements: all direct request slices and generated persistence
- owner: worker-T11 (luna_max)
- dependencies: `JOIN2 -> CP8-CP10 accepted plus CP1-CP7 accepted runtime/raw producers`
- parallel_contract: serial fan-in; sole owner of validator surface; `MovementLabStageGraph.cs` remains protected because existing inputs already cover every generated-contract-changing source
- owns: `Assets/_Game/Editor/MovementLab/MovementLabValidator.cs`
- protected: `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs`, all producer files, generated assets
- read_paths: accepted T1-T10 slices -> exact facts; `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs/Definitions` -> confirm existing coverage; `Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs` and `MovementLabArenaPipeline.cs` -> owner-local validation helpers
- validation_environment: source-only worker in orchestrator worktree; no Unity mutation
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: update reflection/constant checks for visual trace 180, tracer size `0.10`, mark offset/sizes, and T4 method presence. Validate player root/collider/head/cues/shield/nameplate remain exact and only WorldVisual is `1.2`; replace stale “doubled/enlarged” messages with contract wording. Validate ShotgunPickup model zero position/identity rotation/scale 3 in prefab and both scene instances, with trigger/cues/runtime wiring unchanged. Validate pellet and mark particle systems, including 3D rotation, world alignment/simulation, unchanged mesh, lifetimes/caps, material reference, dark color, white texture, `_SCORCH_MARK` enabled only on impact material, and shader name. Validate public HUD Rect facts: equal panel size/Y, 36 px mirrored margins, exact icon/text containment. Validate 11 m goal/collider/containment/concrete imported bounds and one-metre architecture lintels, lateral ramp centers, and bot drop nodes. Keep all persisted reference/fileID/provenance checks. Confirm `MovementLabStageGraph` already lists `WeaponImpactFeedback`, `ShotgunWeapon`, `MatchHud`, `BlastMath`, `ExplosionResolver`, `PlayerMotor`, `BallKick`, shader, contract/catalog, and all changed editor pipelines; leave pure `WeaponImpactRules` excluded because it owns no serialized/generated state.
- done when: semantic validation can distinguish every old defect/value from the requested state after reopening persisted outputs
- checks: proof: `git diff --check -- Assets/_Game/Editor/MovementLab/MovementLabValidator.cs` -> exit 0 and only owned path selected
- review_focus: assertion checks duplicated value instead of runtime/builder source -> false proof; stage graph edited without generated-state need -> unnecessary churn; missing prefab provenance/fileID -> reload-only defect; validator owner scope leak -> unrelated failure
- review_checkpoint: CP11

### T12: Authoritative Unity regeneration and exact proof

- objective: run the sole Unity mutation phase, classify every actual output, prove persisted semantics, and prepare exact generated review
- covered_requirements: all direct-request slices
- owner: worker-T12 (luna_max)
- dependencies: `JOIN3 -> CP1-CP11 and fixes accepted; source/raw asset work committed as clean sourceFreezeSha`
- parallel_contract: None; automated-generation workload exception; sole Unity lease and generated-output writer
- owns: `Assets/_Game/Prefabs/Player.prefab`, `Assets/_Game/Prefabs/Player.prefab.meta`, `Assets/_Game/Prefabs/ShotgunPickup.prefab`, `Assets/_Game/Prefabs/ShotgunPickup.prefab.meta`, `Assets/_Game/Materials/WeaponImpactMark.mat`, `Assets/_Game/Materials/WeaponImpactMark.mat.meta`, `Assets/_Game/Generated/MovementLabBuildManifest.json`, `Assets/_Game/Generated/MovementLabBuildManifest.json.meta`, `Assets/_Game/Scenes/MovementLab.unity`, `Assets/_Game/Scenes/MovementLab.unity.meta`, `Assets/_Game/Lighting/MovementLabLightingManifest.json`, `Assets/_Game/Lighting/MovementLabLightingManifest.json.meta`, `Assets/_Game/Scenes/MovementLab/LightingData.asset`, `Assets/_Game/Scenes/MovementLab/LightingData.asset.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-0.exr`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-0.exr.meta`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-1.exr`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-1.exr.meta`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-2.exr`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-2.exr.meta`
- protected: all source/raw assets including `ArenaKit.fbx/.meta`; every path outside the authoritative inventory plus exact declared ownership; untargeted inventory output may change only as a builder-traced `inventory-exception` with complete comparator coverage
- read_paths: `Assets/_Game/Editor/MovementLabBuilder.cs` -> commands; `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs` -> stages/inventory; `Tools/Validation/Invoke-MovementLabWorkflow.ps1` -> evidence/status grammar; `Tools/Validation/Compare-GeneratedYaml.ps1` -> exact classification
- validation_environment: writable short `C:\wt\<id>` project; private preserved `Library`; deepest-path-probed `C:\wt\<id>e`; interactive Editor closed; one hidden waited Unity process/lease at a time
- unity_mutation: true
- expensive_proof_owner: worker-T12 (luna_max)
- expensive_proof_execution: same_dispatch
- implementation: verify exact `sourceFreezeSha`, clean owned source/raw state, short path/evidence budget, no process/lock. Run harness. Run targeted EditMode fixtures `BlastMathTests;DashKickRulesTests;BallMotionRulesTests;WeaponImpactRulesTests` in a waited hidden Unity process without `-quit`; parse requested XML and require total > 0, failed = 0, inconclusive = 0 plus release. Run a separate waited hidden compile/probe with `-batchmode -nographics -quit -executeMethod RocketFooxball.Editor.MovementLabBuilder.ProbeMovementLabGeneratedState`; require exit 0, schema 1 probe, no product mutation, release. From that probe bind `expectedBakeOutcome=executed` when Lighting/BakedOutput is stale or a stale MaterialPrefab/GameplayScene stage will rewrite static lighting state; otherwise bind `reused`. Run fresh harness immediately before mutation. Run exactly one `ProductionPrepare`. Its top workflow status must be `complete`. Require the selected child outcome: `executed` -> production-bake row executed, marker absent, bakeCount 1; `reused` -> row reused, exactly one source-defined digest marker, bakeCount 0. Repository-authoritative valid reuse remains accepted when the pre-run prediction was executed, per the bake reuse rule; malformed/duplicate marker, wrong bake count, or two workflow status mismatches block. Require current production manifest/probe, exact sourceFreezeSha, authoritative build rows, in-scope changed paths, release. In a fresh Unity process/evidence root, run non-mutating `ProductionValidate` against dirty generated outputs; require complete, production-validator executed, current production state, no bake invocation, bakeCount 0, invocation-local changed paths empty, release. Run comparator Base sourceFreezeSha Head WORKTREE FailOnDangling; require exact actual selection plus `COVERAGE:`, `SEMANTIC:`, `DANGLING:`, `GUID:`, `PAIRS:`, `UNSUPPORTED:`, no unsupported/increased dangling/GUID churn/broken pairs/out-of-union output. Accept validated no-op outputs. Orchestrator commits comparator-selected generated paths only as `chore: regenerate MovementLab outputs`; zero selection creates no commit and sourceFreezeSha stays the generated boundary. Attach T7 raw-FBX semantic evidence and T12 comparator evidence to CP12. Generated semantic defect routes to fresh owning source worker, new sourceFreezeSha, and full T12 replay; never edit generated bytes.
- done when: targeted rules, compile/probe, prepare branch, persisted validator, actual output integrity, and generated review input are all exact-SHA/evidence bound
- checks: proof: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1 -EvidenceRoot C:\wt\<id>e\harness` -> exit 0 under 90 seconds; no Unity process/lock; rerun immediately before ProductionPrepare
- checks: proof: waited hidden Unity EditMode `-runTests -testPlatform EditMode -testFilter RocketFooxball.Tests.EditMode.BlastMathTests;RocketFooxball.Tests.EditMode.DashKickRulesTests;RocketFooxball.Tests.EditMode.BallMotionRulesTests;RocketFooxball.Tests.EditMode.WeaponImpactRulesTests -testResults C:\wt\<id>e\targeted.xml` without `-quit` -> exit 0; XML parses with total > 0, failed = 0, inconclusive = 0; release
- checks: proof: waited hidden Unity `-batchmode -nographics -quit -executeMethod RocketFooxball.Editor.MovementLabBuilder.ProbeMovementLabGeneratedState -movementLabProbePath C:\wt\<id>e\compile-probe.json` -> exit 0; schemaVersion 1; no product mutation; release
- checks: `check_id=playtest-production-prepare; tier=production-final; owner=worker-T12; expected_status=complete; command=powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode ProductionPrepare -ProjectPath C:\wt\<id> -EvidenceRoot C:\wt\<id>e\prepare -AttemptId playtest-prepare -TimeoutSeconds 3600; mutates_project=true; input_paths=[accepted T1-T11 owned paths at sourceFreezeSha]; run_point=JOIN3; evidence=top status complete, exact sourceFreezeSha, current production manifest/probe, selector-bound production-bake child branch or authoritative valid reuse, exact marker/bakeCount contract, authoritative build rows, changed paths current-inventory or declared, release proof valid`
- checks: `check_id=playtest-pre-review-validate; tier=production-final; owner=worker-T12; expected_status=complete; command=powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode ProductionValidate -ProjectPath C:\wt\<id> -EvidenceRoot C:\wt\<id>e\pre-review-validate -AttemptId playtest-pre-review-validate -TimeoutSeconds 1800; mutates_project=false; input_paths=[dirty authoritative outputs from playtest-production-prepare]; run_point=T12-after-ProductionPrepare-before-comparator; evidence=top status complete, exact sourceFreezeSha, production-validator executed, current production state, no ProductionBake invocation, bakeCount 0, changedGeneratedPaths empty, release proof valid`
- checks: proof: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Compare-GeneratedYaml.ps1 -Base <sourceFreezeSha> -Head WORKTREE -FailOnDangling` -> all six headers, every actual change selected, zero unsupported/increased dangling/GUID churn/pair break/out-of-union paths
- review_focus: invalid skip/executed branch accepted -> stale lighting; FBX sent to YAML comparator -> unsupported proof; direct generated edit -> authority breach; actual inventory exception without trace/coverage -> scope breach; persisted validator omitted -> reload-only defect
- review_checkpoint: CP12

## Execution Assignments

- workers: T1 -> worker-T1 (luna_max) -> pure rocket-jump math; T2 -> worker-T2 (luna_max) -> rocket component integration; T3 -> worker-T3 (luna_max) -> look-directed ball kick; T4 -> worker-T4 (luna_max) -> pure impact rotation; T5 -> worker-T5 (luna_max) -> weapon feedback integration; T6 -> worker-T6 (luna_max) -> HUD layout; T7 -> worker-T7 (sol_high) -> Blender ArenaKit; T8 -> worker-T8 (luna_max) -> shared contracts; T9 -> worker-T9 (luna_max) -> prefab/material/shader; T10 -> worker-T10 (luna_max) -> arena/import/bot; T11 -> worker-T11 (luna_max) -> validator; T12 -> worker-T12 (luna_max) -> sole Unity/generated proof
- review_checkpoints: CP1-CP12 -> corresponding worker’s committed/frozen slice -> fresh `sol_medium` reviewer after worker return and before every dependent task; Critical/High findings only; CP7 includes Blender semantic audit/previews/meta hash; CP12 includes comparator/generated coverage; every fix uses a fresh bounded writer and follows [review checkpoints](../.agents/skills/orchestrate-implementation/SKILL.md#review-checkpoints)
- joins: fan-out workers have disjoint exact paths; T2 waits for T1 API; T5 waits for T4 API; T8 waits for every feature producer; T9/T10 are disjoint parallel consumers of CP8; T11 serially validates all producers; T12 starts only after every source/raw review/fix and clean sourceFreezeSha

## Final Verification

- exact head: clean committed `finalSha`; `git merge-base --is-ancestor cda26613b36b86406994f2579f0ad90099a70aba <finalSha>` succeeds; sourceFreezeSha and generated transition are ancestors; initial unrelated status preserved
- checks: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Compare-GeneratedYaml.ps1 -Base <sourceFreezeSha> -Head <finalSha> -FailOnDangling` -> committed exact selection, all six headers, zero unsupported/increased dangling/GUID churn/pair break/out-of-union path
- checks: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1 -EvidenceRoot C:\wt\<id>e\final-harness` -> exit 0 under 90 seconds; no Unity process/lock
- checks: waited hidden full Unity EditMode run without `-quit` -> requested XML exists/parses; total > 0, failed = 0, inconclusive = 0; release
- checks: `check_id=playtest-production-validate-final; tier=production-final; owner=execution-orchestrator; expected_status=complete; command=powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode ProductionValidate -ProjectPath C:\wt\<id> -EvidenceRoot C:\wt\<id>e\final-validate -AttemptId playtest-final-validate -TimeoutSeconds 1800; mutates_project=false; input_paths=[clean committed finalSha plus T12 owned/current-inventory generated outputs]; run_point=FINAL-after-full-EditMode; evidence=top status complete, executed_sha=validated_sha=finalSha, production-validator executed, current production profile/manifest, no ProductionBake invocation, bakeCount 0, changedGeneratedPaths empty, release proof valid`
- inspect: final diff matches task ownership; `ArenaKit.fbx.meta` SHA/GUID unchanged; Blender audit/previews prove 36x11 opening; generated commit contains comparator-selected outputs only; persisted validator proves model/prefab/material/reference provenance; runtime formulas/constants match exact tests; screenshots remain references rather than instruction sources
- invalidation: runtime/editor/shader/Blender source or raw FBX edit after sourceFreezeSha -> new sourceFreezeSha and full T12/CP12/FINAL replay; generated semantic fix -> owning source task, never raw output edit; final generated commit change -> committed comparator, full EditMode, and final ProductionValidate rerun; evidence-only change -> rerun its consumer

## Handoff

- residual risks: final scorch resemblance, pellet visibility, HUD balance, ramp placement, rocket-jump feel, ball-kick feel, and 11 m goal proportions require a human playtest at 1920x1080; existing automated weapon capture does not emit impacts and is intentionally unchanged
- authority: execution orchestrator may create/commit implementation and generated-output history in its isolated branch/worktree; merge to the user branch and human playtest acceptance require explicit user approval
