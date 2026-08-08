# Runtime Architecture

Cross-component ownership and dependency contract only. Behaviour, tuning, slice status -> `plans/core-behaviour.md`. Generated MovementLab assets -> `MovementLabBuilder`.

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

## Generated assets

`MovementLabBuilder` owns `Player.prefab` and `MovementLab.unity` generated wiring. Change builder/source contract -> rebuild -> inspect generated diff. Manual generated-asset edits are not authoritative.
