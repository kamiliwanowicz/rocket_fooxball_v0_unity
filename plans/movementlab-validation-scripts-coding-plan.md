# MovementLab Validation Scripts Coding Plan

Status: proposed
Source: direct user request for fast smoke E2E and on-demand multi-angle visual validation
Run ID: direct
Plan ID: direct
Attempt ID: direct
Covered Requirements: direct request
Baseline: 5a2a930d73fece9b72f16c2024ac8a5321fa35c0
Dependencies: None

## Objective

Add two agent-runnable validation entry scripts:

- `Run-MovementLabSmoke.ps1` -> one fast Unity Play Mode smoke covering movement, jump, look, fire, rocket/blast, kick, scoring, freeze, reset, containment, and unexpected-error health.
- `Capture-MovementLabVisuals.ps1` -> on-demand deterministic 1920x1080 PNG evidence from first-person plus five external viewpoints for AI visual inspection after substantial visual changes.

Completion boundary: scripts run non-interactively against generated `MovementLab`; produce ignored evidence under `Logs/Validation/`; fail reliably on missing execution evidence; preserve builder-owned assets and unrelated work.

## Scope

- in: shared PowerShell Unity discovery/process guard; runtime automation seam in `PlayerInputReader`; one Play Mode smoke fixture and asmdef; editor-only URP screenshot validator; six camera views; XML/log/JSON/PNG evidence; exact batch invocations
- in: new `.meta` files for every new asset under `Assets/`; Unity import owns GUID generation
- out: subjective movement-feel approval; device-specific input injection; legacy input polling; standalone player build; exhaustive regression suite; performance/FPS gate; pixel-perfect golden-image comparison; automatic visual capture on every change; video capture; IMGUI HUD capture; builder/generated scene/prefab/material edits

## Repository Findings

