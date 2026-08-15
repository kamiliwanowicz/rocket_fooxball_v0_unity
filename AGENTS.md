# Rocket Fooxball Unity POC

First-person rocket-jumping football prototype. Test whether rocket movement, ball control, defense, and scoring feel fun, readable, and skill-based.

User new to Unity. Explain Unity-specific concepts at junior level. Keep general technical discussion concise.

## Priorities

1. Responsive, predictable rocket-jumping
2. Satisfying ball control and reliable scoring
3. Fast tuning and stable performance
4. Visual polish follows core gameplay unless current task explicitly targets graphics

## Delivery posture

- PoC -> optimize for fast gameplay learning, not production completeness.
- Prefer smallest reversible change proving intended behavior. Reuse existing patterns and assets.
- Spend effort on issues likely to break playtests, builds, integration, project assets, or iteration speed.
- Defer broad abstraction, speculative future-proofing, production hardening, exhaustive edge-case handling, and untargeted polish unless required for core-loop reliability or explicitly requested.

## Repository map

Layout is discoverable by convention; list directories instead of trusting any enumeration here.

- Runtime gameplay: `Assets/_Game/Scripts/Runtime/` -> one folder per gameplay concern, folder name = concern name -> namespace `RocketFooxball.Runtime.<Folder>`. Find owner of a concern by folder name; new concern -> new folder + matching namespace.
- Editor tooling: `Assets/_Game/Editor/` -> thin command facade `MovementLabBuilder.cs` (menu entry points only) -> per-domain pipeline files under `MovementLab/`, each named for its domain. Namespace `RocketFooxball.Editor`. Read facade first to see which pipelines a command touches.
- Tooling scripts: `Tools/` -> `Validation/` workflow entry points, `Tests/` harness suite guarding them, `Blender/` external asset generation.
- Primary sandbox and build scene: `Assets/_Game/Scenes/MovementLab.unity` — generated output, not hand-authored (see Architecture).
- Input actions: `Assets/InputSystem_Actions.inputactions`
- Agent orchestration skills: `.agents/skills/`. Active design and handoff docs: `plans/`.
- Unity, package, and project configuration: `ProjectSettings/`, `Packages/`

Project-owned gameplay assets -> `Assets/_Game/`. Leave Unity starter content outside that root unchanged unless task targets it.

## Architecture

- Preserve current Unity and package versions unless requested.
- URP rendering. Default Standalone target -> native 1920x1080 High quality: PBR materials, HDR, shadows, SSAO, restrained bloom, modern lighting, baked indirect light, reflection/light probes. Maintain scalable Low fallback. Concrete profile values live in the Editor quality/lighting-profile sources — read them, do not assume. Human-review High/Low visual quality and target-machine performance at 1920x1080 on demand.
- Graphics work may add or replace project-owned arena, ball, rocket, explosion, and containment visuals. Preserve gameplay contracts unless current task explicitly authorizes named gameplay or collision changes.
- Player collision/movement -> `CharacterController`. Ball and projectile physics -> `Rigidbody` forces and impulses.
- Critical gameplay simulation -> fixed-step code. Shared physics configuration -> `GamePhysicsSettings`.
- Device input -> Input System -> `PlayerInputReader` intent -> gameplay components. No legacy `UnityEngine.Input` polling.
- Runtime code -> `RocketFooxball.Runtime`, no `UnityEditor`. Editor tooling -> `RocketFooxball.Editor` with explicit assembly references. Rendering concern owns URP-only behaviour so core gameplay stays testable without URP or editor code.
- Project namespace segment shadows same-named `UnityEngine` type inside it: `RocketFooxball.Runtime.Physics` shadows `UnityEngine.Physics` -> `CS0234`. Fully qualify (`UnityEngine.Physics.Raycast`, `UnityEngine.Physics.IgnoreCollision`). Same risk for `Rendering`, `Animations`, `Audio`.
- One state owner per concern. Callers request operations; owners mutate their own state. Leaf components report narrow events upward and never own match-wide state such as score or coordinated reset. Match concern is the single owner of score, match state machine (`Playing -> GoalFreeze -> Reset -> Playing`), input gate, and reset timing; triggers only raise events, owners execute their own reset.
- Wiring is direct serialized references plus narrow callbacks. No event bus, DI container, or speculative service layer — PoC favours traceable references over indirection. Runtime components never search the scene for gameplay owners; same-object required components may use `GetComponent` fallback; missing serialized dependency -> log exact composition error and disable component. Diagnostics-only components are the sole exception and may keep discovery fallback.
- Frame ownership: gameplay physics, impulses, cooldowns, goal crossing -> `FixedUpdate`; input sampling, look, freeze timers -> `Update`; camera pose/FOV/shake -> `LateUpdate`.
- Editor builder is the composition root: it owns the generated scene, gameplay prefabs, materials, every cross-object reference, build-scene entry, and physics settings. Its validator reopens the generated scene and verifies persisted references, prefab provenance, and component contracts. Rationale: scene and prefabs are build outputs, so hand-edits in the Editor lose to the next rebuild and reference bugs only surface after reload.

