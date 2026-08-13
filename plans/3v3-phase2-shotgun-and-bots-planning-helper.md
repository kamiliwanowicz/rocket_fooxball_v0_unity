# 3v3 Phase 2 Shotgun And Bots Planning Helper

## Authority

- Status: planning helper. Not design authority.
- Gameplay rule conflict -> [`3v3-bots-combat-design-handoff.md`](3v3-bots-combat-design-handoff.md) wins.
- Run-scope/decomposition conflict -> this doc wins.
- Audience: LP + [`task-breakdown`](../.agents/skills/loop-orchestrator/agents/task-breakdown.md). Sections addressed separately below.
- Cite this doc in breakdown dispatch `evidence paths` alongside both handoffs.
- Evidence baseline: `ffe3a14f8d655faad333c2a3c63c0bb55aa8b970`, branch `shotgun_design_and_match_foundation`. Re-observe at INIT; every `observed:` claim below was taken at that SHA.

## User decisions binding this run

- One loop run covers all remaining scope. Long wall clock accepted.
- No mid-run playtest gate. User verifies manually once every piece is in place.
- Consequence accepted: shotgun feel and bot difficulty are tuned together after the run, not separately. Record preserved-vs-changed tuning values per candidate so a regression stays attributable.

## Run instruction for LP

- Route: `hybrid`. Expect ~11 candidates, ~10 waves, parallel lanes only in wave 1. Do not force `single_plan`; do not expect phase-1's lane count.
- Parallel gain is near zero after wave 1. Phase 1 declared 3 waves / 2 lanes and actually ran 5 waves with only wave 1 parallel -> cause recorded in its own state: builder contracts + generated manifest + scene overlap. Same cause applies here, stronger.
- One `run_id` for the whole run. Never open a second run for this objective.
- Dispose stale phase-1 run dir `3v3-phase1-20260811T221945585-98af23fa` (phase `BREAKDOWN`, `Plans: None`, never advanced) before INIT. Resume locates runs by identity, never similarity -> stale sibling is a live recovery hazard.
- Retry budget: phase 1 needed 14 execution attempts for 6 candidates (~2.3 per Unity-mutating candidate; P1 alone took 5). Forecast ~25 attempts here. Record `blocked attempt -> preserved SHA` per candidate so retries resume from preserved work, not wave baseline.
- Environment pre-flight per candidate dispatch, not once per run: short worktree path, private `Library/`, short evidence root probed at deepest expected path, zero Unity process, zero project lock. Two phase-1 attempts died on provisioning alone with zero product mutation.
- Harness pre-gate cost scales with builder ownership. Phase 1 logged 41 harness dirs + 37 Unity invocations for 6 candidates. Every candidate here that edits `Assets/_Game/Editor/MovementLab/*.cs`, `Tools/Validation/*.ps1`, or `Tools/Tests/**` re-stales the gate and re-pays it on every step.
- Bake: every new pickup, bot, and viewmodel is dynamic per HealthPickup precedent -> not a lighting input -> production bake self-skips. Guard the `bakeCount >= 2` hard stop anyway. Any candidate marking a new visual `isStatic = true` converts itself into a rebake + user-authority request.
- Record accepted integration SHA per wave. With ~10 waves a missing per-wave record forces drift replay from run baseline.
- Intermediate waves run Git/scope/downstream rows only. Final wave runs union of pending production-final rows once.
- Run ends at `READY_FOR_USER_MERGE`.

## Run instruction for task-breakdown

- Treat candidate shape below as strong prior, not fixed answer. Deviate only with recorded evidence.
- Resolve every `D*` decision below inside the breakdown/plan `Decisions` block. Workers must never choose these.
- Serialize every candidate that writes `Assets/_Game/Editor/MovementLab/**` or any generated output. Only wave 1 candidates avoid that set.
- Do not propose a registration-only "shared prelude" candidate that adds path consts for assets nobody builds yet -> unstable fragment, cannot validate standalone. Each builder-touching candidate ships a complete vertical slice instead: runtime + builder + validator + regenerated output.
- Size with S/M/L/XL buckets. Full mechanic (state + lifecycle + integration) = L = usually two workers. Phase-1 shipped three XL single-worker feature commits that should have split at seam `pure logic + types -> lifecycle/integration -> scene/prefab composition`.
- Every candidate `owns` must enumerate transitive generated outputs it can stale: `Assets/_Game/Scenes/MovementLab.unity`, `Assets/_Game/Prefabs/Player.prefab`, `Assets/_Game/Generated/MovementLabBuildManifest.json`, affected materials, affected `.meta`. Under-declared `owns` produced 8 separate regen commits in phase 1.
- Forbid new whole-scene or whole-project aggregate assertions. Validator scope must match owner scope, else candidate N breaks candidate M's gate.
- New runtime script directories (`Runtime/Bots/**`, new `Runtime/Weapons/Shotgun*.cs`) must be added to stage input lists in the same candidate that creates them, or builds silently reuse stale outputs.

