---
name: orchestrate-implementation
description: Use when LP delegates one accepted coding plan or delegated implementation needs bounded workers, independent review, fixes, and exact-SHA validation.
---

# Orchestrate Implementation

Execution-orchestrator contract for one accepted coding plan. LP-dispatched execution orchestrator uses exact `sol_high` and becomes sole Git owner for bound plan branch/worktree. It dispatches implementation, reviewer, and fix workers; owns writer barriers, commits, freezes, scope checks, and final validation. LP owns run state, cross-plan coordination, merging-agent dispatch, and user-branch authority.

## LP handoff contract

LP dispatch binds:

- `run_id`, stable `plan_id`, unique `attempt_id`;
- assigned exact execution-orchestrator identity, role, profile `sol_high`;
- accepted plan artifact absolute path, SHA-256 digest, byte size;
- covered requirement IDs and objective;
- exact accepted baseline SHA and accepted dependency SHAs;
- exact plan branch and isolated worktree;
- owned/protected paths;
- checks, proof boundary, evidence locations;
- allowed Git operations limited to plan branch/worktree;
- LP state-file path, read-only for execution orchestrator.

Missing/mismatched field -> `blocked` before product mutation.

## Artifact gate

Before first worker dispatch:

1. Read LP state and dispatch identity.
2. Verify artifact path exists, byte size matches, SHA-256 matches accepted digest, and artifact identity/baseline/dependencies match dispatch.
3. Verify worktree/branch, clean status, HEAD, baseline, dependencies, ownership, and allowed Git operations.

Digest or identity mismatch -> `blocked` with observed digest/size and needed LP action. Perform no product mutation or worker dispatch. Accepted artifact remains immutable throughout attempt; rehash before final return. Post-dispatch mismatch invalidates attempt.

## Ownership and profiles

- Execution orchestrator: sole Git owner for plan worktree. Creates no sibling plan/integration worktrees and never mutates user branch.
- Implementation/fix workers: edit assigned owned paths only; no Git staging, commits, branch/worktree operations, or state edits.
- One writer per path. Parallel writers require disjoint paths and stable inputs. Serialize shared contracts, generated/serialized assets, migrations, and shared validation environments.
- Reviewer: fresh exact `sol_medium`; read-only exact frozen SHA.
- Implementation worker: exact profile required by plan/user/AGENTS; otherwise `luna_max`.
- Fix worker: fresh exact profile required by plan/user/AGENTS; otherwise `luna_max`.
- Required profile unavailable -> `blocked`; no silent substitution.

Execution orchestrator closes writer barrier before Git mutation, freeze, review, or shared validation.

## Child dispatch contract

Each child dispatch carries unique `execution_id`, assigned identity/profile/role, bounded task/done condition, objective/exclusions, full baseline SHA, exact branch/worktree, owned/protected paths, accepted dependencies, allowed Git operations (`None` for writers; read-only for reviewer), checks, proof/evidence boundary, and plan identity/path/digest.

Reviewer dispatch also binds `frozen_sha`. Fix dispatch binds `pre_fix_frozen_sha`, accepted finding IDs, finding-owned paths, and acceptance criteria.

Child return repeats identity and role unchanged:

- `status`: `complete | blocked`;
- implementation/fix: changed paths, checks, evidence, finding disposition when applicable;
- reviewer: reviewed SHA, verdict, Critical/High findings with exact paths/symbols and evidence;
- blocked: exact blocker plus one needed action/recheck.

Reject late, interrupted, replaced, duplicate, foreign, out-of-scope, or Git-inconsistent result. Preserve as evidence only.

## Execution loop

1. Parse accepted plan tasks. Dispatch fewest bounded implementation workers. Parallel dispatch only where plan explicitly proves disjoint ownership and stable inputs.
2. Verify child reports against files, Git, scope, checks, and live identity. Stop writers. Close writer barrier.
3. Stage only accepted owned paths. Commit plan work. Verify clean worktree, scope, and exact full frozen SHA.
4. Dispatch fresh exact `sol_medium` reviewer against frozen SHA. Reviewer reports Critical/High findings only and performs no edits/tests unless explicitly assigned.
5. No accepted finding -> final validation. Accepted finding -> one fresh fix worker with narrow finding-owned scope.
6. Stop fix writer, close barrier, verify scope, stage, commit, and freeze new clean full SHA. Do not re-review fix. Rerun checks invalidated by fix; pre-fix review does not prove post-fix behavior.
7. Run final checks at exact committed HEAD. Rehash accepted plan artifact. Verify clean worktree, branch, baseline ancestry, dependencies, owned path diff, and requirements.

Any required unowned edit, plan decomposition change, dependency drift, artifact mismatch, or product decision outside accepted plan -> `blocked` with needed LP action. Execution orchestrator never expands plan or edits LP state.

## Return to LP

Return concise facts:

- same `run_id`, `plan_id`, `attempt_id`, assigned identity, role `execution orchestrator`, profile `sol_high`;
- `status: complete | blocked`;
- artifact path, accepted digest/size, observed final digest/size;
- exact baseline, dependency SHAs, branch, worktree;
- clean committed exact plan SHA when complete;
- changed paths and scope proof;
- review SHA/verdict, accepted finding dispositions;
- checks: command/workflow, working directory, observed result, evidence, exact SHA;
- blocker, evidence, and one needed LP action/recheck when blocked.

`complete` requires artifact match, exact committed head, clean worktree, owned-only diff, completed review/fix flow, and passing final checks. LP verifies Git and artifact facts before acceptance. Execution orchestrator returns plan SHA only; merging agent handles integration.
