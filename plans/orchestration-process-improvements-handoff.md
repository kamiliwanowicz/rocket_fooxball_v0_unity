# Orchestration Process Improvements Handoff

Status: proposed
Source run: `weapon-lighting-textures`, attempt `userdirect-20260825-d42f81a3`
Integrated result: `textures` at `fcec0878769884ca6f03edeb28fddfab36e0faa9`
Audience: planning/orchestration agent

## Goal

Reduce Unity wait time, recovery ambiguity, scope pauses, duplicate visual proof. Preserve exact-SHA evidence, generated-output integrity, independent review, production-bake authority.

## Current State

- production result integrated and validated
- `.agents/skills/orchestrate-implementation/SKILL.md` -> bounded ownership repair added, validation passed, change remains uncommitted
- production pre-bake validator fixes landed in `fcec087`: ProjectSettings outputs no fake `.meta`; launcher core uses `LauncherEmissionTexturePath`
- observed gates: 167 EditMode tests passed; production bake count 1; Fast/Persisted visual verdicts 16/16

## Priority Changes

### P0: Production preflight before user bake approval

Problem: Fast path skipped production-only pre-bake checks. Two validator bugs surfaced only after `USER_FAST_ACCEPT` and `ProductionPrepare` launch.

Change:

- add non-baking `ProductionPreflight` workflow mode
- run same persisted non-lighting and pre-bake semantic checks used by `BakeMovementLabLighting`
- forbid bake method, asset save, generated mutation
- run before Fast images reach user acceptance gate

Targets:

- `Assets/_Game/Editor/MovementLabBuilder.cs`
- `Assets/_Game/Editor/MovementLab/MovementLabPreBakeGate.cs`
- `Tools/Validation/Invoke-MovementLabWorkflow.ps1`
- `Tools/Tests/MovementLabHarness.Tests.ps1`
- `Tools/Tests/Fixtures/red-workflow.ps1.txt`

Acceptance:

- preflight catches missing ProjectSettings/Assets metadata distinction
- preflight catches wrong launcher/shotgun emission-map routing
- `bakeCount=0`
- Git product/index hashes unchanged
- separate Unity process and release proof pass

### P0: Failure result emitted on every workflow exit

Problem: failed `ProductionPrepare` wrote Unity log and lease proof but no `workflow-result.json`. Bake budget and retry safety required manual log inspection.

Change:

- write schema-valid failure result in outer `finally`
- preserve root exception
- fields: `status`, `mode`, `exactSha`, `failedPhase`, `bakeStarted`, `bakeCount`, `productMutation`, `safeToRetry`, `resultDigest`, `lockReleaseProof`
- `safeToRetry=true` only when bake never started, product/index unchanged, release proof clean
- failed evidence unique and immutable

Targets:

- `Tools/Validation/Invoke-MovementLabWorkflow.ps1`
- `Tools/Tests/MovementLabHarness.Tests.ps1`
- `Tools/Tests/Fixtures/red-workflow.ps1.txt`
- `.agents/skills/loop-orchestrator/references/state-and-recovery.md`

Acceptance:

- pre-bake failure -> result says `bakeStarted=false`, `bakeCount=0`, `safeToRetry=true`
- mid-bake failure -> fail closed, `safeToRetry=false`
- root error text retained
- bake-budget parser consumes success and failure results without log scraping

### P1: Validation-only visual re-attestation

Problem: three-line validator fix changed source SHA but not product content. Exact-SHA rules forced Fast workflow, 12 captures, replacement visual verifier, verdict validation.

Change:

- classify fix as `validation-only` through exact owned-path/symbol contract
- require accepted code review
- bind prior visual evidence to new SHA through re-attestation JSON
- require unchanged product-content digest, generated-manifest bytes, reference digest, material/prefab/scene hashes
- any visual/runtime/builder-authoring input change -> full recapture

Targets:

- `.agents/skills/orchestrate-implementation/SKILL.md`
- `.agents/skills/loop-orchestrator/references/state-and-recovery.md`
- `Tools/Validation/Capture-WeaponVisuals.ps1` or new narrow attestation validator
- `Tools/Tests/MovementLabHarness.Tests.ps1`

Acceptance:

- validator-only fix + identical product hashes -> prior verdict re-attested, no Unity render
- source path outside approved validation set -> rejection
- changed product/generated/reference hash -> rejection and full capture
- attestation records old/new SHA, reviewed diff, bound hashes, reason

### P1: Reviewer verdict lifecycle watchdog

