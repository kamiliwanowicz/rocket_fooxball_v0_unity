# Remaining 3v3 Shotgun Coding Plan

Status: accepted
Source: direct request to decouple the shotgun implementation from [`remaining-3v3-shotgun-and-bots-coding-plan.md`](remaining-3v3-shotgun-and-bots-coding-plan.md); remaining A6 scope from [`3v3-phase2-shotgun-and-bots-planning-helper.md`](3v3-phase2-shotgun-and-bots-planning-helper.md)
Run ID: direct
Plan ID: direct
Attempt ID: direct
Covered Requirements: direct request; A6 shotgun HUD and Shotgun death-summary fidelity
Baseline: 68d3c9f2002e68c6d2db7ff1f1d94816c3c704bf
Dependencies: None

## Objective

Complete the remaining shotgun HUD only: show authoritative inventory/ammo states, display a temporary accepted-hit marker, and preserve Shotgun death-summary text. Completion ends after source review, a clean committed SHA, and Unity compile proof. Bot behavior, bot setup/pause UI, builder composition, and generated outputs are independent and excluded.

## Scope

- in: A6 shotgun inventory HUD, empty/unowned-ammo states, temporary hit marker, Shotgun death-summary verification, and the minimum `MatchHud` source change.
- out: B1-B6 bots and difficulty; setup/pause screens; audio/dry-fire sound; texture/icon assets; right-hand weapon replacement; production UI refactor; generated prefab/scene/material changes; any earlier completed A1-A5 shotgun redesign.

## Decisions

- assumption: observed clean baseline is branch `shotgun_design_and_match_foundation` at `68d3c9f2002e68c6d2db7ff1f1d94816c3c704bf`; implementation preserves unrelated later user changes if execution starts from a different explicitly accepted SHA.
- decision: A6 edits only `MatchHud`; existing `localParticipant.Shotgun`, `HasShotgun`, `ShotgunShells`, and `HitConfirmed` cover the feature, so the frozen three-reference HUD serialized surface and generated scene stay unchanged.
- decision: display four authoritative inventory states: hide when unowned with zero shells; dim `SHOTGUN <count>` when unowned with stored shells; bright when owned and loaded; dim when owned and empty.
- decision: use a centered white `X` for 0.18 unscaled seconds after `HitConfirmed`. The text label is the PoC icon; no texture asset is added.
- decision: preserve `FormatDeathWeapon`; the existing nonempty `"Shotgun"` death-event string already renders `SHOTGUN`.
- question: None

## Execution Graph

