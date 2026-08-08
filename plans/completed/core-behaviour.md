# Core Behaviour Plan

## Purpose [COMPLETED]

AI implementation source of truth for quick Rocket Fooxball POC.

Goal: test responsive Quake II-like movement, rocket traversal, physical ball control, scoring readability.

Runtime ownership and dependency source of truth: `plans/runtime-architecture.md`. This document owns behaviour contracts, tuning targets, and implementation status only.

Status: behaviour decisions confirmed. Runtime T1/T2 owners and T3 generated MovementLab state implemented. User baseline smoke confirms prior movement controls, collision, jumps, HUD, frame-rate behavior, zero Console errors. T3 builder and fresh validator pass in batch.

Workflow record (2026-08-08): Unity `6000.5.6f1`; authoritative builder `Logs/core-behaviour-build.log` exit `0`, marker `Rocket Fooxball Movement Lab built`; second builder `Logs/core-behaviour-build-second.log` exit `0`, current builder-signature scene passed full stale-content validation and was reused, generated hashes stable; fresh validator `Logs/core-behaviour-validate.log` exit `0`, marker `Rocket Fooxball Movement Lab validation succeeded`; protected paths unchanged; baseline `.meta` GUIDs unchanged; no duplicate GUIDs. Batch-only worker evidence. Tooling finding: Unity license access-token warning only; compile/build/validator clean.

Section status: `COMPLETED` -> contract implemented and required batch validation passed. Per-section `implemented:` line records shipped scope and owning files.

Tuning values: initial targets. Expose as serialized Unity Inspector fields. Adjust through playtesting without changing behaviour contract.

## Core Physics Contract [COMPLETED]

- implemented: `CharacterController` player, `60 Hz` fixed step, gravity magnitude `16.875`, visible shell, ramps, `Rigidbody` ball, hidden failsafe containment, shared `PhysicsMaterial`, MovementLab scene/builder, debug HUD -> `Assets/_Game/Scripts/Runtime/PlayerMotor.cs`, `Assets/_Game/Scripts/Runtime/GamePhysicsSettings.cs`, `Assets/_Game/Scripts/Runtime/BallMotor.cs`, `Assets/_Game/Scripts/Runtime/MovementDebugHud.cs`, `Assets/_Game/Editor/MovementLabBuilder.cs`, `Assets/_Game/Scenes/MovementLab.unity`, `Assets/_Game/Prefabs/Player.prefab`, `Assets/_Game/Prefabs/Ball.prefab`, `Assets/_Game/Materials/BallSurface.physicMaterial`
- player: `CharacterController`
- ball: `Rigidbody`
- gravity: same strength for player and ball
- impulse policy: preserve existing momentum, add new valid impulses, apply relevant cap
- frame-rate policy: deterministic movement and impulses in `FixedUpdate()` using `Time.fixedDeltaTime`
- arena collision: visible geometry explains normal impacts
- containment: invisible failsafe colliders directly behind visible arena walls or openings
- surface policy: uniform ball friction and bounce across floor, walls, ramps, goal frames through shared `PhysicsMaterial` settings

## Player Ground Movement [COMPLETED]

- implemented: base speed `10 m/s`, `0.125 s` base-speed acceleration target, about `0.20 s` no-input stop, overspeed friction, momentum-sensitive turn scrub, grounded steering, wall slide, ramp velocity projection, ramp-exit launch -> `Assets/_Game/Scripts/Runtime/MovementMath.cs`, `Assets/_Game/Scripts/Runtime/PlayerMotor.cs`
- feel: fast Quake II-style arena athlete
- base-speed acceleration: reach base run speed in `0.10-0.15 s`
- no-input stop: stop grounded player in about `0.20 s`
- turning: momentum-sensitive
  - smooth direction changes preserve speed
  - sharp turns scrub speed
  - above-base momentum receives stronger turn cost
- wall collision: remove into-wall component, preserve tangential velocity, slide
- ramps: preserve velocity along surface; ramp exit can convert forward speed into upward launch
- sprint: none
- crouch: none
- conventional wall-jump: none
- health, self-damage, fall damage, death loop: none

## Normal Jump And Bunny-Hop [COMPLETED]

