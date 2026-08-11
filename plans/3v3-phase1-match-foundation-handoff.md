# 3v3 Phase 1 Match Foundation Handoff

## Authority

- Status: accepted design handoff. Run-scoped subset of [`3v3-bots-combat-design-handoff.md`](3v3-bots-combat-design-handoff.md).
- Parent doc stays authoritative for every rule. This doc selects phase-1 subset + adds repository grounding.
- Conflict between docs -> parent wins on gameplay rule; this doc wins on run scope.
- Purpose: sole cited source for one loop-orchestrator run.
- Preserve locked decisions. Tuning targets = playtest starting points, not requirements.

## Run instruction for LP

- Route: expect `hybrid`, 6 candidates, 3 waves, 2 parallel lanes per wave. Do not force `single_plan`.
- Provision integration worktree + merging agent. Wave N+1 plans against accepted integration SHA of wave N.
- Run ends at `READY_FOR_USER_MERGE`. User plays merged result, tunes values, then authors phase-2 handoff. No phase-2 work in this run.
- Unity is single-lease resource. Unity candidates declare `unity_mutation: true`, `validation_environment: single Unity lease per plan worktree`. P0 declares `unity_mutation: false`, `validation_environment: Blender headless, no Unity lease` -> P0 and P1 never contend.
- One `expensive_proof_owner` per candidate, run point = final task after fixes accepted.
- P0 acceptance needs user judgment on silhouette. Surface rendered previews and wait. Reviewer covers audit invariants only, never visual fit.

## Run instruction for task-breakdown

Expected candidate shape below. Treat as strong prior, not fixed answer. Deviate only with recorded evidence.

- P0 `blender-shotgun-asset` -> wave 1, parallel to P1 -> baseline = run baseline
  - produces: shotgun meshes + generator, consumed by phase-2 shotgun run, nothing in this run
- P1 `roster-and-vitals` -> wave 1, parallel to P0 -> baseline = run baseline
  - produces: participant identity + team + vitals contract consumed by P2/P3
- P2 `match-rules-and-flow` -> wave 2, parallel to P4 -> baseline = accepted P1 integration SHA
  - produces: clock/score/frag/flow-state contract consumed by P3; goal-reset hook consumed by P5
- P4 `dash-kick-rework` -> wave 2, parallel to P2 -> baseline = accepted P1 integration SHA
  - produces: nothing downstream in this run
- P3 `match-hud` -> wave 3, parallel to P5 -> baseline = accepted wave-2 integration SHA
- P5 `health-pickups` -> wave 3, parallel to P3 -> baseline = accepted wave-2 integration SHA
  - produces: pickup spawn/respawn pattern reused by phase-2 ammo and shotgun pickups

Design obligations breakdown must name for planners:

- P0 -> mesh count and reuse policy; dimensions, origin, forward axis, triangle budget, material slots matched to existing `generate_fps_rocket_launcher.py` output; pump-action silhouette readable at first-person scale and at arena pickup distance.
- P1 -> where participant state lives; how `MatchController` addresses N participants instead of one; whether team identity is enum, component, or roster index; spawn-selection algorithm inputs; immunity collision mechanism against `CharacterController`.
- P2 -> whether `MatchRules` pure static module extends to carry clock/frag/tiebreak state or is displaced by new owner; live-clock pause semantics across `GoalFreeze`; own-goal attribution source.
- P3 -> HUD ownership and update frequency; `Tab` hold input route through `PlayerInputReader`; death overlay visibility scoping to local player only.
- P4 -> which existing `BallKick` behavior survives vs is replaced; where dash momentum state lives given `PlayerMotor` owns movement; grounded-state source for air-use rule; how dash steering composes with existing look input; whether `FpsKick.controller` contact timing still matches a dash-length action; enemy-contact resolution shared with or separate from rocket damage path.
- P5 -> pickup state ownership and respawn timing owner; who drives goal-reset restoration given match concern owns reset; spawn placement authored in builder pipeline; consumption eligibility check against vitals owner.

Do not split P1 further on ownership grounds alone. Roster, vitals, death, immunity all write same prefab + same generated scene -> serialized writers by rule -> split yields zero parallel gain and extra Unity proofs. Split only if planner returns decomposition mismatch.

Wave-2 lanes stay parallel only while P4 writes no `Runtime/Match/**`. Wave-3 lanes stay parallel only while P5 pickup HUD needs stay out of P3's writable set; pickup collection needs no HUD element this phase.