## Decisions required before any candidate is dispatched

Bot control seam (highest risk; all four are one coupled decision):

- D1 intent injection. `PlayerInputReader` is `sealed`, consumers hold concrete typed serialized fields, Unity cannot serialize interface fields. Options -> (a) unseal + virtuals + `BotInputReader` subclass; (b) abstract `PlayerIntentSource` MonoBehaviour base, change 6 serialized field types; (c) leave reader human-only, add programmatic intent API on consumers (`PlayerMotor` move/jump, `BallKick.RequestDash`, launcher fire); (d) synthetic InputSystem devices. (b) and (c) touch validator-locked surfaces; (d) collides with the single shared action asset. Recommend (c) -> smallest validator blast radius, keeps `PlayerInputReader` meaning "device input" per `AGENTS.md`.
- D2 aim ownership. `PlayerLook` is `Update`-only, cursor-gated, disabled for non-local, has no `SetAim`. Aim is consumed from the camera transform. Options -> extend `PlayerLook`; new `BotAim` writing root yaw + `Head.localRotation`; move yaw into `PlayerMotor`. Two components writing the same transforms must be provably mutually exclusive. Also decide write phase: `Update` (look convention) vs `FixedUpdate` (where launcher/kick read it).
- D3 dash route. Bot raises kick intent (keeps cooldown, `DashStarted`, immunity cancel, presentation) vs bot calls `PlayerMotor.TryStartDash` directly (loses all four). Recommend intent route.
- D4 leaf-gate policy. `ParticipantState.cs:387-388` gates launcher and kick on `active && localParticipant` -> bots cannot fire or dash today. Decide final form of these two lines once, and assign them to exactly one candidate. Also decide whether the bot brain is gated by `ApplyLeafSimulation` or self-gates on `MatchSimulationEnabled`.

Bot rest:

- D5 tick site + execution order. `BallKick` is `[DefaultExecutionOrder(-100)]`; intents are consumed in `FixedUpdate`. Bot decision must land before that to be same-step visible.
- D6 component placement. One `BotController` on shared `Player.prefab` disabled for local slot, vs prefab variant, vs `AddComponent` in composer roster loop. Variant breaks the "same prefab for six slots" provenance assumption the validator asserts.
- D7 sensing source. Serialized refs injected by builder vs match-owner reads (`MatchController.Participants`, ball). Scene search is banned. Pickup memory must respect "visible state + remembered timing only".
- D8 difficulty config ownership. Serialized fields vs ScriptableObject profiles vs roster-level selection on `MatchController`.
- D9 pure-logic split. How much decision logic lands in static `Bot*Rules` modules. Determines wave-1 candidate size.
- D10 rocket-jump model. Heuristic (pitch-down + underfoot window) vs ballistic solve. Decide whether bot rocket-jumping is difficulty-gated or cut for this run.
- D11 navigation model. See `I6`. Recommend no NavMesh package, no new builder stage.

Shotgun:

