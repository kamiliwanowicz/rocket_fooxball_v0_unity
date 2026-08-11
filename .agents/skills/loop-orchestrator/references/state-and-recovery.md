# State and Recovery

Every loop run uses one durable state document:

`<git-common-dir>/loop-orchestrator/<run-id>/state.md`

`run_id` is globally unique for repository. Never reuse another run directory, including missing/corrupt-state recovery. Product tree contains no orchestration state.

State contains current run facts only: bound plan worktrees and, for `multi-plan`, integration worktree. No repository-wide worktree inventory.

## Ownership and truth

LP is sole state writer. Breakdown, planner, execution orchestrator, workers, reviewers, fixes, and merging agent read state and return facts for LP recording. Concurrent state edits are invalid.

Truth priority:

`observed Git + live-agent facts -> attempt-bound plan snapshot bytes -> state claims -> agent prose`

State routes work; it never overrides Git ancestry, HEAD, branch/worktree identity, clean status, path diff, live ownership, or authoritative snapshot digest.

## Atomic write

Before every dispatch and after every accepted result, LP:

1. Build complete next state bytes in unique temporary file inside run directory.
2. Flush and close temporary file.
3. Windows existing state -> `[System.IO.File]::Replace($tempPath, $statePath, $backupPath)` with real unique backup path. Never pass `$null` backup.
4. Windows missing state -> `[System.IO.File]::Move($tempPath, $statePath)`.
5. Keep backup until verification passes.
6. Reopen `state.md`; verify readable run ID and intended phase/status before continuing.

State field change -> build complete next document. Targeted text patch must match section plus exact field key exactly once. Zero or multiple matches -> stop. After replacement, verify intended field, expected phase, and unchanged neighboring identifiers.

Partial write, rename failure, or verification mismatch -> no dispatch. Preserve old readable state and return blocker evidence. Never let another agent repair state.

## Required state shape

Use compact Markdown headings/list fields; no YAML ledger or event history.

```markdown
# Loop Run State

Run ID: [unique run_id]
Objective: [bounded outcome]
Phase: INIT | BREAKDOWN | PLANNING | EXECUTION | MERGING | READY_FOR_USER_MERGE | BLOCKED
Baseline SHA: [accepted full SHA]
Authority: [allowed operations; user target branch/candidate approval or None]
Question: [material active question or None]
Blocker: [active blocker + evidence + recheck/action or None]

## Requirements
- REQ-[stable ID]: [requirement] -> [plan_id or unassigned] -> [pending | covered | accepted]

## Breakdown
- attempt_id: [current/latest]
- status: pending | ready | awaiting_user | blocked
- decision: single_plan | multi_sequential | multi_parallel | hybrid | None
- baseline: [full SHA]
- result evidence: [accepted result location/identity or None]
- integration order: [plan_id list or None]

## Plans
### [plan_id]
- status: pending | planning | awaiting_user | planned | executing | done | blocked | merged
- covered requirements: [REQ-*]
- attempt_id: [current/latest]
- baseline: [full SHA]
- execution start SHA: [full SHA or None]
- dependencies: [plan IDs + accepted SHAs or None]
- owned paths: [exact paths]
- protected paths: [exact paths]
- `read_paths`: [exact paths]
- `validation_environment`: [bounded environment and lease]
- `unity_mutation`: true | false
- `expensive_proof_owner`: [identity or None]
- `expensive_proof_run_point`: [boundary or None]
- `proof_invalidation_paths`: [exact paths]
- source artifact: [absolute path or None]
- source artifact sha256: [lowercase digest or None]
- source artifact bytes: [integer or None]
- execution snapshot: [absolute path or None]
- execution snapshot sha256: [lowercase digest or None]
- execution snapshot bytes: [integer or None]
- branch: [exact name or None]
- worktree: [absolute path or None]
- accepted execution SHA: [full SHA or None]
- merge wave/status: [wave + pending | merged | blocked]
- accepted integration SHA: [full SHA or None]
- checks: [check -> result/evidence/SHA or pending]
- executed ledger: [absolute `check-ledger.json` path or None]
- executed ledger sha256: [lowercase digest or None]
- question: [one question or None]
- blocker: [evidence + needed action/recheck or None]

## Integration
- branch: [isolated branch or None]
- worktree: [absolute path or None]
- last accepted integration SHA: [full SHA; initial accepted baseline until first accepted merge]
- expected pre-merge head: [full SHA or None]
- accepted input SHAs: [ordered list or None]
- merged input SHAs: [ordered list or None]
- retry baseline SHA: [full SHA or None]
- retry input SHAs: [ordered list or None]
- drift: [expected/observed full SHAs + rejected | pending_acceptance | accepted + evidence/authority or None]
- merge status: pending | active | blocked | complete
- final SHA: [full SHA or None]
- checks: [check -> result/evidence/SHA or pending]
- clean: true | false | unknown
```