P0 stays parallel only while it writes no Unity wiring. P0 writable set is `Tools/Blender/generate_fps_shotgun.py` plus new files under `Assets/_Game/Models/`, `Assets/_Game/Materials/`, `Assets/_Game/Textures/`. Any P0 need to edit `Assets/_Game/Editor/MovementLab/**`, an existing prefab, or the generated scene -> collision with P1 -> demote P0 to later wave, do not run concurrent writers.

## In scope

### Shotgun asset (P0)

Asset only. Zero gameplay, zero wiring. Follow [`$use-blender`](../.agents/skills/use-blender/SKILL.md) static-prop branch.

- Source: `Tools/Blender/generate_fps_shotgun.py`, idempotent `bpy` generator, sibling pattern to existing `generate_fps_rocket_launcher.py`.
- Two meshes: first-person view model, plus one world model reused for arena pickup and third-person right-hand display.
- Form: pump-action silhouette. Readable at first-person scale and at arena pickup distance. Loaded/empty state never expressed in geometry -> parent handoff hides ammo state from other players.
- Match existing project conventions for dimensions, origin, forward axis, triangle budget, material slots, texture style. Read `generate_fps_rocket_launcher.py` and `generate_retro_textures.py` before choosing values; do not invent a new visual language.
- Deliverable: generator + exported FBX under `Assets/_Game/Models/` + passing mesh audit + seven rendered preview views.

Stop before: import-pipeline registration, prefab mounting, material assignment in generated scene, any `Assets/_Game/Editor/MovementLab/**` edit. Those belong to phase-2 shotgun run.

### Roster and teams

- Six participant slots. Human occupies one Blue slot. Remaining five = inert avatars: full vitals, collision, presentation, zero AI, zero input.
- Teams globally fixed: human team Blue, opponent Red.
- Team identity applied to avatars, goals, spawns, rocket trails, impact accents, immunity effects.
- Shape/symbol cue supplements color for color-blind readability.
- Bot names: short, unique, team color shown, no difficulty suffix.

### Health and damage

- Max health `100`. No regeneration.
- Friendly damage: none. Friendly rocket knockback: none.
- Own rocket -> propels owner, zero self-damage.
- Enemy rocket -> damages target, applies `50%` of owner self-rocket knockback.
- Tuning target: two direct enemy rocket hits kill full-health target.
- No overhead health display on any player. No directional damage indicator.

### Death and respawn

- Death wait: five seconds.
- Death view: spectator view follows ball or ally.
- Respawn: full health, safest team-side spawn, away from ball, enemy goal, visible enemies.
- Post-respawn immunity: two seconds.
- Immunity presentation: unmistakable shield effect + HUD marker.
- Immune player -> moves normally, passes through players, does not block projectiles, keeps immunity while only moving.
- Immunity ends immediately on firing, kicking, meaningful ball contact.
- Self-caused or arena death -> dead player loses one frag, no enemy gains frag.

### Match rules

- Duration five minutes. Live clock continues during death/respawn, pauses during goal summary and kickoff countdown.
- Clock zero -> play stops immediately.
- Winner order: higher goals -> higher team frag total -> draw.
- Team frag total = sum of individual frags. Negative allowed.
- Enemy killing blow -> killer gains one frag. Earlier damage grants nothing.
- Goal credit -> last attacking player touching ball gains individual goal.
- Own goal -> opposing team gains team goal, responsible defender named in summary, no individual goal awarded.

### Match flow

- Match start -> reset positions/state -> three-second `3... 2... 1... GO` countdown.
- Goal -> stop play -> three-second summary -> reset positions/state -> three-second countdown.
- Goal reset -> full health every player, dead players return immediately, every pickup restored with timers reset, no post-kickoff immunity.
- Match end -> winner/draw + deciding rule + final table + `Rematch` + `Exit`.

### Dash-kick (P4)

Rework of existing `BallKick`, not new mechanic. Preserve what already feels right; replace only what target design contradicts.