- D12 hitscan vs projectile. Recommend hitscan -> `MatchController.DestroyAllProjectiles` only knows `Launcher`, so any shotgun projectile leaks through goal freeze, final, and coordinated reset.
- D13 friendly-fire policy reuse. `ParticipantRelationship` + `GetRelationship` are private to `ExplosionResolver`. Extract to shared public type before shotgun, or shotgun silently diverges from rocket rules.
- D14 shotgun state owner. Carried-shotgun flag + shell count on `ParticipantState` (beside `TryRestoreHealth`) vs on the weapon component. Reset clearing sites are `ParticipantState.cs:190` (kickoff) and `:232` (respawn).
- D15 read-model extension. `ParticipantReadModel` has a 9-arg ctor and no shell fields; extending it churns `ParticipantState` + existing test factories. Cheaper -> pass shells/hasShotgun as plain params into new rules modules unless HUD genuinely needs them through the read model.
- D16 HUD split. `MatchHud.cs` is 713 LOC and receives four new features. Decide up front whether to extract per-screen `Draw*` + `MatchHudScreenPolicy` first, or serialize all four HUD changes into one candidate.

## Repository findings

Shotgun asset:

- observed: `Tools/Blender/generate_fps_shotgun.py:56-91` -> two profiles -> `Assets/_Game/Models/FpsShotgun.fbx` (viewmodel, 0.22x0.95x0.30 m) + `Assets/_Game/Models/Shotgun.fbx` (world/third-person, 0.18x0.92x0.26 m). Not interchangeable.
- observed: `generate_fps_shotgun.py:339-341,409-410` -> exactly 3 objects `WeaponMetal`, `WeaponDark`, `WeaponAccent`, one material slot each, slot name == object name, fixed order.
- observed: mesh datablocks are prefixed `FpsShotgun_WeaponMetalMesh` / `Shotgun_WeaponMetalMesh`; launcher uses bare `WeaponMetalMesh` -> name-keyword slot mapping must be re-derived per asset.
- observed: `generate_fps_shotgun.py:29-31,679-691` -> muzzle is Blender `-Y`, mapped to Unity `+Z` by export flags. Nothing in C# asserts this.
- observed: `FpsShotgun.fbx.meta:8,86,105` + `Shotgun.fbx.meta` -> `materialImportMode: 2`, `importAnimation: 1`, `animationType: 2` -> Unity stock defaults, never touched by pipeline. `MovementLabImportPipeline.cs:419-424` asserts `None/false/None` -> all three currently violated.
- observed: shotgun needs no new material or texture family -> reuse `WeaponMetal.mat` / `WeaponDark.mat` / `WeaponAccent.mat` in slot order metal, dark, accent.
- observed: `Assets/_Game/Prefabs/Player.prefab:11380-11447` -> `WeaponVisual` mount pattern: launcher FBX instance under `Head/Camera/Viewmodels` at localPos `(0.28,-0.22,0.34)` with 3 explicit material overrides. Exact mirror site for the shotgun viewmodel.

Combat + vitals:

- observed: only two damage producers exist -> `ExplosionResolver.cs:128` and `BallKick.cs:204`, both calling `ParticipantState.TryApplyDamage(attacker, amount, cause, weapon)` (`ParticipantState.cs:260`).
- observed: `ParticipantState.cs:262` -> single gate enforces friendly-damage-none, self-damage-none, immune-none before any health mutation -> new damage sources inherit it free, no registration.
- observed: knockback policy is NOT inherited -> `ExplosionResolver.cs:115-122,133-161` keeps relationship enum + `GetRelationship` private.
- observed: immunity is a plain float timer (`ParticipantState.cs:38,61`), cancelled via `CancelImmunity()` `:246`; triggers are rocket launch `:443`, dash start `:442`, meaningful ball contact `:332`. `RocketLauncher.cs:76` also cancels directly -> two paths for one event.
- observed: `ArenaPickup.cs:84` `protected abstract bool TryApplyToParticipant(ParticipantState)` -> weapon/ammo pickup immunity cancel needs zero `ParticipantState` changes.
- observed: no weapon abstraction. `RocketLauncher` is `sealed`, held as a single concrete field `ParticipantState.cs:32`, referenced by `MatchController.cs:415-426`, `PlayerPresentation.cs:21`, `MovementLabSceneComposer.cs:224`.
- observed: `ParticipantDamageCause` / `ParticipantDeathCause` (`ParticipantContracts.cs:19-36`) have no Shotgun member; `MatchRules.cs:9` pins them append-only.
- observed: hitscan is feasible with no layer or matrix change. Named layers 8/9/10; collision matrix is all-enabled; player has exactly one collider (`CharacterController`) -> no per-limb hitboxes, falloff must be authored in the weapon.
- observed: pellets must exclude the Projectiles layer or they detonate live rockets (`Rocket.prefab` layer 9, non-trigger).
- observed: ball push entry point -> `BallMotor.QueueImpulse` `:150-158`, hard-capped at 40 `:436-441`. `ApplyKick` replaces velocity and is dash-only -> wrong tool for shotgun.

