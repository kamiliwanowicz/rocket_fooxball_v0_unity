# 3v3 Bots And Combat Design Handoff

## Authority

- Status: accepted design handoff.
- Purpose: input for repository-grounded coding plan.
- Scope: gameplay rules and player-facing behavior. Implementation architecture unresolved.
- Preserve locked decisions below. Treat listed tuning targets as playtest starting points.

## Implementation status legend

Every `##` section below carries `Status:` line. Meaning:

- `DONE phase-1` -> shipped on `shotgun_design_and_match_foundation`, verified in repo. Do not re-plan. Tuning values may still change.
- `PARTIAL` -> some clauses shipped, rest open. `done:` / `todo:` name exact split.
- `TODO` -> nothing shipped.

Phase-1 scope authority -> [`3v3-phase1-match-foundation-handoff.md`](3v3-phase1-match-foundation-handoff.md) `In scope` + `Out of scope`.
Remaining-run planning guidance -> [`3v3-phase2-shotgun-and-bots-planning-helper.md`](3v3-phase2-shotgun-and-bots-planning-helper.md).

Aggregate: phase 1 delivered match foundation + vitals + dash-kick + health pickups + HUD + shotgun mesh. Remaining = shotgun as weapon (integration, economy, HUD) + bots (navigation, perception, decisions, difficulty) + difficulty setup UI.

## Product boundary

Status: PARTIAL
- done: six participant slots exist, human occupies one Blue slot, teams globally fixed Blue/Red.
- todo: five non-human slots remain inert avatars -> zero AI, zero input. Bot brain absent.

- PoC roster: one human + two allied bots vs three enemy bots.
- Human team: Blue. Opponent team: Red.
- Future multiplayer: later milestone. Human players replace bot slots; bots fill vacancies until each team has three active slots.
- Team colors: globally fixed Blue/Red for all clients.
- Networking, right-hand weapon replacement system, combat-assist scoring, production audio: deferred.

## Match rules

Status: DONE phase-1
- clock, pause semantics, winner order, frag tiebreak, negative frags, killing-blow credit, self/arena death penalty, goal credit, own-goal attribution all shipped.
- caveat: frag rules exercised only by human kills so far. Inert avatars never kill -> killer-side paths unproven until bots land.

- Match duration: five minutes.
- Live clock: continues during individual death/respawn; pauses during goal summary and kickoff countdown.
- Clock reaches zero -> play stops immediately.
- Winner order:
  1. Higher goal score.
  2. Equal goals -> higher team frag total.
  3. Equal goals + equal team frags -> draw.
- Team frag total: sum of individual frags; negative values allowed.
- Enemy killing blow -> killer gains one frag. Earlier damage grants no frag or assist.
- Self-caused or arena death -> dead player loses one frag; no enemy gains frag.
- Goal credit -> last attacking player touching ball gains individual goal.
- Own goal -> opposing team gains team goal; responsible defender named in summary; no individual goal awarded.

## Match flow

Status: PARTIAL
- done: start reset + countdown, goal stop + summary + reset + countdown, goal-reset health/position/dead-return/pickup-restore/timer-reset, no post-kickoff immunity, match end screen with `Rematch` + `Exit`.
- todo: goal-reset clause "carried shotgun and shotgun ammo removed" -> needs shotgun ownership state to exist first.

- Match start -> reset positions/state -> three-second `3... 2... 1... GO` countdown.
- Goal -> stop play -> three-second summary -> reset positions/state -> three-second countdown.
- Goal reset:
  - Full health for every player.
  - Dead players return immediately.
  - Carried shotgun and shotgun ammo removed.
  - Every pickup restored immediately; pickup timers reset.
  - No post-kickoff immunity.
- Match end -> winner/draw, deciding rule, final table, `Rematch`, `Exit`.

## Controls and hands

Status: PARTIAL
- done: LMB rocket launcher, `F` dash-kick, automatic pickup collection (health only), rocket stays core weapon.
- todo: RMB shotgun action, right-hand shotgun ownership, no-shotgun -> RMB inert rule.

