# Rocket Fooxball Unity POC

## Goal

First-person rocket-jumping football prototype. Test whether rocket movement, ball control, defense, and scoring feel fun, readable, and skill-based.

User new to Unity. Explain Unity-specific concepts at junior level. Keep general technical discussion concise.

## Priorities

1. Responsive, predictable rocket-jumping
2. Satisfying ball control and reliable scoring
3. Fast tuning and stable performance
4. Visual polish follows core gameplay unless current task explicitly targets graphics

## Repository map

- Runtime gameplay: `Assets/_Game/Scripts/Runtime/`; namespace `RocketFooxball`
- Editor tooling and authoritative lab generator: `Assets/_Game/Editor/MovementLabBuilder.cs`; namespace `RocketFooxball.Editor`
- Primary sandbox and build scene: `Assets/_Game/Scenes/MovementLab.unity`
- Input actions: `Assets/InputSystem_Actions.inputactions`
- Runtime ownership and dependencies: `plans/runtime-architecture.md`
- Active graphics, VFX, containment, and movement overhaul: `plans/comprehensive-graphics-overhaul-coding-plan.md`
- Unity, package, and project configuration: `ProjectSettings/`, `Packages/`

Project-owned gameplay assets -> `Assets/_Game/`. Leave Unity starter content outside that root unchanged unless task targets it.

## Architecture

- Preserve current Unity and package versions unless requested.
- URP rendering. Default Standalone target -> native 1920x1080 High quality with PBR materials, HDR, shadows, SSAO, restrained bloom, modern lighting, baked indirect light, and reflection/light probes. Maintain scalable Low fallback. Validate High/Low visual quality and target-machine performance at 1920x1080.
- Graphics work may add or replace project-owned arena, ball, rocket, explosion, and containment visuals. Preserve gameplay contracts unless current task explicitly authorizes named gameplay or collision changes.
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

## Unity execution

- Tooling: no Computer Use or related `sky.documentation` / `node_repl` tools.
- Unity worktrees: use short paths such as `C:\wt\<id>`. Existing long path -> verified junction or `subst` drive. Use same short project path for all Unity commands and process checks.
- Unity processes: one Editor per project. Close interactive Editor before batch mutation. Batch run -> `Start-Process -Wait -PassThru` -> capture exit code -> confirm process and project lock release.
- Import cache: preserve each worktree's `Library/` between runs. Delete only with cache-corruption evidence. Never share one `Library/` across concurrent worktrees.
- C# inner loop: run relevant existing Unity test when available; its import/compile is sufficient before test execution. Otherwise run compile-only Unity batch with `-batchmode -nographics -quit`. Skip `MovementLabBuilder.BuildMovementLab()` during inner-loop compilation.
- `dotnet build`: optional fast preflight against current Unity-generated project files; never authoritative Unity compile proof.
- Builder no-op gate: validate source signature and generated-output fingerprint before importer, prefab, material, or scene writes. Valid state -> no save or rebuild. Stale state -> authoritative rebuild.
- IDE churn: compare pre/post Git status; remove only newly generated untracked IDE files.

## Validation

- Test creation deferred unless user requests it. Run relevant existing tests.
- Final Unity checks: finish static edits and accepted review fixes first. Run only checks invalidated by final diff; explicit task or plan checks override.
- C# changes: Unity compile with zero Console errors.
- Movement, input, or generated-lab changes: compile plus relevant `MovementLabBuilder.BuildMovementLab()` and `ValidateMovementLab()` batch checks.
- Builder-generated change: run build twice from same SHA. Build 2 must reuse existing outputs; compare hashes for owned scenes, prefabs, controllers, materials, and importer metadata. Any mismatch -> nondeterministic build bug.
- Run `ValidateMovementLab()` in separate Unity process after build 2. Build success alone does not prove persisted references or bindings.
- Scene, prefab, or Editor-tool changes: save, reopen or validate, inspect log and Git diff.
- Project or package changes: restart Unity when required; confirm affected renderer, input, build-scene, and assembly configuration.
- Documentation-only changes: inspect diff; Unity launch unnecessary.
- Report only checks run.
- When user must run Unity menu command, include standalone uppercase line: `MANUAL "ROCKET FOOXBALL → BUILD MOVEMENT LAB" REQUIRED.`

## Code clarity

- Write self-documenting code. Add short comments or class/method descriptions only for non-obvious intent.