Participant gates (bot blockers):

- observed: `ParticipantState.cs:386` -> motor already simulates for bots, no locality gate.
- observed: `ParticipantState.cs:387-388` -> launcher + kick gated on `localParticipant` -> bots cannot fire or dash.
- observed: `ParticipantState.cs:391-401` -> input component disabled, `PlayerLook` disabled, cameraFeedback disabled for non-local.
- observed: net current bot behavior -> stands still, gravity and blast impulses still apply, nothing rotates them.
- observed: all six slots instantiate the same `Player.prefab` (`MovementLabSceneComposer.cs:544-579`) -> bots already carry the full local stack.
- observed: one shared `InputActionAsset` across six readers -> per-bot reader instances double-fire callbacks and can globally disable actions.
- observed: `MatchController.cs:689-735` -> exactly 6 participants, 3 Blue, 3 Red, exactly one local Blue. Roster size frozen.

Builder:

- observed: stage order `Quality -> Importer -> MaterialPrefab -> GameplayScene`; lighting stages are bake-menu only.
- observed: registration is hard-coded per asset, not data-driven -> new FBX/prefab/material each touch `MovementLabContract.cs`, `MovementLabContractCatalog.cs`, `MovementLabStageGraph.cs`, plus pipeline + validator.
- observed: eight editor files are shared by both workstreams -> `MovementLabContract.cs`, `MovementLabContractCatalog.cs`, `MovementLabStageGraph.cs` Definitions, `MovementLabPrefabPipeline.BuildPlayerPrefab`, `MovementLabSceneComposer.AssembleGameplaySceneStage` + `BuildParticipantRoster`, `MovementLabImportPipeline.cs`, `MovementLabMaterialPipeline.cs`, `MovementLabValidator.cs` (1404 LOC).
- observed: pickup precedent is complete and cheap on the runtime side -> `PickupRespawnState` (58 LOC pure) + `ArenaPickup` (176 LOC abstract) + `HealthPickup` (24 LOC sealed). New pickup families = ~25 LOC each runtime, everything else is builder cost across 6 files.
- observed: validator counts are hard-coded per family -> `MovementLabValidator.cs:328` compares to `HealthPickupSpawns.Length`, `:987` to `ParticipantSlots.Length`. New families need their own roots and count contracts; reusing `HealthPickups` root breaks `:322`/`:678`.
- observed: `MovementLabValidator.cs:849-868` -> MatchHud serialized surface frozen to exactly `{match, localParticipant, input}`.
- observed: `MovementLabValidator.cs:663-669` -> `MatchController` forbidden from holding any pickup-typed field or property -> shotgun removal on reset stays event-driven via `CoordinatedResetRequested` (`MatchController.cs:86`).
- observed: manifest write authorization is bound to the exact current Git SHA and consumed on use (`MovementLabManifestStore.cs:356-393`) -> any intervening commit invalidates it.

Match + HUD:

- observed: `MatchHud.cs` 713 LOC, IMGUI, 9 screens, no Setup, no Pause. Frame-snapshot pattern -> new fields captured in `Update`, drawn in `OnGUI`.
- observed: `goRemaining` transient overlay (`MatchHud.cs:94,182,195-198`) is the exact pattern for the hit marker; crosshair is uGUI in the prefab, so an IMGUI centre marker does not contend for ownership.
- observed: match start is ungated -> `MatchController.Start:104-112` calls `BeginNewMatch` unconditionally. A pre-match setup screen requires deferring this.
- observed: `ApplyGameplayGate(bool)` `:403-413` already freezes everything -> pause is a new state, not new machinery.
- observed: `MatchState` is duplicated in `MatchRules.cs:10-18` and `MatchController.cs:17-25`, bridged by a cast at `MatchController.cs:60`, with `MovedFrom` attrs and an ordinal test -> one new phase touches 4 sites.
- observed: clock/frags/tiebreak state lives in `MatchController`, not `MatchRules`. Shotgun and bot logic need no `MatchRules` change.
- observed: EditMode assembly already exists -> `Assets/_Game/Scripts/Tests/EditMode/RocketFooxball.EditModeTests.asmdef`, references `RocketFooxball.Runtime`, 3 test files. New pure-logic tests are siblings, no asmdef edit.
- observed: tiebreak already covered by `MatchRulesTests.cs:113-138` -> no new tiebreak tests unless rules change.