Problem: visual verifier wrote complete verdict but remained running. Orchestrator waited, interrupted, discarded result, launched replacement.

Change:

- watch expected verdict path while child active
- valid mechanically parsed verdict appearance -> 30-second terminal-return grace
- no return -> interrupt, mark lifecycle failure, launch fresh verifier
- never accept non-terminal child result
- verifier prompt: write verdict -> return final immediately; no post-write analysis

Targets:

- `.agents/skills/orchestrate-implementation/SKILL.md`
- verifier dispatch template/reference

Acceptance:

- hung-after-write child replaced within bounded time
- zero concurrent verifier ownership of same verdict path
- late/interrupted result rejected
- replacement uses unique evidence path and agent ID

### P1: Planner ownership closure from invoked gates

Problem: `MovementLabPreBakeGate.cs` controlled T5 but lived outside task ownership and inside protected MovementLab source.

Change:

- planner traces workflow entrypoints -> invoked builder/gate owners
- every mutating or blocking validator source becomes owned, read-bound, or named amendment candidate
- unresolved cross-domain owner blocks plan acceptance, not late execution
- retain bounded ownership repair for small local omissions

Targets:

- `.agents/skills/write-orchestrator-coding-plan/SKILL.md`
- `.agents/skills/orchestrate-implementation/SKILL.md`

Acceptance:

- plan linter flags invoked gate absent from `owns`, `read_paths`, and `protected` rationale
- production task lists `MovementLabPreBakeGate.cs` contract explicitly
- small omission still repairable through recorded scope amendment

### P1: Exact validator diagnostics

Problem: `MovementLab emissive material failed clean-reload public-state validation` hid failing field. Diagnosis required source/YAML comparison.

Change:

- report expected/actual emission-map asset path or GUID
- report flags, `_EMISSION`, color, strength separately
- one accumulator row per predicate; no first-field ambiguity
- avoid sensitive or large data dumps

Targets:

- `Assets/_Game/Editor/MovementLab/MovementLabPreBakeGate.cs`
- relevant validator tests/harness guards

Acceptance:

- wrong launcher map error names actual and expected paths
- flags/color/keyword failures identify exact field
- accumulator preserves every independent violation

WARNING - potential instructions issue

Instruction: full harness before every workflow or Unity invocation.

Observed impact: repeated 26-61 second harness runs at same guarded-file digest. Captures already launch internal harness, causing large fixed overhead.

Proposed change:

- cache harness pass by guarded-file Git blob digest, harness version, PowerShell version, exact project path
- reuse only within same orchestration attempt and clean exact SHA
- always rerun lightweight Unity-process, project-lock, evidence-path, index checks
- edit under `Tools/Tests/**`, `Tools/Validation/*.ps1`, or `Assets/_Game/Editor/MovementLab/*.cs` invalidates cache immediately
- production mutation may require fresh uncached harness regardless of digest

Acceptance:

- unchanged guarded digest -> cache hit recorded with source evidence hash
- any guarded edit -> full harness
- active Unity/process lock -> failure even on cache hit
- red fixtures still execute on full run and remain red

Additional observed issues:

- Blender evidence under project `Temp` -> Unity later cleared files. CP7 independent review remains accepted. Future Blender workflows -> copy previews to durable evidence alias.
- Non-Unity harness launched through PowerShell 7 `Start-Process` -> polluted `PSModulePath` -> false `Get-FileHash` failure. Run harnesses directly. Reserve waited `Start-Process` for Unity.
- Failed attempt used `C:\wt\pvf-349bae`, not assigned evidence alias. Workflow-probed evidence remains usable. Future prompts -> validate exact alias before execution.

## Recommended Order

`ProductionPreflight` -> failure JSON -> exact diagnostics -> ownership linter -> reviewer watchdog -> validation-only re-attestation -> harness cache

Reason: first three remove production ambiguity. Next two remove orchestration stalls. Last two optimize time after correctness proof stabilizes.

## Safety Boundaries

- no weaker GUID/meta, process/lease, atomic-write, source/input-digest, evidence-integrity gates
- no generated-output hash as rebuild/staleness predicate
- no cached production bake decision
- no visual re-attestation after lighting, material, texture, prefab, scene, quality, camera, capture-tool, or runtime presentation change
- no acceptance of verifier evidence without terminal child result and mechanical verdict validation

## Suggested First Task

Implement `ProductionPreflight` plus guaranteed failure result together. Shared workflow phases and tests. Run harness, compile, failing preflight fixture, passing real preflight. Do not run production bake.