- implemented: fixed `6.75 m/s` jump velocity, about `0.75x` player-height jump, about `0.40 s` apex, `80 ms` coyote time, `100 ms` buffer, fresh-press input, jump-frame ground-movement skip, bunny-hop acceleration, `2x` soft cap, above-cap taper, missed-hop overspeed friction -> `Assets/_Game/Scripts/Runtime/PlayerInputReader.cs`, `Assets/_Game/Scripts/Runtime/PlayerMotor.cs`, `Assets/_Game/Scripts/Runtime/MovementMath.cs`
- normal jump: fixed upward impulse
- normal jump height: about `0.75x` player height
- normal jump apex time: about `0.40 s`
- variable jump height: none
- coyote time: about `80 ms`
- pre-landing jump buffer: about `100 ms`
- bunny-hop input: fresh manual press for every landing; holding jump never repeats
- bunny-hop speed building: intentional, quick
- air-strafe acceleration: coordinated mouse turn plus `A` or `D`
- bunny-hop soft cap: about `2x` base run speed
- above-soft-cap input: preserve speed on smooth line; no meaningful further bunny-hop acceleration
- missed hop: ground friction returns excess speed toward base in about `0.50 s`

## Air Control [COMPLETED]

- implemented: strafe-led air acceleration, weaker forward/back correction, yaw-relative steering, no pitch steering, no airborne friction, constant-angular-rate steering producing wider high-speed arcs, soft-to-hard-cap acceleration taper -> `PlayerMotor.ApplyAirMovement()`, `MovementMath.Accelerate()`, `MovementMath.SteerToward()`, `MovementMath.AirAccelerationScale()`
- primary steering: mouse yaw plus matching `A` or `D`
- secondary steering: weaker `W` or `S` correction
- opposite input: bend path and reduce speed gradually; never rapid mid-air brake
- high-speed steering: gradually wider turn radius near hard cap
- pitch: aim only; no direct movement-vector steering from pitch
- airborne momentum: retained until input acceleration, blast, collision, or cap changes it

## Double-Jump [COMPLETED]

- implemented: one air jump per grounded sequence, ground refresh, ledge preservation, additive `6.75 m/s` vertical impulse, additive `2 m/s` current-horizontal-direction impulse, additive external rocket impulse -> `PlayerMotor.TryConsumeJump()`, `PlayerMotor.AddExternalImpulse()`
- count: one airborne jump per grounded movement sequence
- refresh: ground contact only
- ledge case: walking off ledge preserves airborne jump after coyote window expires
- vertical effect: add one normal-jump impulse to current vertical velocity
- horizontal effect: add small impulse along current horizontal movement direction
- horizontal effect never redirects toward crosshair
- rocket, normal-jump, double-jump impulses: fully additive

## Player Speed Limits And Camera [COMPLETED]

- implemented: horizontal `3x` hard cap -> `MovementMath.ClampHorizontal()`; yaw/pitch mouse look, cursor capture, no bob/roll, speed FOV, visual blast shake -> `Assets/_Game/Scripts/Runtime/PlayerLook.cs`, `Assets/_Game/Scripts/Runtime/PlayerCameraFeedback.cs`, `Assets/_Game/Prefabs/Player.prefab`
- player hard cap: about `3x` base run speed
- cap purpose: physics safety only; rockets can exceed bunny-hop soft cap
- blast aim effect: no mechanical camera rotation
- blast feedback: subtle visual shake with unchanged crosshair direction
- speed feedback: subtle camera FOV expansion from base speed toward hard cap
- camera bob: none
- camera roll: none

## Rocket Launcher [COMPLETED]

- implemented: builder-authored rocket prefab, held-fire cooldown, crosshair spawn, constant world flight, owner/rocket collision ignore, active-projectile cleanup -> `Assets/_Game/Scripts/Runtime/RocketLauncher.cs`, `Assets/_Game/Scripts/Runtime/RocketProjectile.cs`, `Assets/_Game/Editor/MovementLabBuilder.cs`, `Assets/_Game/Prefabs/Rocket.prefab`

