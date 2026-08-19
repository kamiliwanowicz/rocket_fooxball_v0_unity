# State and Recovery

Every loop run uses one durable state document:

`<git-common-dir>/loop-orchestrator/<run-id>/state.md`

`run_id` is globally unique for repository. Never reuse another run directory, including missing/corrupt-state recovery. Product tree contains no orchestration state.

State contains current run facts only: bound plan worktrees and, for `multi-plan`, integration worktree. No repository-wide worktree inventory.

## Ownership and truth

LP is sole state writer. Breakdown, planner, execution orchestrator, workers, reviewers, fixes, and merging agent read state and return facts for LP recording. Concurrent state edits are invalid.

Truth priority:

`observed Git + live-agent facts -> attempt-bound artifact at create-once reserved path -> state claims -> agent prose`

State routes work; it never overrides Git ancestry, HEAD, branch/worktree identity, clean status, path diff, live ownership, or authoritative plan artifact at create-once reserved path.

## Atomic write

Before every dispatch and after every accepted result, LP writes state.

Field update -> default scripted targeted patch into unique temporary file. Match target section + exact field key once. Zero/multiple matches -> stop. Full rebuild -> structural or phase change only.

1. Flush and close temporary file.
2. Windows existing state -> `[System.IO.File]::Replace($tempPath, $statePath, $backupPath)` with real unique backup path. Never pass `$null` backup.
3. Windows missing state -> `[System.IO.File]::Move($tempPath, $statePath)`.
4. Keep backup until verification passes.
5. Fully reopen `state.md`; verify readable full state, run ID, intended field/phase/status, unchanged neighboring identifiers.

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

## Rule Set
- active manifest sha256: [lowercase digest]
- active sources: [exact rule source paths + lowercase SHA-256]
- pending manifest sha256: [lowercase digest or None]
- pending sources: [exact rule source paths + lowercase SHA-256 or None]
- reconciliations: [checkpoint ID -> old/new digest -> compatible | blocked -> affected rules/tasks/checks -> evidence; or None]

## Requirements
- REQ-[stable ID]: [requirement] -> [plan_id or unassigned] -> [pending | covered | accepted]

## Breakdown
- attempt_id: [current/latest]
- status: pending | ready | awaiting_user | blocked
- decision: single_plan | multi_sequential | multi_parallel | hybrid | None
- baseline: [full SHA]
- result artifact: [absolute reserved path or None]
- comments: [returned material caveats or None]
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
- plan artifact: [absolute path or None]
- branch: [exact name or None]
- worktree: [absolute path or None]
- accepted execution SHA: [full SHA or None]
- merge wave/status: [wave + pending | merged | blocked]
- accepted integration SHA: [full SHA or None]
- checks: [pending | blocked check IDs]
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
- checks: [pending | blocked check IDs]
- executed ledger: [absolute `check-ledger.json` path or None]
- executed ledger sha256: [lowercase digest or None]
- clean: true | false | unknown