Arena + navigation:

- observed: walkable floor y=0, x +/-65, z +/-45. Long walls and end walls have standable 1 m tops at y=8. Goal openings z +/-18.5, height 0..7.
- observed: ramps at x +/-22, 15 deg, decks rising to y~4.93, inside `slopeLimit 60` -> walkable, no jump link needed.
- observed: ramp deck overlaps the floor in XZ over roughly 9.6 x 18 m per ramp -> a single-height 2D graph cannot represent it.
- observed: goal recesses (37 x 9 each, behind x +/-64.5) are disconnected in plan view and gated by `ShieldCollider` state that toggles at runtime.
- observed: jump apex ~0.98 m (jumpVelocity 4.80, gravity 11.8125) vs ramp ledge drop 4.93 m and wall tops at 8 m -> ledges are one-way down; wall tops reachable only by rocket jump (underfoot blast gives vy up to 24 m/s).
- observed: every objective, spawn, and pickup sits at y 0..1.1 -> upper perches can be excluded from the bot graph for this run with zero objective loss.
- observed: `Packages/manifest.json` has `com.unity.modules.ai` (legacy runtime API) but not `com.unity.ai.navigation` -> no NavMeshSurface bake path on Unity 6.
- observed: `MovementLabSceneComposer.cs:583-633` `BuildParticipantSpawnSet` -> existing precedent for authoring a gameplay-data graph (nodes + scoring weights + visibility mask) in the builder. Reuse this shape for any bot waypoint graph.

## Candidate shape prior

Wave 1 -> parallel, zero builder writes, zero generated output:

- `C1 shotgun-pure-rules` -> `ShotgunAmmoRules` (grant 8, cap 16, overflow discarded, at-cap rejects, collect without shotgun) + `ShotgunDamageRules` (range falloff, fixed spread, two-close-shots kill, pellet-count to ball-force with cap) + EditMode tests. owns `Runtime/Weapons/Shotgun*Rules.cs`, `Tests/EditMode/Shotgun*Tests.cs`. `unity_mutation: false`. size M.
- `C2 bot-pure-rules` -> difficulty parameter table, error model (aim noise, reaction latency, decision jitter), role-assignment and target-scoring primitives + EditMode tests. owns `Runtime/Bots/*Rules.cs`, `Tests/EditMode/Bot*Tests.cs`. `unity_mutation: false`. size L, two workers. Requires D8/D9 settled.

Wave 2+ -> strictly serial, one Unity-mutating candidate per wave:

- `C3 shotgun-asset-import` -> register both FBXes, configure importers to the weapon contract, closed-inventory + fingerprint + `Tools/Validation` metadata path updates. size M. Self-validating without any prefab work.
- `C4 shotgun-viewmodel-mount` -> mount `FpsShotgun` under `Viewmodels`, material slot wiring, presentation ref, per-slot visibility. size M. Visual only, no fire.
- `C5 shotgun-weapon-runtime` -> extract shared relationship/friendly-fire policy (D13), `ShotgunWeapon` component, RMB action + reader surface, shotgun ownership + shells on the vitals owner (D14), reset/respawn clearing, damage-cause enum append, immunity cancel on fire. size L, two workers.
- `C6 shotgun-and-ammo-pickups` -> two `ArenaPickup` subclasses + prefabs + materials + one neutral shotgun spawn + two mirrored ammo spawns + scene roots + validator count contracts + immunity cancel on pickup + goal-reset shotgun removal. size L, two workers.
- `C7 shotgun-hud` -> live-HUD shotgun/ammo widget states, hit marker, death-summary weapon fidelity. Optional first worker: HUD split per D16. size M-L.
- `C8 bot-control-seam` -> D1-D4 implemented, plus builder wiring on the five non-local slots, proven by one trivial hardcoded behavior (face ball, approach ball). size L, two workers. Highest-risk candidate in the run.
- `C9 bot-navigation` -> steering + traversal over floor, two ramp decks, two shield-gated recesses, one-way drop edges. size L.
- `C10 bot-perception-and-roles` -> visibility + pickup-timing memory, dynamic attacker/support/defender, football-first priorities, allied pickup etiquette. size L, two workers.
- `C11 bot-combat` -> aim + prediction, rocket use, dash use, shotgun use, error-model application, optional difficulty-gated rocket jump per D10. size L, two workers.
- `C12 difficulty-and-setup-ui` -> difficulty selection before match start (requires deferring `BeginNewMatch`), pause screen with difficulty display, allied bots pinned Medium. size L.

