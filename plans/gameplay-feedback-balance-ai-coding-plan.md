# Gameplay Feedback Balance And AI Coding Plan

Status: accepted
Source: direct user request
Run ID: direct
Plan ID: direct
Attempt ID: direct
Covered Requirements: direct request
Baseline: 85cd7351789f4abdc572737a5d18eca706787ff8
Dependencies: None

## Objective

Deliver requested combat readability, participant scale/presentation, weapon balance, goal flow, death lifecycle, and bot behavior changes in generated Movement Lab. Preserve current movement, scoring, input, ownership, and builder contracts except exact changes below. Finish with reviewed source, authoritative regenerated outputs, persisted-reference validation, and clean exact-SHA evidence.

## Scope

- in: hit direction feedback; 2x participant collision/visuals; shotgun +70% damage/ball force; 2x enemy rocket impulse; 2x blast radius and matching VFX; team rocket orb/explosion tint; 30-second enemy corpses; enemy nicknames; click-to-dismiss goal summary; 2x shotgun icon; local 8-second respawn; corner recovery/combat bot strategy; more frequent combat; existing bot kick/rocket-jump use; aerial ball miss tuning; downward air-kick regression proof; builder, validator, generated outputs, tests
- out: new imported art; input-action asset changes; movement retuning beyond scale compatibility; ball impulse changes for rockets; friendly-fire rule changes; bot respawn change from 5 seconds; arena geometry change; production abstraction work; PlayMode tests; automated visual capture

## Decisions

- assumption: "twice as big" means physical CharacterController and third-person visuals. Participant root remains scale 1 so movement/viewmodel math stays stable.
- decision: shotgun +70% applies to participant damage and ball impulse: 13.6 damage per pellet, 3.4 ball impulse per pellet, 20.4 cap. Eight close pellets total 108.8 damage, so a full-health target can die in one close shot.
- decision: rocket enemy launch doubles by changing enemy impulse multiplier 0.5 -> 1.0. Self multiplier 1.0, friendly multiplier 0, ball strength 16, damage 50, underfoot tuning, and occlusion stay unchanged.
- decision: blast radius changes 5.85 -> 11.7 with existing surface-distance falloff. Existing targets inside 11.7 receive more force/damage than before, not only newly reached targets.
- decision: explosion prefab root scale becomes 1. `ExplosionVfx.ReferenceVisualRadius = 4.5f`; runtime scale is `max(radius, 0.01) / 4.5`, so radius 11.7 renders at scale 2.6 without compounded legacy scale 1.3.
- decision: firing team is immutable nullable Blue/Red snapshot captured at launch. Owner identity classifies self; valid snapshot classifies other participants; missing/invalid snapshot is unattributed force-only with zero participant damage.
- decision: hit source is dash attacker position, shotgun action origin, or rocket explosion origin. Damage event amount is actual health removed after clamping.
- decision: existing airborne downward kick already propels along full 3D look direction. Preserve current 12 impulse, 30 cap, 0.33 duration, 180 steering, 3-second cooldown, and one-air-use rules; add regression proof only.
- decision: bot dash kicks and High-difficulty rocket jumps already exist. Preserve their gates/cooldowns; correct rocket-jump body facing and make policies exercise them more often.
- decision: local participant respawn is 8 seconds. Nonlocal bots retain 5 seconds through scene-instance override.
- decision: goal summary has no timeout. Fresh keyboard, mouse-button, or gamepad `ButtonControl` press after GoalFreeze entry dismisses it. Held controls, axes, motion, releases, and pre-entry presses do not dismiss.
- decision: celebration orbit lasts 3 seconds, then camera holds final detached pose until dismissal/reset.
- decision: no shotgun icon exists at baseline. Add procedural 128x64 silhouette, defined as 2x nominal 64x32, without imported texture.
- decision: enemy means participant team differs from local participant team. Only enemies show nicknames and leave corpses.
- decision: corpse lifetime advances with unscaled time only while match is not paused. Corpses survive goal resets and victim respawns, coexist across deaths, and clear only when presentation disables or scene unloads.
- decision: corner arena bounds are center `(0,0,0)`, X half-length 65, Z half-width 45. Goals/end axis is X; side axis is Z.
- decision: current baseline manifest marks Lighting and BakedOutput stale; `MovementLabContractCatalog.cs` is also an explicit Lighting input changed by this work. ProductionPrepare must report production bake `executed`, bake count 1, and no skip marker.
- decision: visual/feel acceptance still needs human playtest after semantic gates; deterministic tests cover rules, not subjective feel.
- question: None

## Execution Graph

`START -> {T1 -> CP1 || T6 -> CP6 -> T8 -> CP8 || T7 -> CP7 || T9 -> CP9}`

`CP1 -> {T2 -> CP2 || T3 -> CP3 || T4 -> CP4 -> T5 -> CP5}`

`CP1+CP2+CP3+CP4+CP6+CP8+CP9 -> T10 -> CP10`

