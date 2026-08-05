# AI Readiness Prerequisites Handoff Plan

Status: implemented and batch-validated; acceptance blocked by manual MovementLab smoke
Source: architecture audit -> "fix these"
Baseline: `main@ebb2149`, inspected 2026-08-05
Implemented: 2026-08-05
Resume: complete manual MovementLab smoke -> accept prerequisite gate -> `plans/core-behaviour.md` next gameplay slice; no gameplay reordering

## Objective

Remove AI implementation ambiguity before ball, rocket, explosion, goal, scoring work.

Done boundary:

- runtime ownership and generated-asset authority documented
- input actions named by gameplay intent
- `PlayerInputReader` sole device boundary
- gravity single-sourced
- unused packages removed
- low-cost PC rendering baseline enforced
- Unity compile, builder run, MovementLab smoke pass

## Scope

- in: architecture contract, builder contract, input cleanup, cursor input, gravity deduplication, package cleanup, PC render/resolution baseline
- out: ball, rockets, explosions, kick behavior, goals, scoring, reset implementation, starter-asset cleanup, test redesign, visual polish
- preserve: current user edits in `AGENTS.md`; all `.agents/` content

## Findings and Decisions

- `plans/core-behaviour.md`: behavior source of truth
- `plans/runtime-architecture.md`: runtime ownership source of truth
- `MovementLabBuilder`: authoritative owner of generated `Player.prefab` and `MovementLab.unity` through POC
- generated asset flow: builder source edit -> rebuild -> generated diff inspection
- input flow: Input System asset -> `PlayerInputReader` -> intent consumers
- fixed simulation: intent -> gameplay math/impulses in `FixedUpdate()`
- match state contract: `Playing -> GoalFreeze -> Reset -> Playing`
- tuning: component-local serialized feel values; `GamePhysicsSettings` owns global invariants
- target input: keyboard/mouse primary, gamepad secondary; no XR, touch, generic joystick
- performance: disabled effects remain available for measured later opt-in

Runtime owners to document:

- `PlayerInputReader`: action reads, press buffers, held state, gameplay gate
- `PlayerMotor`: CharacterController movement, external player impulses, cap, reset
- `PlayerLook`: yaw/pitch, cursor, FOV/shake presentation; intent only
- proposed `BallMotor`: Rigidbody cap, resistance, contact response, reset
- proposed `RocketLauncher`: held-fire cooldown, spawn
- proposed `RocketProjectile`: flight, collision filter, detonation request
- proposed `ExplosionResolver`: falloff, surface distance, occlusion, player/ball impulses
- proposed `BallKick`: kick buffer, eligibility, redirect impulse
- proposed `GoalTrigger`: one centre-plane crossing event per entry
- proposed `MatchController`: score, state, input gate, rocket cleanup, coordinated reset
- dependency rule: leaf gameplay components report events upward; leaf components never own score or match reset

## Execution

`P1 contracts -> P2 input/runtime -> P3 project baseline -> batch verification -> R1 combined review -> F1 cursor fix -> manual smoke pending`

Sequential packets. Shared docs, assets, settings, and generated state prevent safe parallel writes.

### P1: Freeze Contracts

- status: implemented; included in combined review
- owner: `luna_xhigh`
- owns:
  - `plans/runtime-architecture.md`
  - `plans/core-behaviour.md`
  - `AGENTS.md`
- reads:
  - `Assets/_Game/Editor/MovementLabBuilder.cs`
  - `Assets/_Game/Scripts/Runtime/*.cs`
- changes:
  - document runtime owners and dependency direction above
  - document `Playing -> GoalFreeze -> Reset -> Playing`
  - declare builder authority and builder-first generated-asset workflow
  - add terse `AGENTS.md` pointer to runtime architecture
  - mark prerequisites required before future gameplay work
- constraints:
  - merge current `AGENTS.md` diff; preserve skill-validation instructions
  - no code or serialized asset changes
  - no event bus, DI framework, multiplayer abstraction, or speculative services
- done when:
  - every current/planned gameplay responsibility has one owner
  - docs contain no conflicting ownership, behavior, or builder rule
- combined review owner: `sol_medium`
- review focus: missing owner, circular dependency, duplicate source of truth, conflict with project instructions

### P2: Normalize Input and Physics

- status: implemented; batch builder passed; included in combined review
- owner: `luna_xhigh`
- depends on: accepted P1
- owns:
  - `Assets/InputSystem_Actions.inputactions`
  - `Assets/_Game/Scripts/Runtime/PlayerInputReader.cs`
  - `Assets/_Game/Scripts/Runtime/PlayerLook.cs`
  - `Assets/_Game/Scripts/Runtime/PlayerMotor.cs`
  - `Assets/_Game/Scripts/Runtime/GamePhysicsSettings.cs`
  - `Assets/_Game/Editor/MovementLabBuilder.cs`
  - generated `Assets/_Game/Prefabs/Player.prefab`
  - generated `Assets/_Game/Scenes/MovementLab.unity`
  - `plans/core-behaviour.md`
- changes:
  - Player actions: `Move`, `Look`, `Jump`, `Fire`, `Kick`, `ReleaseCursor`, `CaptureCursor`
  - remove `Attack`, `Interact`, `Crouch`, `Sprint`, `Previous`, `Next`, unused UI map, XR/touch/joystick bindings
  - bindings:
    - `Move`: WASD, gamepad left stick
    - `Look`: mouse delta, gamepad right stick
    - `Jump`: Space, gamepad south
    - `Fire`: mouse left, gamepad right trigger
    - `Kick`: mouse right, gamepad west
    - `ReleaseCursor`: Escape
    - `CaptureCursor`: mouse left
  - preserve IDs for surviving actions/map; save stable IDs for new actions
  - expose held fire and fresh-press kick through `PlayerInputReader`
  - clear buffered/held gameplay state when input gated
  - route cursor intents through reader; remove direct device polling from `PlayerLook`
  - suppress gameplay fire for cursor-recapture click
  - remove serialized `PlayerMotor.gravity`; consume `GamePhysicsSettings.GravityMagnitude`
  - rebuild player/scene through builder
  - update implemented status in `core-behaviour.md`