- observed: branch `core_mechanics`; worktree root `C:/Users/iwano/Desktop/repos/rocket_fooxball_v0_unity/rocket_fooxball_v0_unity`; accepted source baseline candidate `5a2a930d73fece9b72f16c2024ac8a5321fa35c0`
- observed: initial scan was clean; during planning, unrelated unstaged work appeared in `Assets/_Game/Editor/MovementLabBuilder.cs`, `Assets/_Game/Prefabs/Player.prefab`, `Assets/_Game/Scenes/MovementLab.unity`; plan creation did not mutate these paths; preserve all three
- observed: `ProjectSettings/ProjectVersion.txt` -> Unity `6000.5.6f1`; installed default executable observed at `C:/Program Files/Unity/Hub/Editor/6000.5.6f1/Editor/Unity.exe`
- observed: `Packages/manifest.json` -> Input System `1.20.0`, Unity Test Framework `1.7.0`, URP `17.5.0`, screen-capture and image-conversion modules present
- observed: `ProjectSettings/TimeManager.asset` and `GamePhysicsSettings` -> `60 Hz`; gravity magnitude `16.875`
- observed: `Assets/_Game/Editor/MovementLabBuilder.cs` -> sole generated-asset authority; public batch entry points `BuildMovementLab()` and `ValidateMovementLab()`; validation marker `Rocket Fooxball Movement Lab validation succeeded`
- observed: generated `Assets/_Game/Scenes/MovementLab.unity` -> enabled sole build scene; roots `Arena`, `Player`, `Ball`, `ExplosionResolver`, `MatchController`, `DebugHUD`; player `(0,0,3)` faces `Vector3.back`; ball `(0,0.72,0)`; goals at `z=-44` and `z=44`; containment near `x=+-69`, `y=-4..14`, `z=+-59`
- observed: `PlayerInputReader` -> sole device-intent owner; direct Input System reads for move/look, rising-edge callbacks for jump/kick, held fire sampled in `Update()`; no current automation seam
- observed: runtime chain -> `PlayerInputReader` -> `PlayerMotor`, `PlayerLook`, `RocketLauncher`, `BallKick`; `RocketProjectile` -> `ExplosionResolver` -> player/ball impulse owners; `GoalTrigger` -> `MatchController` -> freeze/reset owners
- observed: `BallKick.TryKickNow()` and reset/launch APIs support deterministic integration arrangement, but smoke must still drive user actions through `PlayerInputReader`
- observed: `plans/core_mechanics_pt2.md` already specifies Play Mode smoke, `PlayerInputReader` test seam, bounded fixed-step advancement, relational assertions, no device-specific event injection, no exact-trajectory assertions
- observed: repository policy defers tests unless user requests them; current request explicitly authorizes smoke test design
- observed: no `Assets/_Game/Tests/`, no validation PowerShell scripts, no screenshot/capture utility
- observed: `Player/Head/Camera` FOV `75`; crosshair uses `ScreenSpaceCamera` at `1920x1080`; camera excludes `LocalPlayerHidden`; baseline hides character head on that layer; current protected builder work expands layer assignment to complete `WorldVisual`; external capture culling `~0` supports either state; viewmodels live under camera and must be hidden during external captures
- observed: `MovementDebugHud` uses `OnGUI`; SRP camera render request will not prove IMGUI HUD appearance
- observed: Unity 6 URP supports `RenderPipeline.StandardRequest` into `RenderTexture`; batch screenshot process requires graphics device and must omit `-nographics`
- constraint: one Unity process per project; each batch run uses `Start-Process -Wait -PassThru`, captures exit code, waits for project process and `Temp/UnityLockfile` release
- constraint: runtime contains no `UnityEditor`; editor utility stays in `RocketFooxball.Editor`; generated asset changes remain builder-owned
- constraint: input change requires compile plus relevant `BuildMovementLab()` and separate-process `ValidateMovementLab()` checks
- proposed: `Tools/Validation/UnityValidation.Common.psm1` -> shared Unity lookup, lock/process guard, evidence directory, process execution, run manifest
- proposed: `Tools/Validation/Run-MovementLabSmoke.ps1` -> smoke entry script
- proposed: `Assets/_Game/Tests/PlayMode/MovementLabSmokeTests.cs` -> one bounded integrated smoke coroutine
- proposed: `Assets/_Game/Editor/MovementLabVisualValidator.cs` -> editor-only deterministic screenshot entry point
- proposed: `Tools/Validation/Capture-MovementLabVisuals.ps1` -> visual entry script

## Decisions

- assumption: smoke target is quick integration health, not tuning or feel certification
- assumption: visual capture runs manually only after large visual, camera, material, model, lighting, arena, or presentation changes
- assumption: current unstaged builder/prefab/scene work remains unrelated protected work; execution may start only after LP either binds an accepted baseline/dependency containing that work or explicitly confirms exclusion; baseline drift requires revised artifact, never silent reuse
- decision: use one Play Mode Unity Test Framework test in Editor; actual scene, `MonoBehaviour` loops, `CharacterController`, `Rigidbody`, collision, score, and reset paths run without standalone build overhead
- decision: add internal `PlayerInputReader` automation seam plus `InternalsVisibleTo("RocketFooxball.PlayModeTests")`; production consumers stay unchanged; no virtual keyboard/mouse, reflection, legacy polling, or device events
- decision: automation seam mirrors input semantics: move/look values; jump/kick rising edges from held-state transitions; fire held; gameplay gate clears all injected state
- decision: smoke assertions use direction, bounds, state transitions, counts, positive motion, caps, and bounded timeouts; never exact PhysX trajectory or subjective feel
- decision: goal flow uses `BallMotor` owner APIs to arrange fast physical plane crossing; calls `MatchController.ResetMatch()` directly after freeze proof to avoid five-second wait
- decision: smoke wrapper omits `-quit` because Test Framework owns completion; parses NUnit XML and pass marker instead of trusting process exit alone
- decision: visual validator calls `MovementLabBuilder.ValidateMovementLab()` but never `BuildMovementLab()`; capture must not repair stale state or mutate generated assets
- decision: visual capture stays Edit Mode and uses serialized spawn state; no physics, input, wall-clock wait, or Play Mode variance
- decision: visual render uses supported `RenderPipeline.StandardRequest`, synchronous `ReadPixels`, and PNG encoding; first render per camera/state is warm-up and discarded; second render becomes evidence
- decision: resolution fixed `1920x1080`; matches crosshair reference and gives AI-readable detail
- decision: no golden image comparison; validator rejects missing, zero-byte, wrong-size, or near-uniform captures; AI inspects output images when workflow is invoked
- decision: evidence lives under ignored `Logs/Validation/`; no `AssetDatabase.Refresh()`, no scene save, no output under `Assets/`
- question: None