## Cleanup
- status: pending | complete | blocked | not_eligible
- removed worktrees: [exact absolute run paths or None]
- removed aliases: [exact `C:\wt` reparse-point paths or None]
- prune evidence: [dry-run and completion evidence or None]
- blocker: [evidence + needed action/recheck or None]
```

## Executed Ledger Pointer

Workflow-owned `check-ledger.json` is sole executed ledger. Harness writes `harness-summary.json`. Plan/integration `checks` store pending/blocked IDs only. Results, evidence, and SHAs -> absolute ledger pointer + lowercase SHA-256. LP never copies or rewrites rows. Rehash recorded ledger before resume or merge; digest mismatch -> `blocked`. Consumers read `production-final` rows/evidence after digest verification. Full row contract stays in producer/consumer policy.

## Workflow Harness Precondition

Harness pre-gate -> [`AGENTS.md`](../../../../AGENTS.md) `Unity execution` command, exit code, runtime limit, process, lock requirements. Run before every workflow/Unity invocation; applies `-PlanOnly` + read-only validation. Nonzero, timeout, Unity process, or project lock -> `blocked`.

Harness stale after edits under `Tools/Tests/**`, `Tools/Validation/*.ps1`, or `Assets/_Game/Editor/MovementLab/*.cs`. Repair/rerun green before next workflow/Unity invocation.

Workflow command only:

`Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode <...> -ProjectPath <...>`

Apply `-PlanOnly`, `-LedgerPath`, `-EvidenceRoot` when applicable. Carry prior accepted `-LedgerPath` across retries/dependencies. Never pass workflow arguments to test runner.

Production-final order: zero writers -> clean exact SHA -> lease -> accepted reviews/fixes -> `ProductionPrepare` -> `ProductionValidate` semantic pass.

## Production Bake Gate

- Budget: rehash bound `workflow-result.json` where `mode == 'ProductionPrepare'`; sum `bakeCount`. Cumulative `>=2` -> `blocked` before `ProductionPrepare`. Postflight `>2` -> evidence-corruption/contract violation; observed total never exceeds `2`.
- Marker/skip/input semantics: [`AGENTS.md`](../../../../AGENTS.md) `Unity execution` production bake gate sole owner. Lighting-input intersection invalidates production-final proof.
- Replacement bake: current lighting-input digest changed after prior production-final attempt -> predicted real rebuild; explicit user authority required before dispatch. Missing authority -> `blocked`; never force rerun. With authority, `RocketFooxball.Editor.MovementLabBuilder.BakeMovementLabLighting` owns skip/rebuild.

Stable requirement IDs and `plan_id` values never change within run. Every dispatch receives fresh unique `attempt_id`; replaced/user-resumed/blocker-resumed attempt never reuses ID.

## Plan status transitions

Normal:

`pending -> planning -> planned -> executing -> done -> merged`

User wait:

`planning -> awaiting_user -> planning (fresh attempt_id)`

Blocked:

`planning | executing | done -> blocked -> blocked while fact unresolved -> prior active stage (fresh attempt_id after observed recheck)`

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

Breakdown acceptance:

1. Stop breakdown. Verify returned artifact path equals reserved path and exists create-once.
2. Record breakdown artifact path, comments, decision, and `ready` atomically. State stores no candidate bodies.
3. Read artifact sections on demand for `plan_id` assignment and planner dispatch binding.

Planner acceptance:

1. Stop planner. Verify reserved artifact exists + create-once.
2. Record plan artifact path + `planned` atomically.
3. Bind that path as sole plan authority for execution dispatch. Never read artifact bytes through agent.

Merge acceptance records expected/observed pre-merge head, ordered accepted inputs, merged inputs, final SHA, checks, and clean status.

## Rule hot-swap

Rule manifest: exact source paths + SHA-256 for instructions, skills, profiles, templates, repository rules used by bound plan/execution dispatch. LP records active manifest before planner/execution dispatch. Plan artifact remains immutable across rule revision.

Hot-swap gate: accepted review checkpoint; covered children returned terminal; writer barrier closed; frozen checkpoint SHA clean; no active child. LP detects revision -> records pending manifest -> same live execution orchestrator rereads changed sources + bound plan -> LP records reconciliation. No orchestrator replacement solely for rule revision.

Reconciliation: changed sources, old/new manifests, checkpoint, affected task/check/profile/ownership/check rules, accepted-checkpoint invalidation, evidence, `compatible | blocked` verdict. `compatible` -> plan satisfies new rules; no authority/product-scope expansion; recheck every invalidated accepted checkpoint before next writer. LP promotes pending manifest atomically; same execution orchestrator continues. `blocked` -> exact conflict + fresh planner action; no plan rewrite, mixed-rule checkpoint, or new dispatch.

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
3. Verify recorded breakdown artifact exists when breakdown status is `ready`, and bound plan artifact exists for every plan at or past `planned`. Rehash each recorded `check-ledger.json` and compare state digest before resume or merge. Breakdown artifact missing -> breakdown `blocked`; fresh breakdown attempt at new reserved path; already-accepted plans keep their bound plan artifacts.
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

Missing bound plan artifact blocks execution; artifact at create-once reserved path is sole authority. Dirty/moving worktree blocks acceptance. Git/live facts override stale state.

## Recovery scenarios

- one plan (`single_plan` route): `pending -> planning -> planned -> executing -> done -> READY_FOR_USER_MERGE`; skip BREAKDOWN and MERGING agents; keep plan worktree clean; accepted execution SHA serves as final integration SHA; no integration worktree or merge.
- parallel: disjoint plans share wave; each reaches `done`; merger consumes breakdown order; each becomes `merged` at one observed integration SHA.
- sequential: prerequisite becomes `merged`; recorded integration SHA becomes dependent planner baseline; dependent planning starts afterward.
- user wait: role returns `needs_user`; status `awaiting_user`; state holds one question; response creates fresh attempt and returns to role stage.
- blocker: role returns `blocked`; status remains blocked across resume until named fact recheck passes; fresh attempt follows.
- source-branch drift after worktree creation: no transition; use bound `start_sha..plan_head` comparison.
- target drift: integration status `blocked`; record expected/observed full SHAs; default retry starts from last recorded accepted integration SHA and replays remaining accepted inputs; gated drift retention requires recorded evidence/authority; user branch unchanged.

## Cleanup and completion

`READY_FOR_USER_MERGE` is handoff, not cleanup eligibility. Automatic cleanup only after all gates pass:

- state: phase `READY_FOR_USER_MERGE`; final handoff facts, state, accepted artifacts, and ledger pointers/digests readable.
- merge: recorded target branch from explicit authority exists; observed target ref contains exact recorded final SHA (`git merge-base --is-ancestor <final-sha> <target-ref>` succeeds). Authority request, candidate availability, or target observation alone fail gate.
- quiescence: no live run agent/writer; every bound run worktree exact-path registered, branch-bound, clean, and no operation/lock remains.
- scope: candidate paths are only exact plan worktrees plus exact integration worktree recorded for this run. Never discover by name, prefix, glob, or repository-wide inventory.

Gate failure -> `not_eligible` for unmerged handoff; otherwise `blocked` with evidence. Preserve worktrees, private `Library/`, aliases, branches, and durable evidence. Never retry cleanup by deleting paths directly.

Eligible cleanup:

1. Reverify each exact recorded worktree immediately before removal. Run `git worktree remove <exact-path>` without `--force`; removal failure -> `blocked`, stop. Never delete branches, primary checkout, Git-common evidence, or any unrecorded worktree.
2. Run `git worktree prune --dry-run`. Continue with `git worktree prune` only when every reported stale registration is proven to belong to an exact removed current-run worktree; unrelated or unprovable registration -> `blocked`, no prune. Requery `git worktree list --porcelain`; removed paths must be absent.
3. Remove only exact recorded `C:\wt` aliases after worktree cleanup: path remains under `C:\wt`; item is a junction/symlink reparse point; recorded target matches current-run removed worktree or durable evidence root. Before unlink, preserve every alias referenced by retained `state.md`, `check-ledger.json`, `workflow-result.json`, or evidence pointer. No pointer canonicalization/migration. Unlink only unreferenced reparse points; no recursive delete. Regular directory, changed/missing target, or unrecorded alias -> preserve and record blocker.
4. Atomically record exact removed worktrees/aliases and prune evidence as `complete`. Retain run state, artifacts, ledgers, and Git-common durable evidence for recovery/audit.

Run complete when state and observed facts agree on `READY_FOR_USER_MERGE`, every requirement accepted, every plan merged or accepted through `single_plan`, required `check-ledger.json` pointers/digests match, final checks bind final SHA, and authority boundary explicit. Otherwise record exact blocker and one needed action/recheck.
