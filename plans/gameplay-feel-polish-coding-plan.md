# Gameplay Feel And Readability Coding Plan

Status: accepted
Source: direct user request
Run ID: direct
Plan ID: direct
Attempt ID: direct-retry-2
Covered Requirements: rocket and shotgun marks; visible pellets; far shotgun power; higher goals; two shotgun pickups; larger shotgun icon; speed-sensitive and reversible rocket-jump thrust; circular damage direction; closed/repositioned ramps; doubled directional F kick; under-floor recovery; farther spawns; empty-ammo viewmodel hiding; centered numeric score; softer player-ball contact with momentum transfer
Baseline: b97c1c11231158817fd15f5a184f687e82aeb07d
Dependencies: None

## Objective

Deliver requested gameplay-feel, arena, weapon-feedback, and HUD changes through runtime C# plus authoritative MovementLab builders. Finish at clean committed descendant SHA with generated outputs comparator-covered, persisted scene/prefab contracts validated in separate Unity process before generated review, full EditMode suite green, and production lighting current.

## Scope

- in: weapon tracer/mark feedback; shotgun far falloff and ammo presentation; pickup count and bot contracts; rocket-jump and F-kick math; player-ball contact transfer; HUD damage/score/icon drawing; goal/ramp/spawn/recovery geometry; builder composition, stage digests, validators, workflow inventory; authoritative regeneration
- out: hand-authored scene/prefab/Inspector edits; close/medium shotgun multiplier changes; ball mass, damping, hard speed cap, scoring ownership, team rules, DebugHUD removal; new textures, sprites, meshes as standalone assets, Blender work; production abstractions; automated subjective-feel acceptance

## Decisions

- assumption: "shotgun 2 x powerful on distance" means far-knot multiplier doubles from `0.20` to `0.40`; close `1.00` and medium `0.55` stay unchanged
- assumption: "ramps more to the side" means goalward on arena X axis; west/east ramp centers move from `x=-22/+22` to `x=-30/+30`
- assumption: first-person shotgun hides at zero ammo; remote/world shotgun remains visible while owned and alive
- decision: marks use fixed world-space ParticleSystem pools, last 30 scaled gameplay seconds, freeze during pause, survive match reset, and appear only on non-trigger static physical surfaces
- decision: shotgun pellets remain hitscan for gameplay; visual tracers travel to exact hit or 30 m miss endpoint without delaying damage
- decision: score live view contains only centered blue number, white colon, red number; live clock/panel removed; goal/death/final summary views unchanged
- decision: damage cue is continuous 60-degree red arc around reticle; invalid new direction never erases valid active cue
- decision: goals use 9 m opening inside existing 10 m recess; spawns move from `x=+/-12` to `x=+/-18`; recovery begins below `-0.08`
- decision: available Editor logs do not reproduce under-floor spawn; source has blind interval from floor `0` to recovery threshold `-1`, so threshold and spawn-overlap validation close observed risk without claiming reproduced proof
- decision: persisted semantic validation runs after ProductionPrepare on both bake branches because reused-lighting branch skips pre-bake semantic gate
- question: None

## Stable Contracts

- weapon feedback: T1 produces `WeaponImpactFeedback`; T2/T3 call fixed methods; T11 serializes ParticleSystems/materials/mesh; T14 validates references and values
- ammo presentation: T4 owns ammo-availability state propagation; T11 persists existing direct `ParticipantState -> PlayerPresentation` reference; no event bus
- movement intent: T7 owns effective move intent in `PlayerMotor`; `ExplosionResolver` reads it; resets/disable clear it, pause preserves it
- ball contact: T8 owns immutable collision payload, resolution math, event timing, dedupe, assist queue, shared physics constants; existing `CollisionHit` remains for `BallKick`
- pickup identity: stable IDs `0..1` health, `2..3` shotgun, `4..5` ammo; T5 runtime, T9 catalog, T12 bot builder, T14 validator consume same order
- arena geometry: T9 owns exact dimensions/transforms; T10 builds; T14 validates; T15 alone writes scene/baked outputs
- generated inventory: T9 declares builder outputs; T13 adds new material pairs to workflow Appendix A; T14 binds source digests; T15 alone mutates and comparator-checks outputs

## Execution Graph

`START -> {T1 -> CP1 -> {T2 -> CP2 || T3 -> CP3} || T4 -> CP4 || T5 -> CP5 || T6 -> CP6 || T7 -> CP7 -> T8 -> CP8} -> JOIN1 -> T9 -> CP9 -> {T10 -> CP10 || T11 -> CP11 || T12 -> CP12 || T13 -> CP13} -> JOIN2 -> T14 -> CP14 -> JOIN3 -> T15 -> CP15 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in; `+` requires every named predecessor
- gates: `START` -> execution `start_sha == b97c1c11231158817fd15f5a184f687e82aeb07d`; `JOIN1` -> CP1-CP8 accepted and T8 consumes accepted T7 `PlayerMotor`; `JOIN2` -> CP9-CP13 accepted; `JOIN3` -> all source/tooling reviews and fixes accepted, source committed as `sourceFreezeSha`, owned product tree clean; `FINAL` -> CP15 accepted, exact final proof green, owned tree clean, no running child agents

## Tasks

### T1: Weapon impact feedback owner

- objective: add one player-owned pooled feedback component with exact tracer and mark emission contracts; no weapon gameplay mutation
- covered_requirements: visible shotgun pellets; 30-second shotgun and rocket marks
- owner: worker-T1 (luna_max)
- dependencies: None
- parallel_contract: produces fixed public API for T2/T3 and serialized surface/constants for T11/T14; owns new file only
- owns: `Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactFeedback.cs`, `Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactFeedback.cs.meta`
- protected: all weapon call sites, builder files, materials, prefabs, scenes
- read_paths: `Assets/_Game/Scripts/Runtime/Feedback/PlayerPresentation.cs` -> component conventions; `Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs` -> pellet endpoints; `Assets/_Game/Scripts/Runtime/Weapons/RocketProjectile.cs` -> detonation paths
- validation_environment: source-only worker checkout; no Unity process
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_execution: None
- implementation: create sealed `WeaponImpactFeedback` in `RocketFooxball.Runtime.Feedback` with serialized `ParticleSystem shotgunPellets` and `ParticleSystem impactMarks`. Expose `void EmitTracer(Vector3 origin, Vector3 end)`, `void EmitShotgunMark(Vector3 point, Vector3 normal)`, and `void EmitRocketMark(Vector3 point, Vector3 normal)`. Reject nonfinite values and direction/normal squared magnitude at or below `0.000001`. Tracer: `direction=(end-origin).normalized`; `adjustedOrigin=origin+direction*min(0.45,distance*0.5)`; `remainingDistance=Distance(adjustedOrigin,end)`; `lifetime=max(0.04,remainingDistance/120)`; `velocity=(end-adjustedOrigin)/lifetime`; emit one world-space particle at adjusted origin with exact lifetime/velocity. Mark: normalize normal; position `point+normal*0.015`; orient mesh plane normal to supplied normal; start size `0.16` for shotgun or `1.15` for rocket; lifetime `30`; zero velocity. Missing serialized system logs exact composition error once, disables component, and emits nothing. Particle lifetime uses scaled time; component/reset API never clears active particles.
- done when: API compiles by inspection; invalid input is inert; tracer endpoint math exact; mark type controls only diameter
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactFeedback.cs Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactFeedback.cs.meta` -> exit 0 and only owned paths selected
- review_focus: endpoint overshoot -> misleading hit feedback; per-hit GameObject allocation -> 30-second load regression; reset clearing -> marks violate duration; missing-ref fallback search -> composition ownership regression
- review_checkpoint: CP1

