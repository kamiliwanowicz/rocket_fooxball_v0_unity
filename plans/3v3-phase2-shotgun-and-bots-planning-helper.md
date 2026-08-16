# 3v3 Phase 2 Shotgun And Bots Status Handoff

## Authority

- Status: implementation handoff. Not gameplay design authority.
- Gameplay-rule conflict -> [`3v3-bots-combat-design-handoff.md`](3v3-bots-combat-design-handoff.md) wins.
- Remaining scope, order, ownership -> this doc wins.
- Audience: planning agents, workers, reviewers.
- Integrated branch: `shotgun_design_and_match_foundation`.
- Product checkpoint: `99f18e88305029a5d3dc3d156c383684bc90ce92`.
- Generated-output checkpoint: `c060e33b8a7e7fa7b4808f60ee586db62e66b508`.
- Recovery source accepted through `32bc4d405d15fe1f91c70be35f6d1f50dea23ee5` only.

## Binding User Decisions

- Finish remaining scope from clean integrated branch.
- No mid-run playtest gate.
- User performs one manual verification after all pieces land.
- One authorized production bake after source freeze. No earlier production bake.
- Stop after final validation wave.

## Completed Shotgun Scope

- A1 rules + tests -> deterministic ammo, falloff, fixed spread, ball-force cap.
- A2 imports -> FPS/world FBX registration, importer contract, inventory/fingerprint coverage.
- A3 presentation -> FPS/world mounts, per-slot visibility, dedicated shotgun materials.
- A4 runtime -> hitscan RMB fire, shells + ownership on `ParticipantState`, reset clearing, shared relationship policy, damage/death cause, immunity cancel, ball impulse, hit-confirm event.
- A5 pickups -> one neutral shotgun spawn, two mirrored ammo spawns, recurring respawn, prefabs/materials/scene roots, validator contracts, reset removal.
- Recovery commits -> `c9ed709`, `7d2dd69`, `d3a13e8`, `b7f8f6a`, `20cd91f`, `cd08440`, `3aaff9e`, `ea4cfdc`.
- Integration -> useful product hunks forward-ported onto `c395eef`; current workflow and generated-YAML fixed-point fixes preserved.
- Deviation -> dedicated `ShotgunMetal`, `ShotgunDark`, `ShotgunAccent` materials created instead of launcher-material reuse.

## Completed Validation

- Harness -> pass.
- Fast workflow -> pass at `99f18e8`; compile + stage probe + Fast build; `bakeCount: 0`.
- Generated comparison -> 15/15 changed authoritative paths reported; 9 semantic YAML/text checks; `DANGLING: 0`.
- New `.meta` files -> asset pairing present; each GUID unique repo-wide.
- Fast proof evidence -> `C:\wt\p2sve\fast-matching-cache\invocation-a1a5-matching-cache-4cf187a6570344088fec7dd24f5bd172\evidence-manifest.json`.
- Production build, production bake, separate semantic validation, manual playtest -> pending.

## Excluded Work

- Recovery framework changes -> excluded: stale orchestration skill, stale AGENTS rules, three-process prepare/bake workflow, YAML whitespace normalizer.
- Recovery generated files -> excluded, then regenerated through current builder.
- Binary PNG/EXR/`LightingData.asset` comparer -> unfinished recovery WIP, not integrated.
- A6 HUD -> absent from recovery checkpoint.
- B1-B6 bots/difficulty -> not started.

## Resolved Shotgun Decisions

- D12 fire model -> hitscan.
- D13 friendly-fire policy -> shared `ParticipantRelationship`.
- D14 shotgun state owner -> `ParticipantState`.
- D15 read model -> no `ParticipantReadModel` extension.
- Damage/death enums -> append-only shotgun values.
- Projectile layer -> excluded from pellet hit mask.
- Ball push -> `BallMotor.QueueImpulse`.

## Remaining Order

1. A6 shotgun HUD.
2. B1 bot pure rules.
3. B2 bot control seam.
4. B3 navigation.
5. B4 perception + roles.
6. B5 combat.
7. B6 difficulty + setup/pause UI.
8. Source freeze.
9. One authorized production bake.
10. Authoritative build -> EditMode tests -> separate semantic validation.
11. User five-minute 3v3 playtest.

Serialize all writes under `Assets/_Game/Editor/MovementLab/**` and all builder-generated outputs. Keep generated churn in separate commit.

## A6 Shotgun HUD

Scope:

- Live widget states -> no shotgun, loaded shotgun + shell count, empty shotgun.
- Temporary hit marker -> subscribe to `ShotgunWeapon.HitConfirmed`; snapshot timer in `Update`; draw in `OnGUI`.
- Death summary -> retain existing event-provided weapon string; verify `Shotgun` fidelity.
- Prefer existing `localParticipant`/`match` references. New serialized HUD ref -> validator + generated-output ownership required.
- Optional D16 split -> extract HUD screen/draw helpers only if focused edit becomes unsafe. No broad UI refactor.

Likely ownership:

- `Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs`
- `Assets/_Game/Editor/MovementLab/MovementLabStageGraph.cs` when input contract changes
- `Assets/_Game/Editor/MovementLab/MovementLabValidator.cs` when HUD contract changes
- affected generated prefab/scene/manifest outputs

