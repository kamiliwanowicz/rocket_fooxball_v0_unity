# State and Recovery

Every loop run uses one durable state document:

`<git-common-dir>/loop-orchestrator/<run-id>/state.md`

`run_id` is globally unique for repository. Never reuse another run directory, including missing/corrupt-state recovery. Product tree contains no orchestration state.

## Ownership and truth

LP is sole state writer. Breakdown, planner, execution orchestrator, workers, reviewers, fixes, and merging agent read state and return facts for LP recording. Concurrent state edits are invalid.

Truth priority:

`observed Git + live-agent facts -> accepted immutable artifact bytes -> state claims -> agent prose`

State routes work; it never overrides Git ancestry, HEAD, branch/worktree identity, clean status, path diff, live ownership, or artifact digest.

## Atomic write

Before every dispatch and after every accepted result, LP:

1. Build complete next state bytes in unique temporary file inside run directory.
2. Flush and close temporary file.
3. Atomically replace existing `state.md`; first write atomically renames temporary file to `state.md`.
4. Reopen `state.md`; verify readable run ID and intended phase/status before continuing.

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
- dependencies: [plan IDs + accepted SHAs or None]
- owned paths: [exact paths]
- protected paths: [exact paths]
- artifact: [absolute path or None]
- artifact sha256: [lowercase digest or None]
- artifact bytes: [integer or None]
- branch: [exact name or None]
- worktree: [absolute path or None]
- accepted execution SHA: [full SHA or None]
- merge wave/status: [wave + pending | merged | blocked]
- accepted integration SHA: [full SHA or None]
- checks: [check -> result/evidence/SHA or pending]
- question: [one question or None]
- blocker: [evidence + needed action/recheck or None]

## Integration
- branch: [isolated branch or None]
- worktree: [absolute path or None]
- expected pre-merge head: [full SHA or None]
- accepted input SHAs: [ordered list or None]
- merged input SHAs: [ordered list or None]
- merge status: pending | active | blocked | complete
- final SHA: [full SHA or None]
- checks: [check -> result/evidence/SHA or pending]
- clean: true | false | unknown
```

Stable requirement IDs and `plan_id` values never change within run. Every dispatch receives fresh unique `attempt_id`; replaced/user-resumed/blocker-resumed attempt never reuses ID.

## Plan status transitions

Normal:

`pending -> planning -> planned -> executing -> done -> merged`

User wait:

`planning -> awaiting_user -> planning (fresh attempt_id)`

Blocked:

`planning | executing | done -> blocked -> blocked while fact unresolved -> prior active stage (fresh attempt_id after observed recheck)`

Digest mismatch:

`planned -> blocked`; preserve accepted artifact metadata, record observed digest/size, mutate no product worktree, and start fresh planning attempt only after LP selects new reserved artifact path.

Merge:

`done -> merged` only after merging agent result matches observed integration Git facts. Merge blocker keeps plan `done` when plan output remains accepted; record integration `blocked`. Plan status `blocked` applies only when plan artifact/execution acceptance itself fails.

`needs_user` always maps to `awaiting_user`, never `blocked`. User response creates fresh role attempt. Blocker resolution requires observable recheck before fresh attempt.

## Dispatch and acceptance writes

Before dispatch, record phase, attempt identity, role/profile, plan status, branch/worktree when applicable, expected head, dependencies, authority, and pending checks.

After result, stop role when required; verify result against live identity, Git/artifact facts, scope, and checks; then atomically record accepted status/facts. Rejected/late result does not advance state.

Planner acceptance:

1. Stop planner.
2. Verify reserved artifact exists and was create-once.
3. Compute SHA-256 and byte size.
4. Record artifact path/digest/size and status `planned` atomically.
5. Rehash immediately before execution dispatch. Mismatch follows digest-mismatch transition.

Merge acceptance records expected/observed pre-merge head, ordered accepted inputs, merged inputs, final SHA, checks, and clean status.

## Resume

1. Locate intended unique run directory from current context/user input. Never choose another run by similarity.
2. Parse full state. Validate readable structure, matching `run_id`, stable IDs, phase/status values, and required fields.
3. Rehash every accepted artifact; compare digest/size.
4. Inspect each recorded branch/worktree: existence, branch binding, HEAD, ancestry, clean status, operation state, and path scope.
5. Inspect live agents: identity, status, current assignment, writer ownership.
6. Replace stale state claims with verified facts through atomic write. Preserve reachable accepted commits.
7. Resume first incomplete mandatory stage. Never repeat completed work whose artifact/SHA/check facts remain valid.

Missing or corrupt state:

- stop new dispatches and writers whose ownership is uncertain;
- inspect Git common run directory, branches/worktrees, reachable commits, artifacts, and live agents;
- recover only facts supported by Git/live observations and artifact hashes;
- write repaired state for same run only when run identity is independently proven;
- otherwise create new unique `run_id` and directory, link recovered accepted SHAs/artifacts as explicit inputs, never reuse corrupt directory.

Artifact mismatch blocks execution. Dirty/moving worktree blocks acceptance. Git/live facts override stale state.

## Recovery scenarios

- one plan: `pending -> planning -> planned -> executing -> done -> merged`; integration pre-head baseline, input execution SHA, final SHA may equal input after fast-forward.
- parallel: disjoint plans share wave; each reaches `done`; merger consumes breakdown order; each becomes `merged` at one observed integration SHA.
- sequential: prerequisite becomes `merged`; recorded integration SHA becomes dependent planner baseline; dependent planning starts afterward.
- user wait: role returns `needs_user`; status `awaiting_user`; state holds one question; response creates fresh attempt and returns to role stage.
- blocker: role returns `blocked`; status remains blocked across resume until named fact recheck passes; fresh attempt follows.
- digest mismatch: rehash differs; status `blocked`; no execution dispatch/product mutation; fresh planner artifact path required.
- target drift: integration status `blocked`; record expected/observed head; merging agent stops; LP provisions fresh isolated integration worktree and attempt; user branch unchanged.

## Cleanup and completion

Stop/verify writers before cleanup. Remove temporary worktrees/branches only after accepted SHAs remain reachable, state/evidence remains readable, and no live writer can mutate accepted work. Preserve ambiguous artifacts until disposition recorded.

Run complete when state and observed facts agree on `READY_FOR_USER_MERGE`, every requirement accepted, every plan merged, integration worktree clean, final checks bound final SHA, and authority boundary explicit. Otherwise record exact blocker and one needed action/recheck.