- ammunition: unlimited
- reload: none
- firing interval: about `0.70 s`
- held fire: repeat whenever cooldown completes
- kick lockout: none; kick and fire use independent cooldowns and can overlap
- recoil: none
- projectile type: visible physical projectile
- trajectory: crosshair-authoritative
- projectile velocity: constant world-space velocity; never inherit player velocity
- target travel feel: about `0.50 s` across half arena
- arming: immediate
- owner collision: ignore projectile collider collision with firing player's `CharacterController`
- owner blast: apply normally
- contact: detonate on first arena, player, ball, goal shield, or containment-boundary collision
- rockets: ignore other rockets; no projectile collision or chain detonation
- missed rocket: containment boundary supplies eventual impact; no off-map escape
- goal/reset: destroy all active rockets on score

## Explosion Model [COMPLETED]

- implemented: shared radius/falloff, surface-distance targeting, clear/occluded force, shield-transparent ball occlusion, additive player/ball impulses, visual shake -> `Assets/_Game/Scripts/Runtime/ExplosionResolver.cs`, `Assets/_Game/Scripts/Runtime/PlayerCameraFeedback.cs`

- blast radius: about `2-2.5x` player height
- falloff: smooth maximum-to-zero falloff
- distance metric: nearest target-collider surface, not body centre
- direct hit: maximum blast strength
- geometry handling: geometry never fully blocks force
- clear path: `100%` computed force
- occluded path: `25%` computed force
- radius: shared between player and ball
- force multipliers: separate player and ball tuning
- stacking: fully additive with existing velocity and other same-fixed-step impulses
- player direction: mostly radial plus small upward bias for blast below or beside player
- ball direction: purely radial; no automatic upward bias
- ideal floor rocket-jump: fast launch reaching about `4x` normal-jump height before added jump or double-jump impulses
- direct rocket-ball contact: explosion only; no extra impact kick
- goal shield: transparent to ball-directed blast force despite blocking rockets

## Ball Body [COMPLETED]

- implemented: dynamic `Rigidbody`, continuous collision, shared surface, rolling resistance, speed cap, queued impulses, reset/freeze state -> `Assets/_Game/Scripts/Runtime/BallMotor.cs`, `Assets/_Game/Prefabs/Ball.prefab`, `Assets/_Game/Materials/BallSurface.physicMaterial`

- size: about waist-height diameter
- normal jump: clears ball comfortably
- collision: solid obstacle; player never phases through ball
- authority: player can move ball; ball cannot impart meaningful velocity change to player
- speed hard cap: about `4x` player base run speed
- bounce: retain about `60-70%` normal impact speed
- rolling resistance: slow steady decay until rest
- endless rolling: none
- deliberate kick spin or curve: none
- visual angular motion: normal `Rigidbody` rolling only
- high-speed collision: use continuous collision detection or equivalent anti-tunnelling setup

## Assisted Body Contact [COMPLETED]

- implemented: real `CharacterController` hit notification, velocity-directed capped contact assist -> `Assets/_Game/Scripts/Runtime/BallMotor.cs`, `Assets/_Game/Scripts/Runtime/PlayerMotor.cs`

- trigger: real player-ball collision only
- magnetic attraction: none
- control target in front of player: none
- push direction: actual player velocity, not camera facing
- push strength: speed-sensitive but capped well below active kick
- airborne contact: enabled with same cap
- player response: retain player velocity except normal solid-collision projection needed to prevent overlap
- intent: remove snagging and run-over instability without creating possession lock

## Active Kick [COMPLETED]

- implemented: fresh press, `500 ms` buffer, `0.40 s` cooldown, range/cone gate, crosshair direction, incoming redirect, cap, airborne use -> `Assets/_Game/Scripts/Runtime/BallKick.cs`

- input: fresh press per attempt; holding never repeats
- range: about half player width beyond physical contact
- eligibility cone: about `35 degrees` total around crosshair
- direction: exact crosshair direction
- aim assistance: none
- strength: fixed base impulse
- stationary result: about `70%` ball hard-cap speed
- comparison: kick slightly stronger than perfect close rocket blast
- player-momentum contribution: limited share of player forward velocity; extreme bunny-hop velocity never bypasses ball cap
- incoming-ball handling: controlled redirect
  - preserve useful incoming momentum
  - reduce velocity opposing intended shot
  - add aimed kick impulse
  - apply ball speed cap
- airborne use: enabled
- player recoil or lunge: none
- deliberate spin or curve: none
- cooldown: about `0.40 s`
- input buffer: `500 ms`
- cooldown start: button press
- buffer semantics: one queued attempt; no overlapping attempt until success or expiry
- miss semantics: miss consumes attempt and cooldown
- fire overlap: allowed