- LMB -> left-hand rocket launcher.
- RMB -> right-hand shotgun when owned.
- `F` -> dash-kick.
- No shotgun -> RMB has no combat action.
- Pickup collection: automatic.
- Rocket launcher remains core/default weapon. Shotgun remains optional arena pickup.

## Health, damage, death

Status: PARTIAL
- done: max health `100`, no regen, no friendly damage, no friendly rocket knockback, own rocket propels with zero self-damage, enemy rocket damage + `50%` knockback fraction, two-hit kill target, dash-kick enemy damage + shove, no overhead health, no directional indicator, personal HUD icon + bar + numeric.
- todo: every shotgun clause (damage, zero propulsion/knockback/stun).
- caveat: enemy-rocket knockback fraction never felt in play -> inert avatars do not fire. First real validation happens in bots run.

- Maximum health: `100`.
- Health regeneration: none.
- Friendly damage: none.
- Friendly rocket knockback: none.
- Own rocket:
  - Propels owner.
  - Causes zero self-damage.
- Enemy rocket:
  - Damages target.
  - Applies `50%` of owner self-rocket knockback.
  - Two direct hits kill full-health target as starting balance target.
- Shotgun:
  - Damages enemies.
  - Applies zero player propulsion, knockback, stun, slow, or aim disruption.
- Dash-kick enemy contact -> small damage + brief shove.
- No enemy or teammate overhead health display.
- No directional incoming-damage indicator.
- Personal HUD health -> icon + bar + numeric value.

## Death and respawn

Status: PARTIAL
- done: five-second wait, spectator view, death overlay (killer + weapon + table + countdown, killed player only), safest-spawn selection, two-second immunity, shield effect + HUD marker, pass-through + non-blocking rules, immunity cancel on fire / kick / ball contact.
- todo: immunity cancel on weapon pickup and ammo pickup -> blocked on shotgun + ammo pickups existing.
- note: death overlay weapon field currently hard-populated with rocket launcher. Shotgun work must feed real weapon identity.

- Death wait: five seconds.
- Death view: spectator view follows ball or ally behind overlay.
- Death overlay: killer + weapon, match table, respawn countdown. Visible only to killed player.
- Respawn: full health at safest team-side spawn, away from ball, enemy goal, visible enemies.
- Post-respawn immunity: two seconds.
- Immunity presentation: unmistakable shield effect + HUD marker.
- Immune player:
  - Moves normally.
  - Passes through players.
  - Does not block projectiles.
  - Movement alone preserves immunity.
- Immunity ends immediately on firing, kicking, meaningful ball contact, weapon pickup, or ammo pickup.

## Arena pickups

Status: PARTIAL
- done: two mirrored health spawns, one-third restore, no overheal, full-health cannot consume, `15s` respawn, automatic collection, model hides while unavailable, no return warning, goal-reset restore.
- todo: contested neutral shotgun spawn, two mirrored ammo spawns, ammo clauses (eight shells, 16 cap, collectible without shotgun, overflow discarded, at-cap cannot consume).
- reuse: health pickup respawn/eligibility pattern is intended template for both new spawn types.

- Layout:
  - One contested neutral shotgun spawn.
  - Two mirrored ammo spawns.
  - Two mirrored health spawns.
- Pickup unavailable -> pickup model/icon disappears.
- Pickup return warning/countdown: none.
- Health pickup:
  - Restores roughly one-third health.
  - Respawns `15s` after collection.
  - Cannot overheal.
  - Full-health player cannot consume.
- Ammo pickup:
  - Grants eight shells up to 16-shell cap.
  - Respawns `15s` after collection.
  - Collectible without shotgun.
  - Partial capacity consumes pack; overflow discarded.
  - Player at cap cannot consume.

## Shotgun economy

Status: TODO
- nothing shipped. No shell state, no carried-weapon state, no shotgun spawn.

