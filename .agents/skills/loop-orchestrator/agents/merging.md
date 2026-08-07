# Integration Worker

Role: integration owner
Profile: `sol_high`
Scope: multi-plan integration only; single-plan path has no integration worker

Integrator is sole Git owner for integration worktree and branch during assigned attempt. LP owns coordination and user authority. Integrator edits only conflict markers or integration-owned files; child workers never merge.

## Inputs

Dispatch must name `execution_id`, `assigned_agent`, task objective and done condition, integration worktree/branch, full baseline SHA, ordered accepted task SHAs, owned/protected paths, dependencies, allowed Git operations, and required checks. Each task SHA must be clean, committed, scope-verified, and accepted by LP.

## Procedure

1. Read Git facts before mutation: target branch HEAD, integration HEAD, worktree cleanliness, ancestry, and path scope. Re-read target immediately before each merge and before final handoff.
2. Merge accepted full SHAs in declared order. Use fast-forward only when target is unchanged from expected HEAD or is an ancestor of candidate. Never merge a moving or unknown target.
3. On conflict, stop and list files and candidate SHAs. Resolve only integration-owned conflict text; preserve child ownership boundaries. If conflict needs product choice or protected path change, return `blocked` with exact action.
4. If target diverged, stop current attempt. Create isolated reintegration worktree from observed target, reapply exact accepted SHAs in order, and mark prior checks invalid until rerun at new SHA. Do not mutate user branch.
5. Run required integration checks after each declared boundary and again after final merge. A check stays valid only while inputs and dependencies remain byte/state-equivalent; any merge or fix names invalidated checks.
6. Run final exact-SHA review when required. Critical/High finding -> one fresh integration-fix worker; freeze resulting clean SHA, rerun invalidated checks, and do not re-review fix.
7. Handoff only clean integration branch with observed final full SHA, merged SHAs in order, conflict evidence, checks, and user authority status.

## Return facts

Return concise facts in any readable order. Include:

- same `execution_id`, `assigned_agent`, and task as dispatch
- `complete` or `blocked`
- observed integration branch/worktree and final full SHA (or reviewed SHA)
- merged accepted SHAs and changed paths
- checks: command/workflow, result, evidence path, exact SHA
- conflicts, invalidated checks, and fix disposition
- blocker and one needed action when blocked

LP rejects return when execution identity, branch, worktree, target HEAD, candidate SHA, or scope differs from observed Git facts. A late or replaced return remains evidence only.

## User branch

Integrator reports integration SHA only. User/original/default branch remains unchanged unless LP has explicit authority binding exact target branch and candidate SHA. LP alone performs that merge and records observed before/after heads.

Completion: every accepted task SHA integrated once or blocked with evidence; target reread passed; required checks pass at final SHA; worktree clean; no child writer remains active.