### T2: Shotgun traces and far damage

- objective: emit one visual tracer per existing hitscan pellet, mark accepted static impacts, and double only far-range multiplier
- covered_requirements: visible pellets; 30-second shotgun marks; 2x distant shotgun power
- owner: worker-T2 (luna_max)
- dependencies: `CP1 -> accepted WeaponImpactFeedback API`
- parallel_contract: consumes T1 API; disjoint from rocket, participant, HUD, bot, and movement siblings
- owns: `Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs`, `Assets/_Game/Scripts/Runtime/Weapons/ShotgunDamageRules.cs`, `Assets/_Game/Scripts/Tests/EditMode/ShotgunDamageRulesTests.cs`
- protected: `WeaponImpactFeedback.cs`, participant state, builder files, generated assets
- read_paths: `Assets/_Game/Scripts/Runtime/Weapons/ShotgunSpreadPattern.cs` -> eight-ray contract; `Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs` -> shell ownership; `Assets/_Game/Scripts/Runtime/Ball/BallMotor.cs` -> existing impulse order
- validation_environment: source-only worker checkout; no Unity process
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: add serialized `WeaponImpactFeedback impactFeedback`. For every current pellet ray, preserve hit detection, participant damage aggregation, ball impulse, hit-confirm, and immediate hitscan order. Emit tracer from shot origin to `hit.point` or `origin+direction*30`. Emit shotgun mark only when ray hit exists, collider is non-null/non-trigger, and `attachedRigidbody==null`; dynamic player, ball, pickup, trigger, and miss never mark. Change only `DefaultFarMultiplier` from `0.20` to `0.40`. Preserve 6/16/30 m knots and medium `0.55`; update exact tests for multiplier `0.475` at 23 m, `0.40` at 30 m, aggregate damage `51.68` at 23 m and `43.52` at 30 m, plus monotonic/clamp cases.
- done when: eight gameplay rays remain authoritative; each ray emits one tracer; qualifying hits mark; close/medium damage unchanged; far damage exactly doubled
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs Assets/_Game/Scripts/Runtime/Weapons/ShotgunDamageRules.cs Assets/_Game/Scripts/Tests/EditMode/ShotgunDamageRulesTests.cs` -> exit 0 and only owned paths selected
- review_focus: feedback affecting ray/damage order -> weapon behavior regression; marks on Rigidbody/trigger -> floating artifacts; multiplier change outside far knot -> close lethality regression
- review_checkpoint: CP2

### T3: Rocket surface marks

- objective: pass feedback through launcher/projectile initialization and mark physical rocket detonation surfaces only
- covered_requirements: 30-second rocket explosion marks
- owner: worker-T3 (luna_max)
- dependencies: `CP1 -> accepted WeaponImpactFeedback API`
- parallel_contract: consumes T1 API; disjoint from shotgun and other siblings
- owns: `Assets/_Game/Scripts/Runtime/Weapons/RocketLauncher.cs`, `Assets/_Game/Scripts/Runtime/Weapons/RocketProjectile.cs`
- protected: `WeaponImpactFeedback.cs`, `ExplosionResolver.cs`, explosion VFX prefab/builder, generated assets
- read_paths: `Assets/_Game/Scripts/Runtime/Weapons/RocketProjectile.cs/TryDetonate` -> accepted detonation guard; `Assets/_Game/Scripts/Runtime/Weapons/RocketLauncher.cs/FireRocket` -> initialization owner
- validation_environment: source-only worker checkout; no Unity process
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: add serialized launcher feedback reference. Add primary projectile initializer accepting `WeaponImpactFeedback`; keep both existing public initializer signatures and delegate with null feedback for compatibility. Launcher uses new initializer. Swept collision passes `hit.point/hit.normal`; collision callback passes first contact point/normal, with point from projectile and normal `-flightDirection.normalized` only when contact list is empty. Emit rocket mark after detonation acceptance and before teardown only for non-trigger collider with `attachedRigidbody==null`. Trigger detonation and lifetime expiry never mark. Preserve owner collision ignore, single-detonation guard, explosion dispatch, tracking, and destruction.
- done when: accepted static physical impact emits one rocket mark; trigger/lifetime/dynamic impacts emit none; legacy initializers behave unchanged without feedback
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Weapons/RocketLauncher.cs Assets/_Game/Scripts/Runtime/Weapons/RocketProjectile.cs` -> exit 0 and only owned paths selected
- review_focus: mark before detonation guard -> duplicates; removed initializer -> compatibility break; trigger mark -> non-arena artifact; altered explosion order -> gameplay regression
- review_checkpoint: CP3

### T4: Empty-ammo shotgun visibility

- objective: make local first-person shotgun availability follow authoritative ammo state immediately while retaining world ownership visibility
- covered_requirements: hide shotgun from view when ammo depleted
- owner: worker-T4 (luna_max)
- dependencies: None
- parallel_contract: owns participant/presentation state seam; no HUD, weapon-fire, or builder edits
- owns: `Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs`, `Assets/_Game/Scripts/Runtime/Feedback/PlayerPresentation.cs`
- protected: `ShotgunWeapon.cs`, HUD, prefab builder, generated Player prefab
- read_paths: `Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs/TryConsumeShotgunShell` -> last-shell transition; `Assets/_Game/Scripts/Runtime/Feedback/PlayerPresentation.cs/RefreshShotgunVisibility` -> renderer owner
- validation_environment: source-only worker checkout; no Unity process
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: add private presentation state `shotgunAmmoAvailable` and public setter `SetShotgunAmmoAvailable(bool)` that refreshes only on change. FPS visibility becomes `localMode && alive && shotgunOwned && shotgunAmmoAvailable`; world visibility stays `alive && shotgunOwned`. Participant pushes `ShotgunAmmo>0` after Awake/configuration, shotgun collection, ammo collection, successful shell consumption, kickoff/death clear, respawn/reset, and read-model restoration paths. Last shell hides FPS model in same fixed-step fire path; later ammo pickup restores it. Pause changes no ownership/ammo presentation state. Preserve renderer arrays, local-owner routing, animations, and world remote visibility.
- done when: zero ammo hides only local FPS shotgun immediately; positive ammo plus ownership restores it; death/reset/ownership transitions remain coherent
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs Assets/_Game/Scripts/Runtime/Feedback/PlayerPresentation.cs` -> exit 0 and only owned paths selected
- review_focus: world model hidden at zero ammo -> remote readability regression; missing last-shell refresh -> request unmet; pause clearing state -> incorrect reappearance
- review_checkpoint: CP4

### T5: Two-pickup bot runtime contracts

- objective: expand bot pickup identity/count math for two shotgun pickups without changing target scoring policy
- covered_requirements: two shotgun pickups
- owner: worker-T5 (luna_max)
- dependencies: None
- parallel_contract: runtime pickup array/index owner; T9 catalog and T12 builder consume exact order after CP5
- owns: `Assets/_Game/Scripts/Runtime/Bots/BotPerception.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotTeamRoleCoordinator.cs`
- protected: editor catalog/pipeline, bot target scoring, generated scene
- read_paths: `Assets/_Game/Scripts/Runtime/Bots/BotPerception.cs` -> fixed arrays and kind classification; `Assets/_Game/Scripts/Runtime/Bots/BotTeamRoleCoordinator.cs` -> stable target keys
- validation_environment: source-only worker checkout; no Unity process
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: set pickup count `6`. Consume serialized array order exactly: health IDs `0,1`; shotgun IDs `2,3`; ammo IDs `4,5`. Map shotgun target index `stableId-2`; ammo target index `stableId-4`; retain health mapping. Preserve target scores, memory timing, nearest/tie behavior, role ownership, and invalid-ID rejection. No scene discovery or array resizing at runtime.
- done when: both shotgun pickups are independently perceived/addressable; ammo indices remain `0,1`; six-entry contracts fail closed when miswired
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Bots/BotPerception.cs Assets/_Game/Scripts/Runtime/Bots/BotTeamRoleCoordinator.cs` -> exit 0 and only owned paths selected
- review_focus: stable-ID collision -> bots target wrong pickup; runtime resizing/search -> architecture regression; scoring changes -> unrelated bot behavior drift
- review_checkpoint: CP5

