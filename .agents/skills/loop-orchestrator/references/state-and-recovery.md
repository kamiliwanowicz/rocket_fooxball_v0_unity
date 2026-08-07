# State and Recovery

Use this reference for identity checks, writer safety, recovery, target drift, and cleanup. Git and collaboration observations outrank worker prose.

## Identity and acceptance

Every dispatch and return shares one single-use `execution_id`, exact `assigned_agent`, task, role, full `baseline_sha`, branch, worktree, owned paths, protected paths, dependencies, and allowed Git operations.

Accept completed current assignment only when:

1. Return carries same single-use `execution_id` and exact `assigned_agent` binding; collaboration state shows current assignment completed or stopped, not interrupted or replaced.
2. Git shows same worktree, branch, baseline ancestry, current head, clean status, and path scope.
3. Return names changed paths or reviewed SHA, checks/evidence, and completion or blocker action.

Reject late, duplicate, replaced, interrupted, or foreign result when identity, Git facts, or scope differ. Keep rejected report as evidence hint; never promote it by rewriting fields. Recheck current facts before retry.

## Writer barrier

Child writers edit only owned paths and perform no Git operations. One active writer per path. Parent inspects `git status` and diff, confirms every changed path, stops writers, and closes barrier before stage, commit, freeze, review, or shared validation.

Uncertain writer identity, overlapping scope, or abandoned process -> interrupt siblings sharing worktree; quarantine uncertain changes outside accepted scope or report `blocked`. Do not stage or commit while uncertainty remains.

## Review and fix binding

Independent read-only review starts from clean committed exact full SHA. Reviewer records reviewed SHA and Critical/High findings only. Accepted finding receives one fresh fix worker with narrow path scope. Fix worker returns changed paths, proof, and new clean full SHA. No fix re-review; rerun checks invalidated by fix. Pre-fix review never proves post-fix SHA.

## Recovery bootstrap

For long or multi-worktree runs, LP may write optional facts-only phase checkpoint under Git common dir, never product tree. Note current flow step, objective, baseline SHA, active execution IDs, branches/worktrees, owned/protected paths, accepted SHAs, blockers, and cleanup candidates. Omit checkpoint for short single-plan work.

On resume, inspect Git and collaboration tools first. Treat checkpoint as stale when facts differ. Preserve every reachable useful commit. For ambiguous Git/tool operation, inspect current repository and process state before repeating operation. Never infer success from missing output.

## Target drift and integration recovery

Integrator rereads target branch HEAD immediately before merge and final handoff. Fast-forward only when target remains expected or is ancestor of candidate. Target divergence -> stop, isolate reintegration worktree, reapply exact accepted SHAs, invalidate affected checks, and rerun them at new SHA. User branch remains unchanged without explicit authority binding target and candidate SHA.

## Cleanup

Stop and verify every writer before cleanup. Remove temporary worktrees or branches only after useful SHAs remain reachable, final evidence is readable, and no accepted result can still be mutated. Preserve ambiguous or rejected artifacts until LP records disposition.

Completion: current Git facts, accepted SHA, scope, review/fix proof, checks, and cleanup all agree; otherwise return `blocked` with exact mismatch and needed action.