Validation:

- Harness before Unity mutation.
- Unity compile.
- Fast build + persisted Fast validation. No production bake.

## Remaining Bot Decisions

- D1 intent injection -> recommend programmatic intent APIs on consumers. Keep `PlayerInputReader` device-only.
- D2 aim ownership -> recommend `BotAim` writing root yaw + `Head.localRotation`; prove mutual exclusion with `PlayerLook`; choose `Update` vs `FixedUpdate` before B2.
- D3 dash route -> bot raises kick intent. Preserve cooldown, events, immunity cancel, presentation.
- D4 leaf gates -> B2 owns final launcher/kick/shotgun simulation conditions and bot brain match gate.
- D5 execution order -> bot decision visible before `BallKick` `FixedUpdate` order `-100`.
- D6 placement -> one `BotController` on shared `Player.prefab`; disabled for local slot.
- D7 sensing -> serialized builder refs or match-owner reads. No scene search.
- D8 difficulty ownership -> decide serialized fields vs profile asset before B1.
- D9 logic split -> pure deterministic scoring/error modules in B1.
- D10 rocket jump -> recommend difficulty-gated heuristic, not ballistic solver.
- D11 navigation -> recommend builder-authored graph + steering. No NavMesh package/stage.
- D16 HUD structure -> resolve during A6; avoid unrelated extraction.

## Bot Task Contracts

### B1 Pure Rules

- Difficulty parameters, reaction latency, aim noise, decision jitter.
- Role assignment + target scoring.
- Pure deterministic EditMode tests.
- No Unity-generated output.

### B2 Control Seam

- Implement D1-D5.
- Wire five non-local slots through builder.
- Proof behavior -> face ball + approach ball.
- Final owner of `ParticipantState` bot simulation gates.
- Never add extra `PlayerInputReader`; all six existing readers share one action asset.

### B3 Navigation

- CharacterController steering over floor, ramp decks, shield-gated recesses.
- Multi-level graph; no flat single-height model.
- One-way drop edges + ledge safety.
- No NavMesh unless user explicitly expands scope.

### B4 Perception And Roles

- Visibility + remembered pickup timing only.
- Dynamic attacker/support/defender roles.
- Football-first priorities.
- Allied pickup etiquette.
- Serialized refs; no runtime scene search.

### B5 Combat

- Aim + prediction.
- Rocket, dash, shotgun use.
- Difficulty error model.
- Optional difficulty-gated rocket jump per D10.
- Preserve friendly-fire, cooldown, immunity, reset contracts.

### B6 Difficulty And Setup UI

- Enemy difficulty selection before kickoff.
- Allied bots fixed Medium.
- Defer `BeginNewMatch` until setup completes.
- Pause screen shows difficulty.
- Match state remains sole gameplay gate owner.

## High-Risk Constraints

- `ParticipantState` control gates -> A4/B2 collision resolved by B2 ownership.
- Shared `Player.prefab` -> bot component + per-slot wiring need gameplay-scene contract bump.
- Builder registration -> contract, catalog, stage graph, pipeline, validator, output inventory move together.
- Manifest authorization -> exact Git SHA, consumed once. Authorize immediately before each build requiring migration.
- Lighting -> current graph fingerprints `MovementLabContractCatalog.cs`; integrated A2-A5 make production lighting stale. Defer required bake until final source freeze.
- HUD -> current validator freezes serialized surface. Prefer existing refs.
- Arena -> ramp deck overlaps floor; shield state changes connectivity; flat graph invalid.
- Render budgets -> report-only. Never add budget throw.

## Final Validation

- Finish all source edits first.
- Harness before every Unity-mutating workflow.
- Pure rules -> existing EditMode assembly.
- One production prepare/bake after source freeze.
- One authoritative build.
- EditMode tests invalidated by final diff.
- Semantic validator in separate Unity process.
- Generated comparison -> exact changed-path coverage, no dangling increase, stable GUIDs, intact asset/`.meta` pairs.
- Report only checks actually run.

## Done Condition

- Five bots move, aim, navigate ramps/recesses, contest ball, defend, score.
- Enemy difficulty selected before kickoff; pause shows value; allies fixed Medium.
- Bots take/deal rocket damage; enemy-rocket knockback observable.
- Shotgun collectible, RMB fire, two close shots kill full-health target, ball push works, empty state works, ammo restock works.
- Shell cap 16; ammo collection without shotgun; at-cap rejection.
- Death/goal removes shotgun + shells.
- Weapon/ammo pickup cancels immunity.
- HUD shows shotgun state, shells, hit marker; death summary names shotgun.
- Final build/tests/semantic validation pass.
- User completes five-minute 3v3 playtest.

## Tuning Targets

- Shotgun -> pellet damage/count, spread, falloff, pump delay, ball force scale/cap.
- Pickups -> pack size, carry cap, ammo respawn, shotgun respawn.
- Bots -> reaction latency, aim noise, prediction error, decision interval, role hysteresis, difficulty multipliers, pickup etiquette.
- Existing unvalidated -> enemy-rocket knockback, dash feel, death wait, immunity duration, spawn-safety weights.
