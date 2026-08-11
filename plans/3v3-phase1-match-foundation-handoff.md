# 3v3 Phase 1 Match Foundation Handoff

## Authority

- Status: accepted design handoff. Run-scoped subset of [`3v3-bots-combat-design-handoff.md`](3v3-bots-combat-design-handoff.md).
- Parent doc stays authoritative for every rule. This doc selects phase-1 subset + adds repository grounding.
- Conflict between docs -> parent wins on gameplay rule; this doc wins on run scope.
- Purpose: sole cited source for one loop-orchestrator run.
- Preserve locked decisions. Tuning targets = playtest starting points, not requirements.

## Run instruction for LP

- Route: expect `hybrid`, 4 candidates, 3 waves. Wave 1 parallel, waves 2-3 sequential. Do not force `single_plan`.
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
- P2 `match-rules-and-flow` -> wave 2 -> baseline = accepted P1 integration SHA
  - produces: clock/score/frag/flow-state contract consumed by P3
- P3 `match-hud` -> wave 3 -> baseline = accepted P2 integration SHA

Design obligations breakdown must name for planners:

- P0 -> mesh count and reuse policy; dimensions, origin, forward axis, triangle budget, material slots matched to existing `generate_fps_rocket_launcher.py` output; pump-action silhouette readable at first-person scale and at arena pickup distance.
- P1 -> where participant state lives; how `MatchController` addresses N participants instead of one; whether team identity is enum, component, or roster index; spawn-selection algorithm inputs; immunity collision mechanism against `CharacterController`.
- P2 -> whether `MatchRules` pure static module extends to carry clock/frag/tiebreak state or is displaced by new owner; live-clock pause semantics across `GoalFreeze`; own-goal attribution source.
- P3 -> HUD ownership and update frequency; `Tab` hold input route through `PlayerInputReader`; death overlay visibility scoping to local player only.

Do not split P1 further on ownership grounds alone. Roster, vitals, death, immunity all write same prefab + same generated scene -> serialized writers by rule -> split yields zero parallel gain and extra Unity proofs. Split only if planner returns decomposition mismatch.

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
- Pickups: shotgun spawn, ammo spawns, health spawns, `15s` respawn timers.
- Dash-kick rework: `F` input, cooldown, air rule, enemy contact damage/shove, extended-leg presentation. Existing `BallKick` stays as-is.
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
- `Tab` shows goals/frags/deaths table; final summary states winner or draw plus deciding rule.

Plus P0, independently acceptable and not part of playable match:

- `Tools/Blender/generate_fps_shotgun.py` runs headless from repository root and exports both meshes;
- mesh audit passes; preview views rendered; user accepted silhouette.

## Playtest tuning targets

Expose as serialized values. Expect change after playtest; do not hard-code.

- rocket damage, enemy-rocket knockback fraction, self-knockback preservation;
- death wait, immunity duration, respawn spawn-safety weights;
- goal summary duration, countdown duration, match duration.

Known gap: enemy-rocket knockback fraction cannot be felt this run. Inert avatars do not fire. Value stays unvalidated until bots run.

## Planning completion criteria

- Every in-scope rule maps to exactly one candidate owner.
- No candidate covers out-of-scope list.
- P0 writable set proven disjoint from P1 before parallel dispatch. Overlap -> serialize, never concurrent writers.
- Codebase conflicts resolved explicitly in plan `Decisions`, not deferred to workers.
- Composition changes routed through builder pipelines, never hand-edited generated assets.
- Tuning targets serialized and named in plan.