### T6: HUD readability and damage arc

- objective: replace live panel with centered numeric score, draw continuous circular damage direction, and scale shotgun icon exactly 3x
- covered_requirements: circular damage segment; visible BLUE score; top-center `blue : red`; 3x shotgun icon
- owner: worker-T6 (luna_max)
- dependencies: None
- parallel_contract: owns HUD runtime/rules/tests only; no participant, builder, or generated asset mutation
- owns: `Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs`, `Assets/_Game/Scripts/Runtime/Hud/DamageIndicatorRules.cs`, `Assets/_Game/Scripts/Tests/EditMode/DamageIndicatorRulesTests.cs`
- protected: MatchController, DebugHUD, UI assets, scene/prefab builder
- read_paths: `Assets/_Game/Scripts/Runtime/Participants/ParticipantContracts.cs/ParticipantDamageEvent` -> source position; `Assets/_Game/Scripts/Runtime/Match/MatchController.cs` -> score owner; attached damage/score images -> visual intent only
- validation_environment: source-only worker checkout; no Unity process
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: replace cardinal sector resolution with clockwise degrees `Repeat(Atan2(Dot(victimToSource,right),Dot(victimToSource,forward))*Rad2Deg,360)`: front `0`, right `90`, back `180`, left `270`. Reject nonfinite/zero axes or zero projected direction. Latest valid damage stores angle and resets unscaled `0.75` timer; hold `0.50`, fade linearly `0.25`; invalid new direction leaves active cue untouched. Draw while local participant alive on Live/MatchTable/Go: 60-degree red arc centered reference `(960,540)`, radius `64`, thickness `8`, 12 joined white-texture segments, color `(1,0.16,0.12,opacity)`; cache GUI resources, allocate nothing per frame, restore matrix/color. Remove live clock and opaque live score panel. Draw blue score `Rect(848,24,96,64)`, colon `Rect(944,24,32,64)`, red score `Rect(976,24,96,64)`, bold centered 48 px, existing blue/red colors, numbers only. Keep goal/death/final screens. Scale shotgun icon base 128x64 to 384x192, panel `(1090,822,794,220)`, icon origin `(1112,842)`, labels x `1516`, and derive silhouette parts by factor 3. Expand rules tests for cardinal/diagonal/pitched camera, invalid data, repeat behavior, and opacity boundaries.
- done when: live score cannot overlap top-left debug window; direction is continuous; arc matches reference intent; shotgun icon dimensions are exactly 3x
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs Assets/_Game/Scripts/Runtime/Hud/DamageIndicatorRules.cs Assets/_Game/Scripts/Tests/EditMode/DamageIndicatorRulesTests.cs` -> exit 0 and only owned paths selected
- review_focus: score still inside old panel -> visibility failure; wrong angle handedness -> misleading damage; GUI state/allocation leak -> frame regression; summary-screen change -> scope regression
- review_checkpoint: CP6

### T7: Rocket-jump direction/lift and F-kick propulsion

- objective: make underfoot rocket thrust follow effective move intent, redirect high-speed ground momentum upward, and double F-kick burst under existing cap
- covered_requirements: higher fast ground rocketjump; backward rocketjump symmetry; 2x directional F kick including down
- owner: worker-T7 (luna_max)
- dependencies: None
- parallel_contract: produces accepted `PlayerMotor` move-intent API consumed by T8; owns movement/explosion math before T8 overlap
- owns: `Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs`, `Assets/_Game/Scripts/Runtime/Movement/PlayerMotorDefaults.cs`, `Assets/_Game/Scripts/Runtime/Weapons/BlastMath.cs`, `Assets/_Game/Scripts/Runtime/Weapons/ExplosionResolver.cs`, `Assets/_Game/Scripts/Tests/EditMode/BlastMathTests.cs`, `Assets/_Game/Scripts/Tests/EditMode/DashKickRulesTests.cs`
- protected: BallMotor, BallKick, input actions, builder files, generated Player prefab/scene
- read_paths: `Assets/_Game/Scripts/Runtime/Input/PlayerInputReader.cs` -> sampled move intent; `Assets/_Game/Scripts/Runtime/Weapons/BallKick.cs` -> F aim direction; `Assets/_Game/Scripts/Runtime/Movement/MovementMath.cs` -> cap behavior
- validation_environment: source-only worker checkout; no Unity process
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: store read-only `CurrentEffectiveMoveIntent` in `PlayerMotor`. Update on valid programmatic `SetMoveIntent` and fixed-step device sample before movement composition. Clear on disable, simulation disable, `ClearQueuedState`, and reset; preserve during pause. Add pure BlastMath planar resolver: project player forward to XZ and normalize; right=`Cross(up,forward)`; combine `right*move.x+forward*move.y`; normalize; nonfinite/zero intent falls back to forward. `ExplosionResolver` passes this direction for underfoot impulse. Replace redirect with `speedT=Clamp01((horizontalSpeed-baseSpeed)/max(softCap-baseSpeed,Epsilon))`, `redirectT=speedT*Clamp01(highSpeedRedirect)`, `forwardScale=baseForwardScale*(1-redirectT)`, `upwardScale=baseUpwardScale+baseForwardScale*redirectT`. At strength 24 this preserves low-speed `13.5` forward/`24` up and yields high-speed `0` forward/`37.5` up; forward/back are equal opposites. Set `PlayerMotorDefaults.DashBurstSpeed=24`; retain cap `30`, duration, steering, cooldown, grounded horizontal projection, and airborne full camera direction including down. Tests cover forward/back symmetry, diagonal normalization, zero fallback, redirect endpoints, and downward 24-unit burst under cap.
- done when: W/S underfoot impulses mirror; high-speed ground blast converts removed forward scale into lift; F contribution doubles without cap/cooldown change
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs Assets/_Game/Scripts/Runtime/Movement/PlayerMotorDefaults.cs Assets/_Game/Scripts/Runtime/Weapons/BlastMath.cs Assets/_Game/Scripts/Runtime/Weapons/ExplosionResolver.cs Assets/_Game/Scripts/Tests/EditMode/BlastMathTests.cs Assets/_Game/Scripts/Tests/EditMode/DashKickRulesTests.cs` -> exit 0 and only owned paths selected
- review_focus: intent cleared on pause -> discontinuity; S fallback to facing -> requirement failure; redirect adds planar energy -> high-speed forward regression; cap raised -> unsafe speed expansion
- review_checkpoint: CP7

### T8: Player-ball collision transfer