## Executed Ledger Pointer

Harness-owned `check-ledger.json` is sole executed ledger. State stores only absolute ledger path plus SHA-256 in plan fields above; LP never copies or rewrites rows. Rehash recorded ledger before resume or merge; digest mismatch -> `blocked`. Consumers read `production-final` rows and evidence only after digest verification. Full row contract stays in producer/consumer policy.

## Production Bake Gate

- Before each bake-capable Unity invocation, rehash every bound run `workflow-result.json` and sum `bakeCount`. Cumulative `>=2` -> `blocked` before Unity. Retain postflight cumulative `>2` only as evidence-corruption/contract-violation detector; observed cumulative must never exceed `2`.
- Replacement `RocketFooxball.Editor.MovementLabBuilder.BakeMovementLabLighting` after any prior production-final attempt requires explicit user authority recorded before dispatch.
- Lighting-input intersection invalidates production-final proof. Missing authority -> `blocked` before Unity; never force rerun. With authority, builder owns skip/rebuild; exact current-lighting skip marker or one bake proves outcome.

Stable requirement IDs and `plan_id` values never change within run. Every dispatch receives fresh unique `attempt_id`; replaced/user-resumed/blocker-resumed attempt never reuses ID.

## Plan status transitions

Normal:

`pending -> planning -> planned -> executing -> done -> merged`

User wait:

`planning -> awaiting_user -> planning (fresh attempt_id)`

Blocked:

`planning | executing | done -> blocked -> blocked while fact unresolved -> prior active stage (fresh attempt_id after observed recheck)`

Pre-bind source digest mismatch:

`planned -> blocked`; preserve accepted artifact metadata, record observed digest/size, mutate no product worktree, and start fresh planning attempt only after LP selects new reserved artifact path.

Post-bind snapshot digest mismatch:

`executing | done -> blocked`; preserve source provenance, record observed snapshot digest/size, stop mutation, and retry through fresh execution attempt plus fresh snapshot. Source artifact drift after snapshot binding is outside run gates and causes no transition.

Merge:

`done -> merged` only after merging agent result matches observed integration Git facts. Merge blocker keeps plan `done` when plan output remains accepted; record integration `blocked`. Plan status `blocked` applies only when plan artifact/execution acceptance itself fails.

`single_plan` route -> `done -> READY_FOR_USER_MERGE` after execution identity, scope, checks, and clean worktree pass. Skip merging agent; accepted execution SHA is final integration SHA.

Route handoff:

- `single_plan` -> clean plan worktree; record accepted execution SHA as final integration SHA; create no integration worktree or merger result.
- `multi-plan` -> clean integration worktree; merge each accepted execution SHA exactly once; record final integration SHA.

`needs_user` always maps to `awaiting_user`, never `blocked`. User response creates fresh role attempt. Blocker resolution requires observable recheck before fresh attempt.

## Dispatch and acceptance writes

Before dispatch, record phase, attempt identity, role/profile, plan status, immutable execution start SHA, branch/worktree when applicable, expected head, dependencies, authority, pending checks, and current `check-ledger.json` pointer/digest when present.

After result, stop role when required; verify result against live identity, Git/artifact facts, scope, and checks; then atomically record accepted status/facts. Rejected/late result does not advance state.

Planner acceptance:

1. Stop planner.
2. Verify reserved artifact exists and was create-once.
3. Compute SHA-256 and byte size.
4. Record source artifact path/digest/size and status `planned` atomically.
5. Read source once into create-once execution snapshot. Reopen snapshot and compare accepted digest/size; mismatch follows pre-bind source-digest transition.
6. Record matching snapshot path/digest/size before execution dispatch.

Merge acceptance records expected/observed pre-merge head, ordered accepted inputs, merged inputs, final SHA, checks, and clean status.

## Target-drift recovery

Unexpected integration HEAD has no acceptance. Current attempt -> `blocked`; record expected/observed full SHAs and last completed input.

Default retry:

