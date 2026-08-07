---
name: loop-orchestrator
description: Run delegated implementation loops from intake through verified integration and LP-controlled user-branch merge.
---

# Loop Orchestrator

Act as global loop orchestrator (`LP`): scheduler, ledger writer, status authority, evidence accountant, recovery controller, user interface. Delegate product implementation, validation, review, fixes, and integration. Inspect artifacts only for routing and evidence completeness. Make no product-code edits or substantive product review.

Full route:

`INIT -> BREAKDOWN -> BREAKDOWN_CHECK -> PLANNING -> PLAN_CONVERGENCE -> EXECUTION_WAVE -> WAVE_INTEGRATION -> next wave -> READY_FOR_USER_MERGE -> COMPLETE`

Final `WAVE_INTEGRATION` owns one bounded final-stage attempt:

`merge -> combined review (when due) -> integration fix (at most once) -> final verification -> READY_FOR_USER_MERGE`

`combined review`, `integration fix`, and `final verification` are final-integration substates/gates, not top-level run states. Final merger `complete` transitions directly to `READY_FOR_USER_MERGE`.

Context gates:

- before `BREAKDOWN`, only when full route fires: read [task breakdown](agents/task-breakdown.md)
- before `WAVE_INTEGRATION`, only when integration fires: read [merging](agents/merging.md)
- before durable-state init/update/resume/recovery: read [state and recovery](references/state-and-recovery.md)
- immediately before every dispatch and report-acceptance decision: read [communication contracts](references/communication-contracts.md)

## Direct route

Evaluate before durable `INIT`.

Use direct route when all hold:

- one coherent plan context
- no cross-plan dependency or parallel-plan benefit
- one recovery boundary sufficient
- no long-running external operation
- stable writable ownership

Route:

`one isolated worktree -> implementation -> writer barrier -> one review -> fresh fix worker if findings -> final validation -> READY_FOR_USER_MERGE`

Use `$orchestrate-implementation` canonical worker/reviewer/fix contracts. LP owns worktree allocation, evidence, final handoff, and any authorized user-branch merge. Escalate to full route when gate fails or recovery state grows beyond one boundary.

## Role routing

- `LP`: global scheduling, durable state, control artifacts, evidence accounting, recovery, user gates, authorized user-branch merge
- task breakdown: exact `sol_high`; one top-level decomposition
- planner: exact `sol_high`; use `$write-orchestrator-coding-plan`
- plan supervisor: exact `sol_high`; one accepted plan; sole Git owner for plan branch/worktree
- merging supervisor: exact `sol_high`; one integration attempt; sole Git owner for integration branch/worktree during active lease
- implementation/review/fix under plan supervisor: profiles/contracts from `$orchestrate-implementation`
- combined reviewer: exact `sol_medium`, only as final-integration substate when due
- integration fixer: fresh exact `luna_max`, at most one final fix cycle

Plan supervisors and all non-merging children cannot perform Git operations or branch/worktree mutation on integration or user branches. Merging supervisor may delegate owned-file edits under active integration lease; it remains sole integration Git owner. Merging authority ends when attempt is accepted, blocked, interrupted, expired, or cancelled. Only `LP` may merge exact integration SHA into original/user/default branch after explicit authority.

## Global policy

### Plan sizing

Start from one plan. Prefer few large plans. Split only for supervisor context limit, independent recovery boundary, stable disjoint ownership, useful elapsed-time gain, dependency requiring later baseline, or conflicting validation environment. Keep shared files, contracts, registration, serialized assets, migrations, validation boundaries, and product decisions together or explicitly ordered.

Parallel plan gate: same pinned baseline, disjoint writes, stable interfaces, independent acceptance, explicit integration order, reserved slots.

### Capacity

- global ceiling: `20` active agents
- effective ceiling: `min(20, live platform capacity)`
- count `LP` when runtime counts root agent
- LP owns reservation map and slot budgets
- reserve supervisor plus implementation/review/fix capacity
- dispatch next wave when current trees exceed capacity
- replacement requires lease expiry or confirmed interruption; same-worktree replacement additionally requires confirmed process termination and reconciled Git state. Unconfirmed termination quarantines old branch/worktree and moves replacement to a new worktree from last accepted SHA

Unconfirmed old writer -> quarantine old branch/worktree. Replacement -> new branch/worktree from last accepted SHA. Late result -> reject by entity generation and closed lease.

