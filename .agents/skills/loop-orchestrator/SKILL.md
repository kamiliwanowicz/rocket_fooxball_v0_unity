---
name: loop-orchestrator
description: Use when user requests plan-first delegated implementation through breakdown, planning, execution orchestration, isolated integration, and verified handoff.
---

# Loop Orchestrator

Act as loop owner (`LP`). Coordinate route:

`INIT -> BREAKDOWN -> PLANNING -> EXECUTION -> MERGING -> READY_FOR_USER_MERGE`

`single_plan` route -> `INIT -> PLANNING -> EXECUTION -> READY_FOR_USER_MERGE`; skip BREAKDOWN and MERGING agents. LP coordinates, resolves blockers, owns durable state, verifies returned facts, and alone may merge exact accepted integration SHA into user branch after explicit authority. LP never writes product files, writes coding plans, dispatches implementation workers directly, performs substantive review, or acts as merging agent.

## Roles

- LP: route owner, sole state writer, blocker resolver, acceptance verifier, user-branch merge authority.
- [`task-breakdown`](agents/task-breakdown.md): exact `sol_high`; returns plan candidates and requirement coverage; skipped for `single_plan`.
- planner: exact `sol_high`; uses [`$write-orchestrator-coding-plan`](../write-orchestrator-coding-plan/SKILL.md) once per ready candidate.
- execution orchestrator: exact `sol_high`; uses [`$orchestrate-implementation`](../orchestrate-implementation/SKILL.md) once per accepted plan.
- [merging agent](agents/merging.md): exact `sol_high`; integrates completed waves in bound isolated integration worktree; skipped for `single_plan`.

Profile unavailable -> current attempt `blocked`; LP records blocker and recheck condition. No silent profile substitution.

## Sources and authority

- Git: durable code facts -> branch, worktree, full SHA, ancestry, clean status, path diff.
- Collaboration tools: live agent identity, status, ownership.
- [Run state](references/state-and-recovery.md): LP-owned routing record; never stronger than Git or live observations.
- Agent reports: evidence hints until LP verifies identity, Git, scope, and checks.

User branch stays unchanged until explicit authority binds target branch and candidate SHA. Approval remains required for user-branch merge, destructive action, material scope or behavior change, external mutation, secrets, migration, or dirty-work overwrite.

Worktree scope is closed: current run's plan worktrees plus multi-plan integration worktree when route requires one. Query and verify exact bound paths/branches only. Keep unrelated repository worktrees outside discovery, state, dispatch context, reports, and cleanup. Inspect target checkout only when needed to establish baseline, dirty overlap, or user-branch merge authority; never persist unrelated worktree inventory.

## INIT

1. Read request, repository instructions, cited sources, dirty paths, current branch, full baseline SHA, checks, and authority.
2. Generate unique `run_id`, stable `REQ-*` IDs, and unique run directory under Git common dir. Create required `state.md` through atomic-write contract before first dispatch.
3. Multi-plan route -> bind breakdown attempt with unique `attempt_id`, exact `sol_high`, objective, requirements, baseline, evidence paths, constraints, checks, and state path. `single_plan` route -> bind planner directly from accepted plan context; no breakdown dispatch.

Dirty owned path overlapping run scope -> protect it. Continue only after user-authorized inclusion or separate accepted commit. Refresh accepted full baseline before provisioning plan worktrees.

Candidate records bind `read_paths`, `validation_environment`, `unity_mutation`, `expensive_proof_owner`, `expensive_proof_run_point`, and `proof_invalidation_paths`. One production-final owner runs proof after source fan-in and accepted fixes. Candidate may contain multiple workers only with disjoint paths and stable validation environments.

## BREAKDOWN

Dispatch [`task-breakdown`](agents/task-breakdown.md) for multi-plan routes. `single_plan` route skips BREAKDOWN agent; LP verifies accepted plan context, requirement coverage, baseline, dependencies, and owned/protected paths before planning. LP never substitutes inline decomposition for multi-plan routes.

Accept result only when strict template is complete, baseline matches observed accepted baseline, each requirement has exactly one candidate owner, dependency graph is acyclic, writable paths do not overlap within parallel wave, and integration order is deterministic.

