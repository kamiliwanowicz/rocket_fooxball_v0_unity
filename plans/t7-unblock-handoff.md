# T7 Unblock Handoff

Reader role: analyst agent. Task: diagnose orchestrator T7 stalls -> give orchestrator minimal unblock guidance. No plan rewrites unless contradiction found.

## State (2026-08-10 ~23:05)

- worktree: `C:\wt\eff-091dc7`; branch `codex/graphics-overhaul-run-efficiency-20260810T111004014-091dc7`
- HEAD: `4022c57 fix: preserve binary lighting data after bake`
- Unity processes: none running
- working tree: ~51 dirty files, mostly generated (materials, prefabs, manifests, URP assets, `MovementLab.unity`) -> INTENTIONAL under drift policy; never reset/clean
- production bake: completed at least once (pass record `Production-<digest>.json` in `.git/architecture-evidence/movement-lab-prebake/passes/`)
- live plan: `C:\Users\iwano\Desktop\repos\rocket_fooxball_v0_unity\rocket_fooxball_v0_unity\plans\graphics-overhaul-run-efficiency-coding-plan.md` -> sections `Policy: Generated Output Drift` + `Policy: Lighting Demoted, Not Reverted` + `Orchestrator Resume Point` override everything, including worktree snapshot copy

## Root-cause diagnosis (2 completed investigations, condensed)

- pipeline validation: 389 `throw` sites, zero collect-all accumulators -> each Unity run reveals exactly 1 defect -> N defects = N runs -> 1h plan becomes 6h
- validators mutate state (scene reopen, serialized field rewrites) -> no cheap check-everything pre-pass possible
- machinery ~6430 LOC vs content generation ~3650 LOC -> all 15+ fix commits landed in machinery, zero in content
- blocker class history: leftover byte-exactness gates + policy contradictions + bugs inside freshly added validation checks (written blind, debugged 1 Unity cycle each ~18min)
- byte-determinism goal was unwinnable: Unity never guarantees byte-identical YAML across runs; correct invariant = GUID/.meta stability + semantic validity (already ratified policy)

## Already fixed (do not re-investigate)

- `ee4271c` -> deleted byte gates: `MovementLabStageRunner.AssertExactEquality`, PS1 build2-noop/forced-comparison/changed-paths throws
- `eab1518` -> deleted `Capture-BrightArenaVisuals.ps1` byte gate
- `4022c57` -> binary lighting data preservation post-bake (`MovementLabSceneComposer` + `MovementLabLightingPipeline`)
- earlier: scene-through-bake-gate, fast-mode manifest, prefab provenance (`IsPersistent` check), emission-without-map, PS1 `Test-Path -and` parse bug

## Remaining landmines (exact, check these FIRST when orchestrator reports blocker)

T7 step 5 = `ProductionValidate -Capture` -> highest risk:

- `Assets/_Game/Editor/BrightArenaVisualCapture.cs:482` -> `throw` "Source scope is dirty before capture" -> fires if scope includes generated paths (tree intentionally dirty). Fix direction: exclude generated paths from scope or drop dirty gate.
- `BrightArenaVisualCapture.cs:466` -> requires wrapper-supplied 40-char git SHA -> exactness-era plumbing; verify wrapper still supplies after `eab1518` deletions.
- `BrightArenaVisualCapture.cs:243` -> "scene became dirty during validation; capture refuses" -> known validator-side-effect class; fix = stop mutation, not add reopen.
- `Invoke-MovementLabWorkflow.ps1:1064` -> ProductionPrepare/Validate throw when NON-generated source dirty -> orchestrator must commit remaining `.cs` edits (T7 step 1) BEFORE these modes; check generated-path classification list covers all 51 dirty files.
- `Invoke-MovementLabWorkflow.ps1:1158` + `:1162` -> same non-generated dirty class post-run.
- `MovementLabStageGraph.cs:509` -> `identity:changed-meta` uses raw `.meta` byte digest, stricter than policy line (GUID only). Fires if Unity reserializes any `.meta`. Fix direction: compare GUID field only.
- `MovementLabFastModeSession.cs:262` -> `HashFile` equality in fast-mode restore; only relevant if fast mode re-entered.
- `MovementLabManifestStore.cs:183/:189/:194` -> fingerprint recompute mismatch -> "manifest write rejected".

## Remaining T7 steps (per live plan)

1. commit remaining source fixes (non-generated) -> unblocks PS1 `:1064`
2. `python Tools/Blender/generate_retro_textures.py --family all` once; NO `--proof-two-run`
3. workflow `Fast` once -> zero bake expected
4. explicit production prepare + bake once -> 200 probes, three `128` scene probes, tag `production`, delete three named `Assets/_Game/Lighting/ReflectionProbe_*.exr` + paired metas
5. clean-process `ProductionValidate -Capture` -> six captures, no magenta, zero shader/Console errors -> FINAL validation
6. GUID map check vs baseline
7. orchestrator commits generated paths separately -> `chore: regenerate MovementLab outputs`

Note: production bake possibly already done (see pass record) -> if lighting-input digest unchanged since, step 4 rebake unnecessary; verify digest before advising rebake.

## Guidance rules for advising orchestrator (enforce these)

- blocker inside gate/validator/machinery code -> default fix = DELETE or demote gate to warning, not patch-and-retry. Burden of proof on gate.
- NEVER re-add byte/hash equality on builder outputs, any language, any file. Includes "just to be safe" hashes.
- new validation check -> lands as warning first; promote to hard stop only after passing 1 real run. No exceptions.
- fix-cycle budget per subsystem per run: 2. Third blocker in same subsystem -> stop patching, escalate: delete subsystem gate wholesale.
- semantic checks that already survived runs (prefab provenance, emission flags, shader compile, GUID/meta) -> keep; battle-tested.
- reviewer findings demanding review markers, SOURCE_FREEZE, byte proofs, two-run texture proof, build-2 comparison -> reject as policy-contradicting (plan `Orchestrator Resume Point` rule 6).
- one Unity process at a time; batch `-batchmode -quit`; check `UnityLockfile` released before next step.
- cost model: every blocker discovery = full Unity cycle ~15-20min. Pre-read landmine list above before each step; pre-delete predicted gate cheaper than discovering it.

## Evidence pointers

- experiments + costs: `C:\wt\eff-091dc7\plans\graphics-overhaul-run-efficiency-handoff.md` (dev bake 31s, production 265s, no-op process 15s, warm compile 13s)
- blocker forensics source commits: `git log be51d6f..HEAD` in worktree
- Unity logs: `C:\wt\eff-091dc7\Logs\`, bake artifacts `Temp\LightBakerOutput`
- pass records: `<worktree>\.git\architecture-evidence\movement-lab-prebake\passes\`
