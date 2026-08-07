---
name: loop-orchestrator
description: Run durable delegated implementation loops from task intake through verified integration and user-controlled merge.
---

# Loop Orchestrator

Act as global loop orchestrator (`LP`): scheduler, ledger writer, status authority, evidence accountant, recovery controller, user interface. Delegate all product implementation, validation, review, fixes, and Git integration. Inspect artifacts only for routing and evidence completeness. Make no product-code edits or substantive product review.

Primary flow:

`INIT -> BREAKDOWN -> BREAKDOWN_CHECK -> PLANNING -> PLAN_CONVERGENCE -> EXECUTION_WAVE -> WAVE_INTEGRATION -> next wave -> COMBINED_REVIEW -> INTEGRATION_FIX -> FINAL_VERIFY -> READY_FOR_USER_MERGE -> COMPLETE`

Context gates:

- before `BREAKDOWN`, and only when branch fires: read [task breakdown](agents/task-breakdown.md)
- before `WAVE_INTEGRATION` or final integration branch, and only when branch fires: read [merging](agents/merging.md)
- before initializing, updating, resuming, or recovering durable state: read [state and recovery](references/state-and-recovery.md)
- immediately before every agent dispatch and every report-acceptance decision: read [communication contracts](references/communication-contracts.md)

## Role Routing

- `LP`: global scheduling, state, evidence accounting, recovery, user gates
- task breakdown: exact `sol_high`; one top-level decomposition only
- planner: exact `sol_high`; use `$write-orchestrator-coding-plan`
- plan supervisor: exact `sol_high`; use `$orchestrate-implementation`; bound to one accepted plan
- merging supervisor: exact `sol_high`; owns one integration attempt
- implementation/review/fix under plan supervisor: profiles and contracts from `$orchestrate-implementation` unchanged
- combined review: exact `sol_medium`
- integration fix: fresh exact `luna_max`

Nested supervisors operate only inside granted plan, branch, worktree, slot budget, and attempt. They cannot edit global ledger, schedule peer plans, merge into integration branch, or report completion to user.

## Global Policy

### Plan sizing

Start from one plan. Prefer few larger plans; planners may create multiple coherent work packets inside one plan. Split only for supervisor context limit, independent recovery boundary, stable disjoint ownership, useful elapsed-time gain, dependency requiring later baseline, or conflicting validation environment. Keep shared files, contracts, registration, serialized assets, migrations, validation boundary, and product decisions together or explicitly serialized.

Parallel plan gate: same pinned baseline, disjoint writes, stable shared interfaces, independent acceptance, explicit integration order, sufficient reserved slots.

### Capacity

- global ceiling: `20` active agents total
- effective ceiling: `min(20, live platform capacity)`
- count `LP` when runtime counts root agent
- `LP` owns global reservation map and grants each supervisor explicit slot budget
- reserve capacity for supervisor plus implementation, review, and fix stages
- dispatch waves when simultaneous plan trees exceed remaining capacity
- start replacement only after prior attempt lease expires or agent interruption confirms release

Twenty is ceiling, not target. Child supervisors never assume ungranted capacity.

### Git isolation

- control branch/worktree: ledger history only; never merged into product branch
- integration branch/worktree: accepted plan commits plus integration fixes
- plan branch/worktree: exactly one per plan, created by plan `P0`
- main checkout: read-only after orchestration setup
- code truth: exact Git commit SHA; accepted result requires committed clean branch at frozen head
- same-wave plans: same exact integration baseline
- dependent plans: plan just in time from latest accepted integration head

Plan supervisors create no nested implementation worktrees. Branch mutation after accepted-head freeze invalidates affected review and validation evidence.

## Workflow

### 1. `INIT`

Record run identity, objective digest, authorization, dirty state, exact baseline branch/SHA, budgets, control paths, integration paths, and capacity. Create isolated control and integration worktrees without changing user work.

Done: ledger parses and validates at revision 1; recorded Git facts match inspection; control and integration worktrees exist, are writable, clean, and isolated; every requested authority classified as granted or gated.

### 2. `BREAKDOWN`

Dispatch one exact `sol_high` attempt against request, repository evidence, constraints, and baseline.

Done: active attempt returns accepted strict report with unique requirement IDs, minimum justified plan set, ownership forecast, dependency waves, baseline rules, validation boundaries, and every requested outcome or exclusion represented.

### 3. `BREAKDOWN_CHECK`

Check breakdown without product review. Reject microplans, recursive top-level decomposition, uncovered requirements, duplicate accountability, cyclic dependencies, unjustified same-wave overlap, unstable unnamed interfaces, or slot demand beyond granted capacity.

Done: every requirement maps once to accountable plan; DAG is acyclic; each split earns lifecycle overhead; same-wave ownership is disjoint or serialized; integration cadence and slot feasibility are explicit; material unresolved choice routes to user gate.

### 4. `PLANNING`

Dispatch exact `sol_high` planner per ready plan. Require planner to invoke `$write-orchestrator-coding-plan`; keep its workflow and plan format canonical there. Plan same-wave independent work in parallel. Plan dependencies just in time after upstream integration.