- Shotgun spawn returns `15s` after every collection, regardless of active carried shotguns.
- Multiple active shotguns: allowed; no arena-wide cap.
- Shotgun pickup grants weapon + eight shells, capped at 16 carried shells.
- Player without shotgun may pre-collect up to 16 shells.
- Player without shotgun at ammo cap may still collect shotgun; ammo remains 16.
- Shotgun holder below cap may collect shotgun pickup -> add eight shells up to cap.
- Shotgun holder at cap cannot consume shotgun pickup.
- Death or goal -> lose shotgun + all shells.
- Empty shotgun:
  - Remains equipped and visible.
  - Cannot fire until ammo pickup or shotgun pickup restocks it.
  - Cannot be manually discarded.
- Other players see shotgun in right hand. Loaded/empty state remains hidden.
- Accepted tradeoff: recurring spawn can arm every surviving player; shotgun saturation intentional.

## Shotgun behavior

Status: TODO
- only asset exists: `Tools/Blender/generate_fps_shotgun.py` -> `Assets/_Game/Models/FpsShotgun.fbx` + `Assets/_Game/Models/Shotgun.fbx`.
- meshes are unintegrated -> no import-pipeline registration, no material, no prefab mount, no hand attachment, no pickup placement.
- zero weapon behavior: no fire, spread, pump delay, damage curve, ball force, empty state.

- Purpose: close combat + easier hits against airborne players + secondary ball control.
- Damage curve: high close-range damage, moderate medium-range damage, weak beyond intended range.
- Full-health target -> two accurate close shots kill as starting balance target.
- Spread: fixed and predictable.
- Headshot multiplier: none.
- Reload action: none for PoC.
- Fire rhythm: visible pump delay between shots; prevent near-instant two-shot kill.
- Ball hit:
  - Force scales with range + pellet contacts.
  - Total force capped for predictable outcomes.
  - Shotgun propels ball only, never shooter.
- Empty trigger feedback:
  - HUD count `0` + dim shotgun icon.
  - Code note/TODO: clear dry-fire sound when audio system added.

## Shotgun HUD and feedback

Status: TODO
- live HUD exists but carries timer + goals + health only. No shotgun widget, no shell count, no hit marker.
- `Global kill feed: none` -> already satisfied by omission.

- No shotgun + zero shells -> shotgun HUD hidden.
- No shotgun + stored shells -> dim shotgun icon + shell count.
- Shotgun owned -> icon + shell count.
- Temporary visual hit marker until reliable hit-confirmation sound exists.
- Audio milestone -> replace temporary hit marker with hit-confirmation sound.
- Global kill feed: none.

## Dash-kick

Status: DONE phase-1
- full clause set shipped: `F` input, forward dash + extended leg, ball-independent activation, camera-set direction with limited steering, no homing, momentum preservation + speed cap, three-second cooldown, one air use until grounded, enemy damage/shove, friendly no-op, wall stop, ball control response, first-person + world presentation, restrained camera impulse.
- open: feel values are unplayed. Preserved-vs-replaced `BallKick` tuning recorded during phase 1; treat regressions as tuning, not re-plan.

- Input: `F`.
- Form: forward dash + fully extended leg, not swing-style football kick.
- Purposes: ball control + movement + limited airborne steering.
- Activation allowed with or without nearby ball.
- Direction:
  - Camera aim sets initial direction.
  - Limited steering during dash.
  - No homing or snap-to-ball assistance.
  - Forgiving contact volume.
- Momentum: preserve existing velocity + controlled forward burst; cap resulting speed.
- Cooldown: three seconds after every activation, including miss.
- Air rule: one use until grounded, even when cooldown expires earlier.
- Enemy contact -> small damage/shove + ends most forward dash.
- Friendly contact -> no damage or shove.
- Solid wall contact -> dash stops.
- Ball contact -> strong forward control response.
- Presentation:
  - First-person extended leg visible.
  - World avatar uses readable extended-leg animation.
  - Restrained camera impulse.
  - Player retains camera control.

## Bots

Status: TODO
- done only: bot identity surface -> short unique names, team color, no difficulty suffix, five non-human slots present with full vitals/collision/presentation.
- todo: everything behavioral -> navigation, perception, memory, role assignment, priorities, aim/prediction, weapon use, dash use, difficulty tiers, error model, pickup etiquette, pre-match difficulty selection, pause-screen difficulty display.
- largest unresolved area in project. No `Runtime/Bots` code, no navmesh baked, no navigation package.

