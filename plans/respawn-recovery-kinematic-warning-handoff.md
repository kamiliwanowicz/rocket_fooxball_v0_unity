# Respawn Recovery And Kinematic Warning Handoff

Status: ready for implementation
Source: user playtest report + `Logs/Editor.log` inspection
Baseline: `37f51088ff83a65fe7543998530d2c9166fdc686`
Investigation worktree: clean; no source changes

## Objective

Prevent participant from remaining below playable arena after reset or movement failure. Emit compact reset/recovery position diagnostics. Remove invalid velocity writes against kinematic `Rigidbody` instances. Preserve movement feel, score flow, participant lifecycle, ball behavior, rocket trajectory, arena geometry, and existing ownership contracts.

## User Observation

- event: player reported respawn/reset into inaccessible location, then stopped Play Mode
- screenshot: first-person camera below pitch; arena underside visible; HUD showed `Grounded: NO`, speed `10.1 m/s`, health `43 / 100`, score `North 1 - South 5`, match state `Playing`
- terminology uncertainty: user called event respawn; evidence supports goal/kickoff reset correlation, not death-respawn proof

## Confirmed Evidence

- inspected log: `Logs/Editor.log`
- six actual goal-reset sequences: `MatchController.DismissGoalFreeze()` -> `PerformCoordinatedReset(MatchResetReason.Goal)`; count matches six total goals from screenshot
- no `NullReferenceException`, `MissingReferenceException`, `ArgumentException`, assertion, NaN, infinity, or `CharacterController` warning during inspected session
- no position telemetry from `OnRespawnRequested()`, `PerformCoordinatedReset()`, `ParticipantState.RespawnAt()`, or `PlayerMotor.ResetState()`
- root cause unresolved: log cannot distinguish bad destination from post-reset fall/impulse/camera failure
- local kickoff destination: `BlueSpawn_0`, world position `(12, 0, 0)`
- spawn root: identity transform
- playable floor: root `Floor`, center Y `-0.5`, height `1`, top surface Y `0`
- lower containment: `FloorContainment`, center Y `-4`, height `1`, top surface Y `-3.5`
- local `CharacterController`: height `3.6`, radius `0.8`, center Y `1.8`; root Y `0` places capsule bottom on playable floor
- no current participant out-of-bounds detector or recovery path

Observed warning snapshot before later log growth:

- `29008`: `Setting linear velocity of a kinematic body is not supported.`
- `7`: `Setting angular velocity of a kinematic body is not supported.`
- dominant source: `RocketProjectile.FixedUpdate()` at `Assets/_Game/Scripts/Runtime/Weapons/RocketProjectile.cs:80`
- launch/configure source: `RocketProjectile.ConfigureBody()` at line `234`
- reset source: `BallMotor.ResetState()` at lines `294-295`
- log remained live after inspection; use counts as incident snapshot, not stable totals

## Root-Cause Candidates

- participant root crosses playable floor after reset due same-step collision/movement interaction
- reset destination correct, then queued/late movement or camera state places view below floor
- spawn overlap or collision reconciliation displaces controller downward
- spectator-to-local camera restoration leaves camera at stale detached pose

Do not declare cause from screenshot alone. Add telemetry, reproduce, then narrow fix. Recovery guard still required: playtest cannot leave participant trapped even when primary cause remains intermittent.

## Scope

- in: reset/recovery telemetry; below-arena participant recovery; safe destination selection; participant-owned recovery operation; kinematic warning removal; builder wiring/validation; targeted regression proof; Unity compile; builder protocol; manual Play Mode proof
- out: movement tuning; arena geometry changes; score/clock/frag changes; health or ammo rewards; death-delay changes; broad logging framework; event bus; PlayMode test suite; production abstraction

## Ownership

- `MatchController`: recovery detection coordination, spawn selection, reason reporting, collision reconciliation
- `ParticipantState`: participant lifecycle/equipment state; expose narrow recovery request operation
- `PlayerMotor`: position/rotation/motion reset owner
- `ParticipantSpawnSet`: destination selection only
- `BallMotor`: ball `Rigidbody` state
- `RocketProjectile`: rocket `Rigidbody` state
- builder: generated scene values, references, validator contracts

## Implementation Contract

### Reset telemetry

- emit one structured line per participant reposition; never log per frame
- stable prefixes:
  - `RF_PARTICIPANT_RESET`
  - `RF_PARTICIPANT_RESPAWN`
  - `RF_PARTICIPANT_RECOVERY`
- fields: `reason`, `slot`, `name`, `spawn`, `from`, `requested`, `actual`, `state`
- invariant-culture coordinates; enough precision to detect sub-floor placement
- `actual`: participant root position after `PlayerMotor.ResetState()` and controller re-enable
- goal/kickoff reset: log all six participants
- death respawn: log selected safest spawn
- recovery: log detection position and destination
- avoid user-facing HUD spam

Example shape:

```text
RF_PARTICIPANT_RESET reason=Goal slot=0 name=Player spawn=BlueSpawn_0 from=(12.000,3.200,1.000) requested=(12.000,0.000,0.000) actual=(12.000,0.000,0.000) state=Alive
```

### Below-arena recovery

