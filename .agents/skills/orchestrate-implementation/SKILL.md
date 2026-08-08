---
name: orchestrate-implementation
description: Use when LP dispatches one accepted coding plan for orchestration, or user explicitly invokes this skill to execute one accepted coding plan through bounded workers, independent reviewers, fixes, and exact-SHA validation.
---

# Orchestrate Implementation

Invocation makes active agent execution orchestrator for one accepted coding plan. Active agent coordinates plan directly, remains sole Git owner for bound plan branch/worktree, and retains orchestration through completion or blocker return. Dispatch child roles only: implementation worker, reviewer, fix worker. Active agent owns writer barriers, commits, freezes, scope checks, dependency gates, and final validation.

## Invocation mode

Select one mode before product mutation or child dispatch:

- `lp-dispatched`: invocation contains explicit LP handoff contract and LP state path. LP owns run state, cross-plan coordination, merging-agent dispatch, and user-branch authority.
- `user-direct`: user explicitly invoked this skill without LP handoff. User owns decisions normally routed to LP. Active agent creates isolated branch/worktree, runs plan there, and communicates blockers/results directly to user. LP state, IDs, assignments, messages, waits, and merge coordination remain outside run.

Never switch mode during attempt. Ambiguous or partial LP handoff -> `blocked`; ask invoking user whether to use `user-direct` or obtain complete LP dispatch.

## LP handoff contract

`lp-dispatched` handoff binds:

- `run_id`, stable `plan_id`, unique `attempt_id`;
- assigned active-agent identity, role `execution orchestrator`, profile `sol_high`;
- accepted plan artifact absolute path, SHA-256 digest, byte size;
- covered requirement IDs and objective;
- accepted dependency SHAs;
- exact plan branch and isolated worktree;
- owned/protected paths;
- checks, proof boundary, evidence locations;
- allowed Git operations limited to plan branch/worktree;
- LP state-file path, read-only for active execution orchestrator.

Missing/mismatched field, including active-agent identity or role -> `blocked` before product mutation.

## User-direct bootstrap

`user-direct` requires accepted plan artifact explicitly identified by user or current context. Before child dispatch:

1. Resolve repository root and accepted plan artifact absolute path. Capture artifact SHA-256 and byte size as immutable attempt identity.
2. Capture launch checkout absolute path, current branch, exact `HEAD` as worktree source, and status. Preserve launch checkout and all existing changes unchanged.
3. Generate stable local `plan_id` plus unique `attempt_id`. Derive unique `codex/<plan-slug>-<attempt-id>` branch and sibling worktree path outside launch checkout.
4. Confirm target parent writable for active agent and child agents. Existing branch/path -> choose new unique names; preserve existing worktrees and branches.
5. Create branch/worktree from captured source SHA. Verify worktree root, branch, `HEAD`, initial status, and accepted artifact readability at frozen absolute path for active agent and children.
6. Bind objective, requirements, owned/protected paths, dependencies, checks, proof boundary, and evidence locations from accepted plan. Missing execution-critical boundary -> `blocked` with one needed user decision.

All plan work, child dispatch, Git mutation, and validation use created worktree. Active agent owns created plan branch only. Launch checkout remains unchanged. Keep completed worktree/branch for user inspection; integrate into launch branch only when user explicitly requested integration.

Worktree creation failure -> `blocked` before product mutation. Return command/error, attempted branch/path, and one needed user action.

## Artifact gate

Before first worker dispatch:

- Current plan-worktree `HEAD` -> authoritative `start_sha`. Plan-declared repository revision metadata never gates execution.
- Capture initial worktree status. Pre-existing changes outside owned paths -> preserve untouched, exclude from staging/review evidence, continue. Pre-existing change overlapping owned path -> `blocked` with exact overlap and one needed authority action.
- `lp-dispatched`: read LP state; verify assigned identity, role `execution orchestrator`, profile `sol_high`, artifact path/digest/size, artifact identity, dependencies, worktree/branch, ownership, and allowed Git operations.
- `user-direct`: verify generated attempt identity, artifact path/digest/size, dependencies, created worktree/branch, ownership, and branch-only Git boundary.

Digest, identity, or mode-contract mismatch -> `blocked` with observed digest/size and needed authority action. Perform no product mutation or child dispatch. Accepted artifact remains immutable throughout attempt; rehash before final return. Post-dispatch mismatch invalidates attempt.

## Ownership and profiles

- Active execution orchestrator: sole Git owner for plan worktree and coordinator for every checkpoint. Creates no additional plan/integration worktrees.
- Child roles: implementation worker, reviewer, fix worker only. Active agent retains plan sequencing, worker coordination, result acceptance, Git operations, review gates, finding disposition, and final validation.
- Implementation/fix workers: edit assigned owned paths only; no Git staging, commits, branch/worktree operations, or state edits.
- One writer per path. Parallel writers require disjoint paths and stable inputs. Serialize shared contracts, generated/serialized assets, migrations, and shared validation environments.
- Reviewer: fresh exact `sol_medium` per review checkpoint; read-only exact frozen SHA.
- Implementation worker: exact profile required by plan/user/AGENTS; otherwise `luna_max`.
- Fix worker: fresh exact profile required by plan/user/AGENTS; otherwise `luna_max`.
- Required profile unavailable -> `blocked`; no silent substitution.

