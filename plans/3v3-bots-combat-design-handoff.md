# 3v3 Bots And Combat Design Handoff

## Authority

- Status: accepted design + post-implementation status handoff.
- Product implementation: merged through `490ad49ddbacb53d7ee6ae6ece3ece79a6d54e52`; later branch commits docs-only.
- Automated proof: 114/114 EditMode tests, generated-output acceptance, separate-process semantic validation.
- Purpose: identify shipped behavior, remaining code, pending playtest/tuning, deferred scope.
- Preserve locked decisions below. Treat tuning targets as playtest starting points.

## Implementation status legend

Every `##` section below carries `Status:` line. Meaning:

- `DONE automated` -> shipped through bots run, automated validation passed. Human playtest may remain.
- `PARTIAL` -> some clauses shipped, rest open. `done:` / `todo:` name exact split.
- `TODO` -> nothing shipped.
- `PLAYTEST PENDING` -> code + automated proof complete; feel/readability/tuning unverified by human playtest.

Aggregate: match foundation, vitals, dash-kick, health/shotgun/ammo pickups, shotgun weapon/economy/presentation, five bots, custom graph navigation, perception/memory, roles, combat, difficulty setup, pause semantics, generated scene composition complete. Remaining code = shotgun/ammo HUD widget + temporary hit marker. Remaining proof = five-minute 3v3 playtest + tuning. Deferred = multiplayer, production combat audio, generic right-hand replacement framework.

## Remaining work

Status: PARTIAL

- code:
  - shotgun/ammo live HUD states: hidden, stored-shell dim icon/count, owned icon/count, empty dim state
  - temporary visual hit marker wired from `ShotgunWeapon.HitConfirmed`
- playtest:
  - five bots activate, navigate floor/ramps/drops/gates, contest/defend/score
  - bots use rockets, dash-kick, shotgun, pickups, enemy knockback, High rocket jump
  - Low/Medium/High setup + locked pause display work during real match
  - shotgun damage/spread/pump/ball impulse, rocket knockback, bot reaction/aim/roles/pickup etiquette feel fair
- deferred, not blockers: dry-fire/hit-confirmation audio, multiplayer, generic right-hand replacement framework, combat assists, production UI refactor

## Product boundary

Status: DONE automated; PLAYTEST PENDING
- done: six active participant slots; one local Blue human + two Blue bots vs three Red bots; fixed Blue/Red teams; five nonlocal slots use bot controllers.
- remaining: human playtest confirms active 3v3 behavior and tuning.

- PoC roster: one human + two allied bots vs three enemy bots.
- Human team: Blue. Opponent team: Red.
- Future multiplayer: later milestone. Human players replace bot slots; bots fill vacancies until each team has three active slots.
- Team colors: globally fixed Blue/Red for all clients.
- Networking, right-hand weapon replacement system, combat-assist scoring, production audio: deferred.

## Match rules

Status: DONE automated; PLAYTEST PENDING
- done: clock, full pause freeze, winner order, frag tiebreak, negative frags, killing-blow credit, self/arena death penalty, goal credit, own-goal attribution.
- remaining: playtest confirms bot-caused rocket/shotgun/dash deaths and score presentation.

- Match duration: five minutes.
- Live clock: continues during individual death/respawn; pauses during goal summary, kickoff countdown, and explicit `Paused` state.
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

Status: DONE automated; PLAYTEST PENDING
- done: setup -> start reset/countdown; goal stop/summary/reset/countdown; health/position/dead return; shotgun + shells cleared; pickups restored; timers reset; no post-kickoff immunity; final `Rematch` + `Exit`.
- remaining: playtest confirms complete reset flow under active bots/projectiles/pickups.

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

Status: DONE automated; PLAYTEST PENDING
- done: LMB rocket, RMB shotgun when owned, `F` dash-kick, automatic pickup collection, no-shotgun RMB inert, first-person + world shotgun visibility.
- remaining: input/hand feel review.

- LMB -> left-hand rocket launcher.
- RMB -> right-hand shotgun when owned.
- `F` -> dash-kick.
- No shotgun -> RMB has no combat action.
- Pickup collection: automatic.
- Rocket launcher remains core/default weapon. Shotgun remains optional arena pickup.

## Health, damage, death

Status: DONE automated; PLAYTEST PENDING
- done: full clause set including shotgun enemy damage, zero shotgun player propulsion/knockback/stun, enemy rocket knockback, relationship gates, dynamic death weapon identity.
- remaining: damage, knockback, and two-hit targets need feel validation against active bots.

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