`CP1+CP2+CP3+CP4+CP5+CP6+CP7+CP8+CP9+CP10 -> JOIN1 -> T11 -> CP11 -> TEST_GATE -> SOURCE_FREEZE -> T12 -> CP12 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in; `+` requires every named predecessor
- gates: `START` -> execution `start_sha` equals Baseline and worktree is scoped-clean; `JOIN1` -> every source worker checkpoint accepted after fixes; `TEST_GATE` -> targeted EditMode and compile-only evidence accepted; `SOURCE_FREEZE` -> source/docs committed, clean, and recorded as `sourceFreezeSha`; `FINAL` -> generated review, full EditMode, ProductionValidate, comparator, and exact-SHA gates pass
- dependency rationale: T2/T3 consume T1 damage request; T5 consumes T1 damage event and T4 GoalFreeze/input API; T8 consumes T6 arena bounds; T10 consumes accepted participant, weapon, match, navigation, corner, and combat contracts; T11 is sole source composition fan-in; T12 is sole Unity/generated-output writer

## Tasks

### T1: Participant damage and local lifecycle contract

- objective: establish one participant-owned damage/lifecycle API. Coupled ParticipantState input and respawn edits stay together because no disjoint writer seam exists.
- covered_requirements: hit indication source; local 8-second death cooldown; downward air-kick proof
- owner: worker-T1 (luna_max)
- dependencies: None
- parallel_contract: produces `ParticipantDamageRequest.SourceWorldPosition`, `ParticipantDamageEvent`, canonical damage overload, local reader-enable invariant, and 8-second local default; T2/T3/T5 consume only after CP1
- owns: `Assets/_Game/Scripts/Runtime/Participants/ParticipantContracts.cs`, `Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs`, `Assets/_Game/Scripts/Runtime/Ball/BallKick.cs`, `Assets/_Game/Scripts/Tests/EditMode/DashKickRulesTests.cs`
- protected: `Assets/_Game/Scripts/Runtime/Input/PlayerInputReader.cs`, `Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs`, `Assets/_Game/Scripts/Runtime/Weapons/ExplosionResolver.cs`, `Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs`
- read_paths: `Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs::StartDash` -> preserve full 3D air impulse; `Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs::ApplyLeafSimulation` -> local reader lifecycle; `Assets/_Game/Scripts/Runtime/Ball/BallKick.cs::ExecuteKick` -> dash damage source
- validation_environment: source-only worker; no Unity process; shared checkout read, owned paths write
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_execution: None
- implementation: add `ParticipantDamageEvent(Victim, Attacker, Amount, Cause, Weapon, SourceWorldPosition)` and `ParticipantDamageRequest.SourceWorldPosition`. Make request overload canonical. Retain current public `TryApplyDamage`/`TryTakeDamage` overloads; legacy fallback source is attacker transform position when attacker exists, otherwise victim transform position. Reject damage under existing invalid/dead/immune rules. Capture health before, clamp/mutate health, publish one `Damaged` event with `Amount = healthBefore - healthAfter`, including lethal hits, then invoke existing death path and `Died`. Set local participant respawn default to 8 seconds. Keep `PlayerInputReader.enabled` true for local participant while dead/frozen; call `SetGameplayInputEnabled(active && localParticipant)` for intent gating. Nonlocal reader remains disabled. Pass owner participant position from dash damage. Do not change dash movement constants or execution. Extend tests for normalized downward look producing negative Y impulse, full-vector cap, airborne use exhaustion, grounded reset, and unchanged horizontal/upward cases.
- done when: damage order/source/amount contract is explicit; local death cannot disable dismissal reader; local default is 8; air-kick behavior remains unchanged and covered
- checks:
  - `proof: git diff --check -- Assets/_Game/Scripts/Runtime/Participants/ParticipantContracts.cs Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs Assets/_Game/Scripts/Runtime/Ball/BallKick.cs Assets/_Game/Scripts/Tests/EditMode/DashKickRulesTests.cs -> exit 0 with no whitespace errors`
- review_focus: event duplication/order or legacy overload bypass could hide lethal hits or break callers; evidence target is every damage path reaching canonical mutation exactly once and Died following Damaged
- review_checkpoint: CP1

### T2: Shotgun power increase

- objective: raise shotgun participant damage and ball force by exactly 70% without changing spread, range bands, cadence, ammo, or target aggregation.
- covered_requirements: shotgun 70% stronger
- owner: worker-T2 (luna_max)
- dependencies: `CP1 -> ShotgunWeapon must pass canonical damage source contract`
- parallel_contract: consumes T1 canonical request; publishes unchanged `RequestFire` and `HitConfirmed` APIs for T10
- owns: `Assets/_Game/Scripts/Runtime/Weapons/ShotgunDamageRules.cs`, `Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs`, `Assets/_Game/Scripts/Tests/EditMode/ShotgunDamageRulesTests.cs`
- protected: `Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs`, `Assets/_Game/Scripts/Runtime/Weapons/ParticipantRelationship.cs`, `Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs`
- read_paths: `Assets/_Game/Scripts/Runtime/Weapons/ShotgunSpreadPattern.cs` -> unchanged pellet directions; `Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs::RequestFire` -> stable bot/player API
- validation_environment: source-only worker; no Unity process
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_execution: None
- implementation: set default pellet damage 8 -> 13.6, per-pellet ball impulse 2 -> 3.4, ball impulse cap 12 -> 20.4. Preserve pellet count 8, range thresholds/multipliers, falloff, pump timing, spread, ammo, relationship policy, and per-target aggregation. Supply shot `actionOrigin` in canonical damage request for every damaged participant. Update tests for 108.8 full close damage, scaled medium/far results, 3.4 single-pellet force, 20.4 cap, invalid inputs, and unchanged pellet clamp.
- done when: all established shotgun output strength values are exactly 1.7x and unrelated weapon behavior is byte/semantic unchanged
- checks:
  - `proof: git diff --check -- Assets/_Game/Scripts/Runtime/Weapons/ShotgunDamageRules.cs Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs Assets/_Game/Scripts/Tests/EditMode/ShotgunDamageRulesTests.cs -> exit 0 with no whitespace errors`
- review_focus: partial 70% application or accidental range/cadence drift changes combat beyond request; evidence target is constants, aggregation, and focused tests
- review_checkpoint: CP2

### T3: Rocket force, radius, team snapshot, and matching VFX

- objective: double requested rocket enemy force/radius and make projectile/explosion visuals encode immutable firing team at actual gameplay radius.
- covered_requirements: enemy rocket launch +100%; blast radius +100%; matching explosion size; red/blue rocket orb shade
- owner: worker-T3 (luna_max)
- dependencies: `CP1 -> ExplosionResolver must publish canonical explosion-origin damage source`
- parallel_contract: preserves `RocketLauncher.RequestFire`; publishes nullable `RocketProjectile.FiringTeam`, `ExplosionVfx.Play(radius, team)`, and `ExplosionVfxSpawner.Play(origin, radius, team)` for builder/T10
- owns: `Assets/_Game/Scripts/Runtime/Weapons/ParticipantRelationship.cs`, `Assets/_Game/Scripts/Runtime/Weapons/ExplosionResolver.cs`, `Assets/_Game/Scripts/Runtime/Weapons/RocketProjectile.cs`, `Assets/_Game/Scripts/Runtime/Feedback/RocketTrailVfx.cs`, `Assets/_Game/Scripts/Runtime/Feedback/ExplosionVfx.cs`, `Assets/_Game/Scripts/Runtime/Feedback/ExplosionVfxSpawner.cs`, `Assets/_Game/Scripts/Tests/EditMode/ParticipantRelationshipTests.cs`, `Assets/_Game/Scripts/Tests/EditMode/BlastMathTests.cs`, `Assets/_Game/Scripts/Tests/EditMode/BlastMathTests.cs.meta`
- protected: `Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs`, `Assets/_Game/Scripts/Runtime/Weapons/RocketLauncher.cs`, `Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs`, `Assets/_Game/Materials`
- read_paths: `Assets/_Game/Scripts/Runtime/Weapons/BlastMath.cs` -> preserve falloff/impulse formulas; `Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs::BuildRocketPrefab` -> planned serialized fields; `Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs::BuildExplosionVfxPrefab` -> 4.5-unit authored reach
- validation_environment: source-only worker; create new test `.meta` manually before review; no Unity process
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_execution: None
- implementation: add rocket relationship adapter accepting target, owner identity, and nullable team snapshot. Validate only Blue/Red as attributed. Classify owner target as Self first; valid snapshot yields Friendly/Enemy; null/invalid yields Unattributed. Preserve force policy: Unattributed/Self/Enemy receive force, Friendly/Immune do not; only Enemy receives damage. Capture nullable team in `RocketProjectile.Initialize` and never reread owner team. Pass snapshot to trail, resolver, and explosion spawner. Set resolver radius 11.7 and enemy impulse multiplier 1.0; preserve other serialized strengths. Pass explosion origin in participant damage request. Add `RocketTrailVfx` serialized ProjectileGlow reference and neutral glow/material fallback; set particle start color and existing shared team material without material instances; null uses warm neutral and no Blue default. Change spawner/effect API to radius/team. Author `ExplosionVfx.ReferenceVisualRadius = 4.5f`; prefab root stays unit; pure scale helper returns `max(radius,0.01)/reference`; Play sets uniform scale and tints Flash via particle vertex color: Blue/Red shades, warm neutral fallback. Cleanup delay is maximum `startDelay.constantMax + duration + startLifetime.constantMax` across configured non-looping systems, minimum 0.01, no 1.25 cap. Tests cover snapshot immutability/classification, unattributed force-only, 11.7 falloff boundary, enemy multiplier, and exact 11.7/4.5 = 2.6 scale.
- done when: gameplay and visual radii share one radius value; all team colors use immutable snapshot; neutral rockets never appear Blue; explosion survives every configured burst
- checks:
  - `proof: git diff --check -- Assets/_Game/Scripts/Runtime/Weapons/ParticipantRelationship.cs Assets/_Game/Scripts/Runtime/Weapons/ExplosionResolver.cs Assets/_Game/Scripts/Runtime/Weapons/RocketProjectile.cs Assets/_Game/Scripts/Runtime/Feedback/RocketTrailVfx.cs Assets/_Game/Scripts/Runtime/Feedback/ExplosionVfx.cs Assets/_Game/Scripts/Runtime/Feedback/ExplosionVfxSpawner.cs Assets/_Game/Scripts/Tests/EditMode/ParticipantRelationshipTests.cs Assets/_Game/Scripts/Tests/EditMode/BlastMathTests.cs Assets/_Game/Scripts/Tests/EditMode/BlastMathTests.cs.meta -> exit 0 and every new source has paired meta`
- review_focus: live-team reread, double visual scaling, or premature cleanup makes force/team/radius unreadable; evidence target is immutable data flow and pure scale/lifetime tests
- review_checkpoint: CP3

### T4: Indefinite goal flow and fresh-button dismissal

- objective: move GoalFreeze exit from timer to match-owned fresh-button request while retaining 3-second camera orbit.
- covered_requirements: goal summary stays until any button
- owner: worker-T4 (luna_max)
- dependencies: `CP1 -> local reader-enable invariant must exist before dismissal subscription is added`
- parallel_contract: publishes `PlayerInputReader.ArmAnyButtonPress`, `ConsumeAnyButtonPress`, `CancelAnyButtonPress`; MatchController remains sole reset/state owner; T5 consumes state only after CP4
- owns: `Assets/_Game/Scripts/Runtime/Match/MatchController.cs`, `Assets/_Game/Scripts/Runtime/Match/MatchRules.cs`, `Assets/_Game/Scripts/Runtime/Input/PlayerInputReader.cs`, `Assets/_Game/Scripts/Tests/EditMode/MatchRulesTests.cs`
- protected: `Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs`, `Assets/_Game/Scripts/Runtime/Feedback/PlayerCameraFeedback.cs`, `Assets/InputSystem_Actions.inputactions`
- read_paths: `Assets/_Game/Scripts/Runtime/Feedback/GoalOrbitModel.cs::Step` -> final pose remains active; `Assets/_Game/Scripts/Runtime/Feedback/PlayerCameraFeedback.cs::BeginGoalCelebration` -> stable 3-second API; `Assets/_Game/Scripts/Runtime/Match/MatchController.cs::EnterGoalFreeze` -> reset owner
- validation_environment: source-only worker; no Unity process
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_execution: None
- implementation: rename serialized field to `goalCelebrationOrbitDuration = 3f`; stack `[FormerlySerializedAs("goalSummaryDuration")]` and `[FormerlySerializedAs("goalFreezeDuration")]`. Enter GoalFreeze with `phaseRemaining = 0`; freeze ball/participants and begin camera celebration with orbit duration. Remove automatic GoalFreeze countdown/transition. Add reader arm/consume/cancel methods backed by one disposable `InputSystem.onAnyButtonPress.CallOnce` subscription. Arm clears old latch/subscription after GoalFreeze entry; callback accepts a new `ButtonControl` press only and latches once. Consume clears latch; cancel disposes/clears. MatchController owns arm on entry, consume in Update, coordinated reset on consume, and cancel on reset/disable/exit. Keep gameplay action maps gated. Update pure rules/tests for indefinite state, zero phase remaining, one accepted dismissal, ignored pre-entry/held/nonbutton state, and existing reset sequence.
- done when: GoalFreeze never exits from time; exactly one new button press requests existing coordinated reset; camera stays final orbit pose until that reset restores it
- checks:
  - `proof: git diff --check -- Assets/_Game/Scripts/Runtime/Match/MatchController.cs Assets/_Game/Scripts/Runtime/Match/MatchRules.cs Assets/_Game/Scripts/Runtime/Input/PlayerInputReader.cs Assets/_Game/Scripts/Tests/EditMode/MatchRulesTests.cs -> exit 0 with no legacy timed transition`
- review_focus: global input subscription leaks or stale/held press bypasses summary; evidence target is subscription disposal and state-transition tests
- review_checkpoint: CP4

### T5: HUD hit direction, goal prompt, and shotgun icon

- objective: render actionable local combat direction and requested HUD size cues from stable participant/match snapshots.
- covered_requirements: hit indication with direction; click prompt; 2x shotgun icon
- owner: worker-T5 (luna_max)
- dependencies: `CP1 -> ParticipantDamageEvent source/ordering`; `CP4 -> indefinite GoalFreeze and dismissal state`
- parallel_contract: HUD reads participant/match/input state only; no match-wide mutation
- owns: `Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs`, `Assets/_Game/Scripts/Runtime/Hud/DamageIndicatorRules.cs`, `Assets/_Game/Scripts/Runtime/Hud/DamageIndicatorRules.cs.meta`, `Assets/_Game/Scripts/Tests/EditMode/DamageIndicatorRulesTests.cs`, `Assets/_Game/Scripts/Tests/EditMode/DamageIndicatorRulesTests.cs.meta`
- protected: `Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs`, `Assets/_Game/Scripts/Runtime/Match/MatchController.cs`, `Assets/_Game/Scripts/Runtime/Input/PlayerInputReader.cs`
- read_paths: `Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs::CaptureFrameSnapshot` -> frame-safe HUD state; `Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs::DrawShotgunWidget` -> baseline text-only widget
- validation_environment: source-only worker; create every new `.meta` manually before review; no Unity process
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_execution: None
- implementation: add pure four-sector resolver. Direction is victim-to-source. Project full direction onto local camera right and forward; suppress when both projections are zero/nonfinite. Compute degrees from `atan2(right, forward)`, normalize `[0,360)`, sector `floor((angle+45)/90)%4`: front/top, right/right, back/bottom, left/left; boundary ties go clockwise. MatchHud subscribes local `Damaged`, stores latest sector for 0.75 unscaled seconds, full opacity first 0.5 then linear fade last 0.25, and draws one high-contrast chevron near corresponding screen edge. Lethal hit remains visible behind death screen only if current screen draw path includes overlay; otherwise cached timer expires normally. Goal summary adds `PRESS ANY BUTTON` and never shows countdown. Replace text-only shotgun block with procedural 128x64 silhouette built from white texture rectangles for stock/body/barrel/trigger, restore GUI color, then draw label/shell count beside it. Hide widget under existing no-weapon/no-shell rule.
- done when: latest accepted hit shows correct cardinal source, timing/fade is deterministic, goal prompt is explicit, and shotgun visual footprint is exactly 128x64 at 1920x1080 reference canvas
- checks:
  - `proof: git diff --check -- Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs Assets/_Game/Scripts/Runtime/Hud/DamageIndicatorRules.cs Assets/_Game/Scripts/Runtime/Hud/DamageIndicatorRules.cs.meta Assets/_Game/Scripts/Tests/EditMode/DamageIndicatorRulesTests.cs Assets/_Game/Scripts/Tests/EditMode/DamageIndicatorRulesTests.cs.meta -> exit 0 and new asset/meta pairs exist`
- review_focus: reversed projection or OnGUI color/matrix leakage makes feedback misleading; evidence target is sector/tie tests and restored GUI state
- review_checkpoint: CP5

### T6: Participant scale, camera clearance, and navigation geometry

- objective: make all participants physically/visually 2x while preserving root/movement scale and aligning every clearance/framing consumer.
- covered_requirements: players and bots twice as big
- owner: worker-T6 (luna_max)
- dependencies: None
- parallel_contract: publishes `BotArenaBounds` and doubled controller/navigation constants; T8 consumes after CP6
- owns: `Assets/_Game/Scripts/Runtime/Feedback/PlayerCameraFeedback.cs`, `Assets/_Game/Scripts/Runtime/Participants/ParticipantSpawnSet.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotNavigationGraph.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotNavigator.cs`, `Assets/_Game/Scripts/Tests/EditMode/BotNavigationRulesTests.cs`
- protected: `Assets/_Game/Scripts/Runtime/Feedback/PlayerPresentation.cs`, `Assets/_Game/Scripts/Runtime/Movement/PlayerMotor.cs`, `Assets/_Game/Editor/MovementLab`
- read_paths: `Assets/_Game/Editor/MovementLab/MovementLabArenaPipeline.cs` -> floor 130x90 and goal X axis; `Assets/_Game/Scripts/Runtime/Bots/BotNavigationRules.cs` -> graph geometry validation
- validation_environment: source-only worker; no Unity process
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_execution: None
- implementation: expose constants/serialized contracts for CharacterController radius 0.8, height 3.6, center Y 1.8, skin 0.08; root scale remains 1. Bot graph expected geometry matches those values. Set ledge/corridor effective radius to `radius-skin = 0.72`, editor-clearance consumer contract 0.72, goal-recess safe radius 1, and rocket-jump ground probe 8. Set spawn LOS eye 2.4 and occupancy radius 2. Add valid `BotArenaBounds` owned by graph: center `(0,0,0)`, `HalfLength=65` on X, `HalfWidth=45` on Z; reject nonfinite/nonpositive values. Update external camera defaults to celebration radius 11, height 5, look height 2.1, spectator offset `(0,5,-10)`. Do not change player motor speed/jump/dash, FPS camera local scale, weapon viewmodels, or input.
- done when: runtime geometry and navigation clearance agree; authoritative bounds match arena axes; camera framing avoids doubled bodies
- checks:
  - `proof: git diff --check -- Assets/_Game/Scripts/Runtime/Feedback/PlayerCameraFeedback.cs Assets/_Game/Scripts/Runtime/Participants/ParticipantSpawnSet.cs Assets/_Game/Scripts/Runtime/Bots/BotNavigationGraph.cs Assets/_Game/Scripts/Runtime/Bots/BotNavigator.cs Assets/_Game/Scripts/Tests/EditMode/BotNavigationRulesTests.cs -> exit 0 and bounds/geometry tests cover invalid and exact values`
- review_focus: root scaling or axis swap breaks movement/corner detection; evidence target is root invariant, exact X/Z bounds, and clearance tests
- review_checkpoint: CP6

### T7: Enemy nicknames and persistent flat corpses

- objective: add enemy-only world presentation with pause-aware 30-second corpse lifecycle and no physics ownership.
- covered_requirements: enemy nicknames; flat body for 30 seconds
- owner: worker-T7 (luna_max)
- dependencies: None
- parallel_contract: publishes serialized PlayerPresentation fields consumed only by T11 builder; no BotController dependency
- owns: `Assets/_Game/Scripts/Runtime/Feedback/PlayerPresentation.cs`
- protected: `Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs`, `Assets/_Game/Scripts/Runtime/Match/MatchController.cs`, `Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs`
- read_paths: `Assets/_Game/Scripts/Runtime/Participants/ParticipantContracts.cs::ParticipantDeathEvent` -> death pose trigger; `Assets/_Game/Scripts/Runtime/Match/MatchController.cs::PauseChanged` -> pause authority
- validation_environment: source-only worker; MonoBehaviour wiring proof deferred to builder validator
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_execution: None
- implementation: add serialized `nicknameVisual`, `nicknameText`, `nicknameCamera`, `localParticipant`, `match`, `showNickname`, `spawnCorpseOnDeath`, and `corpseLifetime=30`. Validate same-object refs in component; cross-scene refs remain builder-wired. Billboard nickname toward serialized local camera in LateUpdate without scene search; text uses immutable `participant.DisplayName`. `ParticipantState.KillInternal` hides WorldVisual before publishing Died, so the cloned WorldVisual starts inactive. On qualifying enemy Died, instantiate only that already team-tinted inactive WorldVisual at death position plus Y 0.05; apply victim yaw then local rotation -90 degrees X so body lies flat; detach; enumerate descendants with `GetComponentsInChildren(..., true)`; set layer 0 recursively; remove/disable colliders and rigidbodies defensively; disable all animators; then call `corpseRoot.SetActive(true)` as the final sanitization step. Never copy nameplate/gameplay scripts. Track multiple active corpse records. Subscribe `match.PauseChanged`; advance each lifetime by `Time.unscaledDeltaTime` only when not paused; destroy at 30 seconds. Do not clear on coordinated reset or respawn. Unsubscribe and destroy owned corpses in OnDisable.
- done when: only enemies show names/corpses, a death-created corpse root is explicitly active after inactive-descendant sanitization, multiple bodies coexist through resets, pause does not consume lifetime, and clones cannot affect physics/gameplay
- checks:
  - `proof: git diff --check -- Assets/_Game/Scripts/Runtime/Feedback/PlayerPresentation.cs -> exit 0 with explicit subscription and cleanup symmetry`
- review_focus: cloning hidden WorldVisual without include-inactive sanitization/final activation makes every corpse invisible; cloning root gameplay object or clearing on reset creates physics/state corruption; evidence target is exact inactive clone -> sanitize including inactive descendants -> activate-last order plus lifecycle symmetry
- review_checkpoint: CP7

### T8: Pure corner-state and assignment rules

- objective: produce deterministic corner recovery/combat assignments from authoritative bounds and observations.
- covered_requirements: stop corner shooting loop; get ball toward net; some bots attack instead
- owner: worker-T8 (luna_max)
- dependencies: `CP6 -> consumes exact BotArenaBounds axis/value contract`
- parallel_contract: publishes `BotCornerState`, `BotCornerAssignmentSet`, and pure advance/assignment APIs; T10 is sole runtime state adapter
- owns: `Assets/_Game/Scripts/Runtime/Bots/BotCornerContracts.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotCornerContracts.cs.meta`, `Assets/_Game/Scripts/Runtime/Bots/BotCornerRules.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotCornerRules.cs.meta`, `Assets/_Game/Scripts/Tests/EditMode/BotCornerRulesTests.cs`, `Assets/_Game/Scripts/Tests/EditMode/BotCornerRulesTests.cs.meta`
- protected: `Assets/_Game/Scripts/Runtime/Bots/BotContracts.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotTeamRoleCoordinator.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotController.cs`
- read_paths: `Assets/_Game/Scripts/Runtime/Bots/BotNavigationGraph.cs::ArenaBounds` -> exact producer; `Assets/_Game/Scripts/Runtime/Bots/BotContracts.cs::BotBallObservation` -> age/visibility data
- validation_environment: source-only worker; create all new `.meta` files manually before review
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_execution: None
- implementation: define `BotCornerIntent { Standard, Recovery, Combat }`, immutable state carrying Active/Dwell/Corner/ReleaseDirection, per-slot assignment carrying intent/navigation/action readiness, and bounded 3-slot assignment set with lowest-slot tie behavior. Pure advance uses freshest valid team ball observation selected by minimum AgeSeconds then observer slot. Compute `endDistance = bounds.HalfLength - abs(ball.x-center.x)`, `sideDistance = bounds.HalfWidth - abs(ball.z-center.z)`. Enter after continuous 0.75 seconds with end <=12, side <=10, and ball Y <=8. Exit immediately when end >18, side >16, ball Y >12, observation expires/invalidates, or explicit reset. Corner is `(center.x + sign(ball.x-center.x)*65, 0, center.z + sign(ball.z-center.z)*45)`. Release is normalized arena-center minus corner. Recovery bot is nearest living nonlocal bot by XZ distance, tie slot. Recovery point is `ball - normalize(enemyGoal-ball)*6`, Y=0, clamped inside both bounds by 2.5; exact-ring distance may shorten at clamp. Action point is ball + release*12, Y=0, clamped by same inset. Ready when recovery bot XZ distance to recovery point <=2.5. Before ready: navigate recovery point, no ball/participant actions. Ready: navigate action point, ball actions only. All other living nonlocal bots are Combat. Combat anchors use base `ball + release*18`, clamped inset: one fighter center; two ascending slots +6/-6 perpendicular; three center/+6/-6. Tests cover dwell hysteresis, X/Z axes, observation/slot ties, reset, clamp, recovery reassignment, readiness, and anchors.
- done when: every corner frame yields one recovery bot at most, explicit fighter assignments, deterministic geometry, and no ambiguous action policy
- checks:
  - `proof: git diff --check -- Assets/_Game/Scripts/Runtime/Bots/BotCornerContracts.cs Assets/_Game/Scripts/Runtime/Bots/BotCornerContracts.cs.meta Assets/_Game/Scripts/Runtime/Bots/BotCornerRules.cs Assets/_Game/Scripts/Runtime/Bots/BotCornerRules.cs.meta Assets/_Game/Scripts/Tests/EditMode/BotCornerRulesTests.cs Assets/_Game/Scripts/Tests/EditMode/BotCornerRulesTests.cs.meta -> exit 0 and all new source/meta pairs exist`
- review_focus: arena-axis error or unstable ties keep ball trapped and desynchronize bots; evidence target is boundary/tie tests with goals on X
- review_checkpoint: CP8

### T9: Pure bot combat, target, aerial miss, and body-facing rules

- objective: increase eligible enemy combat, reduce airborne-ball accuracy, and separate rocket-jump fire aim from body facing without weakening existing safety gates.
- covered_requirements: bots fight more; bots kick/rocket-jump; airborne misses; corner fighters attack
- owner: worker-T9 (luna_max)
- dependencies: None
- parallel_contract: publishes revised `BotCombatInput/Result`, visible-enemy selector, and aerial miss offset; T10 adapts runtime state after CP9
- owns: `Assets/_Game/Scripts/Runtime/Bots/BotContracts.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotCombatRules.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotAimRules.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotDifficultyRules.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotTargetRules.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotCombatEnemyRules.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotCombatEnemyRules.cs.meta`, `Assets/_Game/Scripts/Tests/EditMode/BotCombatRulesTests.cs`, `Assets/_Game/Scripts/Tests/EditMode/BotAimRulesTests.cs`, `Assets/_Game/Scripts/Tests/EditMode/BotDifficultyRulesTests.cs`, `Assets/_Game/Scripts/Tests/EditMode/BotTargetRulesTests.cs`, `Assets/_Game/Scripts/Tests/EditMode/BotCombatEnemyRulesTests.cs`, `Assets/_Game/Scripts/Tests/EditMode/BotCombatEnemyRulesTests.cs.meta`
- protected: `Assets/_Game/Scripts/Runtime/Bots/BotCornerContracts.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotTeamRoleCoordinator.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotController.cs`
- read_paths: `Assets/_Game/Scripts/Runtime/Bots/BotCombatRules.cs::Evaluate` -> football-first arbitration; `Assets/_Game/Scripts/Runtime/Bots/BotAimRules.cs::SolveDirectAim` -> shared offset seam; `Assets/_Game/Scripts/Runtime/Bots/BotDifficultyRules.cs::Sample01` -> deterministic channel sampling
- validation_environment: source-only worker; create new source/test `.meta` files manually before review
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_execution: None
- implementation: remove `ActiveTargetScore` and `EnemyScoreLimit` from enemy eligibility. Evaluate preferred ball action first; if none and participant combat is not suppressed, retain High-only safe rocket-jump check then evaluate visible enemy actions. Preserve dash range 2.2, ball shotgun 20, enemy shotgun 16, launcher/cooldown/LOS/ally-corridor/route gates and action priority. Add visible-enemy selector: HasObservation, IsVisible, IsAlive, finite; minimum XZ distance from action origin, tie slot. Recent nonvisible memory never fires. Add difficulty aerial miss parameters Low `(0.70,8)`, Medium `(0.50,7)`, High `(0.30,6)` and sample channels `AerialMissRoll`, `AerialMissAzimuth`. For airborne ball only, sample once per decision; on hit, build fixed-magnitude world offset perpendicular to canonical actionOrigin-to-observedBall axis using stable fallback basis and sampled azimuth. Add same offset to direct target position and intercept base position; velocity stays unchanged. Ground ball and enemy aim get zero offset. Replace combat result AimDirection with FireAimDirection and BodyFacingDirection. Direct/rocket actions set both to aim direction. RocketJump sets fire direction down/back from existing `GetRocketJumpDirection(routeForwardXZ)` and body direction to finite normalized routeForwardXZ; invalid route preserves no-action safety. Tests cover ball-first fallback to combat, suppression, score removal, selection ties, tier probabilities/magnitudes/sample stability, identical offset, and rocket-jump split directions.
- done when: eligible bots attack visible enemies after no immediate football action, aerial misses match tier values, and rocket-jump body never turns toward down/back fire vector
- checks:
  - `proof: git diff --check -- Assets/_Game/Scripts/Runtime/Bots/BotContracts.cs Assets/_Game/Scripts/Runtime/Bots/BotCombatRules.cs Assets/_Game/Scripts/Runtime/Bots/BotAimRules.cs Assets/_Game/Scripts/Runtime/Bots/BotDifficultyRules.cs Assets/_Game/Scripts/Runtime/Bots/BotTargetRules.cs Assets/_Game/Scripts/Runtime/Bots/BotCombatEnemyRules.cs Assets/_Game/Scripts/Runtime/Bots/BotCombatEnemyRules.cs.meta Assets/_Game/Scripts/Tests/EditMode/BotCombatRulesTests.cs Assets/_Game/Scripts/Tests/EditMode/BotAimRulesTests.cs Assets/_Game/Scripts/Tests/EditMode/BotDifficultyRulesTests.cs Assets/_Game/Scripts/Tests/EditMode/BotTargetRulesTests.cs Assets/_Game/Scripts/Tests/EditMode/BotCombatEnemyRulesTests.cs Assets/_Game/Scripts/Tests/EditMode/BotCombatEnemyRulesTests.cs.meta -> exit 0 and new source/meta pairs exist`
- review_focus: offset recomputation or removed safety suppression makes bots perfect or reckless; evidence target is same-offset identity and preserved LOS/corridor/cooldown tests
- review_checkpoint: CP9

### T10: Bot coordinator and controller integration

- objective: integrate accepted corner/combat contracts into fixed-step bot state, navigation, aiming, and actions.
- covered_requirements: coordinated corner recovery/combat; more fights; bot kicks/rocket jumps; aerial misses
- owner: worker-T10 (luna_max)
- dependencies: `CP1 -> ParticipantState lifecycle`; `CP2 -> accepted ShotgunWeapon contract`; `CP3 -> accepted rocket contract`; `CP4 -> match/reset state`; `CP6 -> navigation/bounds`; `CP8 -> corner assignment API`; `CP9 -> combat/aim result API`
- parallel_contract: consumes source APIs only; presentation CP7 and HUD CP5 remain parallel because BotController references neither
- owns: `Assets/_Game/Scripts/Runtime/Bots/BotTeamRoleCoordinator.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotController.cs`
- protected: `Assets/_Game/Scripts/Runtime/Bots/BotCornerRules.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotCombatRules.cs`, `Assets/_Game/Scripts/Runtime/Bots/BotNavigationGraph.cs`, `Assets/_Game/Scripts/Runtime/Weapons`
- read_paths: `Assets/_Game/Scripts/Runtime/Bots/BotPerception.cs` -> observation API; `Assets/_Game/Scripts/Runtime/Bots/BotNavigator.cs` -> destination API; `Assets/_Game/Scripts/Runtime/Bots/BotController.cs::ExecuteCombatAction` -> shared programmatic actions
- validation_environment: source-only worker; no Unity process
- unity_mutation: false
- expensive_proof_owner: None
- expensive_proof_execution: None
- implementation: coordinator owns one `BotCornerState` and assignment set per team. Every FixedUpdate while Playing, gather freshest valid team ball observation by minimum age/tie observer slot, advance dwell/hysteresis, and rebuild assignments on state/ball/alive changes. Clear on CoordinatedResetRequested and OnDisable; alive-mask change immediately reassigns recovery. Supply graph bounds, team enemy-goal position, participant slots, and bot positions to pure rules. Expose `TryGetCornerAssignment(slotId, out assignment)`. Controller queries assignment before ordinary target navigation. Standard uses current navigation/football suppression. Recovery-not-ready navigates recovery point and constructs combat input with both ball preference and participant combat false so no action. Recovery-ready navigates action point, sets ball preference true and participant suppression true. Corner Combat navigates anchor, disables ball actions, enables participant combat, and uses nearest currently visible enemy from perception; recent enemy may remain navigation target only. Outside corners, use new ball-first-then-enemy fallback. Compute aerial miss offset once per decision ordinal and feed same value into direct/intercept solve. Store FireAimDirection for kick/shotgun/launcher execution and BodyFacingDirection for root yaw. RocketJump fires down/back while yaw follows route; retain High/ground/upward-transition/launcher/line/corridor/cooldown gates. Existing kick/shotgun/rocket programmatic APIs and animations remain shared with player.
- done when: one bot executes intentional corner release, teammates fight, ordinary bots fight after unavailable ball action, and rocket-jump launch direction no longer rotates body backward
- checks:
  - `proof: git diff --check -- Assets/_Game/Scripts/Runtime/Bots/BotTeamRoleCoordinator.cs Assets/_Game/Scripts/Runtime/Bots/BotController.cs -> exit 0 with one coordinator state owner and no scene discovery`
- review_focus: recomputed candidate/offset or mode leakage makes all bots shoot ball/attack simultaneously; evidence target is assignment-to-controller value identity and explicit mode flags
- review_checkpoint: CP10

### T11: Builder, validator, stage graph, and handoff integration

- objective: make generated prefabs/scene authoritative for all accepted runtime contracts and provide persisted semantic proof.
- covered_requirements: every request requiring prefab/scene wiring and generated validation
- owner: worker-T11 (luna_max)
- dependencies: `JOIN1 -> every serialized field, constant, and API must be accepted before composition source changes`
- parallel_contract: sole editor composition/source fan-in; no generated file writes; T12 is sole output writer
- owns: `Assets/_Game/Editor/MovementLab/MovementLabContract.cs`, `Assets/_Game/Editor/MovementLab/MovementLabContractCatalog.cs`, `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs`, `Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs`, `Assets/_Game/Editor/MovementLab/MovementLabSceneComposer.cs`, `Assets/_Game/Editor/MovementLab/MovementLabBotPipeline.cs`, `Assets/_Game/Editor/MovementLab/MovementLabValidator.cs`, `plans/3v3-bots-combat-design-handoff.md`
- protected: `Assets/_Game/Scenes/MovementLab.unity`, `Assets/_Game/Prefabs`, `Assets/_Game/Materials`, `Assets/_Game/Generated`, `Assets/_Game/Lighting`, `ProjectSettings`
- read_paths: `Assets/_Game/Editor/MovementLabBuilder.cs` -> staged entry points; `Assets/_Game/Editor/MovementLab/MovementLabContract.cs::MaterialPrefabOutputs/GameplaySceneOutputs/BakedOutputPaths` -> output authority; `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs::Definitions` -> staleness keys
- validation_environment: source-only worker; execution-orchestrator owns shared targeted tests and compile after CP11
- unity_mutation: false
- expensive_proof_owner: execution-orchestrator
- expensive_proof_execution: orchestrator_phase
- implementation: increment `SerializedContractVersion` 6 -> 7; keep manifest schema 8. Remove legacy `BlastVisualScale=1.30`; author ExplosionVfx prefab root scale 1, serialized reference radius 4.5, ProjectileGlow reference/neutral material, team VFX refs, and validate persisted prefab references. Configure resolver 11.7/1.0 and shotgun 13.6/3.4/20.4. Build player root at scale 1 with controller 0.8/3.6/center1.8/skin0.08, WorldVisual scale2, Head Y3.1, doubled shield/team cues, collider-free Nameplate Y4.1, TextMesh reference, and doubled camera values. Prefab keeps cross-scene presentation refs null. Scene wires match/local participant/local camera to every presentation; sets showNickname/spawnCorpse only when team differs from local; wires match to corpse pause; immutable DisplayName to nameplate; local respawn8 and every nonlocal bot respawn5. Wire match local input and renamed orbit duration3. Configure spawn eye2.4/occupancy2, bot graph geometry/bounds, ledge0.72, goal safe1, jump probe8, corner thresholds/goal transforms, and combat tiers. Validator reopens scene and checks exact serialized values, root/child scale, nickname/corpse enemy policy, cross-object refs, bot assignments/config, match dismissal refs, rocket nullable visual refs, explosion unit scale/reference reach, generated provenance, nonzero YAML fileIDs, and existing unrelated contracts. StageGraph adds RocketProjectile, ExplosionVfx, and PlayerCameraFeedback source+meta to MaterialPrefab inputs; adds MatchHud, ExplosionVfxSpawner, PlayerCameraFeedback, PlayerPresentation, ParticipantSpawnSet, BotController, and BotTeamRoleCoordinator source+meta to GameplayScene inputs, deduplicating existing keys. Update bot handoff with corner intents, X-axis goals, combat fallback, aerial miss, and existing action gates.
- done when: editor sources contain every composition value/reference; validator owns exact persisted assertions; no generated asset changed before T12
- checks:
  - `proof: git diff --check -- Assets/_Game/Editor/MovementLab/MovementLabContract.cs Assets/_Game/Editor/MovementLab/MovementLabContractCatalog.cs Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs Assets/_Game/Editor/MovementLab/MovementLabPrefabPipeline.cs Assets/_Game/Editor/MovementLab/MovementLabSceneComposer.cs Assets/_Game/Editor/MovementLab/MovementLabBotPipeline.cs Assets/_Game/Editor/MovementLab/MovementLabValidator.cs plans/3v3-bots-combat-design-handoff.md -> exit 0 with no generated-path diff`
  - `proof: powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1 -> exit 0 in under 90 seconds with no Unity process/project lock immediately before targeted tests`
  - `proof: powershell -NoProfile -Command "$p = Start-Process -FilePath 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe' -ArgumentList @('-batchmode','-nographics','-projectPath','C:\wt\<id>','-runTests','-testPlatform','EditMode','-testFilter','RocketFooxball.Tests.EditMode.DashKickRulesTests;RocketFooxball.Tests.EditMode.ShotgunDamageRulesTests;RocketFooxball.Tests.EditMode.ParticipantRelationshipTests;RocketFooxball.Tests.EditMode.BlastMathTests;RocketFooxball.Tests.EditMode.MatchRulesTests;RocketFooxball.Tests.EditMode.DamageIndicatorRulesTests;RocketFooxball.Tests.EditMode.BotNavigationRulesTests;RocketFooxball.Tests.EditMode.BotCornerRulesTests;RocketFooxball.Tests.EditMode.BotCombatRulesTests;RocketFooxball.Tests.EditMode.BotAimRulesTests;RocketFooxball.Tests.EditMode.BotDifficultyRulesTests;RocketFooxball.Tests.EditMode.BotTargetRulesTests;RocketFooxball.Tests.EditMode.BotCombatEnemyRulesTests','-testResults','C:\wt\<id>e\targeted.xml','-logFile','C:\wt\<id>e\targeted.log') -Wait -PassThru -WindowStyle Hidden; exit $p.ExitCode" -> exit 0; XML has zero failures for every named affected class`
  - `proof: powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1 -> exit 0 in under 90 seconds with no Unity process/project lock immediately before compile-only`
  - `proof: powershell -NoProfile -Command "$p = Start-Process -FilePath 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe' -ArgumentList @('-batchmode','-nographics','-quit','-projectPath','C:\wt\<id>','-logFile','C:\wt\<id>e\compile.log') -Wait -PassThru -WindowStyle Hidden; exit $p.ExitCode" -> exit 0 with zero Unity Console compile errors`
- review_focus: missing builder field or stage key makes source pass but regeneration lose behavior; evidence target is exact prefab/scene writer plus validator assertion plus stage input
- review_checkpoint: CP11

### T12: Authoritative generation and production prepare

- objective: run sole Unity mutation after source freeze, classify every output, and commit only accepted generated changes.
- covered_requirements: generated Movement Lab delivery and production bake evidence
- owner: worker-T12 (luna_max)
- dependencies: `CP11 -> builder source accepted`; `TEST_GATE -> targeted tests and compile accepted`; `SOURCE_FREEZE -> clean committed sourceFreezeSha exists`
- parallel_contract: sole Unity/project/generated writer; no sibling dispatch
- owns:
  - material/prefab outputs: `Assets/_Game/Animations/FpsKick.controller`, `Assets/_Game/Animations/FpsKick.controller.meta`, `Assets/_Game/Animations/WorldCharacter.controller`, `Assets/_Game/Animations/WorldCharacter.controller.meta`, `Assets/_Game/Generated/BlueCircleCueMesh.asset`, `Assets/_Game/Generated/BlueCircleCueMesh.asset.meta`, `Assets/_Game/Generated/RedTriangleCueMesh.asset`, `Assets/_Game/Generated/RedTriangleCueMesh.asset.meta`, `Assets/_Game/Materials/AmmoShell.mat`, `Assets/_Game/Materials/AmmoShell.mat.meta`, `Assets/_Game/Materials/ArenaGlow.mat`, `Assets/_Game/Materials/ArenaGlow.mat.meta`, `Assets/_Game/Materials/ArenaHazard.mat`, `Assets/_Game/Materials/ArenaHazard.mat.meta`, `Assets/_Game/Materials/ArenaPrimary.mat`, `Assets/_Game/Materials/ArenaPrimary.mat.meta`, `Assets/_Game/Materials/ArenaTrim.mat`, `Assets/_Game/Materials/ArenaTrim.mat.meta`, `Assets/_Game/Materials/Ball.mat`, `Assets/_Game/Materials/Ball.mat.meta`, `Assets/_Game/Materials/BallSurface.physicMaterial`, `Assets/_Game/Materials/BallSurface.physicMaterial.meta`, `Assets/_Game/Materials/CharacterBlack.mat`, `Assets/_Game/Materials/CharacterBlack.mat.meta`, `Assets/_Game/Materials/CharacterCream.mat`, `Assets/_Game/Materials/CharacterCream.mat.meta`, `Assets/_Game/Materials/CharacterEye.mat`, `Assets/_Game/Materials/CharacterEye.mat.meta`, `Assets/_Game/Materials/CharacterRed.mat`, `Assets/_Game/Materials/CharacterRed.mat.meta`, `Assets/_Game/Materials/ContainmentGridCeiling.mat`, `Assets/_Game/Materials/ContainmentGridCeiling.mat.meta`, `Assets/_Game/Materials/ContainmentGridEndWall.mat`, `Assets/_Game/Materials/ContainmentGridEndWall.mat.meta`, `Assets/_Game/Materials/ContainmentGridLongWall.mat`, `Assets/_Game/Materials/ContainmentGridLongWall.mat.meta`, `Assets/_Game/Materials/Explosion.mat`, `Assets/_Game/Materials/Explosion.mat.meta`, `Assets/_Game/Materials/ExplosionAdditive.mat`, `Assets/_Game/Materials/ExplosionAdditive.mat.meta`, `Assets/_Game/Materials/ExplosionSparks.mat`, `Assets/_Game/Materials/ExplosionSparks.mat.meta`, `Assets/_Game/Materials/Floor.mat`, `Assets/_Game/Materials/Floor.mat.meta`, `Assets/_Game/Materials/GoalFrame.mat`, `Assets/_Game/Materials/GoalFrame.mat.meta`, `Assets/_Game/Materials/Hazard.mat`, `Assets/_Game/Materials/Hazard.mat.meta`, `Assets/_Game/Materials/HealthPickup.mat`, `Assets/_Game/Materials/HealthPickup.mat.meta`, `Assets/_Game/Materials/Marking.mat`, `Assets/_Game/Materials/Marking.mat.meta`, `Assets/_Game/Materials/ProjectileGlow.mat`, `Assets/_Game/Materials/ProjectileGlow.mat.meta`, `Assets/_Game/Materials/RetroSunnySky.mat`, `Assets/_Game/Materials/RetroSunnySky.mat.meta`, `Assets/_Game/Materials/Rocket.mat`, `Assets/_Game/Materials/Rocket.mat.meta`, `Assets/_Game/Materials/RocketHot.mat`, `Assets/_Game/Materials/RocketHot.mat.meta`, `Assets/_Game/Materials/Shield.mat`, `Assets/_Game/Materials/Shield.mat.meta`, `Assets/_Game/Materials/ShieldBlue.mat`, `Assets/_Game/Materials/ShieldBlue.mat.meta`, `Assets/_Game/Materials/ShieldRed.mat`, `Assets/_Game/Materials/ShieldRed.mat.meta`, `Assets/_Game/Materials/ShotgunAccent.mat`, `Assets/_Game/Materials/ShotgunAccent.mat.meta`, `Assets/_Game/Materials/ShotgunDark.mat`, `Assets/_Game/Materials/ShotgunDark.mat.meta`, `Assets/_Game/Materials/ShotgunMetal.mat`, `Assets/_Game/Materials/ShotgunMetal.mat.meta`, `Assets/_Game/Materials/Smoke.mat`, `Assets/_Game/Materials/Smoke.mat.meta`, `Assets/_Game/Materials/TeamBlue.mat`, `Assets/_Game/Materials/TeamBlue.mat.meta`, `Assets/_Game/Materials/TeamBlueShield.mat`, `Assets/_Game/Materials/TeamBlueShield.mat.meta`, `Assets/_Game/Materials/TeamBlueTrail.mat`, `Assets/_Game/Materials/TeamBlueTrail.mat.meta`, `Assets/_Game/Materials/TeamRed.mat`, `Assets/_Game/Materials/TeamRed.mat.meta`, `Assets/_Game/Materials/TeamRedShield.mat`, `Assets/_Game/Materials/TeamRedShield.mat.meta`, `Assets/_Game/Materials/TeamRedTrail.mat`, `Assets/_Game/Materials/TeamRedTrail.mat.meta`, `Assets/_Game/Materials/Trim.mat`, `Assets/_Game/Materials/Trim.mat.meta`, `Assets/_Game/Materials/Wall.mat`, `Assets/_Game/Materials/Wall.mat.meta`, `Assets/_Game/Materials/WeaponAccent.mat`, `Assets/_Game/Materials/WeaponAccent.mat.meta`, `Assets/_Game/Materials/WeaponDark.mat`, `Assets/_Game/Materials/WeaponDark.mat.meta`, `Assets/_Game/Materials/WeaponMetal.mat`, `Assets/_Game/Materials/WeaponMetal.mat.meta`, `Assets/_Game/Prefabs/AmmoPickup.prefab`, `Assets/_Game/Prefabs/AmmoPickup.prefab.meta`, `Assets/_Game/Prefabs/Ball.prefab`, `Assets/_Game/Prefabs/Ball.prefab.meta`, `Assets/_Game/Prefabs/ExplosionVfx.prefab`, `Assets/_Game/Prefabs/ExplosionVfx.prefab.meta`, `Assets/_Game/Prefabs/HealthPickup.prefab`, `Assets/_Game/Prefabs/HealthPickup.prefab.meta`, `Assets/_Game/Prefabs/Player.prefab`, `Assets/_Game/Prefabs/Player.prefab.meta`, `Assets/_Game/Prefabs/Rocket.prefab`, `Assets/_Game/Prefabs/Rocket.prefab.meta`, `Assets/_Game/Prefabs/ShotgunPickup.prefab`, `Assets/_Game/Prefabs/ShotgunPickup.prefab.meta`
  - scene/project outputs: `Assets/_Game/Scenes/MovementLab.unity`, `Assets/_Game/Scenes/MovementLab.unity.meta`, `ProjectSettings/DynamicsManager.asset`, `ProjectSettings/EditorBuildSettings.asset`, `ProjectSettings/TagManager.asset`, `ProjectSettings/TimeManager.asset`
  - lighting profile outputs: `Assets/_Game/Lighting/MovementLabLightingSettings.asset`, `Assets/_Game/Lighting/MovementLabLightingSettings.asset.meta`, `Assets/_Game/Lighting/MovementLabVolumeProfile.asset`, `Assets/_Game/Lighting/MovementLabVolumeProfile.asset.meta`
  - baked outputs: `Assets/_Game/Lighting/MovementLabLightingManifest.json`, `Assets/_Game/Lighting/MovementLabLightingManifest.json.meta`, `Assets/_Game/Scenes/MovementLab/LightingData.asset`, `Assets/_Game/Scenes/MovementLab/LightingData.asset.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-0_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-1_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-2_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-3_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_dir.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_dir.png.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_light.exr`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_light.exr.meta`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_shadowmask.png`, `Assets/_Game/Scenes/MovementLab/Lightmap-4_comp_shadowmask.png.meta`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-0.exr`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-0.exr.meta`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-1.exr`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-1.exr.meta`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-2.exr`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-2.exr.meta`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-3.exr`, `Assets/_Game/Scenes/MovementLab/ReflectionProbe-3.exr.meta`
  - generated manifest outputs: `Assets/_Game/Generated/MovementLabBuildManifest.json`, `Assets/_Game/Generated/MovementLabBuildManifest.json.meta`
