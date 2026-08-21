# Shadow Weapon Readability Workflow Handoff

Status: implementation merged; workflow lessons pending reuse
Audience: orchestration, validation, harness, lighting agents
Source plan: `plans/shadow-and-weapon-readability-ai-coding-plan.md`
Source branch final: `df4f10dea3e15fda03382237331428cbd7f33f94`
Merged `gameplay` boundary: `f3f5989`
User visual gate: `VG4 PASS`

## Purpose

Prevent repeated waste from this implementation. Preserve working fixes. Avoid reopening resolved visual, capture, digest, manifest, or bake-topology failures.

## Waste Summary

- visual tuning started before deterministic capture path became reliable -> user performed repeated checks; agents lacked fast local feedback
- early tuning targeted ambient and shadow floor too broadly -> arena became washed out while walls stayed dark
- capture wrapper accumulated process redirection and timeout plumbing -> Windows PowerShell 5.1 returned blank `ExitCode`; multiple fix agents iterated on same failure
- repository source input hashed from raw worktree bytes -> LF/CRLF differences caused false stage staleness
- Development-generated lightmap state carried into Production attempts -> missing/recreated atlases caused GUID churn and retry loops
- non-lighting stage closure rewrote stale Lighting/BakedOutput records -> Production incorrectly reused stale bake with zero bakes
- baked-output guard assumed fixed presence for optional atlas variants -> stable missing outputs treated as destructive loss
- validator expected five atlases while Unity production bake emitted four -> valid bake rejected
- reviewer received invalid short SHA `8a49fcb7` -> manual commit resolution required

## Visual Iteration Failure

Observed sequence:

- initial wall problem interpreted as shadow crush
- weapon texture lift improved weapons
- global ambient lift reduced contrast and washed arena
- stronger key light improved some views, not dark goal-side walls
- baked neutral wall wash fixed broad wall visibility
- wider red/blue goal lights fixed goal-wall coverage
- user accepted final result at VG4

Better sequence:

`baseline capture -> identify receiver/light coverage -> one lighting-family change -> local High/Low capture -> agent visual check -> user gate`

Rules:

- capture before first subjective change
- diagnose material darkness, ambient fill, direct-light direction, and local-light coverage separately
- preserve dark shadows when issue comes from missing wall illumination
- avoid stacking ambient, sun strength, wall wash, and team accents in one tuning step
- ask user only after agent checks deterministic screenshots
- stop broad ambient changes once frame starts washing out

Final accepted lighting contract:

- neutral baked wall fill: three north spots plus three south spots
- neutral spot intensity: `900`
- neutral range: `32`
- outer angle: `120`
- inner angle: `105`
- positions: X `-43, 0, 43`; Z `-24` north / `24` south
- targets: wall Z `-44.5` north / `44.5` south; Y `4`
- neutral color: `(1, 0.82, 0.64)`
- neutral lights: baked, soft shadows, shadow strength `0.85`
- goal accents: Z `-22` red / `22` blue; X `-58` red / `58` blue; Y `5`
- goal intensity: `350`; range `24`; realtime point lights; no shadows
- Low quality: neutral baked wall visibility retained; realtime goal accents disabled

## Capture Wrapper Failure