1. Bind `retry baseline SHA` to last recorded accepted integration SHA before drift. Before first accepted merge, use run's accepted baseline SHA.
2. Derive `retry input SHAs` from accepted execution SHAs not already recorded merged at retry baseline. Preserve declared order.
3. Provision fresh isolated integration branch/worktree at exact retry baseline.
4. Dispatch fresh attempt with fresh `attempt_id`; replay retry inputs. Drift SHA remains outside retry ancestry.

Retain drift SHA only when LP records all gate evidence before retry:

- exact observed full SHA and ancestry from last accepted integration SHA;
- exact changed-path and content scope from last accepted integration SHA;
- independent Critical/High review and dispositions bound to drift SHA;
- required checks passing at drift SHA;
- authority acceptance binding drift SHA and scope. Obtain explicit user authority for any scope, behavior, permission, or mutation outside authority already recorded in state.

Complete gate -> record drift `accepted`, promote exact drift SHA to last accepted integration SHA/retry baseline, and derive remaining inputs from verified ancestry plus prior accepted merge records. Incomplete gate -> record drift `rejected`; use default retry. Observed ancestry alone never accepts drift.

## Resume

1. Locate intended unique run directory from current context/user input. Never choose another run by similarity.
2. Parse full state. Validate readable structure, matching `run_id`, stable IDs, phase/status values, and required fields.
3. Rehash source artifact only for plans before execution snapshot binding. Rehash bound snapshot for `executing`, `done`, and `merged` plans. Rehash each recorded `check-ledger.json` and compare state digest before resume or merge. Source drift after binding is ignored.
4. Inspect each exact branch/worktree recorded for current run: existence, branch binding, `HEAD` descent from `start_sha`, `start_sha..HEAD` path scope, clean status, and operation state. Source-branch ref remains outside execution recovery.
5. Inspect live agents: identity, status, current assignment, writer ownership.
6. Replace stale state claims with verified facts through atomic write. Preserve reachable accepted commits.
7. Resume first incomplete mandatory stage. Never repeat completed work whose artifact/SHA/check facts remain valid.

Missing or corrupt state:

- stop new dispatches and writers whose ownership is uncertain;
- inspect Git common run directory, exact run-provisioned branches/worktrees proven by run evidence, reachable commits, artifacts, and live agents;
- recover only facts supported by Git/live observations and artifact hashes;
- write repaired state for same run only when run identity is independently proven;
- otherwise create new unique `run_id` and directory, link recovered accepted SHAs/artifacts as explicit inputs, never reuse corrupt directory.

Authoritative artifact mismatch blocks execution: source before snapshot binding; snapshot after binding. Dirty/moving worktree blocks acceptance. Git/live facts override stale state.

## Recovery scenarios

- one plan (`single_plan` route): `pending -> planning -> planned -> executing -> done -> READY_FOR_USER_MERGE`; skip BREAKDOWN and MERGING agents; keep plan worktree clean; accepted execution SHA serves as final integration SHA; no integration worktree or merge.
- parallel: disjoint plans share wave; each reaches `done`; merger consumes breakdown order; each becomes `merged` at one observed integration SHA.
- sequential: prerequisite becomes `merged`; recorded integration SHA becomes dependent planner baseline; dependent planning starts afterward.
- user wait: role returns `needs_user`; status `awaiting_user`; state holds one question; response creates fresh attempt and returns to role stage.
- blocker: role returns `blocked`; status remains blocked across resume until named fact recheck passes; fresh attempt follows.
- source digest mismatch before binding: status `blocked`; no execution dispatch/product mutation; fresh planner artifact path required.
- snapshot digest mismatch after binding: status `blocked`; fresh execution attempt and snapshot required; source drift ignored.
- source-branch drift after worktree creation: no transition; use bound `start_sha..plan_head` comparison.
- target drift: integration status `blocked`; record expected/observed full SHAs; default retry starts from last recorded accepted integration SHA and replays remaining accepted inputs; gated drift retention requires recorded evidence/authority; user branch unchanged.

## Cleanup and completion

Stop/verify writers before cleanup. Remove current run's temporary worktrees/branches only after accepted SHAs remain reachable, state/evidence remains readable, and no live writer can mutate accepted work. Preserve ambiguous artifacts until disposition recorded.

Run complete when state and observed facts agree on `READY_FOR_USER_MERGE`, every requirement accepted, every plan merged or accepted through `single_plan`, required `check-ledger.json` pointers/digests match, final checks bind final SHA, and authority boundary explicit. Otherwise record exact blocker and one needed action/recheck.