- protected: `Assets/_Game/Scripts/Runtime`, `Assets/_Game/Editor`, `plans`
- read_paths: `Tools/Validation/Invoke-MovementLabWorkflow.ps1` -> exact workflow/ledger contract; `Tools/Validation/Compare-GeneratedYaml.ps1` -> comparator; `Assets/_Game/Generated/MovementLabBuildManifest.json` -> authoritative inventory/stale baseline
- validation_environment: short writable `C:\wt\<id>` project, private warm Library, short writable evidence alias `C:\wt\<id>e`, Unity 6000.5.6f1, one Editor process, no interactive Editor/lock
- unity_mutation: true
- expensive_proof_owner: worker-T12 (luna_max)
- expensive_proof_execution: same_dispatch
- implementation: assert evidence path budget/write probe, clean sourceFreezeSha, no Unity process/lock. Run harness immediately before workflow. Run ProductionPrepare once via workflow. Require process/workflow success, current production probe, production-bake executed, bakeCount1, no skip marker, and durable ledger/payload. Carry exact LedgerPath. Run generated comparator Base sourceFreezeSha Head WORKTREE FailOnDangling. Classifier union is current authoritative inventory plus exact owned paths above; reject any untraced/undeclared path, incomplete semantic header, dangling increase, unsupported type, existing GUID churn, or broken asset/meta pair. Existing `.meta` GUIDs remain stable. Comparator-selected nonzero paths become separate `chore: regenerate MovementLab outputs` commit; zero paths means no regeneration commit and sourceFreezeSha remains generated boundary. Before and after commit, recompute SHA-256 for every prepared generated path and require exact equality with immutable ProductionPrepare payload `generated_hashes`/`generated_hash_digest`; comparator report binds those same bytes from sourceFreezeSha to generated commit. Preserve production-bake row `executed_sha == validated_sha == sourceFreezeSha`. Remove only newly generated root IDE files. Return final generated SHA, ledger path/digest, hash-binding evidence, comparator report, inventory, and Git status to CP12.
- done when: one production prepare owns every mutation, all outputs are classified/reviewable, immutable prepare hashes exactly bind committed generated bytes, production bake evidence remains bound to sourceFreezeSha, and worktree is clean at generated commit
- checks:
  - `proof: powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1 -> exit 0 in under 90 seconds with no Unity process/project lock immediately before ProductionPrepare`
  - `check_id=production-bake; tier=production-final; owner=worker-T12; expected_status=executed; command=powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode ProductionPrepare -ProjectPath C:\wt\<id> -EvidenceRoot C:\wt\<id>e -AttemptId <attempt>; mutates_project=true; input_paths=[Assets/_Game/Lighting, Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs, Assets/_Game/Editor/MovementLab/MovementLabLightingProfiles.cs, Assets/_Game/Lighting/MovementLabLightingSettings.asset, Assets/_Game/Lighting/MovementLabLightingSettings.asset.meta, Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset, Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset.meta, Assets/_Game/Lighting/MovementLabVolumeProfile.asset, Assets/_Game/Lighting/MovementLabVolumeProfile.asset.meta, Assets/_Game/Lighting/MovementLabLightingManifest.json, Assets/_Game/Lighting/MovementLabLightingManifest.json.meta]; input_digest=workflow Get-InputDigest over canonical Git blob or normalized bytes; environment_fingerprint=workflow Unity version/path + project path + package lock + OS/toolchain fingerprint; invalidation_paths=[Assets/_Game/Lighting, Assets/_Game/Editor/MovementLab/MovementLabLightingPipeline.cs, Assets/_Game/Editor/MovementLab/MovementLabLightingProfiles.cs, Assets/_Game/Lighting/MovementLabLightingSettings.asset, Assets/_Game/Lighting/MovementLabLightingSettings.asset.meta, Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset, Assets/_Game/Lighting/MovementLabLightingSettings_Development.asset.meta, Assets/_Game/Lighting/MovementLabVolumeProfile.asset, Assets/_Game/Lighting/MovementLabVolumeProfile.asset.meta, Assets/_Game/Lighting/MovementLabLightingManifest.json, Assets/_Game/Lighting/MovementLabLightingManifest.json.meta]; subsumes=[]; run_point=SOURCE_FREEZE; evidence=workflow complete, executed_sha=sourceFreezeSha, status executed, bake_count=1, bake_marker null, current production probe, durable evidence path/digest, exactly one ProductionBake command`
  - `proof: powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Compare-GeneratedYaml.ps1 -Base <sourceFreezeSha> -Head WORKTREE -FailOnDangling -> exit 0 with exact selected headers, SEMANTIC/DANGLING/GUID/PAIRS/UNSUPPORTED evidence, no undeclared output, no GUID churn, no dangling increase`