Status: DONE automated; PLAYTEST PENDING
- done: full clause set; shotgun/ammo pickups cancel immunity; death overlay receives rocket/shotgun/dash weapon identity.
- remaining: spectator readability, spawn safety, immunity clarity under 3v3 load.

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

Status: DONE automated; PLAYTEST PENDING
- done: two health, one neutral shotgun, two mirrored ammo spawns; `15s` respawn; automatic collection; hide/restore flow; eight-shell grants; 16-shell cap; pre-collection; overflow discard; at-cap rejection.
- remaining: placement/readability/contest timing playtest.

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

Status: DONE automated; PLAYTEST PENDING
- done: carried state, shell state, neutral recurring pickup, mirrored ammo, death/goal clearing, empty retention, restock, visibility, saturation rules.
- remaining: saturation/economy feel review.

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

Status: DONE automated; PLAYTEST PENDING
- done: model import/materials, prefab mounts, pickup placement, deterministic spread, distance falloff, pump gate/presentation, enemy damage, capped ball impulse, empty retention, bot programmatic fire.
- remaining: feel/tuning; dry-fire audio deferred.

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

Status: PARTIAL
- done: first-person/world shotgun visibility, recoil/pump presentation, `ShotgunWeapon.HitConfirmed` event, no global kill feed.
- todo: live shotgun/ammo widget, shell count, empty dim state, stored-ammo-without-weapon state, temporary visual hit marker.
- deferred: dry-fire sound + audio replacement for temporary hit marker.

- No shotgun + zero shells -> shotgun HUD hidden.
- No shotgun + stored shells -> dim shotgun icon + shell count.
- Shotgun owned -> icon + shell count.
- Temporary visual hit marker until reliable hit-confirmation sound exists.
- Audio milestone -> replace temporary hit marker with hit-confirmation sound.
- Global kill feed: none.

## Dash-kick

Status: DONE automated; PLAYTEST PENDING
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

Status: DONE automated; PLAYTEST PENDING
- done: five nonlocal controllers; deterministic reaction/decision/aim error; custom multi-level graph navigation; ledge/gate/drop handling; per-observer perception + pickup memory; dynamic roles; football-first targets; rockets/dash/shotgun/rocket jump; human pickup etiquette; Low/Medium/High setup; locked pause display; full pause/reset/death lifecycle.
- implementation note: custom serialized graph by design; no NavMesh package required.
- remaining: five-minute 3v3 playtest + tuning only.

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

Status: DONE automated; PLAYTEST PENDING
- done: Blue/Red identity across existing surfaces; shotgun pickup, ammo pickup, shotgun world model integrated; no bot-specific marker introduced.
- remaining: human visual readability review.

- Blue/Red identity applied to avatars, goals, spawns, HUD, markers, rocket trails, impact accents, immunity effects.
- Shape/symbol cues supplement color for color-blind readability.
- Rocket explosion stays visually natural; restrained team accents identify source.

## HUD and summaries

Status: PARTIAL
- done: timer/goals/health, hidden live frags, `Tab` table, goal/death/final summaries, dynamic death weapon, setup difficulty selection, pause screen with locked difficulty.
- todo: shotgun/ammo widget + temporary hit marker.

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

Status: ACTIVE for HUD follow-up + playtest
- gameplay vertical slice implemented through bots run.
- binding code scope: shotgun/ammo HUD + temporary hit marker only.
- tuning changes require playtest evidence; do not reopen settled architecture without defect.

- Preserve football-first bot priorities despite frag tiebreak.
- Keep rockets default focus despite intentional shotgun accumulation.
- Defer online multiplayer implementation.
- Defer combat audio; retain explicit dry-fire requirement and temporary hit marker transition.
- Expose damage, force, pump delay, health restore, bot reaction/aim, spawn safety, camera impulse as tuning values.
- Build smallest playable vertical slice proving 3v3 flow, damage/death/respawn, pickups, shotgun, dash-kick, bot roles, difficulty, scoring, summaries.

## Planning completion criteria

Status: DONE for bots run; ACTIVE for HUD follow-up
- bot coding plan executed and validated. Completed sections need no candidate owner.
- next code plan, if requested, owns only shotgun/ammo HUD + temporary hit marker.

- completed bot plan preserved locked rules without reopening settled decisions.
- completed bot plan separated PoC work from deferred multiplayer/audio/right-hand expansion.
- completed bot plan bound repository owners, generated assets, Unity validation, playtest tuning gate.
- no unresolved codebase conflict blocks remaining HUD follow-up or playtest.