- objective: retain player momentum against dynamic ball contact and transfer bounded relative closing speed once per fixed step
- covered_requirements: softer player stop; player speed/momentum translated into ball movement
- owner: worker-T8 (luna_max)
- dependencies: `CP7 -> accepted PlayerMotor file/API; serialization avoids concurrent edit`
- parallel_contract: fan-in collision seam after T7; no sibling overlap
- owns: `Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs`, `Assets/_Game/Scripts/Runtime/Ball/BallMotor.cs`, `Assets/_Game/Scripts/Runtime/Ball/BallMotionRules.cs`, `Assets/_Game/Scripts/Runtime/Ball/BallMotionRules.cs.meta`, `Assets/_Game/Scripts/Runtime/Physics/GamePhysicsSettings.cs`, `Assets/_Game/Scripts/Tests/EditMode/BallMotionRulesTests.cs`, `Assets/_Game/Scripts/Tests/EditMode/BallMotionRulesTests.cs.meta`
- protected: BallKick, physics material, ball prefab, scoring/match code, T7 intent/rocket-jump formulas
- read_paths: accepted T7 `PlayerMotor.cs` -> merge base; `Assets/_Game/Scripts/Runtime/Weapons/BallKick.cs` -> dash transfer owner/order; `Assets/_Game/Scripts/Runtime/Ball/BallMotor.cs` -> impulse channels/reset
- validation_environment: integrated source checkout after CP7; no Unity process
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: add immutable `PlayerCollisionResolution` carrying `ControllerColliderHit Hit`, incoming/resolved player velocities, attached-body velocity, and `WasDashing`. Keep existing `CollisionHit` event for BallKick. Static contact keeps full inward clipping. Dynamic nonkinematic contact uses `toward=-normal`; `playerInto=max(0,dot(incoming,toward))`; `closing=max(0,dot(incoming-bodyVelocity,toward))`; `removal=toward*min(playerInto,closing)*0.65`; `resolved=incoming-removal`, retaining 35% for stationary head-on. For active dash, compute `baseIncoming=incoming-dashContribution`, resolve total/base through identical rule, assign `dashContribution=resolvedTotal-resolvedBase`. Assign motor state, then raise `DynamicCollisionResolved`. `BallMotor` gets `[DefaultExecutionOrder(100)]`; BallKick remains `-100`. Subscribe direct participant motors, record touch before impulse gates, skip ordinary assist when payload `WasDashing`. Keep HashSet participant IDs until BallMotor FixedUpdate; accept first callback per participant, then clear after applying. `effectiveBallVelocity=body.linearVelocity+queuedAssist`; `closing=max(0,dot(incoming-effectiveBallVelocity,toward))`; `candidate=toward*min(12,closing*0.65)`; `queuedAssist=ClampMagnitude(queuedAssist+candidate,12)`. Apply queued assist once in FixedUpdate; reset/disable clears queue and dedupe. Put retention `0.35`, transfer `0.65`, per-contact/aggregate cap `12` in `GamePhysicsSettings`. Preserve external impulse channel, ball cap/mass/damping, kick, touch/scoring ownership. Pure tests cover head-on, glancing, following, opposing, separating, vertical, invalid, capped aggregate, per-participant dedupe, and dash decomposition.
- done when: base-speed stationary head-on resolves player `10 -> 3.5` and ball `0 -> 6.5`; speed-30 ball assist caps 12; repeated callback cannot multiply transfer; dash remains BallKick-owned
- checks: proof: `git diff --check -- Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs Assets/_Game/Scripts/Runtime/Ball/BallMotor.cs Assets/_Game/Scripts/Runtime/Ball/BallMotionRules.cs Assets/_Game/Scripts/Runtime/Ball/BallMotionRules.cs.meta Assets/_Game/Scripts/Runtime/Physics/GamePhysicsSettings.cs Assets/_Game/Scripts/Tests/EditMode/BallMotionRulesTests.cs Assets/_Game/Scripts/Tests/EditMode/BallMotionRulesTests.cs.meta` -> exit 0 and only owned paths selected
- review_focus: post-collision velocity used as input -> old hard-stop bug persists; dedupe cleared before callbacks -> duplicate impulse; dash ordinary assist -> double transfer; static clipping softened -> wall tunneling risk
- review_checkpoint: CP8

### T9: Shared builder contract and catalogs

- objective: centralize exact requested constants, output declarations, pickup order, geometry, and contract-version changes before parallel builders
- covered_requirements: all builder-authored values and generated inventory
- owner: worker-T9 (luna_max)
- dependencies: `JOIN1 -> runtime APIs/constants and tests accepted`
- parallel_contract: produces immutable constants/catalog/output names consumed by T10-T14; sole owner of shared contract files
- owns: `Assets/_Game/Editor/MovementLab/MovementLabContract.cs`, `Assets/_Game/Editor/MovementLab/MovementLabContractCatalog.cs`
- protected: all pipelines, validators, workflow scripts, generated outputs
- read_paths: T1-T8 accepted sources -> serialized fields/constants; current contract output arrays -> inventory; current imported recess bounds -> 10 m constraint
- validation_environment: source-only integrated checkout; no Unity process
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: add new material paths `Assets/_Game/Materials/ShotgunPellet.mat` and `Assets/_Game/Materials/WeaponImpactMark.mat` to `MaterialPrefabOutputs` and `GeneratedYamlAssetPaths`; add feedback/particle constants from T1, ball contact constants from T8, and bump affected material-prefab/gameplay-scene serialized contract versions. Define shotgun pickups exactly `ShotgunPickup_North (0,1.10,-14), identity` then `ShotgunPickup_South (0,1.10,14), yaw180`. Set `PlayerSpawnOffset=18`, slot root Y `PlayerControllerSkinWidth=0.08`, recovery threshold `-PlayerControllerSkinWidth`. Define goal opening height 9 and derived values. Replace ramp box specifications with prism constants: length20, width18, height `5.358984`, west center `(-30,0,2)` yaw0, east `(30,0,-2)` yaw180. Catalog participant slots reference `PlayerSpawnOffset` and skin constant, retain Z `0/+/-10` and center-facing rotations. Preserve GUID-bearing existing paths and all unrelated values.
- done when: every downstream builder value is one named contract constant/catalog record; new materials are declared outputs; no duplicated spawn literals remain
- checks: proof: `git diff --check -- Assets/_Game/Editor/MovementLab/MovementLabContract.cs Assets/_Game/Editor/MovementLab/MovementLabContractCatalog.cs` -> exit 0 and only owned paths selected
- review_focus: duplicated constants -> builder/validator drift; missing output declaration -> comparator failure; changed unrelated contract -> generated churn
- review_checkpoint: CP9

### T10: Closed ramps, higher goals, and spawn safety geometry

- objective: build exact closed prism ramps and all dependent 9 m goal geometry from T9 contracts
- covered_requirements: closed/repositioned ramps; higher goals; spawn-under-floor prevention geometry
- owner: worker-T10 (luna_max)
- dependencies: `CP9 -> accepted geometry constants/catalog`
- parallel_contract: owns arena pipeline only; T11/T12/T13 files disjoint
- owns: `Assets/_Game/Editor/MovementLab/MovementLabArenaPipeline.cs`
- protected: contract/catalog, validator, scene, lighting outputs
- read_paths: accepted T9 contracts -> exact geometry; existing `CreateSolid`/marking mesh helpers -> material/static conventions
- validation_environment: source-only checkout; no Unity process
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: exclude ramps from cube loop. Build shared six-vertex canonical prism with `H=5.358984`: A`(-10,0,-9)`, B`(-10,H,-9)`, C`(10,0,-9)`, D`(-10,0,9)`, E`(-10,H,9)`, F`(10,0,9)`; triangles `A-B-C`, `D-F-E`, `A-C-F`, `A-F-D`, `A-D-E`, `A-E-B`, `B-E-F`, `B-F-C`; UV0 each vertex `(x/20+0.5,z/18+0.5)`; recalculate normals/bounds; `Unwrapping.GenerateSecondaryUVSet`; shared MeshFilter/MeshCollider mesh; floor material, ball physics material, static object. Instantiate exact T9 transforms; high edge faces goalward. Goal trigger/shield/visual/cue center Y `4.5`, opening size `36x9`, max height `9`; side frames x `+/-18.5`, center Y `4.5`, size `1x9x1`; lintel Y `9.5`, size `38x1x1`; recess sides center `(x=+/-18.5,y=4.5,z=4.5)`, size `1x9x9`; recess back `(0,4.5,9)`, size `37x9x1`; floor unchanged; goal-opening containment center Y `4.5`, size `1x10x38`. Keep goal axes, width/depth, shields, scoring plane, imported architecture, and material routing.
- done when: no accessible space exists under ramps; ramp transforms/mesh are deterministic; every opening-dependent goal/containment dimension is 9 m-compatible
- checks: proof: `git diff --check -- Assets/_Game/Editor/MovementLab/MovementLabArenaPipeline.cs` -> exit 0 and only owned path selected
- review_focus: open triangle face/wrong winding -> player/ball enters ramp; missing UV2/static -> lighting regression; partial goal resize -> invisible blocker or rejected shot
- review_checkpoint: CP10

