# Runtime Architecture

Cross-component ownership and dependency contract only. Behaviour, tuning, slice status -> `plans/completed/core-behaviour.md`. Generated MovementLab assets -> `Assets/_Game/Editor/MovementLabBuilder.cs`.

## Rules

- Direct serialized references plus narrow callbacks. No event bus, DI framework, multiplayer abstraction, or speculative service layer.
- One state owner. Callers request operations; owners mutate their state.
- Leaf gameplay components report narrow events upward. Leaves never own score or coordinated reset.
- `MatchController` alone owns score, match state, gameplay-gate policy, and reset timing.

## Runtime ownership

- `BallMotor`: ball `Rigidbody`, velocity mutation, cap, resistance, contact response, containment, reset
- `RocketLauncher`: held-fire cooldown, spawn, active-projectile tracking, cleanup execution
- `RocketProjectile`: flight, collision filter, one detonation request
- `ExplosionResolver`: falloff, surface distance, occlusion, player/ball impulse requests
- `BallKick`: kick buffer/cooldown, eligibility, redirect request to `BallMotor`
- `GoalTrigger`: centre-plane crossing, one-event-per-entry latch, goal event to `MatchController`
- `MatchController`: score, `Playing -> GoalFreeze -> Reset -> Playing`, input gate, cleanup/reset commands

`MatchController` decides when cleanup/reset occurs. Owning components perform operations: `PlayerInputReader` clears input, `RocketLauncher` destroys rockets, `PlayerMotor`/`BallMotor` reset bodies, `GoalTrigger` re-arms.

## Folders and namespaces

- `Assets/_Game/Scripts/Runtime/Ball` -> `RocketFooxball.Runtime.Ball`
- `Assets/_Game/Scripts/Runtime/Match` -> `RocketFooxball.Runtime.Match`
- `Assets/_Game/Scripts/Runtime/Movement` -> `RocketFooxball.Runtime.Movement`
- `Assets/_Game/Scripts/Runtime/Input` -> `RocketFooxball.Runtime.Input`
- `Assets/_Game/Scripts/Runtime/Weapons` -> `RocketFooxball.Runtime.Weapons`
- `Assets/_Game/Scripts/Runtime/Feedback` -> `RocketFooxball.Runtime.Feedback`
- `Assets/_Game/Scripts/Runtime/Diagnostics` -> `RocketFooxball.Runtime.Diagnostics`
- `Assets/_Game/Scripts/Runtime/Physics` -> `RocketFooxball.Runtime.Physics`
- `Assets/_Game/Scripts/Runtime/Rendering` -> `RocketFooxball.Runtime.Rendering`

`RocketFooxball.Runtime` contains gameplay and input. `RocketFooxball.Rendering` is separate and owns URP-only runtime behaviour. Core gameplay must not reference URP or editor code.

## Composition

Cross-object references are serialized fields. Runtime components do not search scene-wide for gameplay owners. Same-object required components may use `GetComponent` fallback; missing serialized dependencies log an exact composition error and disable their component.

`MovementLabBuilder` is composition root: `MovementLabSceneComposer` creates prefabs/scene and writes every cross-object reference. `MovementLabValidator` reopens generated scene and verifies persisted references, prefab provenance, and component contracts. `MovementDebugHud` is diagnostics-only and retains discovery fallback.

## Canonical APIs

- impulse -> `PlayerMotor.AddExternalImpulse`, `BallMotor.QueueImpulse`
- simulation gate -> `SetSimulationEnabled`
- reset -> `ResetState` or owner-specific `ResetFeedback`, `ResetView`, `Rearm`, `ResetInputState`
- goal event -> `MatchController.NotifyGoal`
- rocket launch -> `RocketLauncher.LaunchRocket`
- blast -> `ExplosionResolver.ResolveExplosion`
- camera shake -> `PlayerCameraFeedback.RequestBlastShake`

Compatibility aliases are removed after repository, YAML, and UnityEvent scans prove zero callers.

## Fixed-step and frame ownership

- `FixedUpdate`: `PlayerMotor`, `BallMotor`, `BallKick`, `RocketLauncher`, `RocketProjectile`, `GoalTrigger`; all gameplay physics, impulses, cooldowns, and goal crossing.
- `Update`: `PlayerInputReader` samples held input; `PlayerLook` consumes look/cursor intent; `MatchController` advances unscaled goal-freeze timer.
- `LateUpdate`: `PlayerCameraFeedback` applies camera pose/FOV/shake after gameplay movement.
- `OnGUI`: `MovementDebugHud` presents owner properties only.

## Generated assets

`MovementLabBuilder` owns generated MovementLab scene, gameplay prefabs, materials, wiring, build-scene entry, and physics settings. Change builder/source contract -> rebuild -> validate -> inspect diff. Manual generated-asset edits are not authoritative.
