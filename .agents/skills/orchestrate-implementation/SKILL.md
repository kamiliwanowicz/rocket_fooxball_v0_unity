---
name: orchestrate-implementation
description: Use when delegated implementation needs bounded workers, independent review, or review fixes.
---

# Orchestrate Implementation

Role contract for implementation workers, reviewers, and review-fix workers. Parent/supervisor owns task flow and Git. Shared recovery and integration behavior lives in [`$loop-orchestrator`](../loop-orchestrator/SKILL.md).

## Ownership and profiles

- Parent/supervisor is sole Git owner for task branch and worktree. Parent creates or selects worktree, stages, commits, freezes, verifies scope, and runs final validation.
- Child implementation and fix workers edit assigned owned paths only. They perform no Git mutation, staging, committing, branch/worktree mutation, or freeze.
- One writer per path. Parallel writers require disjoint paths and stable inputs. Serialize shared files, contracts, generated or serialized assets, and validation environments.
- Parent closes writer barrier before stage, commit, freeze, review, or shared validation.
- Reviewer inspects exact frozen SHA read-only. Reviewer performs no edits or tests unless dispatch explicitly assigns them.
- User and `AGENTS.md` profile requirements win. Otherwise use suitable available role: implementation defaults to `luna_max`, reviewer uses fresh exact `sol_medium`, and fix uses fresh exact `luna_max`. Required profile unavailability -> `blocked`; do not substitute silently.

## Dispatch contract

Each dispatch has one-use identity and task facts. Include:

- `execution_id`: unique ID, never reused
- `assigned_agent`: exact agent identity; include `role` (`implementation`, `reviewer`, or `fix`)
- `task`: bounded task name and `done_condition`
- `objective`: requested outcome and exclusions
- `baseline_sha`: full 40-character SHA
- `branch`, `worktree`: exact branch name and absolute worktree path
- `owned_paths`, `protected_paths`: exact repository-relative paths or symbols
- `dependencies`: accepted SHAs or `None`
- `allowed_git_ops`: exact operations and target; writer/fix workers -> `None`, reviewer -> read-only inspection when assigned
- `checks`: required commands/workflows and evidence locations

Reviewer dispatch also carries exact `frozen_sha` and review boundary. Fix dispatch carries `pre_fix_frozen_sha`, accepted finding IDs, and finding-owned paths.

Workers receive task-local context, objective, done condition, scope, dependencies, constraints, checks, and proof bar. Stop before unowned edits; request narrow named scope. Route foreign-path findings to owning worker.

## Return and acceptance

Return in any readable heading order. Repeat dispatch `execution_id`, `assigned_agent`, `role`, and `task` unchanged. Include:

- `status`: `complete` or `blocked`
- `changed_paths`: implementation/fix paths and concise changes, or `reviewed_sha` for reviewer
- `checks`: command/workflow, observed result, and evidence path
- `blocker`, `needed_action`: exact blocker and one action or recheck fact when `blocked`; `None` when complete

Parent accepts only when current execution identity plus observed collaboration, Git, branch, worktree, baseline/head, and scope facts match dispatch. Reject late, replaced, or foreign results; preserve report as hint only. Parent verifies report claims against files and Git. A report cannot promote itself by changing fields.

## Implementation worker

Use one implementation worker per ready owned scope. Worker sequence:

1. Read dispatch facts and inspect assigned/protected paths.
2. Implement objective within owned paths. Keep unrelated changes untouched. Use project-required tools and APIs.
3. Run assigned checks; capture command, result, and reproducible evidence.
4. Return identity, `complete` with changed paths and proof, or `blocked` with exact scope request and needed action.

Success flow: dispatch -> edit/check owned scope -> worker `complete` -> parent verifies paths and closes writer barrier -> parent stages, commits, and freezes exact SHA -> reviewer dispatch.

## Reviewer

After writer barrier, parent dispatches an independent fresh exact `sol_medium` reviewer with exact `frozen_sha`. Reviewer performs read-only Git/file inspection against that SHA only. No edits or tests unless explicitly assigned. Reviewer checks correctness, regressions, security, scope, validation gaps, and proof discrimination. Report Critical/High findings only; assign each finding to exact path/symbol and required fix. Return `reviewed_sha`, verdict, finding evidence, and checks/evidence.

Review pass: no Critical/High finding -> parent final validation. Finding -> parent accepts or rejects finding using observed facts, then dispatches fresh fix worker for accepted scope.

## Review-fix worker

Parent dispatches fresh exact `luna_max` fix worker with `pre_fix_frozen_sha`, accepted Critical/High findings, exact finding-owned paths, acceptance criteria, and required checks. Fix worker:

- edits accepted finding paths only; stop and request scope for any other path
- performs no Git mutation or review
- supplies proof that each accepted finding is fixed and unrelated scope preserved
- returns finding disposition, changed paths, checks/evidence, residual risk, or exact blocker/action

No fix re-review. Parent closes writer barrier, stages, commits, and freezes post-fix exact SHA; reruns checks invalidated by fix and runs final validation at that committed SHA. Pre-fix review does not cover post-fix behavior.

## Parent final validation

Parent runs required final checks at exact current committed SHA and clean worktree. Bind each check to command, working directory, observed result, and evidence. Accept only matching branch/worktree, scope, clean status, and head. Missing or drifted fact -> `blocked` with evidence and needed action.

Finding/fix flow: frozen SHA -> fresh reviewer -> Critical/High finding -> accepted narrow path -> fresh fix worker -> proof -> parent commit/freeze -> rerun invalidated checks -> final validation; no re-review.
