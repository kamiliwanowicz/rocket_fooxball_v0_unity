---
name: orchestrate-implementation
description: Use when active agent must orchestrate one accepted coding plan through bounded implementation workers, independent reviewers, fixes, and exact-SHA validation.
---

# Orchestrate Implementation

Invocation makes active agent execution orchestrator for one accepted coding plan. Active agent coordinates plan directly and remains sole Git owner for bound plan branch/worktree. It dispatches only implementation workers, reviewers, and fix workers; owns writer barriers, commits, freezes, scope checks, dependency gates, and final validation. Orchestration stays with active agent through completion or blocker return; never dispatch or hand off another orchestrator. LP owns run state, cross-plan coordination, merging-agent dispatch, and user-branch authority.

## LP handoff contract

LP dispatch binds:

- `run_id`, stable `plan_id`, unique `attempt_id`;
- assigned active-agent identity, role `execution orchestrator`, profile `sol_high`;
- accepted plan artifact absolute path, SHA-256 digest, byte size;
- covered requirement IDs and objective;
- exact accepted baseline SHA and accepted dependency SHAs;
- exact plan branch and isolated worktree;
- owned/protected paths;
- checks, proof boundary, evidence locations;
- allowed Git operations limited to plan branch/worktree;
- LP state-file path, read-only for active execution orchestrator.

Missing/mismatched field, including active-agent identity or role -> `blocked` before product mutation.

## Artifact gate

Before first worker dispatch:

1. Read LP state and verify dispatch assigns role `execution orchestrator` and profile `sol_high` to active agent.
2. Verify artifact path exists, byte size matches, SHA-256 matches accepted digest, and artifact identity/baseline/dependencies match dispatch.
3. Verify worktree/branch, clean status, HEAD, baseline, dependencies, ownership, and allowed Git operations.

Digest, identity, or role mismatch -> `blocked` with observed digest/size and needed LP action. Perform no product mutation or worker dispatch. Accepted artifact remains immutable throughout attempt; rehash before final return. Post-dispatch mismatch invalidates attempt.

## Ownership and profiles

- Active execution orchestrator: sole Git owner for plan worktree and coordinator for every plan checkpoint. Creates no sibling plan/integration worktrees and never mutates user branch.
- Child roles: implementation worker, reviewer, or fix worker only. Active execution orchestrator retains plan sequencing, worker coordination, result acceptance, Git operations, review gates, finding disposition, and final validation.
- Implementation/fix workers: edit assigned owned paths only; no Git staging, commits, branch/worktree operations, or state edits.
- One writer per path. Parallel writers require disjoint paths and stable inputs. Serialize shared contracts, generated/serialized assets, migrations, and shared validation environments.
- Reviewer: fresh exact `sol_medium` per review checkpoint; read-only exact frozen SHA.
- Implementation worker: exact profile required by plan/user/AGENTS; otherwise `luna_max`.
- Fix worker: fresh exact profile required by plan/user/AGENTS; otherwise `luna_max`.
- Required profile unavailable -> `blocked`; no silent substitution.

Active execution orchestrator closes writer barrier before Git mutation, freeze, review, or shared validation.

## Child dispatch contract

Each child dispatch carries unique `execution_id`, assigned identity/profile/role, bounded task/done condition, objective/exclusions, full baseline SHA, exact branch/worktree, owned/protected paths, accepted dependencies, allowed Git operations (`None` for writers; read-only for reviewer), checks, proof/evidence boundary, and plan identity/path/digest.

Reviewer dispatch also binds `checkpoint_id`, covered worker/task execution IDs, `review_base_sha`, and `frozen_sha`. Fix dispatch binds `pre_fix_frozen_sha`, accepted finding IDs, finding-owned paths, and acceptance criteria.

Child return repeats identity and role unchanged:

- `status`: `complete | blocked`;
- implementation/fix: changed paths, checks, evidence, finding disposition when applicable;
- reviewer: reviewed SHA, verdict, Critical/High findings with exact paths/symbols and evidence;
- blocked: exact blocker plus one needed action/recheck.

Reject late, interrupted, replaced, duplicate, foreign, out-of-scope, or Git-inconsistent result. Preserve as evidence only.

## Review checkpoints

Default: one review checkpoint after each implementation worker returns. Close writer barrier, verify scope, commit accepted worker changes, freeze clean full SHA, then dispatch fresh exact `sol_medium` reviewer before next implementation worker.

Accepted plan may group multiple implementation workers into one checkpoint only when combined chunk creates stronger review boundary than partial worker states. Plan must name checkpoint, covered tasks/workers, join condition, and technical rationale. Valid rationale: producer/consumer contract, coordinated code/serialized asset wiring, or another state whose partial review lacks meaningful proof. Throughput or fewer reviewer calls is insufficient. Missing explicit grouped checkpoint -> per-worker review.

Parallel workers belong to one explicit grouped checkpoint. Wait for every covered worker, stop writers, close barrier, verify combined scope, commit, and freeze before review. No downstream worker crosses checkpoint dependency gate before verdict/fix disposition.

Review scope: checkpoint diff from `review_base_sha` to `frozen_sha`, plus Critical/High integration risks visible at frozen SHA. Fix result advances accepted checkpoint head without re-review. Next checkpoint uses post-fix head as `review_base_sha`.

## Execution loop

1. Parse tasks and review checkpoints. Missing grouping -> assign one checkpoint per implementation worker. Dispatch bounded workers for next checkpoint only. Parallel dispatch only where plan explicitly proves disjoint ownership, stable inputs, and grouped review boundary.
2. Verify covered child reports against files, Git, scope, checks, and live identity. Stop writers. Close writer barrier. Require checkpoint join condition.
3. Stage only accepted owned paths. Commit checkpoint work. Verify clean worktree, scope, and exact full frozen SHA.
4. Dispatch fresh exact `sol_medium` reviewer for checkpoint. Reviewer reports Critical/High findings only and performs no edits/tests unless explicitly assigned.
5. No accepted finding -> advance to next checkpoint or final validation. Accepted finding -> one fresh fix worker with narrow finding-owned scope.
6. Stop fix writer, close barrier, verify scope, stage, commit, and freeze new clean full SHA. Do not re-review fix. Rerun checks invalidated by fix; pre-fix review does not prove post-fix behavior. Advance from post-fix head.
7. Repeat steps 1-6 until every checkpoint has verdict and finding disposition. Run final checks at exact committed HEAD. Rehash accepted plan artifact. Verify clean worktree, branch, baseline ancestry, dependencies, owned path diff, and requirements.

Any required unowned edit, plan decomposition change, dependency drift, artifact mismatch, or product decision outside accepted plan -> `blocked` with needed LP action. Active execution orchestrator never expands plan or edits LP state.

## Return to LP

Return concise facts:

- same `run_id`, `plan_id`, `attempt_id`, assigned active-agent identity, role `execution orchestrator`, profile `sol_high`;
- `status: complete | blocked`;
- artifact path, accepted digest/size, observed final digest/size;
- exact baseline, dependency SHAs, branch, worktree;
- clean committed exact plan SHA when complete;
- changed paths and scope proof;
- each checkpoint ID, covered executions, review base/frozen SHA, verdict, accepted finding dispositions;
- checks: command/workflow, working directory, observed result, evidence, exact SHA;
- blocker, evidence, and one needed LP action/recheck when blocked.

`complete` requires artifact match, exact committed head, clean worktree, owned-only diff, every worker covered by completed checkpoint review/fix flow, and passing final checks. LP verifies Git and artifact facts before acceptance. Active execution orchestrator returns plan SHA only; merging agent handles integration.