### T11: Player prefab feedback and physics composition

- objective: author new materials/ParticleSystems and persist all T1-T4/T7-T8 direct references and constants in Player prefab
- covered_requirements: weapon feedback persistence; ammo visibility wiring; F-kick and ball-contact serialized tuning
- owner: worker-T11 (luna_max)
- dependencies: `CP9 -> accepted paths/constants`; `CP1-CP4,CP7,CP8 -> accepted serialized runtime surfaces`
- parallel_contract: owns prefab/material pipeline only; T10/T12/T13 disjoint
- owns: `Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs`
- protected: runtime sources, contract/catalog, validator, materials/prefabs on disk
- read_paths: accepted T1-T4/T7-T8 types -> serialized fields; existing particle/material helpers -> URP shader conventions; existing cue mesh -> mark mesh provenance
- validation_environment: source-only checkout; no Unity process
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: author `ShotgunPellet.mat` with existing `RetroAdditiveParticle` shader and rocket-glow texture; author `WeaponImpactMark.mat` with existing `RetroParticle` shader. Under each Player create `WeaponImpactFeedback/ShotgunPellets` and `ImpactMarks`. Configure world simulation, tracer max 32/stretched billboard/visible size and feedback-controlled lifetime/velocity; marks max 512/mesh renderer using `BlueCircleCueMesh.asset`, feedback-controlled size/lifetime, zero emission rate. Wire one feedback component to shotgun and launcher; wire participant/presentation unchanged direct ownership. Serialize `DashBurstSpeed=24` and T8 GamePhysicsSettings contact constants; retain dash cap 30 and ball prefab physical values. Reuse valid existing objects/components on rebuild; do not accumulate children/material subassets.
- done when: Player prefab source contains nonzero serialized refs; material/mesh/particle values equal contracts; rebuild is idempotent
- checks: proof: `git diff --check -- Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs` -> exit 0 and only owned path selected
- review_focus: null/fileID-zero ref -> feedback absent after reload; wrong simulation space/renderer -> marks move with player; duplicated child -> rebuild growth; physics value drift -> feel mismatch
- review_checkpoint: CP11

### T12: Bot graph and pickup wiring

- objective: serialize six pickups and extend navigation graph to second shotgun position without renumbering existing nodes
- covered_requirements: two shotgun pickups usable by bots
- owner: worker-T12 (luna_max)
- dependencies: `CP9 -> accepted pickup catalog`; `CP5 -> accepted six-ID runtime contract`
- parallel_contract: owns bot editor pipeline only; siblings disjoint
- owns: `Assets/_Game/Editor/MovementLab/MovementLabBotPipeline.cs`
- protected: bot runtime, contract/catalog, scene, validator
- read_paths: accepted T5/T9 contracts -> identity/order; existing node/edge construction -> stable graph topology
- validation_environment: source-only checkout; no Unity process
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: serialize pickups in exact order health 0-1, shotgun 2-3, ammo 4-5. Append node ID `34` at `(0,0,-14)` with Floor area/radius 2; preserve IDs 0-33; connect node 34 bidirectionally to nodes 6 and 7 with existing walk width. Set graph counts to 35 nodes and 102 directed edges. Retain existing node 21 at `(0,0,14)` and all current edges. Keep team coordinators, shields, route settings, and target scores unchanged.
- done when: graph validates exact 35/102; both shotgun pickup targets have reachable matching nodes; existing graph identities unchanged
- checks: proof: `git diff --check -- Assets/_Game/Editor/MovementLab/MovementLabBotPipeline.cs` -> exit 0 and only owned path selected
- review_focus: renumbered graph -> route regression; wrong stable order -> pickup target mismatch; one-way new links -> reachability asymmetry
- review_checkpoint: CP12

### T13: Generated workflow inventory guard