- Same health, damage, movement, pickup, weapon, death, respawn, immunity, scoring rules as humans.
- Allied bots: fixed Medium difficulty.
- Enemy bots: one team-wide `Low`, `Medium`, or `High` selection before match.
- Difficulty locked until next match.
- Difficulty shown in pre-match setup + pause screen. No difficulty marker beside bot name.
- Difficulty changes:
  - Low -> slower decisions, weaker aim/prediction, more errors.
  - Medium -> competent baseline.
  - High -> stronger positioning, prediction, aim, reaction, teamwork; human-like error retained.
- Difficulty never changes health, damage, speed, pickup value, cooldown, hidden knowledge, or awareness rules.
- Pickup knowledge: visible state + remembered timing only; no unseen respawn knowledge.
- Roles: dynamic attacker/support/defender assignment; one bot usually retains defensive responsibility.
- Priorities: scoring + defending first; opportunistic kills allowed; frag hunting never replaces strong ball play.
- Player commands: none.
- Allied pickup etiquette:
  - Yield nearby shotgun to human unless human already owns one or remains far away.
  - Avoid taking health needed by critically injured teammate.
- Bot names: short + unique. Team color shown. Difficulty suffix omitted.

## Team readability

Status: PARTIAL
- done: Blue/Red identity on avatars, goals, spawns, HUD, markers, rocket trails, impact accents, immunity effects; shape/symbol cue; natural explosion with restrained team accent.
- todo: same treatment extended to shotgun pickup, ammo pickup, shotgun world model, and any bot-specific marker introduced later.

- Blue/Red identity applied to avatars, goals, spawns, HUD, markers, rocket trails, impact accents, immunity effects.
- Shape/symbol cues supplement color for color-blind readability.
- Rocket explosion stays visually natural; restrained team accents identify source.

## HUD and summaries

Status: PARTIAL
- done: live HUD (timer + goals + health), frags hidden from live HUD, `Tab` hold table with goals/frags/deaths, goal summary with own-goal attribution, death summary scoped to killed player, opening rules screen, final summary with deciding rule.
- todo: shotgun/ammo widget in live HUD, hit marker, pre-match difficulty selection screen, pause screen showing difficulty.

- Normal live HUD: timer + team goals + personal health + shotgun/ammo state.
- Team frags hidden from persistent live HUD.
- Hold `Tab` -> full match table while alive.
- Match table columns: `Goals | Frags | Deaths`.
- Goal summary: team goals, team frags, full match table, own-goal attribution when applicable.
- Death summary: killer/weapon + full match table + respawn countdown; killed player only.
- Opening rules screen: goals primary, team frags break tied goals, equal frags produce draw.
- Final summary: winner/draw + deciding rule + full table.
- Accepted tradeoff: frags influence strategy because tied goals use frag tiebreak.

## Planning guardrails

Status: ACTIVE for remaining run
- vertical-slice guardrail is satisfied for match flow, damage/death/respawn, health pickups, dash-kick, scoring, summaries.
- still binding for shotgun + bot roles + difficulty.

- Preserve football-first bot priorities despite frag tiebreak.
- Keep rockets default focus despite intentional shotgun accumulation.
- Defer online multiplayer implementation.
- Defer combat audio; retain explicit dry-fire requirement and temporary hit marker transition.
- Expose damage, force, pump delay, health restore, bot reaction/aim, spawn safety, camera impulse as tuning values.
- Build smallest playable vertical slice proving 3v3 flow, damage/death/respawn, pickups, shotgun, dash-kick, bot roles, difficulty, scoring, summaries.

## Planning completion criteria

Status: ACTIVE for remaining run
- applies to remaining scope only. Sections marked `DONE phase-1` need no candidate owner.

- Coding plan accounts for every locked rule without reopening settled decisions.
- Coding plan separates PoC work from deferred multiplayer/audio/right-hand expansion.
- Coding plan identifies repository owners, generated-asset impacts, Unity validation, playtest tuning gates.
- Any codebase conflict with this handoff surfaced explicitly before implementation.