Target command:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Validation/Capture-BrightArenaVisuals.ps1 -ProjectPath C:\wt\<id> -EvidenceRoot C:\wt\<id>e -AttemptId <unique>
```

Current output:

- three deterministic views at High
- same three views at Low
- `1920x1080` PNGs
- `CaptureResult.json`
- typical measured time: harness `~15s`; Unity `~21s`; total `~39s`

Failed implementation pattern:

- `Start-Process -PassThru` plus redirected stdout/stderr under Windows PowerShell 5.1
- timed `WaitForExit(90000)` followed by parameterless `WaitForExit()` and `Refresh()`
- process object still exposed blank/disposed `ExitCode`
- initial narrow fix changed wait sequence only -> full wrapper still failed before Unity

Working pattern:

- no harness stdout/stderr redirection
- `Start-Process -WindowStyle Hidden -PassThru`
- `WaitForExit(90000)`
- kill only returned harness child on timeout
- parameterless `WaitForExit()` plus `Refresh()` before integer `ExitCode` read
- full one-command proof required; isolated process snippet insufficient

Do not reintroduce redirect/replay plumbing without Windows PowerShell 5.1 end-to-end proof. `pwsh` success does not prove `powershell.exe` compatibility.

Efficiency rule: wrapper owns harness pre-gate. Caller must not run same harness separately before capture command. One capture attempt -> one harness run.

## Stage Digest Failure

Cause:

- new `MovementLabLightingPipeline.cs` stage edge used raw `HashFile()` worktree bytes
- `.cs` text checkout may use LF, CRLF, or mixed endings
- identical Git content produced different stage digests across worktrees
- false GameplayScene/BakedOutput staleness triggered unnecessary rebuild/bake risk

Resolved in `7e43242`:

- repository inputs -> `HashRepositoryInputFile()` / `HashRepositoryInputBytes()`
- text-like bytes -> newline normalization before hashing
- NUL-bearing binary inputs -> raw hashing
- generated output capture -> raw `HashFile()` unchanged
- self-check proves LF/CRLF equality plus content sensitivity

Rule: repository source/input digest and generated-output byte digest serve different contracts. Never reuse raw output hasher for repository text inputs.

## Bake And Manifest Failures

### Optional output deletion false positive

Development bake may omit complete lightmap asset/meta pairs for variants `2`, `3`, or `4`. Initial guard compared missing-before/missing-after as destructive deletion.

Resolved:

- `7cb9b63`: stable missing outputs no longer fail guard
- `1b17b6d`: optional baked variants accepted only as complete asset/meta pairs; Baked contract bumped

Required regression cases:

- optional pair missing before and after -> pass
- optional pair present then legitimately omitted by bake -> pass when contract allows transition
- asset missing but `.meta` present -> fail
- `.meta` missing but asset present -> fail
- required atlas missing -> fail

### Stale-record laundering

Non-lighting stage runner called `MarkCurrent()` across full probe. Lighting/BakedOutput remained stale, but persisted records became live current records. Production bake gate then emitted incorrect zero-bake reuse.

Resolved in `209b7d5`:

- non-lighting closure preserves stale Lighting/BakedOutput records
- only completed non-lighting ownership becomes current

Required proof after lighting input change:

`AssembleWithoutLighting -> probe still reports Lighting/BakedOutput stale -> ProductionPrepare performs exactly one bake`

Zero-bake reuse after known lighting change means digest or manifest bug. Never accept reuse from plan expectation alone.

### Atlas-count drift

Production bake emitted four atlases. Validator/catalog expected five. Valid production result failed until `e563ad5` aligned expected count to four and stopped requiring atlas `4`.

Rule:

- one owner for expected production atlas topology
- validator, generated-path inventory, pair checks, and comparator derive from same owner
- historical owned path may remain recognized for auditable paired deletion
- do not duplicate literal atlas count across validators

### Dirty Development outputs entering Production

Development attempts removed optional atlas outputs. Production started from dirty Development topology, recreated some maps, and caused GUID churn/retry work.

Safer production boundary:

`accepted source commit -> restore task-owned generated outputs to source-freeze bytes -> ProductionPrepare -> comparator -> generated commit -> separate ProductionValidate`

Never carry disposable Development lightmaps directly into milestone Production bake. Never restore unrelated user files. Tooling should own scoped generated reset if automated later.

## Orchestration Lessons

- quick capture work used several sequential fix agents for one Windows process bug
- first fixes passed parser or isolated process tests but failed real wrapper
- next owner had to reproduce same environment again

Improved dispatch:

- one owner gets exact `powershell.exe` version, command, timeout, evidence root, and success contract
- owner must run full one-command wrapper once before review
- review begins only after real `CaptureResult.json` plus six image dimension checks exist
- failed real run returns to same owner unless ownership conflict or agent unavailable
- fresh fix worker remains required for Critical/High review findings; avoid creating new worker for unreviewed self-discovered retry
- reviewer input uses exact resolvable SHA from `git rev-parse`, preferably full 40 characters
- reviewer checks only requested Critical/High scope; no duplicate review after unchanged patch

## Missing Regression Coverage

Current source contains fixes and some self-checks. Future harness work should cover failure chain without launching Unity where possible:

- repository digest: LF == CRLF; changed content differs; binary bytes remain raw
- optional baked output: paired missing accepted; half-pair rejected
- stale manifest: non-lighting closure cannot mark Lighting/BakedOutput current
- atlas contract: expected count, generated inventory, and validator remain synchronized
- capture process: Windows PowerShell 5.1 child exit `0`, nonzero, and timeout paths

Keep fixtures narrow. Avoid full production bake as harness regression. Use production workflow only for persisted Unity proof.

## Known-Good Evidence

- final generated boundary: `df4f10dea3e15fda03382237331428cbd7f33f94`
- comparator: coverage `20/20`; dangling `0`; GUID stable `20`; GUID churn `0`; pairs broken `0`; unsupported `0`
- ProductionPrepare: bake count `1`
- separate ProductionValidate: bake count `0`; changed generated paths empty
- final capture result: `C:\wt\swr-981d3ddde\vg4-production-final\CaptureResult.json`
- final capture: six independent `1920x1080` images
- final Git state after merge: clean `gameplay` at `f3f5989`

Evidence root may be removed later. Commit SHAs and source contracts remain authoritative.

## Next Workflow Changes

Priority order:

1. add non-Unity regression tests for stale-record preservation and optional-pair handling
2. update future graphics plans: deterministic capture setup before tuning loop
3. update production orchestration: clean generated source-freeze boundary before ProductionPrepare
4. retain minimal capture wrapper; reject extra log plumbing without measured need
5. measure harness cost before optimization; preserve required pre-gate until replacement proves equal protection

Do not reopen accepted VG4 lighting values during workflow remediation.