## Rocket-Ball Balance [COMPLETED]

- implemented: resolver ball impulse/falloff and kick comparison tuning -> `Assets/_Game/Scripts/Runtime/ExplosionResolver.cs`, `Assets/_Game/Scripts/Runtime/BallKick.cs`

- perfect close rocket blast: powerful but below kick strength
- stationary close-blast target: ball travels roughly half arena before collision or rolling decay
- edge-radius blast: small adjustment
- multiple blasts: additive until ball hard cap
- rocket launch direction: explosion-origin-to-ball-centre radial vector
- grounded downward force: normal collision response can absorb downward component; no hidden lift correction

## Goals [COMPLETED]

- implemented: symmetric north/south goal openings, trigger plane, one-entry latch, visible shield/frame/recess, ball-only shield collision ignore, opposing score callback -> `Assets/_Game/Scripts/Runtime/GoalTrigger.cs`, `Assets/_Game/Scripts/Runtime/MatchController.cs`, `Assets/_Game/Editor/MovementLabBuilder.cs`

- active goals: both
- score ownership: ball entering either goal awards opposing side; own goals possible
- scoring condition: ball centre crosses goal plane inside opening
- player access: goal recess blocked
- goal shield:
  - visible energy-field treatment
  - blocks player
  - blocks rockets and causes explosion at shield
  - allows ball passage
  - does not reduce blast force applied to ball
- goal frame: solid, uniform arena bounce

## Goal Celebration And Reset [COMPLETED]

- implemented: `5 s` unscaled goal freeze, input/owner gate, rocket cleanup, ball/player/goal/camera reset -> `Assets/_Game/Scripts/Runtime/MatchController.cs`, `Assets/_Game/Scripts/Runtime/BallMotor.cs`, `Assets/_Game/Scripts/Runtime/PlayerMotor.cs`, `Assets/_Game/Editor/MovementLabBuilder.cs`

- score trigger -> freeze physics and gameplay input for `5 s`
- frozen frame: scoring moment held for clear feedback
- active rockets: destroy immediately
- reset ball: centre position, zero `Rigidbody.linearVelocity`, zero `Rigidbody.angularVelocity`
- reset player: neutral midfield position, short offset from ball, face ball, zero motor velocity
- reset action state: clear jump buffer, kick buffer, fire state, cooldowns
- kickoff countdown: none
- post-freeze: controls resume immediately

## Input Resolution Rules [COMPLETED]

- implemented: Input System Move/Look reads, fresh Jump/Kick capture, held Fire, cursor intents, gameplay gate clear, jump priority/buffering, launcher/kick/goal-freeze consumers -> `Assets/InputSystem_Actions.inputactions`, `Assets/_Game/Scripts/Runtime/PlayerInputReader.cs`, `Assets/_Game/Scripts/Runtime/PlayerMotor.cs`, `Assets/_Game/Scripts/Runtime/RocketLauncher.cs`, `Assets/_Game/Scripts/Runtime/BallKick.cs`, `Assets/_Game/Scripts/Runtime/MatchController.cs`
- jump press while grounded or within coyote window -> normal jump
- jump press while airborne with air-jump available -> double-jump
- jump press within `100 ms` before landing -> queued normal jump on contact
- airborne press within `100 ms` of landing with air-jump available -> double-jump wins; buffer consumed immediately
- held jump -> no repeated normal jump or double-jump
- kick press -> start cooldown and one `500 ms` eligibility buffer
- held kick -> no repeat
- held fire -> launch whenever launcher cooldown permits
- goal freeze -> disable gameplay actions and clear held/queued state before kickoff

## Explicit Exclusions [COMPLETED]

- sprint input
- crouching or crouch-jump
- variable-height jump
- conventional wall-jump
- health, damage, death, fall damage
- ammo, pickups, reload
- firing recoil
- rocket velocity inheritance
- rocket-rocket interaction
- direct-hit bonus layered over rocket explosion
- magnetic dribbling
- ball-driven player knockback
- charged kick
- kick aim assist
- kick spin or curve
- player entry into goal recess
- post-goal physics continuation
- kickoff countdown
- multiplayer or AI behaviour