- `ready`: record result; assign stable `plan_id` per candidate; start eligible planning.
- `needs_user`: record breakdown status `awaiting_user`, material question, and safe independent work. User response -> fresh breakdown attempt with new `attempt_id`.
- `blocked`: record exact blocker and recheck condition. Resolution -> recheck facts, then fresh breakdown attempt.

Prefer one large plan. Split only for independent ownership, meaningful parallel gain, or accepted dependency. Shared contracts, generated/serialized assets, migrations, and product decisions stay serialized.

## PLANNING

For each ready candidate, LP reserves unique create-once artifact path:

`<git-common-dir>/loop-orchestrator/<run-id>/plans/<plan-id>/<attempt-id>.md`

Dispatch exact `sol_high` planner with `run_id`, `plan_id`, `attempt_id`, covered requirement IDs, accepted baseline SHA, dependencies, owned/protected paths, reserved artifact path, checks, state path, and objective. Independent disjoint candidates may plan in parallel. Dependent candidate waits until prerequisite merger records accepted integration SHA; that observed SHA becomes planning baseline.

Planner result handling:

- `ready`: stop planner; verify artifact exists at reserved path; compute SHA-256 and byte size; record source path/digest/size.
- `needs_user`: record `awaiting_user` and question. User response -> fresh planner attempt, new `attempt_id`, new reserved path.
- `blocked`: record blocker and recheck condition. Resolution -> fresh planner attempt and new reserved path.
- decomposition change: route candidate revision through fresh task-breakdown attempt; planner never splits candidates.

Accepted source artifact remains pre-execution input only. Execution binding performs one source read into attempt snapshot, then verifies snapshot against accepted digest/size.

## EXECUTION

For each accepted plan, LP creates one new isolated branch/worktree from exact recorded plan-baseline SHA. Verify initial worktree `HEAD` equals baseline; bind full SHA as immutable execution `start_sha`. Source branch ref becomes provenance only. Read accepted source once into unique create-once execution snapshot:

`<git-common-dir>/loop-orchestrator/<run-id>/plans/<plan-id>/executions/<attempt-id>.md`