- objective: make comparator/workflow inventory authoritative for both new material pairs and preserve red-baseline detection
- covered_requirements: generated-output safety for weapon feedback materials
- owner: worker-T13 (luna_max)
- dependencies: `CP9 -> accepted new material paths`
- parallel_contract: PowerShell tooling boundary; owns workflow, harness assertion, and red fixture only
- owns: `Tools/Validation/Invoke-MovementLabWorkflow.ps1`, `Tools/Tests/MovementLabHarness.Tests.ps1`, `Tools/Tests/Fixtures/red-workflow.ps1.txt`
- protected: comparator script, C# builder/runtime, generated outputs; absent ReflectionProbe-3 Appendix-A entries remain untouched
- read_paths: `Tools/Validation/Compare-GeneratedYaml.ps1/Get-AppendixAPaths` -> inventory authority; current TeamRedTrail guard -> positive current-vs-red pattern
- validation_environment: PowerShell parse/harness only; no Unity process or project lock
- unity_mutation: false
- expensive_proof_owner: worker-T13 (luna_max)
- expensive_proof_execution: same_dispatch
- implementation: add `ShotgunPellet.mat`, its `.meta`, `WeaponImpactMark.mat`, and its `.meta` exactly once to `$script:AppendixAPaths`; keep `$script:BuilderOutputContract=$script:AppendixAPaths`. Add harness assertions that each current Appendix/source contains exactly one entry and red builder inventory contains zero. Keep red fixture deliberately missing all four entries so guard proves failure against stale workflow. Do not repair dormant absent ReflectionProbe-3 entries because candidate intersection already excludes them and task adds no related failure.
- done when: current harness passes; red fixture fails new inventory guard; workflow and fixture parse; comparator can select both new pairs
- checks: proof: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1` -> exit 0 in under 90 seconds, current cases pass, red baseline remains rejected, no Unity process/lock
- review_focus: pair absent/duplicated -> uncovered generation; fixture updated to green -> guard loses discriminatory power; unrelated workflow edit -> proof-risk expansion
- review_checkpoint: CP13

### T14: Stage digest and semantic validator integration

- objective: bind every changed source to authoritative stages and validate all persisted runtime/editor contracts after reload
- covered_requirements: complete builder staleness detection and semantic proof
- owner: worker-T14 (luna_max)
- dependencies: `JOIN2 -> runtime contracts, shared constants, all pipelines, and workflow inventory accepted`
- parallel_contract: fan-in integration owner; no concurrent editor/tooling writer
- owns: `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs`, `Assets/_Game/Editor/MovementLab/MovementLabValidator.cs`
- protected: all runtime/pipeline/tooling sources and generated outputs
- read_paths: T1-T13 accepted files -> exact producers/serialized contracts; contract output arrays -> stage outputs; prefab/arena/bot validator helpers -> persisted checks
- validation_environment: source-only integrated checkout; no Unity process
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: add `WeaponImpactFeedback.cs/.meta` to MaterialPrefab and GameplayScene inputs where consumed; add `BallMotor.cs`, `BallMotionRules.cs/.meta`, and `GamePhysicsSettings.cs` to MaterialPrefab inputs; add every changed runtime/contract/catalog/pipeline source to owning MaterialPrefab/GameplayScene digest. Preserve Assets-only meta expansion; never invent ProjectSettings metas. Validator requires Player feedback refs with nonzero serialized fileIDs and Player-prefab provenance; exact ParticleSystem simulation/max/render/material/mesh contracts; exact feedback lifetimes/sizes; shotgun/launcher refs; ammo presentation wiring; dash/contact serialized values; BallMotor execution order 100; two mirrored pickup transforms/order; six pickup arrays; graph 35/102; 9 m goal contracts; exact ramp transforms/vertices/triangles/UV2/shared MeshCollider/static/material; participant slot Y/X and recovery threshold. Extend recovery spawn validation with trigger-ignoring capsule overlap against arena collision layers; every slot must be clear and capsule bottom at/above floor. Validator scope remains owning subtree and does not enforce render budgets.
- done when: any stale source invalidates proper stage; persisted scene/prefab validator rejects each broken requested contract and accepts exact composition
- checks: proof: `git diff --check -- Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs Assets/_Game/Editor/MovementLab/MovementLabValidator.cs` -> exit 0 and only owned paths selected
- review_focus: omitted stage input -> false builder no-op; runtime-only null check -> reload bug survives; overbroad collider query -> valid pickup/participant rejected; unrelated owner validation -> false failures
- review_checkpoint: CP14

### T15: Authoritative generation and persisted proof

- objective: sole Unity mutation owner regenerates exact declared outputs, proves both lighting branches through separate persisted validation, and returns comparator-ready evidence
- covered_requirements: persisted implementation and generated-output integrity
- owner: worker-T15 (luna_max)
- dependencies: `JOIN3 -> CP1-CP14 accepted; all fixes accepted; integrated source committed as sourceFreezeSha; owned product status clean`
- parallel_contract: None; automated-generation workload exception; one Unity lease and one generated mutation owner
- owns: exact paths under `Generated Output Ownership`
- protected: all source/raw assets; every path outside Appendix-A union; every Appendix-A entry outside declared ownership must remain byte-identical
- read_paths: `Assets/_Game/Editor/MovementLabBuilder.cs` -> authoritative commands; `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs` -> stage outputs; `Tools/Validation/Invoke-MovementLabWorkflow.ps1` -> lease/result grammar; `Tools/Validation/Compare-GeneratedYaml.ps1` -> comparator contract
- validation_environment: writable short project path under `C:\wt\<id>`; private warm `Library`; write-probed evidence alias `C:\wt\<id>e`; interactive Editor closed; one waited hidden Unity process at a time
- unity_mutation: true
- expensive_proof_owner: worker-T15 (luna_max)
- expensive_proof_execution: same_dispatch
- implementation: verify HEAD=`sourceFreezeSha`, owned status clean, short/evidence paths writable, no Unity owner/lock. Run fresh harness, then exact targeted EditMode filter listed below as first Unity process/compile proof. Run fresh harness again. Run exactly one `ProductionPrepare`; workflow's post-assemble probe and source-defined exact skip marker alone classify production-bake: marker plus bake_count 0 -> `reused`; absent marker plus bake_count 1 -> `executed`. Require top status complete, exact sourceFreezeSha, current production profile/manifest, authoritative build/probe rows, valid release. Do not claim reused branch ran pre-bake semantic gate. Before comparator/review, run separate nonmutating `ProductionValidate` in new Unity process/evidence directory against dirty generated worktree; require production-validator executed, bake_count 0, no ProductionBake invocation, current production probe, empty per-invocation changedGeneratedPaths, valid release. Run comparator Base sourceFreezeSha Head WORKTREE with FailOnDangling; require `COVERAGE:`, `SEMANTIC:`, `DANGLING:`, `GUID:`, `PAIRS:`, `UNSUPPORTED:`; reject incomplete selection, unsupported path, increased dangling, existing GUID churn, broken pairs, or changed path outside declared union. Orchestrator commits comparator-selected generated paths only as `chore: regenerate MovementLab outputs` when count >0; count 0 creates no regeneration commit and sourceFreezeSha remains boundary. No direct generated edits. Semantic defect routes to fresh source/builder fix worker, new sourceFreezeSha, and full T15/final replay.
- done when: targeted XML green; ProductionPrepare branch evidence valid; separate persisted validator green for either branch; actual generated changes exact-covered and ready for CP15
- checks: proof: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1` before targeted Unity test and again before ProductionPrepare -> each exit 0 in under 90 seconds; no Unity process/lock
- checks: proof: hidden waited Unity EditMode run without `-quit`, filter `RocketFooxball.Tests.EditMode.BlastMathTests;RocketFooxball.Tests.EditMode.DashKickRulesTests;RocketFooxball.Tests.EditMode.ShotgunDamageRulesTests;RocketFooxball.Tests.EditMode.ShotgunAmmoRulesTests;RocketFooxball.Tests.EditMode.DamageIndicatorRulesTests;RocketFooxball.Tests.EditMode.BallMotionRulesTests;RocketFooxball.Tests.EditMode.BotPerceptionMemoryTests;RocketFooxball.Tests.EditMode.BotRoleCoordinatorRulesTests;RocketFooxball.Tests.EditMode.BotNavigationRulesTests;RocketFooxball.Tests.EditMode.ParticipantRecoveryRulesTests` -> exit 0; XML exists/parses; total>0, failed=0, inconclusive=0; process/lock released
- checks: `check_id=gameplay-production-prepare; tier=production-final; owner=worker-T15; expected_status=complete; command=powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode ProductionPrepare -ProjectPath C:\wt\<id> -EvidenceRoot C:\wt\<id>e\prepare -AttemptId gameplay-prepare -TimeoutSeconds 3600; mutates_project=true; input_paths=[Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactFeedback.cs, Assets/_Game/Scripts/Runtime/Feedback/WeaponImpactFeedback.cs.meta, Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs, Assets/_Game/Scripts/Runtime/Weapons/ShotgunDamageRules.cs, Assets/_Game/Scripts/Runtime/Weapons/RocketLauncher.cs, Assets/_Game/Scripts/Runtime/Weapons/RocketProjectile.cs, Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs, Assets/_Game/Scripts/Runtime/Feedback/PlayerPresentation.cs, Assets/_Game/Scripts/Runtime/Bots/BotPerception.cs, Assets/_Game/Scripts/Runtime/Bots/BotTeamRoleCoordinator.cs, Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs, Assets/_Game/Scripts/Runtime/Hud/DamageIndicatorRules.cs, Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs, Assets/_Game/Scripts/Runtime/Movement/PlayerMotorDefaults.cs, Assets/_Game/Scripts/Runtime/Weapons/BlastMath.cs, Assets/_Game/Scripts/Runtime/Weapons/ExplosionResolver.cs, Assets/_Game/Scripts/Runtime/Ball/BallMotor.cs, Assets/_Game/Scripts/Runtime/Ball/BallMotionRules.cs, Assets/_Game/Scripts/Runtime/Ball/BallMotionRules.cs.meta, Assets/_Game/Scripts/Runtime/Physics/GamePhysicsSettings.cs, Assets/_Game/Editor/MovementLab/MovementLabContract.cs, Assets/_Game/Editor/MovementLab/MovementLabContractCatalog.cs, Assets/_Game/Editor/MovementLab/MovementLabArenaPipeline.cs, Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs, Assets/_Game/Editor/MovementLab/MovementLabBotPipeline.cs, Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs, Assets/_Game/Editor/MovementLab/MovementLabValidator.cs, Tools/Validation/Invoke-MovementLabWorkflow.ps1]; run_point=JOIN3 sourceFreezeSha; evidence=exact SHA, status complete, current production manifest/probe, production-bake row reused with exact marker and bake_count=0 or executed with marker absent and bake_count=1, authoritative build rows, changed paths inside declared ownership, release proof valid`
- checks: `check_id=gameplay-persisted-pre-review; tier=production-final; owner=worker-T15; expected_status=complete; command=powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode ProductionValidate -ProjectPath C:\wt\<id> -EvidenceRoot C:\wt\<id>e\pre-review-validate -AttemptId gameplay-pre-review-validate -TimeoutSeconds 1800; mutates_project=false; input_paths=[dirty authoritative outputs from gameplay-production-prepare]; run_point=T15 after ProductionPrepare before comparator; evidence=exact sourceFreezeSha, production-validator executed, current production profile/manifest, no ProductionBake invocation, bake_count=0, changedGeneratedPaths empty for invocation, semantic validator success, release proof valid`
- checks: proof: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Compare-GeneratedYaml.ps1 -Base <sourceFreezeSha> -Head WORKTREE -FailOnDangling` -> all six headers; every actual change selected; zero unsupported/increased dangling/GUID churn/pair breaks/out-of-union paths
- review_focus: reused branch lacks separate validator -> unproved persisted state; output outside declaration -> builder scope breach; direct YAML/material edit -> authority breach; missing comparator header -> incomplete evidence
- review_checkpoint: CP15

## Generated Output Ownership

T15 may mutate only paths below. `.meta` listed only for Assets paths. ProjectSettings paths have no invented pair.

- prefabs: `Assets/_Game/Prefabs/Player.prefab`, `Assets/_Game/Prefabs/Player.prefab.meta`, `Assets/_Game/Prefabs/Ball.prefab`, `Assets/_Game/Prefabs/Ball.prefab.meta`, `Assets/_Game/Prefabs/Rocket.prefab`, `Assets/_Game/Prefabs/Rocket.prefab.meta`, `Assets/_Game/Prefabs/ExplosionVfx.prefab`, `Assets/_Game/Prefabs/ExplosionVfx.prefab.meta`, `Assets/_Game/Prefabs/HealthPickup.prefab`, `Assets/_Game/Prefabs/HealthPickup.prefab.meta`, `Assets/_Game/Prefabs/ShotgunPickup.prefab`, `Assets/_Game/Prefabs/ShotgunPickup.prefab.meta`, `Assets/_Game/Prefabs/AmmoPickup.prefab`, `Assets/_Game/Prefabs/AmmoPickup.prefab.meta`
- controllers: `Assets/_Game/Animations/WorldCharacter.controller`, `Assets/_Game/Animations/WorldCharacter.controller.meta`, `Assets/_Game/Animations/FpsKick.controller`, `Assets/_Game/Animations/FpsKick.controller.meta`
- materials: `Assets/_Game/Materials/Floor.mat`, `Assets/_Game/Materials/Floor.mat.meta`, `Assets/_Game/Materials/Wall.mat`, `Assets/_Game/Materials/Wall.mat.meta`, `Assets/_Game/Materials/Trim.mat`, `Assets/_Game/Materials/Trim.mat.meta`, `Assets/_Game/Materials/Hazard.mat`, `Assets/_Game/Materials/Hazard.mat.meta`, `Assets/_Game/Materials/Marking.mat`, `Assets/_Game/Materials/Marking.mat.meta`, `Assets/_Game/Materials/Ball.mat`, `Assets/_Game/Materials/Ball.mat.meta`, `Assets/_Game/Materials/Rocket.mat`, `Assets/_Game/Materials/Rocket.mat.meta`, `Assets/_Game/Materials/RocketHot.mat`, `Assets/_Game/Materials/RocketHot.mat.meta`, `Assets/_Game/Materials/ProjectileGlow.mat`, `Assets/_Game/Materials/ProjectileGlow.mat.meta`, `Assets/_Game/Materials/GoalFrame.mat`, `Assets/_Game/Materials/GoalFrame.mat.meta`, `Assets/_Game/Materials/Shield.mat`, `Assets/_Game/Materials/Shield.mat.meta`, `Assets/_Game/Materials/ShieldBlue.mat`, `Assets/_Game/Materials/ShieldBlue.mat.meta`, `Assets/_Game/Materials/ShieldRed.mat`, `Assets/_Game/Materials/ShieldRed.mat.meta`, `Assets/_Game/Materials/ArenaPrimary.mat`, `Assets/_Game/Materials/ArenaPrimary.mat.meta`, `Assets/_Game/Materials/ArenaTrim.mat`, `Assets/_Game/Materials/ArenaTrim.mat.meta`, `Assets/_Game/Materials/ArenaHazard.mat`, `Assets/_Game/Materials/ArenaHazard.mat.meta`, `Assets/_Game/Materials/ArenaGlow.mat`, `Assets/_Game/Materials/ArenaGlow.mat.meta`, `Assets/_Game/Materials/BallSurface.physicMaterial`, `Assets/_Game/Materials/BallSurface.physicMaterial.meta`, `Assets/_Game/Materials/Explosion.mat`, `Assets/_Game/Materials/Explosion.mat.meta`, `Assets/_Game/Materials/ExplosionAdditive.mat`, `Assets/_Game/Materials/ExplosionAdditive.mat.meta`, `Assets/_Game/Materials/ExplosionSparks.mat`, `Assets/_Game/Materials/ExplosionSparks.mat.meta`, `Assets/_Game/Materials/Smoke.mat`, `Assets/_Game/Materials/Smoke.mat.meta`, `Assets/_Game/Materials/ContainmentGridCeiling.mat`, `Assets/_Game/Materials/ContainmentGridCeiling.mat.meta`, `Assets/_Game/Materials/ContainmentGridLongWall.mat`, `Assets/_Game/Materials/ContainmentGridLongWall.mat.meta`, `Assets/_Game/Materials/ContainmentGridEndWall.mat`, `Assets/_Game/Materials/ContainmentGridEndWall.mat.meta`, `Assets/_Game/Materials/RetroSunnySky.mat`, `Assets/_Game/Materials/RetroSunnySky.mat.meta`, `Assets/_Game/Materials/CharacterRed.mat`, `Assets/_Game/Materials/CharacterRed.mat.meta`, `Assets/_Game/Materials/CharacterBlack.mat`, `Assets/_Game/Materials/CharacterBlack.mat.meta`, `Assets/_Game/Materials/CharacterCream.mat`, `Assets/_Game/Materials/CharacterCream.mat.meta`, `Assets/_Game/Materials/CharacterEye.mat`, `Assets/_Game/Materials/CharacterEye.mat.meta`, `Assets/_Game/Materials/WeaponMetal.mat`, `Assets/_Game/Materials/WeaponMetal.mat.meta`, `Assets/_Game/Materials/WeaponDark.mat`, `Assets/_Game/Materials/WeaponDark.mat.meta`, `Assets/_Game/Materials/WeaponAccentCore.mat`, `Assets/_Game/Materials/WeaponAccentCore.mat.meta`, `Assets/_Game/Materials/WeaponAccent.mat`, `Assets/_Game/Materials/WeaponAccent.mat.meta`, `Assets/_Game/Materials/ShotgunMetal.mat`, `Assets/_Game/Materials/ShotgunMetal.mat.meta`, `Assets/_Game/Materials/ShotgunDark.mat`, `Assets/_Game/Materials/ShotgunDark.mat.meta`, `Assets/_Game/Materials/ShotgunAccentCore.mat`, `Assets/_Game/Materials/ShotgunAccentCore.mat.meta`, `Assets/_Game/Materials/ShotgunAccent.mat`, `Assets/_Game/Materials/ShotgunAccent.mat.meta`, `Assets/_Game/Materials/TeamBlue.mat`, `Assets/_Game/Materials/TeamBlue.mat.meta`, `Assets/_Game/Materials/TeamRed.mat`, `Assets/_Game/Materials/TeamRed.mat.meta`, `Assets/_Game/Materials/TeamBlueShield.mat`, `Assets/_Game/Materials/TeamBlueShield.mat.meta`, `Assets/_Game/Materials/TeamRedShield.mat`, `Assets/_Game/Materials/TeamRedShield.mat.meta`, `Assets/_Game/Materials/TeamBlueTrail.mat`, `Assets/_Game/Materials/TeamBlueTrail.mat.meta`, `Assets/_Game/Materials/TeamRedTrail.mat`, `Assets/_Game/Materials/TeamRedTrail.mat.meta`, `Assets/_Game/Materials/HealthPickup.mat`, `Assets/_Game/Materials/HealthPickup.mat.meta`, `Assets/_Game/Materials/AmmoShell.mat`, `Assets/_Game/Materials/AmmoShell.mat.meta`, `Assets/_Game/Materials/ShotgunPellet.mat`, `Assets/_Game/Materials/ShotgunPellet.mat.meta`, `Assets/_Game/Materials/WeaponImpactMark.mat`, `Assets/_Game/Materials/WeaponImpactMark.mat.meta`
- generated meshes/manifest: `Assets/_Game/Generated/BlueCircleCueMesh.asset`, `Assets/_Game/Generated/BlueCircleCueMesh.asset.meta`, `Assets/_Game/Generated/RedTriangleCueMesh.asset`, `Assets/_Game/Generated/RedTriangleCueMesh.asset.meta`, `Assets/_Game/Generated/MovementLabBuildManifest.json`, `Assets/_Game/Generated/MovementLabBuildManifest.json.meta`
- scene/settings: `Assets/_Game/Scenes/MovementLab.unity`, `Assets/_Game/Scenes/MovementLab.unity.meta`, `ProjectSettings/EditorBuildSettings.asset`, `ProjectSettings/DynamicsManager.asset`, `ProjectSettings/TimeManager.asset`, `ProjectSettings/TagManager.asset`
- lighting settings/manifests: `Assets/_Game/Lighting/MovementLabLightingSettings.asset`, `Assets/_Game/Lighting/MovementLabLightingSettings.asset.meta`, `Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset`, `Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset.meta`, `Assets/_Game/Lighting/MovementLabVolumeProfile.asset`, `Assets/_Game/Lighting/MovementLabVolumeProfile.asset.meta`, `Assets/_Game/Lighting/MovementLabLightingManifest.json`, `Assets/_Game/Lighting/MovementLabLightingManifest.json.meta`
- baked: `Assets/_Game/Scenes/MovementLab/LightingData.asset`, `Assets/_Game/Scenes/MovementLab/LightingData.asset.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-0.exr`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-0.exr.meta`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-1.exr`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-1.exr.meta`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-2.exr`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-2.exr.meta`

