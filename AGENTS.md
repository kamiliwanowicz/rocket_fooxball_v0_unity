# Rocket Fooxball Unity POC

## Goal

First-person rocket-jumping football prototype. Test whether rocket movement, ball control, defense, and scoring feel fun, readable, and skill-based.

User new to Unity. Explain Unity-specific concepts at junior level. Keep general technical discussion concise.

## Priorities

1. Responsive, predictable rocket-jumping
2. Satisfying ball control and reliable scoring
3. Fast tuning and stable performance
4. Visual polish last

## Repository map

- Runtime gameplay: `Assets/_Game/Scripts/Runtime/`; namespace `RocketFooxball`
- Editor tooling and authoritative lab generator: `Assets/_Game/Editor/MovementLabBuilder.cs`; namespace `RocketFooxball.Editor`
- Primary sandbox and build scene: `Assets/_Game/Scenes/MovementLab.unity`
- Input actions: `Assets/InputSystem_Actions.inputactions`
- Runtime ownership and dependencies: `plans/runtime-architecture.md`
- Behaviour, tuning, and implementation status: `plans/completed/core-behaviour.md`
- Unity, package, and project configuration: `ProjectSettings/`, `Packages/`

Project-owned gameplay assets -> `Assets/_Game/`. Leave Unity starter content outside that root unchanged unless task targets it.

## Architecture

- Preserve current Unity and package versions unless requested.
- URP rendering. Default to low-cost visuals; measure before adding expensive effects and keep scalable fallbacks.
- Player collision/movement -> `CharacterController`. Ball and projectile physics -> `Rigidbody` forces and impulses.
- Critical gameplay simulation -> fixed-step code. Shared physics configuration -> `GamePhysicsSettings`.
- Device input -> Input System -> `PlayerInputReader` intent -> gameplay components. No legacy `UnityEngine.Input` polling.
- Runtime code -> `RocketFooxball.Runtime`, no `UnityEditor`. Editor tooling -> `RocketFooxball.Editor` with explicit assembly references.
- `MovementLabBuilder` owns generated MovementLab scene, gameplay prefabs, materials, wiring, build-scene entry, and physics settings.

## Unity asset safety

- Preserve `.meta` files and GUIDs. Move or delete asset and `.meta` together.
- Prefer `MovementLabBuilder` or Unity Editor APIs over direct serialized-YAML edits.
- Builder-owned change -> edit source/builder -> rebuild -> validate -> inspect diff. Manual generated-asset edits are not authoritative.
- Serialized prefab component reference: runtime non-null check insufficient. Save/reload, require nonzero YAML `fileID`, verify `PrefabUtility` source provenance.
- Imported animation lookup: exact clip name first; delimiter-safe suffix fallback only. Validate expected object identity and distinct state motions, not names alone.
- Generated controller rebuild: reuse valid states/transitions or remove stale subassets before replacement. Never clear arrays then append replacement subassets indefinitely.
- Reject unrelated reserialization, GUID churn, and prefab/scene changes after Editor saves.
- One Unity Editor process per project. Close interactive Editor before batch mutation.

## Technical Issues

- Computer Use prohibited for every task; never use related `sky.documentation` / `node_repl` tooling.
- Orchestrator state writes: follow `.agents/skills/loop-orchestrator/references/state-and-recovery.md` atomic-write contract.
- Unity batch runs: use `Start-Process -Wait -PassThru`, capture exit code, and confirm project process and lock release before next run.
- Unity IDE churn: compare pre/post status; remove only newly generated untracked IDE files and preserve prior changes.

## Unity execution speed

- Worktree path budget: create Unity worktrees near drive root, such as `C:\wt\<id>`. Existing long worktree -> verified junction or `subst` drive; use same short project path for every Unity command and process-ownership check.
- Import cache: preserve each worktree's `Library/` between runs. Delete only with cache-corruption evidence. Never share one `Library/` across concurrent worktrees.
- Validation batching: finish static edits and accepted review fixes before Unity launch, then run only checks invalidated by final diff. Explicit task or plan checks override.
- Builder no-op gate: validate source signature and generated-output fingerprint before importer, prefab, material, or scene writes. Valid state -> no save or rebuild. Stale state -> authoritative rebuild.

## Validation

- Tests deferred pending redesigned strategy. Add or require tests only when user requests them.
- C# changes: Unity compile with zero Console errors.
- Movement, input, or generated-lab changes: compile plus relevant `MovementLabBuilder.BuildMovementLab()` and `ValidateMovementLab()` batch checks.
- Builder-generated change: run build twice from same SHA. Build 2 must reuse existing outputs; compare hashes for owned scenes, prefabs, controllers, materials, and importer metadata. Any mismatch -> nondeterministic build bug.
- Run `ValidateMovementLab()` in separate Unity process after build 2. Build success alone does not prove persisted references or bindings.
- Scene, prefab, or Editor-tool changes: save, reopen or validate, inspect log and Git diff.
- Project or package changes: restart Unity when required; confirm affected renderer, input, build-scene, and assembly configuration.
- Documentation-only changes: inspect diff; Unity launch unnecessary.
- Report only validation actually run. Preserve unrelated user work.

## AI project
- This is AI-native project, build exclusive by AI agents. So whatever you write, make it AI agents fiendly. 
- Write self-documenting code. Leave short comments or class/method description if they can help agents understand why something was build in that way - only if it brings value. 