Ordering rules:

- C3 -> C4 -> C5 -> C6 -> C7 is a hard chain: import before mount, mount before weapon, weapon before pickups (pickups grant shells), pickups before HUD only for the full state matrix.
- C8 must precede C9-C11. C8 owns the final form of `ParticipantState.cs:387-388`; C5 must not restructure those lines.
- C12 last -> it needs every difficulty parameter to exist.
- C1/C2 outputs are consumed by C5/C11 respectively -> wave 1 must merge before those.

## Issues to surface before implementation

- I1 `ParticipantState.cs:387-388` is a guaranteed two-candidate collision: C5 wants a sibling shotgun gate line, C8 must change the condition itself. Assign final form to C8; C5 adds its line in the form C8 will consume.
- I2 Three files absorb most of the run: `MatchHud.cs` 713 LOC gets 4 features, `MatchController.cs` 737 LOC gets 4 changes (goal reset, pause, deferred start, difficulty), `ParticipantState.cs` 511 LOC gets shotgun state + bot gates. Serialize or split first; do not fan out across them.
- I3 Render budgets no longer gate anything. All budget throws removed repo-wide -> `MovementLabLightingPipeline.cs` whole-scene 140/150000/180/8, `MovementLabArenaPipeline.cs` Architecture renderer 80 and ArenaKit triangle 75000, Blender per-module + aggregate + weapon + character triangle raises, retro-texture 96 MiB gate. Counts still measured and logged/manifested. `AGENTS.md` now states render budgets are report-only. Consequence for this run: no renderer-count planning, no viewmodel renderer merging for budget reasons, no early guard candidate. Do not reintroduce a budget throw. Perf remains a real concern -> user verifies manually; treat frame cost as playtest feedback, not a build gate.
- I4 Registering the shotgun FBXes rewrites both `.meta` files and changes closed-inventory and fingerprint sets, including `Tools/Validation/Invoke-MovementLabWorkflow.ps1:69-70`. That is a guard path -> harness pre-gate goes stale and must be re-run.
- I5 `materialImportMode` currently 2 (Import). Switching to None breaks any prefab referencing an extracted FBX sub-material to pink. Wire the three slots explicitly, as `Player.prefab:11427-11444` does for the launcher.
- I6 NavMesh is the largest unscoped item. No package, no stage, and `MovementLabSceneComposer.cs:151` rebuilds the scene from scratch every GameplayScene run. Adding it needs a package add, a new stage enum member + definition + runner case, a `ManifestSchemaVersion` bump, an SHA-bound migration authorization, and a document-transplant to survive scene recreation. Recommend rejecting NavMesh for this run -> `CharacterController` steering + raycast visibility + a builder-authored waypoint graph mirroring `BuildParticipantSpawnSet`. If breakdown disagrees, NavMesh becomes its own candidate ahead of C9, not a task inside it.
- I7 Arena is genuinely multi-layer. Any flat single-height nav model either buries the ramp decks or makes the under-ramp floor unwalkable. Shield colliders mutate connectivity at runtime -> a statically baked graph routes bots into a solid wall during shield-up phases.
- I8 Fall damage and ledge safety are unmodeled. A bot graph treating descents as free will walk bots off 4.93 m ramp ledges and 8 m wall tops.
- I9 Prefab-only changes silently skip scene composition. If GameplayScene is stale only for `dependency-state-changed`, the composer does not run. A bot component added to `Player.prefab` without a GameplayScene contract or input key change ships with no per-slot configuration. Bump `gameplay-scene-contract` in the same change.
- I10 Manifest migration authorization is bound to the exact Git SHA and consumed on use. In a ~10-wave run, authorize immediately before each build; never batch.
- I11 Damage/death cause enums are append-only. An unmapped shotgun cause degrades to `Unknown` -> frags still score, weapon fidelity in death and goal summaries is lost.
- I12 Bots must not instantiate additional `PlayerInputReader` components. Six readers already share one `InputActionAsset`; extra instances double-fire callbacks and can globally disable actions.
- I13 `AGENTS.md` is stale where it says the first EditMode test assembly should be created when next touching pure gameplay logic. It exists at `Assets/_Game/Scripts/Tests/EditMode/RocketFooxball.EditModeTests.asmdef` with three test files. Do not plan a candidate to create it.
- I14 No committed audit artifact exists for the shotgun meshes. Triangle counts and bounds live only in Blender stdout. Any "already verified" claim about shipped tri counts is unbacked; re-run the generator if the number matters for I3.
- I15 The muzzle axis convention (Blender `-Y` -> Unity `+Z`) is asserted nowhere in C#. A fire-direction bug presents as backwards pellets and no existing validator catches it.
- I16 `ExplosionResolver` is a single shared scene instance with fixed-size shared buffers wired to all six launchers. Per-weapon blast tuning needs new serialized fields or a second instance; nested resolves within one frame reuse the same buffers.
- I17 Immunity is cancelled twice per rocket shot (direct call plus event). Harmless today. A shotgun copying only one path diverges; pick one deliberately.
- I18 Enemy-rocket knockback fraction (0.5) has never been felt in play -> inert avatars do not fire. C8 is the first moment it becomes observable, and it lands in the same run as shotgun feel and bot difficulty.
- I19 `MatchHud` serialized surface is frozen to exactly three refs by the validator -> shotgun and ammo data must reach the HUD through `localParticipant` or `match`, or the HUD candidate also owns a validator edit and contends with the builder candidate on the same file.
- I20 Nine hard-coded screen-policy rows in the validator (`MovementLabValidator.cs:870-889`) break on any new screen or `Resolve` signature change -> C12 writes both `MatchHud.cs` and `MovementLabValidator.cs`.