## Execution Graph

`START -> T1 -> CP1 -> T2 -> CP2 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in; `+` requires every named predecessor
- gates: `START` -> accepted artifact bound to baseline `5a2a930d73fece9b72f16c2024ac8a5321fa35c0` and current protected dirty work explicitly excluded, or fresh revised artifact bound after that work is accepted upstream; `CP1` -> smoke workflow committed, frozen, Critical/High review complete, accepted fixes disposed; `CP2` -> visual workflow committed, frozen, Critical/High review complete, accepted fixes disposed; `FINAL` -> both scripts pass at same clean committed head and visual evidence is inspectable
- rule: shared PowerShell process contract created in T1; T2 consumes but does not change it; serialized execution prevents shared-contract drift

## Tasks

### T1: Fast MovementLab smoke workflow

- objective: deliver one command that exercises basic gameplay motion and integrated match flow in a bounded Play Mode smoke run
- covered_requirements: fast smoke E2E; basic motions; reliable pass/fail; agent-runnable evidence
- owner: W1, implementation worker, `luna_max`
- depends_on: None
- owns: `Tools/Validation/UnityValidation.Common.psm1`, `Tools/Validation/Run-MovementLabSmoke.ps1`, `Assets/_Game/Scripts/Runtime/PlayerInputReader.cs`, `Assets/_Game/Scripts/Runtime/AssemblyInfo.cs`, `Assets/_Game/Scripts/Runtime/AssemblyInfo.cs.meta`, `Assets/_Game/Tests.meta`, `Assets/_Game/Tests/PlayMode.meta`, `Assets/_Game/Tests/PlayMode/RocketFooxball.PlayModeTests.asmdef`, `Assets/_Game/Tests/PlayMode/RocketFooxball.PlayModeTests.asmdef.meta`, `Assets/_Game/Tests/PlayMode/MovementLabSmokeTests.cs`, `Assets/_Game/Tests/PlayMode/MovementLabSmokeTests.cs.meta`
- protected: `Assets/InputSystem_Actions.inputactions`, `Assets/_Game/Editor/MovementLabBuilder.cs`, `Assets/_Game/Scenes/MovementLab.unity`, `Assets/_Game/Prefabs/**`, `Assets/_Game/Materials/**`, `Packages/**`, `ProjectSettings/**`, every runtime file except `PlayerInputReader.cs` and new `AssemblyInfo.cs`, unrelated user changes
- focused_reads: `plans/core_mechanics_pt2.md` for smoke contract; `plans/runtime-architecture.md` for state owners; `plans/completed/core-behaviour.md` for mechanics; `PlayerInputReader.cs`, `PlayerMotor.cs`, `PlayerLook.cs`, `RocketLauncher.cs`, `RocketProjectile.cs`, `ExplosionResolver.cs`, `BallKick.cs`, `BallMotor.cs`, `GoalTrigger.cs`, `MatchController.cs` for exact public state and lifecycle; `MovementLabBuilder.cs` for spawn/goal/containment values; runtime/editor asmdefs for assembly boundaries
- implementation: create `UnityValidation.Common.psm1`; export `Resolve-UnityEditorPath`, `New-ValidationRunDirectory`, `Invoke-UnityBatchProcess`, `Assert-UnityProjectAvailable`; parse `m_EditorVersion`; accept explicit `-UnityPath`; default to standard Hub path; require executable exists
- implementation: common invocation always adds `-batchmode`, quoted absolute `-projectPath`, quoted absolute `-logFile`; optional `-NoGraphics`; caller supplies remaining arguments; launch exact executable through `Start-Process -Wait -PassThru -WindowStyle Hidden`; never shell-evaluate argument text
- implementation: before launch, fail when `Temp/UnityLockfile` exists or a `Unity.exe` command line owns normalized project path; after exit, wait up to `30 s` in `250 ms` intervals for both process ownership and lock release; timeout fails run
- implementation: create unique UTC evidence directory `Logs/Validation/<workflow>/<yyyyMMdd-HHmmssfff>`; preserve every run; no cleanup or overwrite; write `run.json` with schema, workflow, start/end UTC, project root, Unity version/path, Git HEAD, dirty flag, full argument list, process exit, evidence paths, status; failure path still writes manifest when process started
- implementation: add `AssemblyInfo.cs` with only `[assembly: InternalsVisibleTo("RocketFooxball.PlayModeTests")]`
- implementation: extend `PlayerInputReader` with internal automation state and methods: `EnableAutomationInput()`, `SetAutomationMove(Vector2)`, `SetAutomationLook(Vector2)`, `SetAutomationJumpHeld(bool)`, `SetAutomationFireHeld(bool)`, `SetAutomationKickHeld(bool)`, `DisableAutomationInput()`
- implementation: `Move`, `Look`, `FireHeld` read injected state only while automation enabled; jump/kick setters set existing one-shot flags only on false-to-true transition; repeated true stays held and creates no new press; real Input System callbacks and device sampling cannot mix with automation; `ClearGameplayState()`, gameplay disable, component disable, and automation disable clear injected values/held edges/one-shot state; automation fire bypasses cursor lock only inside automation mode
- implementation: preserve production behavior byte-for-byte outside automation branches; no public gameplay API expansion; no `UnityEditor`, reflection, static global input state, or device event queue
- implementation: create `RocketFooxball.PlayModeTests.asmdef`; assembly name exact; root namespace `RocketFooxball.Tests`; reference `RocketFooxball.Runtime`; `optionalUnityReferences` contains `TestAssemblies`; Editor-only Play Mode test assembly; no Input System test framework reference because smoke uses automation seam
- implementation: create `MovementLabSmokeTests` with one `[UnityTest]`, `[Category("Smoke")]`, exact method `MovementLab_BasicGameplaySmoke`; load `Assets/_Game/Scenes/MovementLab.unity` single; yield one render frame plus bounded fixed steps; resolve exactly one player/input/look/camera/launcher/kick/ball/match/resolver/HUD and exactly two goals; assert `60 Hz`, gravity, spawn containment, serialized owner readiness
- implementation: add reusable coroutine helpers `WaitFixedSteps(count)`, `WaitFixedUntil(predicate,maxSteps,label)`, `WaitFramesUntil(predicate,maxFrames,label)`, `AssertContained(player,ball)`; every wait has explicit bound and failure diagnostic; no unbounded `while`
- implementation: movement phase -> inject `(0,1)` for `12` fixed steps; require movement toward player forward, displacement `>0.25 m`, speed positive, speed `<= HardCap + 0.05`; release for `8` fixed steps; require speed decrease; assert containment
- implementation: jump phase -> reset match; wait grounded up to `30` fixed steps; hold jump; require positive vertical velocity; keep held across `6` fixed steps and prove no second upward discontinuity; release one fixed step; press again while airborne; require vertical velocity increase and `IsAirJumpAvailable == false`; release; assert containment
- implementation: look/fire/rocket/blast phase -> reset; inject downward look across bounded render frames until `PlayerLook.PitchDegrees` falls in `25..45` degrees and camera forward points downward; clear look; hold fire until one projectile registers; release immediately; require exactly one launch; wait up to `90` fixed steps for detonation/unregister; require projectile count returns zero and player or ball receives positive speed change from blast; assert cooldown finite and caps/containment hold
- implementation: kick phase -> reset; restore neutral aim then use bounded look input toward ball; inject forward move until physical player-ball centre distance is within reach; stop; subscribe `KickSucceeded`; press/release kick through automation; require one event, positive ball speed, ball velocity broadly aligned with camera, speed `<= HardCap + 0.05`; immediate second press during cooldown must not increment event; unsubscribe in `finally`; assert containment
- implementation: score/freeze/reset phase -> reset; launch one rocket through automation and require active count `1`; place ball immediately field-side of North plane with `BallMotor.ResetState(new Vector3(0,0.72,-43.5), Quaternion.identity)`; call `Physics.SyncTransforms()`; drive crossing with `BallMotor.ApplyKick(Vector3.back, Vector3.zero)`; wait at most `15` fixed steps; require South score `1`, North score `0`, match `GoalFreeze`, North goal latched, input/player/ball/launcher/kick disabled, ball kinematic, active rockets zero; call `ResetMatch()` directly; require `Playing`, all owners enabled, goals rearmed, player reset/facing/velocity, ball x/z reset and zero linear/angular velocity, score retained; allow ball y depenetration range instead of exact zero because current reset centre is floor-level
- implementation: fixture teardown always disables automation, restores time scale, destroys remaining rockets, resets match/score when objects survive, and unloads/replaces active scene; unexpected Unity error/exception logs fail; success logs exact marker `MOVEMENT_LAB_SMOKE_PASS` plus move distance, first/second jump velocity, kick speed, score, fixed-step count
- implementation: create `Run-MovementLabSmoke.ps1`; import common module; accept optional `-UnityPath` and `-OutputRoot`; allocate `Logs/Validation/Smoke/<run-id>`; call Unity with `-nographics -runTests -testPlatform PlayMode -assemblyNames RocketFooxball.PlayModeTests -testFilter RocketFooxball.Tests.MovementLabSmokeTests.MovementLab_BasicGameplaySmoke -testResults <run>/results.xml`; omit `-quit`
- implementation: wrapper passes only when process exit `0`, `results.xml` exists, exact test case exists once with result `Passed`, suite failed count `0`, Editor log contains `MOVEMENT_LAB_SMOKE_PASS`, and log contains no compiler failure/unhandled exception outside test-runner summary; print absolute evidence directory; any missing evidence fails nonzero
- done when: one wrapper command completes from clean project; actual generated scene executes every required phase; XML and log prove one passing smoke; failure in any phase produces nonzero wrapper result and bounded diagnostic
- checks: W1 snapshots Git status and builder-owned asset hashes; confirms no Unity process/lock; runs one fresh-process `BuildMovementLab()` using common process function; exit `0`, build marker present, builder-owned hashes unchanged; waits for release; runs separate-process `ValidateMovementLab()`; exit `0`, success marker, zero compile/Console errors; waits; runs `powershell -ExecutionPolicy Bypass -File Tools/Validation/Run-MovementLabSmoke.ps1`; exit `0`, exact XML test passed, marker present, no error/exception; inspect Git status for owned-only source/meta changes and no generated asset drift; `git diff --check`; invalidation = any T1 source, asmdef, wrapper, common module, or generated meta edit reruns compile/build/validate/smoke
- proof: smoke must discriminate stale/missing wiring, dead fixed loop, no movement, broken jump edge, cursor-dependent automation leak, no rocket creation/detonation, missing blast response, kick miss/cooldown failure, no score transition, incomplete freeze/reset, containment escape, and unexpected error; low relational thresholds avoid tuning lock
- review_focus: Critical/High production-input regression from automation branch; automation state surviving gate/reset; Play Mode fixture global-state leak; false pass from absent/wrong XML test; unbounded wait; direct state mutation bypassing required action path; generated asset mutation; unsafe PowerShell process/path handling
- review_checkpoint: CP1
- return_evidence: exact changed paths/symbols; Unity build/validate run directories; smoke run directory; XML case/result; pass marker and metrics; pre/post generated hashes; process/lock release proof; owned-only diff; residual platform risk

### T2: On-demand multi-angle visual workflow

- objective: deliver one command that validates current generated scene then emits deterministic, inspectable multi-angle PNG evidence without changing project assets
- covered_requirements: optional visual check; screenshots from different angles; AI-verifiable output after large visual changes
- owner: W2, implementation worker, `luna_max`
- depends_on: CP1 accepted clean committed SHA
- owns: `Tools/Validation/Capture-MovementLabVisuals.ps1`, `Assets/_Game/Editor/MovementLabVisualValidator.cs`, `Assets/_Game/Editor/MovementLabVisualValidator.cs.meta`
- protected: `Tools/Validation/UnityValidation.Common.psm1`, T1 smoke/runtime/test paths, `Assets/_Game/Editor/MovementLabBuilder.cs`, every builder-owned scene/prefab/material/controller/texture/model/shader, `Packages/**`, `ProjectSettings/**`, unrelated user changes
- focused_reads: T1 common module export contract; `MovementLabBuilder.BuildMovementLab()`/`ValidateMovementLab()` and `BuildArena()` for ownership/layout; player prefab creation for camera/viewmodel/head layers; generated scene hierarchy; `MovementDebugHud` for known IMGUI exclusion; `RocketFooxball.Editor.asmdef`; active URP/quality/graphics settings
- implementation: create `MovementLabVisualValidator` in `RocketFooxball.Editor`; public static command entry `CaptureMovementLab()`; optional menu item `Rocket Fooxball/Capture Movement Lab Visual Evidence`; keep file separate from builder
- implementation: parse exact custom arguments `-rocketFooxballVisualOutput`, `-rocketFooxballSourceSha`, `-rocketFooxballSourceDirty`; command-line run requires absolute output directory outside `Assets`; directory must be missing or empty and is created once; menu run defaults to new `Logs/Validation/Visual/Manual/<UTC-id>/captures`
- implementation: fail before rendering when graphics device is null, active render pipeline absent, quality level differs from project default index `0`, output invalid, or scene validation fails; call `MovementLabBuilder.ValidateMovementLab()` once; never call builder build/save APIs
- implementation: resolve `Player`, `Player/Head/Camera`, `Player/Head/Camera/Viewmodels`, `Player/Head/Camera/CrosshairCanvas`, `Ball`, `Arena`, both goals, active render pipeline; require serialized scene spawn state and clean scene before staging
- implementation: fixed `1920x1080`; allocate `RenderTexture` with `ARGB32`, `24`-bit depth, sRGB/default color conversion, one sample; create `RenderPipeline.StandardRequest { destination = renderTexture }`; require `RenderPipeline.SupportsRenderRequest(camera, request)`; submit once for warm-up, clear/read nothing, submit second time for evidence
- implementation: set `RenderTexture.active`; `Texture2D.ReadPixels`; `Apply(false,false)`; `ImageConversion.EncodeToPNG`; restore prior active target and camera state in `finally`; destroy temporary textures/cameras immediately; synchronous readback only
- implementation: first-person capture uses existing player camera at neutral serialized pose/FOV; reset `PlayerCameraFeedback`; force canvas layout; keep viewmodels/crosshair active; do not enter Play Mode
- implementation: external captures use one disabled `HideAndDontSave` camera; `CopyFrom(playerCamera)`; set culling mask `~0` so `LocalPlayerHidden` head renders; set far clip at least `250`; temporarily disable player camera, `Viewmodels`, and `CrosshairCanvas` so external shots show world character without floating first-person objects; restore exact active/enabled state in `finally`
- implementation: capture exact ordered views: `01_gameplay_spawn.png` -> existing player camera/FOV `75`; `02_arena_overview_ne.png` -> position `(78,62,70)`, target `(0,1,0)`, FOV `55`; `03_north_goal_three_quarter.png` -> `(30,12,-14)`, target `(0,3.5,-44)`, FOV `50`; `04_south_goal_three_quarter.png` -> `(-30,12,14)`, target `(0,3.5,44)`, FOV `50`; `05_player_front_three_quarter.png` -> `(3.6,2.2,-1.2)`, target `(0,1.05,3)`, FOV `38`; `06_ball_field_detail.png` -> `(4.5,2.4,5.5)`, target `(0,0.72,0)`, FOV `45`
- implementation: each capture must decode at `1920x1080`, produce nonzero PNG, pass sampled luminance-range/unique-color sanity threshold preventing black/blank/solid false success, and receive SHA-256 plus byte length; threshold remains broad and does not compare artistic pixels
- implementation: write `capture-manifest.json` only after all six images pass; use same-directory unique temporary file then atomic no-overwrite rename; schema includes status, UTC, Unity version, project path, scene path/GUID/dependency hash, source Git SHA/dirty flag, validation passed, quality level, pipeline asset path/name, graphics device type/name/version, color space, width/height, warm-up count, and ordered captures with file/hash/size/camera pose/target/FOV/culling mask/luminance metrics
- implementation: after capture, require scene never saved and asset dependency hash unchanged; log exact marker `MOVEMENT_LAB_VISUAL_CAPTURE_PASS <absolute-manifest-path>`; exception escapes command entry so Unity batch returns failure; no `AssetDatabase.Refresh()`
- implementation: create `Capture-MovementLabVisuals.ps1`; import T1 common module without modification; accept optional `-UnityPath` and `-OutputRoot`; allocate `Logs/Validation/Visual/<run-id>` plus empty `captures/`; pass Git HEAD/dirty state to editor command; invoke Unity with graphics enabled, `-quit -executeMethod RocketFooxball.Editor.MovementLabVisualValidator.CaptureMovementLab`; never add `-nographics`
- implementation: wrapper passes only when process exit `0`, marker exists, manifest status is `passed`, source SHA/dirty values match wrapper observation, exactly six ordered captures exist, hashes/byte lengths match files, and no extra PNG is present; print absolute capture directory for `view_image`
- done when: one wrapper command produces six valid viewpoints plus manifest/log in ignored output; source/generated assets unchanged; each PNG can be opened by AI image inspection
- checks: W2 confirms CP1 SHA and common-module contract; records pre-run status and generated hashes; confirms no Unity process/lock; runs `powershell -ExecutionPolicy Bypass -File Tools/Validation/Capture-MovementLabVisuals.ps1`; exit `0`, validation and visual pass markers, six PNGs, manifest/hash/dimension/sanity checks pass; opens every PNG through image inspection and confirms expected framing: first-person viewmodel/crosshair/ball, full arena overview, both goals, full external character including head, ball/material/field detail; compares pre/post generated hashes and Git status; zero source/generated mutation outside T2 paths; `git diff --check`; invalidation = visual validator/wrapper/common module/camera/render/scene/material/model/shader/quality/pipeline edit reruns capture and six-image inspection
- proof: first-person/external layer handling prevents missing head or floating viewmodel false evidence; overview proves whole-arena framing; symmetric goal views prove both ends; detail shots prove character/ball/material readability; warm-up discard and image sanity prevent blank shader-startup output; manifest binds evidence to source state without treating cross-machine pixels as golden tests
- review_focus: Critical/High accidental scene/asset save; use of `-nographics`; unsupported SRP request; state not restored; output written under `Assets`; first-person/external culling corruption; false pass on missing/blank images; path overwrite; PowerShell lock/process safety; manifest/source mismatch
- review_checkpoint: CP2
- return_evidence: exact changed paths/symbols; capture run directory; manifest; six hashes/dimensions; validation/pass markers; image-inspection notes; pre/post asset hashes; process/lock release proof; owned-only diff; residual GPU/HUD risk

## Execution Assignments

- workers: T1 -> W1 `luna_max`, serial lane; T2 -> W2 `luna_max`, starts only after CP1 accepted SHA
- review_checkpoints: CP1 -> T1/W1 -> after W1 return, scope check, commit, clean frozen SHA -> fresh `sol_medium` Critical/High review -> fixes by fresh `luna_max`, no re-review, invalidated checks rerun; CP2 -> T2/W2 -> after W2 return, scope check, commit, clean frozen SHA -> fresh `sol_medium` Critical/High review -> fixes by fresh `luna_max`, no re-review, invalidated checks rerun

## Final Verification

- exact head: clean committed plan SHA descendant of `5a2a930d73fece9b72f16c2024ac8a5321fa35c0`; accepted artifact digest/size unchanged; owned-only baseline diff
- checks: confirm no interactive Unity/project lock; snapshot builder-owned hashes; run one fresh `BuildMovementLab()` process; require exit `0`, marker, no generated hash drift; wait release; run separate `ValidateMovementLab()` process; require exit `0`, exact success marker, zero compiler/Console errors; wait release
- checks: run `Tools/Validation/Run-MovementLabSmoke.ps1`; require wrapper exit `0`, one exact passed XML test, smoke marker/metrics, no unexpected errors; wait release
- checks: run `Tools/Validation/Capture-MovementLabVisuals.ps1`; require wrapper exit `0`, validation marker, visual marker, manifest passed, six verified PNGs; wait release
- inspect: use image inspection on all six PNGs; record visible expected content and any current visual issue separately from capture-tool correctness; inspect manifest source binding; inspect `git diff --check 5a2a930d73fece9b72f16c2024ac8a5321fa35c0 HEAD -- <owned paths>`; verify `.meta` pairs/GUID uniqueness; verify `git status --short` clean; preserve unrelated pre-existing work
- invalidation: runtime input/test/common/smoke edit -> rerun build, validate, smoke, final status checks; editor visual/wrapper edit -> rerun capture and six-image inspection; generated scene/prefab/material/model/shader/quality/pipeline edit -> repository-required build-twice determinism checks, separate validate, then capture; fix worker reruns every check invalidated by accepted finding

## Handoff

- changed paths: `Tools/Validation/UnityValidation.Common.psm1`, `Tools/Validation/Run-MovementLabSmoke.ps1`, `Tools/Validation/Capture-MovementLabVisuals.ps1`, `Assets/_Game/Scripts/Runtime/PlayerInputReader.cs`, new runtime `AssemblyInfo` pair, new Play Mode test tree and meta files, new editor visual validator pair
- residual risks: PhysX results relational but not cross-platform bit-identical; visual pixels vary with GPU/driver/shader cache; logged-in Windows graphics session required; `MovementDebugHud` IMGUI excluded from render-request screenshots; automated smoke cannot certify movement feel or visual taste
- authority: execution orchestrator may commit only plan-owned paths on isolated plan branch; current unstaged builder/prefab/scene work is protected and never copied, rebuilt, cleaned, staged, or committed by this plan; merging agent integrates clean plan SHA; user-branch mutation requires LP/user authority

## Done Criteria

- every covered requirement maps to task, owner, check, and proof
- every task passes implementation design gate
- Execution Graph includes every task and review checkpoint exactly once and makes every sequential dependency explicit
- every implementation worker maps to one review checkpoint
- exact baseline and dependencies are factual
- execution route uses immutable accepted artifact and `$orchestrate-implementation`
- final checks bind clean committed head or blocker names needed action