Active execution orchestrator closes writer barrier before Git mutation, freeze, review, or shared validation.

## Child dispatch contract

Each child dispatch carries unique `execution_id`, invocation mode, assigned identity/profile/role, bounded task/done condition, objective/exclusions, `start_sha`, exact branch/worktree, initial unrelated-status exclusions, owned/protected paths, accepted dependencies, allowed Git operations (`None` for writers; read-only for reviewer), checks, proof/evidence boundary, and plan identity/path/digest.

Reviewer dispatch also binds `checkpoint_id`, covered worker/task execution IDs, `review_base_sha`, and `frozen_sha`. Fix dispatch binds `pre_fix_frozen_sha`, accepted finding IDs, finding-owned paths, and acceptance criteria.

Child return repeats identity and role unchanged:

- `status`: `complete | blocked`;
- implementation/fix: changed paths, checks, evidence, finding disposition when applicable;
- reviewer: reviewed SHA, verdict, Critical/High findings with exact paths/symbols and evidence;
- blocked: exact blocker plus one needed action/recheck.

Reject late, interrupted, replaced, duplicate, foreign, out-of-scope, or Git-inconsistent result. Preserve as evidence only.

## Review checkpoints

Default: one review checkpoint after each implementation worker returns. Close writer barrier, verify scope, commit accepted worker changes, require owned paths clean, freeze exact SHA, then dispatch fresh exact `sol_medium` reviewer before next implementation worker.

Accepted plan may group multiple implementation workers into one checkpoint only when combined chunk creates stronger review boundary than partial worker states. Plan must name checkpoint, covered tasks/workers, join condition, and technical rationale. Valid rationale: producer/consumer contract, coordinated code/serialized asset wiring, or another state whose partial review lacks meaningful proof. Throughput or fewer reviewer calls is insufficient. Missing explicit grouped checkpoint -> per-worker review.

Parallel workers belong to one explicit grouped checkpoint. Wait for every covered worker, stop writers, close barrier, verify combined scope, commit, require owned paths clean, and freeze before review. No downstream worker crosses checkpoint dependency gate before verdict/fix disposition.

Review scope: checkpoint diff from `review_base_sha` to `frozen_sha`, plus Critical/High integration risks visible at frozen SHA. Fix result advances accepted checkpoint head without re-review. Next checkpoint uses post-fix head as `review_base_sha`.

## Execution loop

1. Parse tasks and review checkpoints. Missing grouping -> one checkpoint per implementation worker. Dispatch bounded workers for next checkpoint only. Parallel dispatch only where plan explicitly proves disjoint ownership, stable inputs, and grouped review boundary.
2. Verify covered child reports against files, Git, scope, checks, and live identity. Stop writers. Close writer barrier. Require checkpoint join condition.
3. Stage only accepted owned paths. Commit checkpoint work. Verify owned paths clean, initial unrelated status preserved, scope, and exact full frozen SHA.
4. Dispatch fresh exact `sol_medium` reviewer for checkpoint. Reviewer reports Critical/High findings only and performs no edits/tests unless explicitly assigned.
5. No accepted finding -> advance to next checkpoint or final validation. Accepted finding -> one fresh fix worker with narrow finding-owned scope.
6. Stop fix writer, close barrier, verify scope, stage, commit, require owned paths clean, and freeze new full SHA. Do not re-review fix. Rerun checks invalidated by fix; pre-fix review does not prove post-fix behavior. Advance from post-fix head.
7. Repeat until every checkpoint has verdict and finding disposition. Run final checks at exact committed `HEAD`. Rehash accepted plan artifact. Verify owned paths clean, initial unrelated status preserved, branch, dependencies, owned path diff, and requirements.

Required unowned edit, plan decomposition change, dependency drift, artifact mismatch, or product decision outside accepted plan -> `blocked`. Route one needed action to LP in `lp-dispatched`; route it to user in `user-direct`. Active agent preserves accepted plan boundary and authority state.

## Completion routing

Return common facts:

- invocation mode, `plan_id`, `attempt_id`, active-agent identity, role `execution orchestrator`, profile;
- `status: complete | blocked`;
- artifact path, accepted digest/size, observed final digest/size;
- exact `start_sha`, dependency SHAs, branch, worktree, initial unrelated-status snapshot;
- clean committed exact plan SHA when complete;
- changed paths and scope proof;
- each checkpoint ID, covered executions, review base/frozen SHA, verdict, accepted finding dispositions;
- checks: command/workflow, working directory, observed result, evidence, exact SHA;
- blocker, evidence, and one needed authority action/recheck when blocked.

`lp-dispatched` also returns same `run_id` and LP-bound identity fields to LP. LP verifies Git/artifact facts and dispatches merging agent. Active agent returns plan SHA only.

`user-direct` returns facts directly to user, including launch checkout path/branch/status snapshot and created branch/worktree. Result remains isolated unless user explicitly authorized integration.

`complete` requires artifact match, exact committed head, owned paths clean, initial unrelated status preserved, owned-only committed diff, every worker covered by completed checkpoint review/fix flow, and passing final checks.