`START -> T1 -> CP1 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in; `+` requires every named predecessor
- gates: `START` -> exact accepted baseline is available in an isolated worktree with no owned-path overlap; `FINAL` -> CP1 accepted, accepted fixes committed, the production-final Unity compile passes at one clean exact SHA, no generated or unrelated paths changed, zero live child agents, and the pinned plan artifact still matches.

## Tasks

### T1: Complete and compile the shotgun HUD

- objective: add all live shotgun inventory states and a temporary hit marker without changing serialized composition or generated outputs.
- covered_requirements: A6 shotgun HUD and Shotgun death-summary fidelity
- owner: `luna_max` implementation worker for source; execution orchestrator for the declared orchestrator-phase proof
- owns: `Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs`
- protected: `MatchHud` serialized fields `match`, `localParticipant`, `input`; `MatchHudScreenPolicy`; `ParticipantDeathEvent.Weapon`; all bot, Editor, generated, scene, prefab, material, package, and project-setting paths
- read_paths: `Assets/_Game/Scripts/Runtime/Participants/ParticipantState.cs -> inventory/read owner`; `Assets/_Game/Scripts/Runtime/Weapons/ShotgunWeapon.cs -> hit event and Shotgun weapon string`; `Assets/_Game/Editor/MovementLab/MovementLabValidator.cs/ValidateMatchHudSerializedSurface -> frozen composition contract`
- validation_environment: source edit in an isolated worktree; final compile uses one Unity Editor lease on short `C:\wt\<id>` with a private preserved `Library`, short evidence alias `C:\wt\<id>e`, no interactive Editor/project lock, and the same short project path for every process check
- unity_mutation: `true`
- expensive_proof_owner: execution orchestrator
- expensive_proof_execution: `orchestrator_phase`
- implementation: snapshot `HasShotgun` and `ShotgunShells` in `CaptureFrameSnapshot`; cache `localParticipant.Shotgun`, subscribe/unsubscribe `HitConfirmed` beside `Died`, and make the callback assign a 0.18-second timer; decrement with `Time.unscaledDeltaTime` in `Update`, then snapshot visibility for `OnGUI`; add a bottom-right PoC widget using the existing styles and exact four-state colors/visibility from Decisions; draw the centered white `X` after the current screen so it remains visible on live/table/GO screens only while the local participant is alive; keep `FormatDeathWeapon` unchanged.
- done when: no-shotgun/zero hides; stored ammo without weapon is dim; loaded owned weapon is bright; empty owned weapon remains visible and dim; one accepted enemy hit shows then expires the marker; no serialized field, bot source, or generated file changes; shotgun deaths still show `SHOTGUN`; Unity compiles the accepted source at the final clean SHA.
- checks: `proof: dotnet build RocketFooxball.Runtime.csproj --no-restore -> successful fast preflight for the edited runtime assembly`
- checks: `proof: powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Tests/Invoke-HarnessTests.ps1 -> pass in under 90 seconds before the Unity compile, with no Editor or project lock`
- checks: orchestrator-phase trigger is `CP1 accepted + T1 source committed + accepted fixes applied + scoped-clean status`; declared producer is T1; declared generated outputs are none.
- checks: `check_id=unity-compile; tier=production-final; owner=execution orchestrator; expected_status=passed; command="C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath C:\wt\<id> -logFile C:\wt\<id>e\unity-compile.log; mutates_project=true; input_paths=[Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs, Assets/_Game/Scripts/Runtime, Assets/_Game/Editor, Packages, ProjectSettings]; input_digest=canonical Git-blob SHA-256 at accepted committed SHA; environment_fingerprint=Windows + Unity 6000.5.6f1 + private Library; invalidation_paths=[Assets/_Game/Scripts/Runtime, Assets/_Game/Editor, Packages, ProjectSettings]; subsumes=[authoritative Unity compile]; run_point=after CP1/source commit/accepted fixes and before final handoff; evidence=process exit 0, zero Unity compile/Console errors, exact accepted SHA, scoped-clean status with no versioned Unity output changes, and project-lock release`
- review_focus: an added serialized HUD dependency, event-lifetime leak, or `OnGUI` read of mutable gameplay state could dirty the generated scene or create inconsistent frames; inspect field surface, subscription symmetry, timer reset, snapshot-only drawing, alive/screen visibility, and death-string preservation.
- review_checkpoint: CP1

## Execution Assignments

- workers: T1 -> `luna_max` -> runtime-only HUD source; execution orchestrator -> final Unity compile after CP1 and accepted fixes.
- review_checkpoints: CP1 -> T1 worker -> terminal/source frozen -> no dependency -> per-worker fresh `sol_medium`. Review/fix/re-review behavior follows [`orchestrate-implementation`](../.agents/skills/orchestrate-implementation/SKILL.md#review-checkpoints).

## Final Verification

- exact head: require a clean committed SHA descended from `68d3c9f2002e68c6d2db7ff1f1d94816c3c704bf`; compile evidence binds this exact SHA, and the pinned plan hash/size still matches.
- checks: run the T1 fast preflight, harness pre-gate, and one production-final Unity compile in their declared order after CP1 and accepted fixes. Do not run the MovementLab builder, bake, semantic validator, or duplicate compile.
- inspect: `git diff <start_sha>..<final_sha> -- Assets/_Game/Scripts/Runtime/Hud/MatchHud.cs` contains only the intended HUD implementation; full status contains no bot, generated, scene, prefab, material, package, project-setting, IDE, or unrelated changes; the serialized HUD field surface remains exactly three references.
- invalidation: any accepted fix or runtime/editor/package/project-setting change after the compile reruns it. Do not reuse evidence across a different SHA.
- manual post-handoff: user may confirm the four inventory states, 0.18-second hit marker, and `SHOTGUN` death summary in the later combined five-minute playtest; no implementation worker waits on this check.

## Handoff

- residual risks: precise HUD placement, brightness, and marker feel remain human playtest findings; the temporary text widget and hit marker intentionally await the later audio/UI milestone.
- authority: implementation remains on the isolated plan branch. Integration into the user branch and any follow-up tuning require explicit user/LP approval; the execution orchestrator has no merge authority.