- constraints:
  - Input System only
  - fixed-step-safe gameplay intent
  - held jump/kick never repeat; fire remains held state
  - preserve movement tuning and input asset GUID
  - reject unrelated YAML churn or GUID changes
- done when:
  - device polling exists only in `PlayerInputReader`
  - distinct fire/kick semantics exist
  - recapture click cannot fire
  - gravity `16.875` has one code source
  - generated diff contains intended changes only
- combined review owner: `sol_medium`
- review focus: action lifecycle, lost/duplicate presses, cursor/fire conflict, fixed-step safety, serialized references, gravity drift

### P3: Clean Packages and PC Baseline

- status: implemented; package resolution and batch compile passed; included in combined review
- owner: `luna_xhigh`
- depends on: accepted P2
- owns:
  - `Packages/manifest.json`
  - `Packages/packages-lock.json`
  - `ProjectSettings/Packages/com.unity.ai.assistant/Settings.json`
  - `Assets/Settings/PC_Renderer.asset`
  - `ProjectSettings/ProjectSettings.asset`
- changes:
  - confirm no source/asmdef/asset dependency, then remove:
    - `com.unity.ai.assistant`
    - `com.unity.ai.inference`
    - `com.unity.ai.navigation`
    - `com.unity.collab-proxy`
    - `com.unity.multiplayer.center`
    - `com.unity.timeline`
    - `com.unity.visualscripting`
  - retain URP, Input System, UGUI, Test Framework, IDE integrations
  - resolve lock through matching Unity Editor; remove obsolete AI Assistant settings
  - preserve `.agents/`
  - disable existing SSAO renderer feature; retain dormant feature data
  - set default screen `1920x1200`; enable resizable window
  - preserve render scale `0.8`, HDR off, MSAA off, main-light shadows off, VSync 1
- constraints:
  - no package upgrades
  - no new quality tiers, assets, shaders, post-processing
  - Unity Editor API or controlled Inspector edits for serialized settings
  - reject unrelated lock, settings, or renderer churn
- done when:
  - Package Manager resolves without error
  - runtime/editor assemblies compile
  - AI Assistant startup hook no longer mutates repository
  - SSAO inactive
  - standalone defaults match target display and remain resizable
  - PC quality still references PC URP asset
- combined review owner: `sol_medium`
- review focus: required dependency removal, version churn, `.agents/` damage, URP reference loss, graphics regression, broad serialization

## Review and Fix

Combined `sol_medium` review completed after P1-P3 implementation.

- finding: `F1 medium` in `PlayerInputReader` -> cached cursor state could drift from `Cursor.lockState`, block recapture, and leak recapture click into Fire
- fix: fresh `luna_xhigh` removed duplicate cursor state; actual `Cursor.lockState` now gates look, recapture, and Fire suppression
- fix validation: Unity 6000.5.6f1 batch compile passed; no compiler errors; `git diff --check` passed
- re-review: intentionally not run after fix

Agent messages: terse Markdown via `llm-oriented-markdowns`; no JSON, transcript, or repeated plan.

## Final Verification

- status: batch checks complete; interactive Play-mode smoke pending
- batch owner: `luna_xhigh`
- manual owner: next operator with working Unity UI control
- precondition: no interactive Unity process owns project
- run:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe' -batchmode -quit -projectPath '<repo-root>' -executeMethod RocketFooxball.Editor.MovementLabBuilder.BuildMovementLab -logFile '<movement-lab-log>'
```

- result: exit `0`; zero compiler errors; intended semantic generated diff only; Unity scene/solution ID-order churn rejected
- restart Unity once after package/settings changes
- reopen: player prefab, MovementLab scene, Input Actions asset
- pending: reopen player prefab, MovementLab scene, and Input Actions asset; play `MovementLab.unity`
- smoke:
  - WASD, mouse look, stop/turn
  - jump, buffered jump, double-jump, air control
  - wall/corner collision, speed cap, HUD
  - Escape releases cursor; left click recaptures without gameplay fire
  - Game view `1920x1200`; target laptop presents smooth 60 Hz
- inspect:
  - complete: `git diff --check`
  - complete: no missing meta or duplicate GUID
  - complete: no tracked generated/local state
  - complete: no unrelated `.agents/`, scene, package, or settings churn retained
- automated tests: deferred by project contract

## Risks

- concurrent `AGENTS.md`/`.agents/` work -> narrow merges; never replace or clean paths
- input ID churn -> preserve surviving IDs; inspect asset/prefab references
- capture/fire shared binding -> gate fire on locked cursor; consume capture transition
- builder erases manual work -> builder-first rule; generated diff inspection
- package lock churn -> accept only graph removals caused by listed packages
- Unity mass reserialization -> reject unrelated serialized changes
- Editor performance differs from standalone -> target-device standalone measurement before graphics upgrade

## Done Criteria

- complete: P1-P3 implemented; combined review finding fixed
- complete: package resolve, compile, and MovementLab rebuild pass
- complete: diff contains approved prerequisite paths plus preserved pre-existing user work
- complete: `core-behaviour.md` records resulting baseline
- pending: manual MovementLab smoke passes, including cursor recapture after `F1` fix
- pending: target-laptop standalone performance confirms smooth 60 Hz at `1920x1200`
- blocked until pending checks pass: accept gate and start next gameplay slice
