# Rocket Fooxball Unity POC

## Goal

First-person rocket-jumping football prototype. Test whether rocket movement, ball control, defense, scoring feel fun, readable, skill-based.

User new to Unity. Explain Unity-specific concepts at junior level. Keep general technical discussion concise.

## Tech contract

- Unity: `6000.5.6f1`; do not upgrade editor or packages unless requested
- Renderer: Universal Render Pipeline (`com.unity.render-pipelines.universal`)
- Input: Input System package; do not add legacy `UnityEngine.Input` polling
- Platform: Windows + Git
- Player movement: `CharacterController`, not `Rigidbody`
- Ball physics: `Rigidbody` when added
- Gameplay root: `Assets/_Game/`
- Input actions: `Assets/InputSystem_Actions.inputactions`
- Runtime assembly: `RocketFooxball.Runtime`
- Editor assembly: `RocketFooxball.Editor`, Editor-only

## Priorities

1. Responsive, predictable rocket-jumping
2. Satisfying ball control and reliable scoring
3. Fast tuning and stable performance
4. Visual polish last

## Graphics and performance

- Target system: HP EliteBook 840 14 inch G11 Notebook PC; Intel Core Ultra 5 135U (12 cores, 14 logical processors); integrated Intel Graphics; 32 GB RAM; 1920x1200 at 60 Hz.
- POC must run smoothly at 1920x1200 on target system.
- Default to simple, low-cost graphics: primitive geometry, basic URP materials, limited effects.
- Preserve upgrade path for considerably higher visual fidelity when requested.
- Add higher-cost graphics only after measuring target-system performance; keep scalable quality options or fallbacks.

## MVP

- Enclosed arena + two goals
- One controllable player + physics ball
- Rocket launcher, rocket-jumping, explosions affecting ball
- Goal detection, score, reset, debug HUD
- Primitive geometry, simple URP materials

## Scope limits

Exclude unless requested:

- Multiplayer/networking, accounts, progression, inventory, classes
- Extra weapons, realistic football rules, advanced AI
- Detailed characters, animation, procedural levels
- New third-party assets/plugins, advanced shaders, post-processing, destruction

Expand scope only after core interaction validates.

## Repository layout

- `Assets/_Game/Scripts/Runtime/`: runtime gameplay C#; namespace `RocketFooxball`
- `Assets/_Game/Editor/`: editor tooling; namespace `RocketFooxball.Editor`
- `Assets/_Game/Scenes/MovementLab.unity`: current isolated playtest scene and enabled build scene
- `Assets/_Game/Prefabs/Player.prefab`: current player prefab
- `Assets/_Game/Materials/`: project-owned URP materials
- `Packages/manifest.json`, `Packages/packages-lock.json`: package contract
- `ProjectSettings/`: shared Unity project configuration

Keep new project-owned gameplay assets under `Assets/_Game/`. Do not extend Unity starter content under `Assets/Scenes`, `Assets/Settings`, or `Assets/TutorialInfo` unless task concerns it.

Maintain assembly boundaries:

- Runtime code must not reference `UnityEditor` or Editor assembly.
- Editor code may reference Runtime assembly.
- Add asmdef references explicitly when introducing package APIs.
- Do not collapse asmdefs or move Editor code into Runtime assembly.

## Gameplay implementation

- Treat `MovementLab` as primary movement sandbox. Preserve quick iteration, visible telemetry, predictable reset behavior.
- Run gameplay simulation in fixed-step code. Use `Time.fixedDeltaTime` for critical movement/physics math.
- Preserve `GamePhysicsSettings` contract: 60 Hz fixed step and gravity magnitude `16.875`, unless task explicitly retunes it.
- Keep movement formulas deterministic where practical: seeded randomness, pure math helpers, no frame-rate-dependent critical state.
- `CharacterController` owns player collision. Apply gravity and velocity explicitly; move through `CharacterController.Move`.
- Keep tunable gameplay values serialized with useful constraints. Avoid hidden magic values spread across components.
- Preserve Input System action asset and `PlayerInputReader` boundary. Gameplay components consume intent, not device APIs.
- Use `Rigidbody` forces/impulses for future ball and explosion interactions. Do not move dynamic rigidbodies by editing transforms.
- Prefer invariants and bounds over exact physics outcomes: no wall tunnelling, velocity cap respected, impulse direction correct, ball contained, goal event once per entry.

## Unity asset safety

- Treat `.unity`, `.prefab`, `.asset`, `.mat`, `.inputactions`, and `.meta` files as serialized project state.
- Keep every asset with its existing `.meta`. Move/delete asset and `.meta` together. Never regenerate GUIDs to resolve conflicts.
- Avoid hand-editing scene/prefab YAML. Use Unity Editor APIs or targeted text edits only when serialization format and GUID impact are understood.
- For editor-generated content, use `SerializedObject`, `PrefabUtility`, `EditorSceneManager`, and `AssetDatabase`; save assets/scenes explicitly.
- Apply intended prefab changes to prefab asset, not only one scene instance. Check overrides before saving.
- Preserve serialized data when renaming fields; use `FormerlySerializedAs` where required.
- Inspect diffs after editor saves. Reject unrelated mass reserialization, GUID churn, or scene/prefab changes.
- Do not run two Unity Editor processes against this project. Close interactive Editor before batch-mode mutation.

## Movement lab workflow

Interactive rebuild: Unity menu -> `Rocket Fooxball/Build Movement Lab`.

Batch rebuild, with matching Unity executable:

```powershell
& '<Unity.exe>' -batchmode -quit -projectPath '<repo-root>' -executeMethod RocketFooxball.Editor.MovementLabBuilder.BuildMovementLab -logFile '<log-path>'
```

Builder overwrites `Assets/_Game/Prefabs/Player.prefab` and `Assets/_Game/Scenes/MovementLab.unity`, updates materials/build scene/fixed timestep. Run only when task intends those changes. Inspect generated diff and log. Omit `-nographics` unless command is known not to require graphics/shader initialization.

## Validation

Tests intentionally deferred until test strategy is redesigned. Do not add tests or require current automated tests for completion unless user requests them.

Validate changes proportionally:

- C# change: Unity script compile with zero Console errors.
- Movement/input change: play `Assets/_Game/Scenes/MovementLab.unity`; verify affected controls, collision, jump states, HUD, and frame-rate independence.
- Scene/prefab/editor-tool change: run intended editor workflow, save, reopen affected asset, inspect Console/batch log and Git diff.
- Project/package setting change: restart Unity when required; confirm URP, Input System, build scene, and assembly compilation remain intact.
- Documentation-only change: inspect diff; Unity launch unnecessary.

Do not claim Unity validation unless Editor or batch command actually ran. Report skipped validation and reason.

Future test redesign should favor pure deterministic math tests, scene-level smoke/invariant checks, and playtests. Do not assert exact simulated positions or test subjective feel/rendering.

## Generated state

Never edit, review as source, or commit generated/local state:

- `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`
- `Build/`, `Builds/`, `MemoryCaptures/`, `Recordings/`
- generated IDE files such as `*.csproj`, `*.sln`, `*.suo`, `.vs/`

Commit relevant source assets, `.meta` files, package manifests, and intentional `ProjectSettings/` changes. Preserve unrelated user work in dirty worktrees.