- review_focus: out-of-inventory mutation, stale GUID, or ledger mismatch corrupts generated authority; evidence target is exact owned inventory, comparator coverage, clean separate commit, and bound ledger
- review_checkpoint: CP12

## Execution Assignments

- workers: T1 -> worker-T1 (luna_max) -> participant damage/lifecycle; T2 -> worker-T2 (luna_max) -> shotgun tuning; T3 -> worker-T3 (luna_max) -> rocket gameplay/VFX; T4 -> worker-T4 (luna_max) -> goal/input flow; T5 -> worker-T5 (luna_max) -> HUD feedback; T6 -> worker-T6 (luna_max) -> size/navigation/bounds; T7 -> worker-T7 (luna_max) -> nicknames/corpses; T8 -> worker-T8 (luna_max) -> pure corner rules; T9 -> worker-T9 (luna_max) -> pure combat/aim; T10 -> worker-T10 (luna_max) -> bot integration; T11 -> worker-T11 (luna_max) -> builder/validator; T12 -> worker-T12 (luna_max) -> sole generation
- review_checkpoints: CP1 -> T1 fresh sol_medium after worker return/fixes; CP2 -> T2 fresh sol_medium after CP1; CP3 -> T3 fresh sol_medium after CP1; CP4 -> T4 fresh sol_medium after CP1; CP5 -> T5 fresh sol_medium after CP4; CP6 -> T6 fresh sol_medium; CP7 -> T7 fresh sol_medium; CP8 -> T8 fresh sol_medium after CP6; CP9 -> T9 fresh sol_medium; CP10 -> T10 fresh sol_medium after declared producer checkpoints; CP11 -> T11 fresh sol_medium after JOIN1; CP12 -> T12 fresh sol_medium generated-output review after ProductionPrepare/comparator/commit
- review/fix contract: follow [`$orchestrate-implementation`](../.agents/skills/orchestrate-implementation/SKILL.md) review checkpoints; every Critical/High fix goes to a new worker; rerun invalidated checkpoint before downstream dispatch