## Validation

- Follow `AGENTS.md` `Unity execution` + `Validation` verbatim. Harness pre-gate before every workflow or Unity invocation, including `-PlanOnly`.
- Pure logic (ammo economy, damage curve, difficulty params, bot decision scoring, cooldown math) -> EditMode tests as siblings in the existing assembly.
- Builder-generated change -> builder protocol: production bake current -> one authoritative build -> semantic validate in a separate Unity process.
- Cheap early guard against I3: run `Assemble` + fast build before any bake work on C4 and C6.
- Declare `expected_status` before every production-final invocation. Two consecutive mismatches halt the run.
- Generated churn -> separate commit `chore: regenerate MovementLab outputs`.
- Report only checks run.

## Done condition

Playable five-minute 3v3 match in the Movement Lab scene:

- five bots move, aim, navigate ramps and recesses, contest the ball, defend, and score;
- enemy bot difficulty selected before kickoff and shown in the pause screen; allied bots fixed Medium;
- bots take and deal rocket damage; enemy-rocket knockback observable for the first time;
- shotgun collectible from the neutral spawn, fires on RMB, kills a full-health target in two close shots, pushes the ball, empties, and restocks from ammo pickups;
- shell cap 16 respected; ammo collectible without a shotgun; at-cap collection rejected;
- death or goal removes carried shotgun and shells;
- immunity cancels on weapon and ammo pickup;
- HUD shows shotgun state, shell count, and hit marker; death summary names the correct weapon.

## Tuning targets

Expose as serialized values. Expect change after the user's manual verification pass.

- shotgun: per-pellet damage, pellet count, spread angle, range falloff curve, pump delay, ball force scale and cap;
- ammo: pack size, carry cap, respawn delay, shotgun spawn respawn delay;
- bots: reaction latency, aim noise, prediction error, decision interval, role-switch hysteresis, per-difficulty multipliers, pickup-etiquette thresholds;
- previously exposed and still unvalidated: enemy-rocket knockback fraction, dash feel values, death wait, immunity duration, spawn-safety weights.