## Unity asset safety

- Preserve `.meta` files and GUIDs. Move or delete asset and `.meta` together.
- Prefer `MovementLabBuilder` or Unity Editor APIs over direct serialized-YAML edits.
- Prefer public runtime/editor API over private serialized project-setting fields: `UnityEngine.Physics.IgnoreLayerCollision(a, b, false)` + dirty/save `PhysicsManager`, not `m_LayerCollisionMatrix` writes. Read live `SerializedProperty.propertyType` under pinned Unity version before trusting remembered layout; `LayerMask` reports `SerializedPropertyType.LayerMask`, assign through `intValue`.
- Generated-manifest schema migration is separate state transition, not incremental stage write: keep legacy manifest resumable -> run full non-lighting pass -> persist/reload outputs -> write complete migrated manifest -> resume strict per-stage probing. Never write partial current-schema state before every newly declared output exists.
- Builder-owned change -> edit source/builder -> rebuild -> validate -> inspect diff. Manual generated-asset edits are not authoritative.
- Serialized prefab component reference: runtime non-null check insufficient. Save/reload, require nonzero YAML `fileID`, verify `PrefabUtility` source provenance.
- Imported animation lookup: exact clip name first; delimiter-safe suffix fallback only. Validate expected object identity and distinct state motions, not names alone.
- Generated controller rebuild: reuse valid states/transitions or remove stale subassets before replacement. Never clear arrays then append replacement subassets indefinitely.
- Generated YAML stays in Unity-native serialization form. Unity writes empty scalars as `key: ` (trailing space); never post-process generated asset or `.meta` bytes to strip it. A counter-normalizer has no fixed point — Unity re-adds the space on the next import/save, so every run dirties unrelated generated files in both directions. Comparators already `TrimEnd()` each line, so the trailing space carries no semantic weight.
- Atomic generated-file replacement: one helper owns it — `File.Replace(` may appear only in `Assets/_Game/Editor/MovementLab/MovementLabAtomicFile.cs`, and every `Tools/Validation/*.ps1` must parse clean. Harness guards enforce both; a partially written generated asset corrupts the import cache, so scattering raw replaces is a hard no.
- Capture pre/post Git status. Classify changed generated output from authoritative inventory + exact task declaration; ownership affects scope only. Every changed authoritative output needs exact coverage: comparator path/output, `SEMANTIC:`, `DANGLING:`, GUID stability, asset/`.meta` pairing; attach at checkpoint/reviewer even when separate regeneration commit excludes raw diff. Changed bytes -> run comparator -> accept only canonical-equal + no `DANGLING:` increase + no GUID churn + intact asset/`.meta` pairing; comparator-unsupported -> reject until supported; otherwise reject. Keep generated churn in separate `chore: regenerate MovementLab outputs` commit. Remove only newly generated IDE files.

## Unity execution

