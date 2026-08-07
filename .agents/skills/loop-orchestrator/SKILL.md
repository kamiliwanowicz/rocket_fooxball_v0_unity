---
name: loop-orchestrator
description: Use when user requests plan-first delegated implementation through breakdown, planning, execution orchestration, isolated integration, and verified handoff.
---

# Loop Orchestrator

Act as loop owner (`LP`). Coordinate mandatory route:

`INIT -> BREAKDOWN -> PLANNING -> EXECUTION -> MERGING -> READY_FOR_USER_MERGE`

Every run follows every stage, including `single_plan`. LP coordinates, resolves blockers, owns durable state, verifies returned facts, and alone may merge exact accepted integration SHA into user branch after explicit authority. LP never writes product files, writes coding plans, dispatches implementation workers directly, performs substantive review, or acts as merging agent.

## Roles

- LP: route owner, sole state writer, blocker resolver, acceptance verifier, user-branch merge authority.
- [`task-breakdown`](agents/task-breakdown.md): exact `sol_high`; returns plan candidates and requirement coverage.
- planner: exact `sol_high`; uses [`$write-orchestrator-coding-plan`](../write-orchestrator-coding-plan/SKILL.md) once per ready candidate.
- execution orchestrator: exact `sol_high`; uses [`$orchestrate-implementation`](../orchestrate-implementation/SKILL.md) once per accepted plan.
- [merging agent](agents/merging.md): exact `sol_high`; integrates every completed wave in bound isolated integration worktree.

Profile unavailable -> current attempt `blocked`; LP records blocker and recheck condition. No silent profile substitution.

## Sources and authority

- Git: durable code facts -> branch, worktree, full SHA, ancestry, clean status, path diff.
- Collaboration tools: live agent identity, status, ownership.
- [Run state](references/state-and-recovery.md): LP-owned routing record; never stronger than Git or live observations.
- Agent reports: evidence hints until LP verifies identity, Git, scope, and checks.

User branch stays unchanged until explicit authority binds target branch and candidate SHA. Approval remains required for user-branch merge, destructive action, material scope or behavior change, external mutation, secrets, migration, or dirty-work overwrite.

## INIT

1. Read request, repository instructions, cited sources, dirty paths, current branch, full baseline SHA, checks, and authority.
2. Generate unique `run_id`, stable `REQ-*` IDs, and unique run directory under Git common dir. Create required `state.md` through atomic-write contract before first dispatch.
3. Bind breakdown attempt with unique `attempt_id`, exact `sol_high`, objective, requirements, baseline, evidence paths, constraints, checks, and state path.

Dirty owned path overlapping run scope -> protect it. Continue only after user-authorized inclusion or separate accepted commit. Refresh accepted full baseline before provisioning plan worktrees.

## BREAKDOWN

Dispatch [`task-breakdown`](agents/task-breakdown.md) for every run. LP never substitutes inline decomposition.

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

- `ready`: stop planner; verify artifact exists at reserved path; compute SHA-256 and byte size; record accepted path/digest/size; artifact becomes immutable.
- `needs_user`: record `awaiting_user` and question. User response -> fresh planner attempt, new `attempt_id`, new reserved path.
- `blocked`: record blocker and recheck condition. Resolution -> fresh planner attempt and new reserved path.
- decomposition change: route candidate revision through fresh task-breakdown attempt; planner never splits candidates.

Rehash accepted artifact immediately before execution dispatch. Mismatch -> plan `blocked`; no product worktree mutation.

## EXECUTION

For each accepted plan, LP provisions one isolated branch/worktree from recorded plan baseline. Bind one exact `sol_high` execution orchestrator using [`$orchestrate-implementation`](../orchestrate-implementation/SKILL.md). Dispatch fields follow its [LP handoff contract](../orchestrate-implementation/SKILL.md#lp-handoff-contract).

Execution orchestrator becomes sole Git owner for plan worktree. LP does not dispatch its workers or perform its review/fix loop. Parallel execution allowed only for breakdown-approved disjoint candidates with stable inputs.

Accept `complete` only when exact execution identity matches, accepted plan digest rehash matches, observed branch/worktree match, committed head descends from bound baseline, changed paths stay owned, required checks bind head, and worktree is clean. `blocked` records concrete needed LP action. Any retry uses fresh `attempt_id` and fresh dispatch identity.

## MERGING

Before first merge, LP provisions unique isolated integration branch/worktree from observed accepted baseline. Record branch, worktree, expected pre-merge head, exact allowed Git operations, integration order, and checks in state.

Dispatch [merging agent](agents/merging.md) after every completed wave, including one-plan wave. Inputs are exact accepted execution SHAs in breakdown-declared order. Sequential dependent planning waits for prerequisite wave merge and accepted integration SHA.

Accept merge result only after rereading integration Git facts, accepted input ancestry, observed pre/post heads, clean status, scope, and checks. Each accepted execution SHA merges exactly once. One-plan fast-forward may leave commit identity unchanged; isolated branch/worktree plus expected pre-merge and observed post-merge heads prove merge stage occurred.

Target drift -> current merge attempt `blocked`. LP follows [target-drift recovery](references/state-and-recovery.md#target-drift-recovery): default retry baseline is last recorded accepted integration SHA before drift; fresh attempt replays remaining accepted inputs in declared order. Drift SHA enters retry ancestry only after required evidence and authority acceptance are recorded. Merging agent never mutates user branch.

## Dispatch identity and results

Every dispatch carries `run_id`, `plan_id` or `None`, unique `attempt_id`, exact assigned agent/profile/role, bounded task and done condition, baseline SHA, branch/worktree when applicable, owned/protected paths, dependencies, allowed Git operations, checks, and state path.

Role-specific statuses:

- breakdown and planner: `ready | needs_user | blocked` using their strict contracts.
- execution, implementation, review, fix, and merge: `complete | blocked` using owning skill contract.

Reject late, replaced, interrupted, duplicate, or foreign returns. Preserve rejected report as evidence only. Never promote report by rewriting identity or facts.

LP writes state atomically before each dispatch and after accepting each result. Other agents read state and report facts; they never edit it.

## Recovery and completion

Use [state and recovery](references/state-and-recovery.md) for every run, resume, user wait, blocker, digest mismatch, target drift, and cleanup. Resume from recorded accepted facts only after validating run identity, artifacts, Git, and live agents. Preserve reachable accepted commits.

Final handoff requires:

- phase `READY_FOR_USER_MERGE`;
- observed clean integration branch/worktree and exact full final SHA;
- every requirement covered and every accepted execution SHA merged once;
- required checks bound to final SHA;
- Critical/High finding dispositions recorded;
- changed paths and residual risks recorded;
- explicit authority request binding user target branch and exact integration SHA.

Missing or drifted fact -> `blocked` with evidence and one needed action.