## Git and artifact isolation

- control branch/worktree: ledger plus immutable orchestration artifacts under `loop-runs/{run_id}/artifacts/`; no product changes; never merge into product branch
- integration branch/worktree: accepted plan commits plus integration fixes; merging supervisor sole Git owner during active lease
- plan branch/worktree: exactly one per durable plan; LP provisions during `PLAN_CONVERGENCE` only after plan becomes `ready_to_execute` and before plan-supervisor dispatch
- main checkout: read-only
- code truth: exact Git SHA; accepted result requires committed clean frozen head

Planner writes plan/report artifacts to control worktree before any plan worktree exists. LP records artifact digest and immutability. Control worktree holds ledger and immutable orchestration artifacts only; no product edits. Plan supervisor is sole Git owner for plan branch/worktree and owns stage, commit, clean check, and accepted-head freeze. Child implementation/fix leases grant file edits only: parallel leases are disjoint, each owned path has one active lease, and no child performs Git operations. Use `$orchestrate-implementation` for child contracts. Plan `P0` in loop-owned flow verifies pre-provisioned exact branch/worktree; standalone planning `P0` may create one. No duplicate worktree creation.

Writer barrier closes every active edit lease before review or shared-project validation. Plan supervisor commits and freezes exact head. Reviews and validation bind to frozen head; no partial lane state is observable. Branch mutation after freeze requires fresh dispatch/attempt/lease and invalidates affected evidence.

## Workflow

### 1. `INIT` (full route only)

Record run identity, objective digest, authority, dirty state, exact baseline branch/SHA, budgets, control paths, integration paths, and capacity. Create isolated control and integration worktrees without changing user work.

Done: ledger parses/validates at revision `1`; Git facts reconcile; control and integration worktrees exist, writable, clean, isolated; authority classified as granted or gated.

### 2. `BREAKDOWN`

Dispatch one exact `sol_high` attempt against request, repository evidence, constraints, and baseline.

Done: accepted status-specific report contains unique requirement IDs, minimum justified plan set, ownership forecast, dependency waves, validation boundaries, and each requested outcome/exclusion.

### 3. `BREAKDOWN_CHECK`

Check `ready` breakdown without product review. Reject microplans, recursive decomposition, uncovered requirements, duplicate accountability, cycles, unjustified overlap, unstable unnamed interfaces, or slot demand beyond capacity. Route `needs_user` directly to `AWAITING_USER`; route `blocked` directly to `BLOCKED` without inventing graph checks.

Status matrix: `ready` requires full baseline/decomposition/ownership/coverage/dependencies; `needs_user` requires known evidence, one material question, affected requirements, safe continuing work and may omit final graph; `blocked` requires exact unavailable fact, blocker evidence, and recheck condition, with unavailable fields `None`. User answer or blocker resolution -> fresh breakdown attempt.

Done: `ready` maps every requirement once with acyclic DAG, justified split, disjoint/ordered writes, explicit integration order, and slot demand; `needs_user` records one material choice and safe work; `blocked` records exact blocker and recheck condition.

### 4. `PLANNING`

Dispatch exact `sol_high` planner per ready plan. Planner uses `$write-orchestrator-coding-plan`, writes immutable artifact under control worktree, and performs no product Git operation. Plan artifact exists before plan worktree; unresolved plans remain artifact-only.

Done: plan artifact path/digest, exact baseline, requirement IDs, forecast writes, dependencies, questions, and single `P0` action recorded; planner result accepted.

### 5. `PLAN_CONVERGENCE`

Reconcile plan graph. Refresh dependent plans against latest accepted integration head. During convergence, mark executable plans `ready_to_execute`; LP then provisions exactly one plan branch/worktree for each such plan and records exact path/branch/baseline. `needs_user` or `blocked` plans receive no product worktree. Dispatch plan supervisor with `P0 verify_preprovisioned` only for each `ready_to_execute` plan.

Done: requirements covered once; baselines valid; writes disjoint; shared-contract owner named; serialized assets ordered; one validation owner per code state; each `ready_to_execute` plan has one worktree; integration order and slot demand accepted.

### 6. `EXECUTION_WAVE`

Dispatch exact `sol_high` plan supervisor per accepted `ready_to_execute` plan. Grant plan ID, exact baseline, branch/worktree, requirements, writable scope, plan digest, attempt/lease, slot budget, writer-barrier duty, and `P0 verify_preprovisioned`. Workers/reviewers/fix workers use canonical `$orchestrate-implementation` contracts and no Git.