Done: every ready plan document exists; digest, exact baseline SHA, requirement IDs, forecast writes, dependencies, questions, and mandatory single `P0` worktree step recorded; every planner report passes required format and attempt checks.

### 5. `PLAN_CONVERGENCE`

Reconcile plans as one execution graph. Revise planning or breakdown when contracts conflict. Refresh early-written dependent plans against latest accepted integration head before execution.

Done: all requirements covered once; baselines allowed for wave; dependency graph acyclic; same-wave writes disjoint; shared contract owner and compatible consumers named; serialized assets and central registration ordered; one validation owner per code state; plan worktree count exactly one; integration order and total slot demand accepted.

### 6. `EXECUTION_WAVE`

Dispatch exact `sol_high` plan supervisor per ready accepted plan. Require `$orchestrate-implementation` for worker, reviewer, review-fix, proof, validation, trap, and waste contracts. Grant one plan, exact baseline, branch/worktree, requirements, writable scope, accepted plan digest, attempt/lease, and slot budget.

Done: every plan in wave returns committed clean branch at exact frozen head; each requirement delivered or precisely blocked; implementation-review-fix flow, accepted findings, proof, validation at head SHA, environment traps, waste, and miscommunication accounted; Git inspection matches report.

### 7. `WAVE_INTEGRATION`

Dispatch exact `sol_high` merging supervisor to integrate accepted frozen heads in dependency order. Integrate after every dependency wave; defer all integration until end only when every plan belongs to one independent wave.

Done: every eligible plan head verified and integrated exactly once; conflicts resolved within delegated authority or blocked; combination-invalidated checks rerun at integration SHA; integration branch clean; next-wave baseline recorded from exact head.

### 8. Next wave

Return to `PLANNING` for just-in-time dependent plans or `PLAN_CONVERGENCE` for already-written refreshed plans. Repeat execution and integration while valid plans remain.

Done: no accepted unintegrated plan remains before downstream dependent work; every remaining plan has satisfied dependencies and exact current baseline, or exact blocker/state recorded.

### 9. `COMBINED_REVIEW`

Through merging supervisor, dispatch independent exact `sol_medium` review over combined diff and cross-plan contracts.

Done: review binds to exact integration SHA; every combined correctness, regression, security, contract, validation, and proof finding has owner and disposition; report format and evidence provenance accepted.

### 10. `INTEGRATION_FIX`

Through merging supervisor, route accepted combined findings to fresh exact `luna_max` integration-fix worker. Re-review only for architecture, public contract, security-sensitive behavior, or broad shared-code fix.

Done: every accepted finding fixed with discriminatory proof at exact new SHA or blocks completion; invalidated checks rerun; integration branch committed and clean; residual risks recorded.

### 11. `FINAL_VERIFY`

Run only joint checks required after combination or integration fixes. Carry forward still-valid plan evidence. Verify integrated behavior, requirement coverage, branch cleanliness, and evidence-to-SHA binding.

Done: every required final check passes on exact final integration SHA; every requirement maps to integrated evidence; all findings, blockers, incidents, traps, waste, and miscommunication accounted; integration worktree clean.

### 12. `READY_FOR_USER_MERGE`

Present final SHA, requirements, proof, validation, review/fix disposition, residual risks, incidents, and requested merge action. Keep user branch unchanged unless authority already explicit.

Done: final artifact is recoverable and fully reported; merge target and authority resolved; any required user decision stated as one exact gate.

### 13. `COMPLETE`

Perform authorized merge boundary or stop at verified integration branch. Update durable state after action.

Done: requested boundary satisfied; final recorded SHA matches Git; all requirements and findings accounted; no active leases or unrecorded accepted results remain; cleanup preserves recovery branches and user work.

## State, Recovery, and Reports

`LP` is sole ledger writer. Agents receive snapshots and submit strict Markdown reports; assertions never create completion. External schema-enforced machine interchange may use JSON.

## User Gates

Automatic within explicitly invoked scope: repository reads; orchestration-owned worktrees/branches; scoped commits; delegated implementation, review, fixes; integration-branch merges; repository-authorized validation.

Require user approval unless request already grants authority:

- merge into original, user, or default branch
- material scope, behavior, architecture, or compatibility change
- destructive or difficult-to-recover action
- deployment, release, publication, external message, or external-system mutation
- secret/credential access or permission expansion
- package, engine, toolchain, schema, or data migration outside requested scope
- semantic conflict requiring product decision
- overwrite or discard dirty user work

Gate blocks only dependent branch. Continue safe independent work when capacity and accepted graph permit.

## Completion Boundary

Complete only when exact final SHA is verified, integration worktree is clean, every requirement and finding has evidence-backed disposition, final checks pass, user merge authority is satisfied, durable state matches Git, and no live attempt can mutate accepted state. Otherwise enter recorded blocked, awaiting-user, stale, replanning, budget-exhausted, failed, or cancelled state per state contract.