- Input: `F`.
- Form: forward dash + fully extended leg. Not swing-style football kick.
- Purposes: ball control + movement + limited airborne steering.
- Activation allowed with or without nearby ball.
- Camera aim sets initial direction. Limited steering during dash. No homing or snap-to-ball assist. Forgiving contact volume.
- Momentum: preserve existing velocity + controlled forward burst. Cap resulting speed.
- Cooldown: three seconds after every activation, including miss.
- Air rule: one use until grounded, even when cooldown expires earlier.
- Enemy contact -> small damage + brief shove + ends most forward dash.
- Friendly contact -> no damage, no shove.
- Solid wall contact -> dash stops.
- Ball contact -> strong forward control response.
- Presentation: first-person extended leg visible; world avatar readable extended-leg animation; restrained camera impulse; player retains camera control.

### Health pickups (P5)

- Two mirrored health spawns. No neutral or shotgun spawns this phase.
- Restores roughly one-third health. Cannot overheal. Full-health player cannot consume.
- Respawns `15s` after collection.
- Collection automatic on contact.
- Unavailable -> model/icon disappears. No return warning or countdown.
- Goal reset -> restored immediately, timers reset.

### HUD and summaries

- Live HUD: timer + team goals + personal health icon/bar/numeric value.
- Team frags hidden from persistent live HUD.
- Hold `Tab` -> full match table while alive.
- Match table columns: goals, frags, deaths.
- Goal summary: team goals, team frags, full match table, own-goal attribution when applicable.
- Death summary: killer + weapon, full match table, respawn countdown. Visible only to killed player.
- Opening rules screen: goals primary, frags break tied goals, equal frags produce draw.
- Final summary: winner/draw + deciding rule + full table.
- Global kill feed: none.

## Out of scope

Deferred to later runs. Reject any candidate covering these.

- Bots: navigation, roles, difficulty tiers, pickup etiquette, allied etiquette.
- Shotgun behavior: weapon component, RMB fire, ammo economy, shell cap, shotgun HUD, hit marker, pump delay, damage curve, ball force. Mesh is in scope per P0; everything that makes it a weapon is not.
- Shotgun asset integration: import-pipeline registration, prefab mounting, hand attachment, pickup placement in generated scene.
- Pickups: shotgun spawn, ammo spawns. Health spawns are in scope per P5.
- Networking, right-hand weapon replacement, combat-assist scoring, audio.
- Goal-reset clause "carried shotgun and shotgun ammo removed" -> phase-2 concern; phase-1 goal reset implements health/position/pickup-timer clauses only.
- Immunity cancel on weapon pickup and ammo pickup -> phase-2; phase-1 implements fire/kick/ball-contact cancels.
- Death summary weapon field -> phase-1 has one weapon; keep field, populate with rocket launcher.

## Repository findings