Reopen snapshot; verify accepted digest and size. Mismatch -> plan `blocked`; no product mutation or dispatch. Match -> record snapshot path/digest/size atomically. Bind one exact `sol_high` execution orchestrator using [`$orchestrate-implementation`](../orchestrate-implementation/SKILL.md). Handoff carries source path as provenance and snapshot as sole plan authority. Dispatch fields follow its [LP handoff contract](../orchestrate-implementation/SKILL.md#lp-handoff-contract).

Snapshot and `start_sha` binding close source boundary. Target/launch checkout, source branch, and source artifact leave execution observation, recovery, and acceptance gates. Later changes there do not pause or invalidate attempt. LP and execution orchestrator use plan worktree plus exact `start_sha..plan_head` comparisons until attempt ends.

Execution orchestrator builds declared checks before worker dispatch. Workflow writes sole executed `check-ledger.json`; harness writes `harness-summary.json`; state stores only ledger pointer plus SHA-256. Workers run compact fast/local proof; production-final rows retain full contract. Apply [workflow harness precondition](references/state-and-recovery.md#workflow-harness-precondition) before every workflow or Unity invocation. Then run `Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode <...> -ProjectPath <...>` with applicable `-PlanOnly`, `-LedgerPath`, and `-EvidenceRoot`; carry previous accepted `-LedgerPath`; never pass workflow arguments to test runner. Production-final order: zero writers, clean exact source SHA, one Unity lease, accepted reviews/fixes -> `ProductionPrepare` bake -> `ProductionValidate` semantic pass. Apply [production bake gate](references/state-and-recovery.md#production-bake-gate).

Execution orchestrator becomes sole Git owner for plan worktree. LP does not dispatch its workers or perform its review/fix loop. Parallel execution allowed only for breakdown-approved disjoint candidates with stable inputs.

Accept `complete` only when exact execution identity matches, bound snapshot digest rehash matches, observed branch/worktree match, committed head descends from `start_sha`, exact `start_sha..plan_head` changed paths stay owned, required checks bind head, and index/worktree are clean. Source-branch ref never participates. `blocked` records concrete needed LP action. Any retry uses fresh `attempt_id` and fresh dispatch identity.

## MERGING

Before first multi-plan merge, LP provisions unique isolated integration branch/worktree from observed accepted baseline. Record branch, worktree, expected pre-merge head, exact allowed Git operations, integration order, and checks in state. `single_plan` route provisions no merger; LP records accepted execution SHA after exact scope/check verification.

Dispatch [merging agent](agents/merging.md) after every completed multi-plan wave. Inputs are exact accepted execution SHAs in breakdown-declared order. Sequential dependent planning waits for prerequisite wave merge and accepted integration SHA. `single_plan` route dispatches planner then execution orchestrator directly and skips merger.

Accept merge result only after rereading integration Git facts, accepted input ancestry, observed pre/post heads, clean status, scope, and checks. Each accepted execution SHA merges exactly once. `single_plan` route accepts execution SHA as final integration SHA only after clean scope/check proof; no merge-stage agent result exists.

Intermediate waves run Git, scope, and downstream-contract checks. Final wave runs union of pending or invalidated production-final rows once. Unchanged multi-plan fast-forward reuses non-bake evidence after `check-ledger.json` digest attestation and production-bake-gate reattest. Merge or fix invalidates only intersecting rows. Apply [production bake gate](references/state-and-recovery.md#production-bake-gate) when lighting inputs intersect.

Target drift -> current merge attempt `blocked`. LP follows [target-drift recovery](references/state-and-recovery.md#target-drift-recovery): default retry baseline is last recorded accepted integration SHA before drift; fresh attempt replays remaining accepted inputs in declared order. Drift SHA enters retry ancestry only after required evidence and authority acceptance are recorded. Merging agent never mutates user branch.

## Dispatch identity and results

Every dispatch carries `run_id`, `plan_id` or `None`, unique `attempt_id`, exact assigned agent/profile/role, bounded task and done condition, baseline SHA, immutable execution `start_sha` when applicable, branch/worktree, owned/protected paths, dependencies, allowed Git operations, checks, and state path.

Role-specific statuses:

- breakdown and planner: `ready | needs_user | blocked` using their strict contracts.
- execution, implementation, review, fix, and merge: `complete | blocked` using owning skill contract.

Reject late, replaced, interrupted, duplicate, or foreign returns. Preserve rejected report as evidence only. Never promote report by rewriting identity or facts.

LP writes state atomically before each dispatch and after accepting each result. Other agents read state and report facts; they never edit it.

## Quiet waiting

After dispatch, let assigned agent work. Prefer longest practical bounded wait for completion or attention signal. Treat unchanged live status as no event.

- Routine checks: silent. Emit no user commentary, state write, or agent message for polling, elapsed time, unchanged status, or another wait cycle.
- Manual status check: only when required for dependency scheduling, user-requested status, recovery, suspected stall, or ownership conflict. Take one compact snapshot, act on material change, then resume waiting.
- Agent contact: send follow-up only with new task-required information, correction, or concrete unblock action. Never ping for progress alone.
- User update: only for material phase transition, actionable blocker/question, requested status, or final handoff. Collapse repeated unchanged state into silence.

## Recovery and completion

Use [state and recovery](references/state-and-recovery.md) for every run, resume, user wait, blocker, digest mismatch, target drift, and cleanup. Resume from recorded accepted facts only after validating run identity, artifacts, Git, and live agents. Preserve reachable accepted commits.

Final handoff requires:

- phase `READY_FOR_USER_MERGE`;
- `single_plan` -> clean plan worktree; accepted execution SHA recorded as final integration SHA; zero integration worktree and zero merger;
- `multi-plan` -> clean integration worktree; every accepted execution SHA merged exactly once; exact final integration SHA;
- every requirement covered;
- required checks bound to final SHA;
- Critical/High finding dispositions recorded;
- changed paths and residual risks recorded;
- explicit authority request binding user target branch and exact integration SHA.

Missing or drifted fact -> `blocked` with evidence and one needed action.