## Final Verification

- exact head: `final_sha` is clean committed generated boundary after CP12. Immutable ProductionPrepare `production-bake.executed_sha == production-bake.validated_sha == sourceFreezeSha`; its generated hash digest equals committed generated bytes at final_sha. ProductionValidate `production-validator.executed_sha == production-validator.validated_sha == final_sha`. Any later source/generated edit invalidates corresponding evidence.
- checks:
  - `proof: powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1 -> exit 0 in under 90 seconds with no Unity process/project lock immediately before full EditMode`
  - `proof: powershell -NoProfile -Command "$p = Start-Process -FilePath 'C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe' -ArgumentList @('-batchmode','-nographics','-projectPath','C:\wt\<id>','-runTests','-testPlatform','EditMode','-testResults','C:\wt\<id>e\full-editmode.xml','-logFile','C:\wt\<id>e\full-editmode.log') -Wait -PassThru -WindowStyle Hidden; exit $p.ExitCode" -> exit 0 and full EditMode XML reports zero failures`
  - `proof: powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1 -> exit 0 in under 90 seconds with no Unity process/project lock immediately before ProductionValidate`
  - `check_id=production-validator; tier=production-final; owner=execution-orchestrator; expected_status=executed; command=powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode ProductionValidate -ProjectPath C:\wt\<id> -EvidenceRoot C:\wt\<id>e -AttemptId <attempt> -LedgerPath <ProductionPrepare-LedgerPath>; mutates_project=false; input_paths=[Assets/_Game/Editor, Assets/_Game/Scripts/Runtime, Assets/_Game/Generated, Tools/Validation, Packages, ProjectSettings]; input_digest=workflow Get-InputDigest over canonical Git blob or normalized bytes at final_sha; environment_fingerprint=workflow Unity version/path + project path + package lock + OS/toolchain fingerprint; invalidation_paths=[Assets/_Game/Editor, Assets/_Game/Scripts/Runtime, Assets/_Game/Generated, Tools/Validation, Packages, ProjectSettings, Assets/_Game/Lighting, Assets/_Game/Scenes]; subsumes=[validator-readonly]; run_point=FINAL; evidence=workflow complete in separate Unity process, executed status, current Production probe, production-validator validated_sha=executed_sha=final_sha, carried immutable production-bake row remains sourceFreezeSha-bound, final generated_hash_digest equals prepare generated_hash_digest, zero semantic validation errors, durable evidence path/digest`
  - `proof: powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Compare-GeneratedYaml.ps1 -Base 85cd7351789f4abdc572737a5d18eca706787ff8 -Head <final_sha> -FailOnDangling -> exit 0 with complete semantic/GUID/pair coverage and no undeclared generated path`
  - `proof: git status --short && git rev-parse HEAD -> empty status and exact final_sha`
- inspect: generated Player/Rocket/Explosion prefabs and MovementLab scene provenance; local/enemy respawn values; all serialized references after reload; optional human 1920x1080 playtest for cardinal hit cues, 2x participants, labels/corpses/pause lifetime, indefinite goal prompt, shotgun icon, team rocket orbs, 11.7-radius explosion, corner releases, combat frequency, bot kick/rocket jump, and aerial misses
- invalidation: any edit under production-validator invalidation paths reruns harness + affected tests + compile when C# + ProductionPrepare when builder/generated inputs changed + comparator + generated review + full EditMode + ProductionValidate at new clean SHA; lighting-input edit also reruns production bake expectation from current probe

## Handoff

- residual risks: close shotgun now one-shots at 108.8 theoretical damage; doubled blast radius changes damage/force at previously affected distances; doubled collision can expose arena crowding/traversal feel; bot probability/geometry values need playtest tuning; production bake may be lengthy
- authority: execution uses [`$orchestrate-implementation`](../.agents/skills/orchestrate-implementation/SKILL.md); execution orchestrator owns integration, exact-SHA validation, and evidence; merging agent/user retains approval for user branch mutation