- observed: `Assets/_Game/Scripts/Runtime/Match/MatchController.cs:24-32` -> singular serialized owners `input`, `player`, `playerLook`, `cameraFeedback`, `ball`, `launcher`, `kick`. Single-participant assumption.
- observed: `MatchController.cs:37` -> single `playerResetPosition`. No spawn set.
- observed: `MatchController.cs:42-48` -> `northScore`/`southScore`. Geometry-keyed, not team-keyed.
- observed: `MatchController.cs:119` `ApplyGameplayGate` + `MatchController.cs:132` `ResetMatch` -> address singular owners directly.
- observed: `Assets/_Game/Scripts/Runtime/Match/MatchRules.cs` -> pure static transitions, `MatchState` = `Playing|GoalFreeze|Reset`, `GoalTransition` readonly struct. No clock, no frags, no participants.
- observed: `MatchController.cs:15` + `MatchRules` types carry `MovedFrom` attributes -> serialized-type renames need migration care.
- observed: `grep -rE 'health|team|damage|respawn' Assets/_Game/Scripts` -> zero matches. Entire vitals domain absent.
- observed: `Assets/_Game/Scripts/Runtime/Diagnostics/MovementDebugHud.cs` -> only existing HUD, diagnostics-only, discovery-fallback exception. Not a match HUD foundation.
- observed: `Assets/_Game/Scripts/Runtime/Feedback/PlayerPresentation.cs` + `RocketTrailVfx.cs` -> existing presentation owners; team accents land here.
- observed: `Assets/_Game/Scripts/Runtime/Weapons/ExplosionResolver.cs` + `ExplosionTargetCollector.cs` + `BlastMath.cs` -> current blast applies force only; damage + team filter attach here.
- observed: `Assets/_Game/Scripts/Runtime/Ball/BallKick.cs` -> working tuned mechanic, 240 lines. Cone contact `coneTotalDegrees 35`, `kickRange 4.50`, `contactReachPadding 1.00`, `contactReachScale 1.50`, `cooldown 0.40`, `inputBuffer 0.50`, `speedFraction 0.91`, `playerMomentumShare 0.20`. Public surface `TryKickNow()`, `SetSimulationEnabled`, `ResetState`, events `KickSucceeded`, `KickAttempted`.
- observed: `Assets/_Game/Animations/FpsKick.controller` + `WorldCharacter.controller` -> extended-leg presentation already exists both views.
- observed: `Tools/Blender/generate_fps_kick_rig.py` -> `FpsKickRig.blend` -> `Assets/_Game/Models/FpsKickRig.fbx`; contact timing baked `FPS 30`, `KICK_START 1`, `KICK_CONTACT 5`, `KICK_END 11` -> kick action currently `0.33s`. Dash-length action may invalidate this timing.
- observed: `Assets/InputSystem_Actions.inputactions` -> `Player` map contains `Move`, `Look`, `Fire`, `Jump`, `Kick`. `Kick` action already exists; rebinding to `F` is a binding change, not a new action.
- observed: `Assets/_Game/Editor/MovementLabBuilder.cs:15-189` -> builder facade owns staged entry points; `BuildMovementLab`, `AssembleMovementLab`, `ValidateMovementLab`, bake commands.
- observed: `Tools/Tests/Fixtures/` -> single fixture `red-workflow.ps1.txt`, guards `Tools/Validation/*.ps1` only. Editor C# edits need no fixture update.
- observed: `Assets/_Game/Scripts/Tests/` -> absent. No test assembly exists.
- observed: `Tools/Blender/` -> `generate_fps_rocket_launcher.py`, `generate_low_poly_rocket.py`, `generate_low_poly_character.py`, `generate_arena_kit.py`, `generate_retro_textures.py`, `generate_fps_kick_rig.py`; `.blend` sources `FpsKickRig.blend`, `LowPolyCharacter.blend`. First-person weapon generator precedent exists.
- observed: generated asset roots under `Assets/_Game/` -> `Models`, `Materials`, `Textures`, `Animations`, `Prefabs`, `Generated`, `Scenes`, `Shaders`, `Lighting`.

## Codebase conflicts to surface before implementation

- `MatchController` is single owner of score, state machine, input gate, reset timing per `AGENTS.md` "One state owner per concern". Phase 1 multiplies participants and adds clock/frags/deaths. Conflict: does match concern keep sole ownership, or does participant concern own vitals while match concern owns aggregate? Breakdown must decide boundary, not workers.
- `MatchRules` is pure static with struct returns. Clock + per-participant frags + tiebreak is stateful. Conflict: extend pure module with wider state parameters, or introduce new owner and demote `MatchRules` to goal transitions. Naming decision affects `MovedFrom` migration.
- Generated-asset rule dominates composition. `AGENTS.md` -> `Assets/_Game/Scenes/MovementLab.unity` is generated output, builder is composition root owning scene, prefabs, and every cross-object reference. Six participants + team spawn points + HUD wiring must be authored in `Assets/_Game/Editor/MovementLab/` pipelines then rebuilt. Direct scene or prefab hand-edit is not authoritative and loses to next rebuild.
- `AGENTS.md` "Runtime components never search the scene for gameplay owners" + "No event bus" -> roster wiring stays direct serialized references from builder. Reject service-locator or registry-singleton designs.
- `AGENTS.md` "Wiring is direct serialized references" + six participants -> serialized reference count grows. Confirm builder validator covers new references, nonzero `fileID`, prefab provenance.
- `AGENTS.md` "First test assembly -> `Assets/_Game/Scripts/Tests/EditMode/` ... create when next touching pure gameplay logic" and names match state machine + scoring as targets. Phase 1 is that moment. Expect P2 to create the assembly + asmdef referencing `RocketFooxball.Runtime`.
- Frame ownership rule -> damage, death timers, respawn timers, cooldowns belong in `FixedUpdate`; clock and freeze timers in `Update`; HUD reads in `LateUpdate` or `Update`. Existing `AdvanceGoalFreeze` uses `Time.unscaledDeltaTime` in `Update`; live match clock semantics must not silently inherit unscaled timing.
- Player collision uses `CharacterController`. Immunity pass-through cannot rely on `Rigidbody` layer tricks alone. Planner must settle concrete mechanism.
- P4 regresses a tuned core mechanic. `AGENTS.md` `Priorities` ranks responsive rocket-jumping first and satisfying ball control second; current `BallKick` values are hand-tuned against both. Target design changes cooldown `0.40 -> 3.0` and converts instant cone kick into a dash. Conflict: existing `speedFraction`/`playerMomentumShare` model assumes instant impulse, not sustained dash. Planner must state which existing values survive and why, and must not silently retune ball-control feel while adding the dash.
- P4 movement ownership. `AGENTS.md` "One state owner per concern" + `PlayerMotor` owns movement. Dash momentum, air-use flag, and wall-stop detection are movement state, not kick state. Conflict: does `BallKick` gain movement authority, or does `PlayerMotor` expose a dash operation that `BallKick` requests. Decide before implementation; wrong choice splits movement ownership across two components.
- P5 reset ownership. Match concern owns coordinated reset per `AGENTS.md`; pickups own their own respawn timers. Conflict: goal reset must restore pickups without match concern reaching into pickup internals. Settle whether match concern raises a reset signal pickups execute themselves, consistent with "owners execute their own reset".

