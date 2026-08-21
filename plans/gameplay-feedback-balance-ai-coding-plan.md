# Gameplay Feedback Balance And AI Coding Plan

Status: completed; historical implementation ledger
Execution directive: do not dispatch original T1-T12 tasks
Source: direct user request
Original implementation baseline: `85cd7351789f4abdc572737a5d18eca706787ff8`
Current audited baseline: `5dfdfde94e7acb42726dda0587866e5776c0fcea`
Gameplay generated boundary: `d9b28952209c751911e4a974a0957e13ee235fb6`
Latest visual/generated boundary: `df4f10dea3e15fda03382237331428cbd7f33f94`
Merged `gameplay` boundary: `f3f5989615614d34def802b266f7a5d132623541`
Dependencies: None

## Start Rule

- user-direct execution baseline: current clean launch `HEAD`
- recorded SHAs: ancestry provenance only; never equality gates
- require `85cd7351789f4abdc572737a5d18eca706787ff8` and `f3f5989615614d34def802b266f7a5d132623541` ancestors of launch `HEAD`
- launch `HEAD` newer than this document: valid when ancestry passes
- original implementation already present: do not recreate, revert, or regenerate from old ownership lists

## Delivered Requirements

- directional local hit feedback
- doubled participant collision and third-person presentation while root scale stays `1`
- shotgun damage and ball force increased `70%`
- enemy rocket impulse doubled
- blast radius doubled to `11.7`; matching team-aware rocket/explosion presentation
- enemy nicknames and pause-aware 30-second corpses
- indefinite goal summary dismissed by fresh button press
- procedural `128x64` shotgun icon
- local respawn `8` seconds; bot respawn `5` seconds
- deterministic corner recovery/combat assignments
- more frequent safe bot combat; retained kicks and High rocket jumps
- tiered aerial-ball miss; corrected rocket-jump body facing
- builder, validator, stage inputs, generated prefabs/scene, tests

## Completion Ledger

- T1 participant damage/lifecycle: `4c6d8d8f684bc44d445ed89fe75901a456413206`
- T2 shotgun balance: `5858484be6f38d71d7e9e8fd64ee14b33e97764a`
- T3 rocket force/team/VFX: `bd548fac1f17f0f74a216e4f199cd196f437cb4c`, `40e45c35b01c2ffe48ea8a4a968d3297f57158ad`
- T4 goal dismissal/input lifecycle: `5ff641eb9834728145f81d141dddffc7b6784fff`, `37f51088ff83a65fe7543998530d2c9166fdc686`
- T5 HUD feedback: `662d9b3dee50814531dbd235d757792ad04646b6`
- T6 participant/navigation scale: `a9f0eff3f0a60aea7427095c1512ba25bdcf7928`, `6855936cdad52dc9dd86961c5b56a6c8e6a1077e`
- T7 nicknames/corpses: `4889d4eabeb024bf96b74cead201a598b99c0698`, `37f51088ff83a65fe7543998530d2c9166fdc686`
- T8 corner rules: `f90e9d05843c48243ce71027f9b85d0b7ca24228`, `9da04bd4524655f4e6d4d5927e89de851beff7bc`
- T9 bot combat/aim rules: `e38dda363ddb6dd347bc89854d660712620569e9`, `9a667bc375b4d95184b8e31fa72958916567fff7`
- T10 bot integration: `6b21de19b71063d31a1501fd9d603b653ab291d8`, `538d32e2dfd0551fd2a244edab610ee98d60b722`
- T11 builder/validator integration: `b7acef794378480dd791a593ac4c4ec43c82f99f`, `f56c2d96f12334412f9d621e2dea54fdebec88d4`
- T12 generated outputs: `d9b28952209c751911e4a974a0957e13ee235fb6`
- accepted tests and bake disposition: `9a667bc375b4d95184b8e31fa72958916567fff7`, `b06fedd59a3a74779e1df8eb8d686f083ceb03ad`

## Latest Superseding Contracts

Later shadow/weapon-readability work changed overlapping explosion, builder, stage, scene, lighting, and generated-output paths. Current source and [`shadow-weapon-readability-workflow-handoff.md`](shadow-weapon-readability-workflow-handoff.md) supersede original T3/T11/T12 implementation details.

- preserve user-approved `VG4 PASS` lighting
- preserve current five-system explosion/radius cue and `BlastMathTests`
- preserve `SerializedContractVersion = 7`; manifest schema `8`
- preserve repository text-input newline normalization in stage digests
- preserve stale Lighting/BakedOutput records across non-lighting stage closure
- production atlas topology: current required maps `0..3`; map `4` removed intentionally
- optional baked variants: complete asset/`.meta` pair may be absent when current contract permits
- generated-output inventory: derive from current authoritative manifest and workflow payload; never copy old fixed list
- production bake expectation: derive from current probe; valid `reused` with zero bakes is accepted; never force bake to match old plan
- quick visual capture: `Tools/Validation/Capture-BrightArenaVisuals.ps1`; use only when later visual change invalidates accepted capture

## Orchestrator Routing

- request equals delivered requirements only -> report plan already completed; no worker dispatch, Unity run, generated write, or bake
- request adds tuning or new behavior -> create new plan from current launch `HEAD`; use current source contracts; reference this file as history only
- suspected regression -> create narrow regression plan; audit exact symptom; run only invalidated checks; fix only confirmed gap
- never use original T1-T12 ownership, bake-count, lightmap-count, or comparator assumptions for new execution

## Remaining Human Acceptance

- gameplay feel remains playtest-owned: one-shot shotgun feel, `11.7` blast reach, doubled-body crowding, bot combat frequency, corner release quality, aerial misses
- subjective tuning request -> new scoped plan; not blocker for completed implementation

## Historical Risks

- close shotgun theoretical damage: `108.8`
- doubled blast radius affects new targets plus falloff for previously affected targets
- doubled collision may expose crowding/traversal issues
- bot probability and geometry values may need playtest tuning
