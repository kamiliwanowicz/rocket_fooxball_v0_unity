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
- before durable-state init/update: read relevant schema, invariant, or transition section in [state and recovery](references/state-and-recovery.md)
- before resume/recovery: read [Recovery Bootstrap](references/state-and-recovery.md#recovery-bootstrap) plus referenced recovery branch
- before dispatch/report acceptance: read only operation branch named by [communication contracts](references/communication-contracts.md)

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

Direct state, binding, and escalation: [Direct Route And Canonical Pointers](references/state-and-recovery.md#direct-route-and-canonical-pointers). Direct-owner actions and child contracts: [`$orchestrate-implementation` Execution Ownership](../orchestrate-implementation/SKILL.md#execution-ownership).

## Role routing

- `LP`: global scheduling, durable state, control artifacts, evidence accounting, recovery, user gates, authorized user-branch merge
- task breakdown: exact `sol_high`; one top-level decomposition
- planner: exact `sol_high`; use `$write-orchestrator-coding-plan`
- plan supervisor: exact `sol_high`; one accepted plan; sole Git owner for plan branch/worktree
- merging supervisor: exact `sol_high`; one integration attempt; sole Git owner for integration branch/worktree during active lease
- implementation/review/fix under plan supervisor: profiles/contracts from `$orchestrate-implementation`
- combined reviewer: exact `sol_medium`, only as final-integration substate when due
- integration fixer: fresh exact `luna_max`, at most one final fix cycle

Role dispatch uses relevant [communication-contract branch](references/communication-contracts.md). Durable identity and authority use [state and recovery](references/state-and-recovery.md). Execution child roles use `$orchestrate-implementation`.

## Global policy

### Plan sizing

Start from one plan. Prefer few large plans. Split only for supervisor context limit, independent recovery boundary, stable disjoint ownership, useful elapsed-time gain, dependency requiring later baseline, or conflicting validation environment. Keep shared files, contracts, registration, serialized assets, migrations, validation boundaries, and product decisions together or explicitly ordered.

Parallel plan gate: same pinned baseline, disjoint writes, stable interfaces, independent acceptance, explicit integration order, reserved slots.

### Capacity

Before each reservation/spawn branch, apply [Capacity](references/state-and-recovery.md#capacity). Insufficient capacity remains queued before any dispatch side effect.

## Workflow

### 1. `INIT` (full route only)

Record run identity, objective digest, authority, dirty state, exact baseline branch and committed full SHA, budgets, control paths, integration paths, and capacity. Create isolated control and integration worktrees without changing user work.

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

Reconcile plan graph and refresh dependent plans against latest accepted integration head. For each executable plan, apply [Plan Convergence](references/state-and-recovery.md#plan-convergence). Dispatch plan supervisor with `P0 verify_preprovisioned` only after accepted transition. `needs_user` or `blocked` plans remain artifact-only.

Done: requirements covered once; baselines valid; writes disjoint; shared-contract owner named; serialized assets ordered; one validation owner per code state; each `ready_to_execute` plan has one worktree; integration order and slot demand accepted.

### 6. `EXECUTION_WAVE`

Dispatch exact `sol_high` plan supervisor per accepted `ready_to_execute` plan. Grant plan ID, exact baseline, branch/worktree, requirements, writable scope, plan digest, attempt/lease, slot budget, writer-barrier duty, and `P0 verify_preprovisioned`. Run `implementation -> writer barrier -> review -> fix if needed -> final plan validation -> integration handoff`. Workers/reviewers/fix workers use canonical `$orchestrate-implementation` contracts and no Git.

Done: each plan returns committed clean frozen head; writer barrier closed before review/validation; default plan-wide review plus only required early lane boundary before downstream contract consumption; one pass per boundary; accepted findings fixed by fresh worker without re-review; final plan validation passes at frozen head before integration handoff; requirements, traps, waste, and miscommunication accounted.

### 7. `WAVE_INTEGRATION`

Dispatch exact `sol_high` merging supervisor with active lease and integration worktree/branch. Merger alone stages, commits, merges exact accepted heads, resolves integration conflicts, runs assigned integration checks, and owns final substates. LP remains ledger writer.

Intermediate wave -> merge exact validated plan heads, run integration checks, return `wave_complete`, record next-wave baseline.

Final wave -> apply canonical final-stage mapping from [state and recovery](references/state-and-recovery.md#canonical-report-transitions) and merger procedure from [merging](agents/merging.md). Integration receives only validated frozen plan heads. Final merger `complete` -> direct `READY_FOR_USER_MERGE`.

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