## Validation

- Follow `AGENTS.md` `Unity execution` + `Validation` verbatim. Harness pre-gate before every Unity-mutating workflow.
- P0 validation is Blender-side: mesh audit invariants + seven preview views under `Temp/BlenderPreviews/`, never committed. No Unity lease, no builder protocol, no bake. New FBX imports on next Unity open; that import belongs to phase 2, not P0 acceptance.
- Builder-generated change -> builder protocol: production bake current -> one authoritative build -> semantic validate in separate Unity process.
- Pure logic added (state machine, scoring, tiebreak, cooldown math) -> EditMode tests.
- Generated churn -> separate commit `chore: regenerate MovementLab outputs`.
- Report only checks run.

## Done condition

Playable five-minute match in Movement Lab scene:

- six participants present, Blue/Red identity readable;
- human rockets an inert opponent -> damage -> death -> spectator view -> five-second wait -> respawn with visible two-second immunity;
- friendly rocket deals zero damage and zero knockback; own rocket still propels;
- goals score to correct team, own goal attributes correctly;
- clock runs five minutes, pauses on goal summary and countdown, reaches zero and stops play;
- `Tab` shows goals/frags/deaths table; final summary states winner or draw plus deciding rule;
- `F` dashes with extended leg, three-second cooldown, one air use, stops on wall, damages and shoves enemies, passes through allies harmlessly;
- damaged player collects health pickup for roughly one-third restore; pickup disappears, returns after `15s`; full-health player cannot consume.

Plus P0, independently acceptable and not part of playable match:

- `Tools/Blender/generate_fps_shotgun.py` runs headless from repository root and exports both meshes;
- mesh audit passes; preview views rendered; user accepted silhouette.

## Playtest tuning targets

Expose as serialized values. Expect change after playtest; do not hard-code.

- rocket damage, enemy-rocket knockback fraction, self-knockback preservation;
- death wait, immunity duration, respawn spawn-safety weights;
- goal summary duration, countdown duration, match duration;
- dash burst force, dash duration, steering authority, speed cap, cooldown, enemy contact damage and shove force, camera impulse;
- health restore fraction, pickup respawn delay.

Known gaps at this run's playtest gate:

- enemy-rocket knockback fraction cannot be felt. Inert avatars do not fire. Stays unvalidated until bots run.
- P4 changes ball-control feel in same run that introduces match loop. Two variables move together. Tune dash separately from match pacing; do not read one through the other. Preserved-vs-replaced `BallKick` values must be recorded so a regression is attributable.

## Planning completion criteria

- Every in-scope rule maps to exactly one candidate owner.
- No candidate covers out-of-scope list.
- P0 writable set proven disjoint from P1 before parallel dispatch. Overlap -> serialize, never concurrent writers. Same proof required for wave-2 and wave-3 lane pairs.
- P4 plan states every existing `BallKick` tuning value as preserved or replaced, with reason. Silent retune -> not ready.
- Codebase conflicts resolved explicitly in plan `Decisions`, not deferred to workers.
- Composition changes routed through builder pipelines, never hand-edited generated assets.
- Tuning targets serialized and named in plan.