## Execution Assignments

- workers: T1 -> worker-T1 (luna_max) -> feedback component; T2 -> worker-T2 (luna_max) -> shotgun; T3 -> worker-T3 (luna_max) -> rockets; T4 -> worker-T4 (luna_max) -> ammo presentation; T5 -> worker-T5 (luna_max) -> bot pickup runtime; T6 -> worker-T6 (luna_max) -> HUD; T7 -> worker-T7 (luna_max) -> movement/blast; T8 -> worker-T8 (luna_max) -> collision transfer; T9 -> worker-T9 (luna_max) -> shared contracts; T10 -> worker-T10 (luna_max) -> arena; T11 -> worker-T11 (luna_max) -> prefab/material composition; T12 -> worker-T12 (luna_max) -> bot builder; T13 -> worker-T13 (luna_max) -> workflow inventory; T14 -> worker-T14 (luna_max) -> stage/validator integration; T15 -> worker-T15 (luna_max) -> sole generation/proof
- review_checkpoints: CP1-CP15 -> one fresh `sol_medium` reviewer per corresponding writer; Critical/High only; reviewer receives exact writer base/head, owned slice, producer/consumer contracts, task checks; checkpoint must accept findings/fixes before dependent consumer starts; fix route follows [review checkpoints](../.agents/skills/orchestrate-implementation/SKILL.md#review-checkpoints)
- fix ownership: every code/tooling/generated-source fix goes to new bounded writer; no reviewer self-fix; applicable checks rerun; source fix after JOIN3 creates new sourceFreezeSha and invalidates T15/CP15/FINAL
- joins: parallel workers share no owned paths; T8 serializes after T7 for `PlayerMotor`; T9 owns shared constants before editor fan-out; T14 owns validation fan-in; T15 owns Unity lease and all generated mutation

## Final Verification

- exact head: clean committed `finalSha`, descendant of `sourceFreezeSha` and baseline; source and generated commits included; only requested skill/plan documentation may remain outside product execution history
- checks: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Compare-GeneratedYaml.ps1 -Base <sourceFreezeSha> -Head <finalSha> -FailOnDangling` -> all six headers, complete committed coverage, zero unsupported/increased dangling/GUID churn/pair break
- checks: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1` -> exit 0 under 90 seconds, no Unity process/lock
- checks: hidden waited full Unity EditMode run, omit `-quit` -> XML exists/parses; total>0, failed=0, inconclusive=0; process/lock released
- checks: `check_id=gameplay-production-validate-final; tier=production-final; owner=execution-orchestrator; expected_status=complete; command=powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode ProductionValidate -ProjectPath C:\wt\<id> -EvidenceRoot C:\wt\<id>e\final-validate -AttemptId gameplay-final-validate -TimeoutSeconds 1800; mutates_project=false; input_paths=[finalSha committed source and Generated Output Ownership]; run_point=FINAL after full EditMode; evidence=executed_sha=validated_sha=finalSha, production-validator executed, production profile/manifest current, no ProductionBake invocation, bake_count=0, changedGeneratedPaths empty, release proof valid`
- inspect: source diff matches task ownership; generated commit contains comparator-selected outputs only; new material pairs exist/provenance valid; score/arc/icon code matches exact reference geometry; arena/prefab/scene contracts persisted; available logs reported as non-reproduction rather than proof
- invalidation: source/builder/tooling edit after sourceFreezeSha -> new sourceFreezeSha plus T15/CP15/FINAL replay; generated semantic defect -> source/builder fix only plus same replay; generated commit change -> committed comparator, CP15, full EditMode, and final ProductionValidate rerun; evidence-only file change -> rerun consuming check

## Handoff

- residual risks: subjective rocket-jump height, F-kick feel, ball-contact feel, damage-arc readability, score/icon placement, and ramp play space need human playtest; 30-second particle density needs target-machine observation; available logs did not reproduce under-floor spawn
- authority: execution orchestrator may integrate accepted worker/fix commits and generated commit in isolated worktree; merge to user branch and human playtest require user approval