- Tooling: no Computer Use or related `sky.documentation` / `node_repl` tools.
- Orchestrator-created worktrees, junctions, evidence aliases, scratch roots, fixtures, and temporary directories on `C:` -> descendants of `C:\wt` only. Never create `C:\<name>` or use Windows temp directories for project tooling. Durable evidence may stay under Git-common run root; short alias stays under `C:\wt`.
- Unity worktrees: use short `C:\wt\<id>` path or verified junction/subst alias there. Use same short project path for all Unity commands and process checks. Create short evidence alias such as `C:\wt\<id>e` before worker dispatch and probe deepest expected path. Workflow `Assert-EvidencePathBudget` rejects over-long evidence path and write-probes before lease, Unity, or product mutation.
- Unity processes: one Editor per project. Close interactive Editor before batch mutation. Batch run -> `Start-Process -Wait -PassThru` -> capture exit code -> confirm process and project lock release.
- Failure classification: decide pass/fail from process exit code, workflow result JSON, Unity exception, compile errors, and semantic validation result. Never fail from keyword-only log scan. Known-benign here: `[Licensing::Module] LicensingClient has failed validation; ignoring`, `[Licensing::Module] Error: Access token is unavailable; failed to update`, `d3d12: failed to query info queue interface (0x80004002).`
- Harness pre-gate: before any Unity-mutating workflow, run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1` harness-unit suite; finish in `<90s` without Unity process or project lock. Failure or timeout blocks Unity.
- Harness red baseline: `Tools/Tests/Fixtures/` holds deliberately broken copies of guarded scripts. Missing fixture throws instead of silently skipping. Guard cases must pass at HEAD and fail against their fixture, proving the guard can still detect regressions. Edit a guarded workflow script -> update its fixture so it stays red, else harness self-check breaks.
- Import cache: preserve each worktree's `Library/` between runs. Delete only with cache-corruption evidence. Never share one `Library/` across concurrent worktrees. New worktree -> provision its own private `Library/` before first Unity mutation; preserve across retries.
- C# inner loop: run relevant existing Unity test when available; its import/compile is sufficient before test execution. Otherwise run compile-only Unity batch with `-batchmode -nographics -quit`. Skip `MovementLabBuilder.BuildMovementLab()` during inner-loop compilation.
- `dotnet build`: optional fast preflight against current Unity-generated project files; never authoritative Unity compile proof.
- Successful Unity builder/validator execution already supplies compile proof for covered source. Builder protocol subsumes generic build/validate rows; do not launch duplicate compile checks.
- Builder no-op gate: derive staleness from source/input digest before importer, prefab, material, or scene writes. Current input digest -> no save or rebuild; stale input -> authoritative rebuild.
- Production bake gate: bake entry point owns skip/rebuild from current lighting inputs. Valid skip emits source-defined digest marker and zero bakes; absent marker -> exactly one bake; duplicate or malformed marker -> fail. Copy marker from source, never retype.
- Integrity gates: source/input digests decide staleness and bake reuse. Generated-output bytes/hashes provide provenance only; never gate rebuild, acceptance, or nondeterminism. Digest text inputs from canonical Git blob bytes (`git hash-object`) or newline-normalized bytes. Raw worktree bytes differ by CRLF/LF across checkouts (`.gitattributes` -> `*.cs text`, unity YAML `eol=lf`) and produce false staleness plus unowned regeneration churn.
- Builder command surface: facade exposes staged entry points (assemble without lighting -> pre-bake validation gate -> production bake -> full build) so agents can run the cheapest sufficient stage. Read the facade for current names and composition. Invariant: full build and semantic validate both fail unless a production bake is already current -> bake first.

## Unity tests direction

- `com.unity.test-framework` already installed; project EditMode test assembly: `Assets/_Game/Scripts/Tests/EditMode/RocketFooxball.EditModeTests.asmdef`.
- Target: EditMode NUnit tests for deterministic pure runtime logic only (bot decisions, match state machine, scoring, cooldown math). The test assembly references `RocketFooxball.Runtime`.
- Skip: PlayMode tests, coverage goals, feel/physics assertions (playtests own feel), MonoBehaviour wiring tests (builder validator owns wiring).
- Tests grow only where regression would break playtests.

## Validation

- Final Unity checks: finish source edits first. Run only checks invalidated by final diff; explicit task or plan checks override.
- C# changes: Unity compile with zero Console errors.
- Movement, input, generated-lab, or other builder-generated change -> run builder protocol.
- Builder protocol: ensure production bake current (bake command self-skips when inputs unchanged) -> one authoritative build -> semantic validate in a separate Unity process. Separate process proves references persisted to disk. Do not require second builds.
- Semantic proof always comes from the builder's validate entry point run directly. Automated screen capture never substitutes for it. Human visual review stays on demand.
- Validator scope matches owner scope: contract assertions run against owning subtree. New presentation object never invalidates unrelated owner's contract.
- Render budgets are report-only. Renderer counts, triangle counts, opaque passes, transparent statics, and texture memory are measured and logged/manifested, never enforced. No build, import, validate, or Blender generate step fails on a budget. Do not reintroduce a budget throw without explicit user instruction.
- Scene, prefab, or Editor-tool changes: save, reopen or validate, inspect log and Git diff.
- Project or package changes: restart Unity when required; confirm affected renderer, input, build-scene, and assembly configuration.
- Documentation-only changes: inspect diff; Unity launch unnecessary.
- Lighting posture: Fast preview is default. Development bake is explicit, on-demand, best-effort. Replacement production bake is explicit and milestone-only after source edits settle in scoped-clean worktree. Digest reattestation or zero-bake reuse may run whenever builder protocol requires current lighting. Later lighting-input edits reopen affected checks.
- Report only checks run.
- When user must run Unity menu command, include standalone uppercase line: `MANUAL "ROCKET FOOXBALL → BUILD MOVEMENT LAB" REQUIRED.`

## Code clarity

- Write self-documenting code. Add short comments or class/method descriptions only for non-obvious intent.
- Reference shared contract constants through owning type (`MovementLabContract.HealthPickupEastSouthRotation`), never unqualified.