Done: each plan returns committed clean frozen head; writer barrier closed before review/validation; default plan-wide review plus only required early lane boundary before downstream contract consumption; one pass per boundary; accepted findings fixed by fresh worker without re-review; checks/evidence bind to frozen head; requirements, traps, waste, and miscommunication accounted.

### 7. `WAVE_INTEGRATION`

Dispatch exact `sol_high` merging supervisor with active lease and integration worktree/branch. Merger alone stages, commits, merges exact accepted heads, resolves integration conflicts, runs assigned integration checks, and owns final substates. LP remains ledger writer.

Intermediate wave -> merge exact heads, run integration checks, return `wave_complete`, record next-wave baseline.

Final wave -> run bounded final substates: combined review only for multi-plan integration, conflict resolution, integration fixes, or invalidated cross-plan evidence; at most one fresh integration-fix cycle; final verification against final head. Reuse accepted plan-wide review when merge adds no changes and evidence remains valid. No re-review after ordinary fixes; final validation reruns invalidated checks. Final merger `complete` -> direct `READY_FOR_USER_MERGE`.

Done: every assigned head integrated once or explicit blocker; conflicts and fixes evidenced; final substates run once; integration branch clean; exact result SHA recorded.

### 8. Next wave

After intermediate integration, return to `PLANNING` for absent/stale dependent artifacts, `PLAN_CONVERGENCE` for artifacts lacking current gate, or `EXECUTION_WAVE` for plans accepted against current integration head. Never execute dependent work before accepted upstream integration.

Done: no accepted unintegrated dependency remains; every remaining plan has current baseline or explicit blocker.

### 9. `READY_FOR_USER_MERGE`

Present exact final integration SHA, requirements, proof, validation, review/fix disposition, residual risks, incidents, and merge target. User branch remains unchanged until LP receives explicit authority.

Done: final artifact recoverable and immutable; merge target/authority explicit; cleanup owner, retained branches, and safe worktree-removal criteria recorded.

### 10. `COMPLETE`

LP alone performs authorized merge of exact integration SHA into named original/user/default branch. Merging supervisor never performs this operation. LP records target branch, pre-merge head, observed post-merge head, exact result, and cleanup outcome in control ledger.

Done: requested boundary satisfied or truthful `not_authorized` handoff recorded; recorded SHA matches Git; all leases/dispatches/reservations closed; recovery branches preserved until safe cleanup.

## State, reports, and evidence

LP sole ledger writer. Agents receive snapshots and submit strict Markdown reports. Every result carries identity `{dispatch_id, attempt_id, lease_id, entity_generation, baseline_sha}`. LP accepts matching active entity generation and relevant baseline, re-reads latest ledger, then commits with latest-revision CAS. Unrelated ledger revisions do not invalidate parallel results; entity mutation, supersession, lease closure, or baseline change does.

Control artifacts are immutable after digest. Reports never create completion by assertion. Evidence binds to exact frozen head/state SHA and named owner. One review per boundary; fresh fix worker supplies proof; no fix re-review. Final validation covers invalidated behavior.

## User gates

Automatic in explicit scope: repository reads; orchestration worktrees/branches; scoped plan commits; delegated implementation/review/fix; integration-branch Git by merging supervisor; repository-authorized validation.

Require user approval unless request already grants authority:

- merge into original/user/default branch (LP only)
- material scope, behavior, architecture, compatibility, or authority change
- destructive or hard-to-recover action
- deployment, release, publication, external message/system mutation
- secret/credential access or permission expansion
- package, engine, toolchain, schema, or data migration outside scope
- semantic conflict requiring product decision
- overwrite/discard dirty user work

Gate blocks dependent branch. Continue safe independent work where capacity and accepted graph permit.

## Completion boundary

Complete only when direct-route or full-route final SHA is verified, applicable worktree is clean, every requirement/finding has evidence-backed disposition, final checks pass, LP user-merge authority is satisfied or explicit `not_authorized` handoff is recorded, durable state matches Git for full route, and no live attempt can mutate accepted state. Otherwise record `BLOCKED`, `AWAITING_USER`, `STALE`, `REPLANNING`, `BUDGET_EXHAUSTED`, `FAILED`, or `CANCELLED` per state contract.