- fixed-step detection only
- active during `Playing`; ignore dead/respawning participants and match reset/countdown/freeze states
- proposed generated-scene threshold: root Y `< -1f`; validates below playable-floor top and above lower-containment top
- threshold must be serialized/configured by builder and asserted by validator; runtime must not reference editor contract
- non-finite position -> recover immediately and log invalid coordinates safely
- use `ParticipantSpawnSet.SelectSafestSpawn()`; deterministic kickoff spawn fallback when no safe result
- request recovery through `ParticipantState`; no direct transform mutation from `MatchController`
- recovery must:
  - preserve health, lifecycle, score, frags, shotgun ownership/ammo, match clock
  - clear player motion, queued impulses, dash, input intent, weapon requests, camera shake/spectator state, bot intent
  - reset view toward `resetLookTarget`
  - reconcile participant collision pairs after move
  - avoid death-respawn immunity unless later playtest proves immediate spawn damage problem
- no repeated recovery loop: post-move root must remain above threshold; log exact composition error and stop repeat spam if destination invalid

### Spawn safety

- keep authored spawn coordinates unless telemetry proves bad authoring
- validate each candidate against playable floor and `CharacterController` capsule clearance
- runtime safest-spawn scoring currently checks roster occupancy, ball distance, enemy goal distance, and enemy visibility; it does not validate world geometry
- prefer builder validator for static geometry clearance; add runtime fallback rejection only when required by reproduction
- no manual `MovementLab.unity` YAML edit

### `RocketProjectile` warning fix

- body configured `isKinematic = true`; flight uses `Rigidbody.MovePosition()`
- remove `body.linearVelocity = Vector3.zero` from `FixedUpdate()`
- remove or reorder invalid write from `ConfigureBody()`; never assign velocity after body becomes kinematic
- `Velocity` property remains logical `flightDirection * speed`
- preserve raycast-before-move collision, speed `48`, lifetime `8`, pause, cancel, owner-ignore, explosion, and `MovePosition()` behavior
- verify zero exact warning strings during launch, flight, detonation, reset, and goal freeze

### `BallMotor` warning fix

- goal freeze makes body kinematic through `SetSimulationEnabled(false)`
- `PerformCoordinatedReset()` calls `ResetState()` while body remains kinematic
- `ResetState()` currently writes `linearVelocity` and `angularVelocity` unconditionally
- only assign velocity channels while body dynamic
- reset stored motion snapshots to zero so later simulation re-enable cannot restore pre-goal velocity
- audit `SetSimulationEnabled()` and `SetPaused()` restore branches: if restored mode remains kinematic, do not write velocity channels
- preserve transform reset, freeze/pause mode restoration, queued-impulse clearing, sleep behavior, and goal attribution clearing

## Expected Source Touches

- `Assets/_Game/Scripts/Runtime/Match/MatchController.cs`
- `Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs`
- `Assets/_Game/Scripts/Runtime/Participants/ParticipantSpawnSet.cs` only if runtime clearance needed
- `Assets/_Game/Scripts/Runtime/Ball/BallMotor.cs`
- `Assets/_Game/Scripts/Runtime/Weapons/RocketProjectile.cs`
- `Assets/_Game/Editor/MovementLab/MovementLabContract.cs` or owning contract catalog for generated threshold
- `Assets/_Game/Editor/MovementLab/MovementLabSceneComposer.cs`
- `Assets/_Game/Editor/MovementLab/MovementLabValidator.cs`
- `Assets/_Game/Scripts/Tests/EditMode/ParticipantRecoveryRulesTests.cs` if pure threshold/state rule extracted
- generated `Assets/_Game/Scenes/MovementLab.unity` through builder only

Avoid new component/prefab touch unless implementation proves direct `MatchController` ownership infeasible.

## Tests

Pure EditMode tests when recovery rule extracted:

- Y above/equal threshold -> no recovery
- Y below threshold -> recovery
- NaN/infinity -> recovery
- non-`Playing`, dead, respawning -> no recovery request
- valid post-recovery position clears trigger

Do not add MonoBehaviour wiring tests. Builder validator owns references and generated scene values.

## Validation

1. capture pre-change Git status
2. run harness before Unity-mutating workflow: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1`
3. run targeted EditMode tests when added
4. run Unity compile with zero Console errors
5. builder-generated changes -> builder protocol:
   - ensure production bake current; accept source-defined valid skip marker
   - one authoritative build
   - semantic validate in separate Unity process
6. generated YAML comparator with task-declared scene output and `-FailOnDangling`
7. manual Play Mode proof:
   - clear Console/log
   - launch multiple rockets; observe full flight/detonation
   - score goal; dismiss freeze; verify six reset lines and valid local `actual` position
   - force local root below `-1f`; verify one recovery line, safe spawn, retained health/equipment, usable controls
   - kill local participant; verify death respawn line distinct from goal reset/recovery
   - confirm zero exact kinematic velocity warnings
8. inspect final Git diff; generated outputs in separate `chore: regenerate MovementLab outputs` commit when changed

## Acceptance

- trapped-below-arena state self-recovers within next fixed step
- recovery preserves match and participant combat state listed above
- goal reset, death respawn, recovery each identifiable from log with requested and actual positions
- no invalid spawn destination passes validator
- zero `Setting linear velocity of a kinematic body is not supported.`
- zero `Setting angular velocity of a kinematic body is not supported.`
- rocket flight/detonation unchanged by warning cleanup
- ball remains frozen during goal summary, resets stationary, resumes dynamic simulation after countdown
- semantic validator passes after scene reload

## Handoff Warning

Current incident lacks position telemetry. Implement logging before reproduction claims. Recovery guard fixes player-visible trap; telemetry determines primary cause for later narrow correction.
