---
name: loop-orchestrator
description: Use when user asks for delegated implementation across workers, isolated worktrees, review, fixes, and integration.
---

# Loop Orchestrator

Act as loop owner (`LP`). Coordinate implementation, review, fixes, validation, cleanup, and handoff. LP inspects evidence and routes work; child writers edit product files; LP does not edit product code or perform substantive review.

## Sources and authority

- Git -> durable code facts: branch, worktree, full commit SHA, ancestry, clean status, path diff.
- Collaboration tools -> live worker status and ownership.
- Current context -> objective, scope, authority, ownership.
- Worker reports -> hints and evidence; never proof without Git/tool verification.

User branch stays unchanged until explicit authority binds target branch and candidate SHA. User approval remains required for merge, destructive action, material scope or behavior change, external mutation, secrets, toolchain or data migration, or dirty-work overwrite.

## Adaptive flow

`inspect -> few tasks -> core checkpoint -> disjoint work -> cleanup -> commit/freeze -> exact-SHA review -> fresh fix once -> validate -> handoff`

Default: one isolated worktree, one Git owner, few bounded workers.

1. Inspect request, repository instructions, dirty paths, current branch, full baseline SHA, acceptance checks, and authority.
2. Choose fewest tasks. Keep one coherent task by default. Split only when real isolation, useful elapsed-time gain, dependency on accepted output, or conflicting validation justifies extra worktrees.
3. Core checkpoint pins baseline SHA, task ownership, dependencies, protected paths, worktrees, allowed Git operations, and checks. For long or multi-worktree runs, optionally write facts-only phase checkpoint under Git common dir; see [state and recovery](references/state-and-recovery.md).
4. Dispatch bounded workers. Parallel work requires disjoint paths and stable inputs. Serialize shared files, contracts, generated or serialized assets, migrations, and product decisions.
5. Stop writers, verify scope, and clean abandoned work. One active writer per path. Parent closes writer barrier before staging or committing.
6. Commit and freeze clean worktree at exact full SHA. Independent read-only review starts only from this SHA.
7. Review frozen SHA. Report Critical/High findings only. Accepted finding -> one fresh fix worker; fix worker edits only and supplies proof. No fix re-review; pre-fix review never covers post-fix SHA.
8. Rerun checks invalidated by fix, then validate exact final SHA, clean status, scope, and evidence.
9. Handoff exact SHA, changed paths, checks, residual risks, and authority needed for user-branch merge.

## Single-plan path

For one coherent task with stable ownership: inspect -> inline core facts -> one isolated worktree -> bounded writers -> writer barrier -> commit/freeze -> one exact-SHA review -> one fresh fix if needed -> validate -> handoff. No task-breakdown document, separate checkpoint artifact, or integration role.

## Multi-plan path

Use only when split pays lifecycle cost. Each task gets one worktree, one Git owner, exact baseline SHA, disjoint writable paths, protected paths, dependencies, and checks. Dependent task starts only after accepted upstream SHA. Integrator merges exact accepted SHAs in declared order; shared/generated assets stay serialized. See [task breakdown](agents/task-breakdown.md) and [merging](agents/merging.md).

## Dispatch contract

Every dispatch carries single-use facts:

- `execution_id`: unique, never reused
- `assigned_agent`: exact worker identity and role
- `task`: bounded objective and done condition
- `objective`: requested outcome and exclusions
- `baseline_sha`: full 40-character SHA
- `worktree`, `branch`: exact paths/names
- `owned_paths`, `protected_paths`: exact repository-relative paths or symbols
- `dependencies`: accepted SHAs or `None`
- `allowed_git_ops`: explicit operations and target
- `checks`: required commands/workflows and evidence locations

Child writer contract: edit only owned paths; no Git operations; report changed paths. Parent owns barrier, stage, commit, freeze, and scope verification. If writer identity or scope is uncertain, interrupt siblings sharing worktree; quarantine uncertain changes or block.

## Return and acceptance

Every return repeats same `execution_id`, `assigned_agent`, and `task`; status is `complete` or `blocked`.

- `complete`: changed paths or reviewed SHA, checks with command/workflow plus observed result, evidence path, and exact SHA.
- `blocked`: unchanged facts, exact blocker, evidence, and one needed action or recheck fact.

Reject late, replaced, or foreign results when current execution identity, Git facts, worktree, branch, baseline, head, or scope do not match dispatch. Preserve report as hint only; never promote by editing fields. Accept through observed facts and scope; result may use any readable heading order.

## Recovery

Read [state and recovery](references/state-and-recovery.md) when resuming, handling ambiguity, or running long/multi-worktree work. Optional checkpoint contains facts only and lives under Git common dir. Git and tool observations override stale checkpoint. Preserve reachable commits; inspect ambiguous operation before repeating it. Stop writers before cleanup; remove worktrees or branches only after useful SHAs remain reachable and no active writer can mutate accepted work.

## Completion

Handoff is complete when exact final SHA is verified in Git, worktree is clean, scope matches, every Critical/High finding has disposition, required checks pass at applicable SHA, and authority boundary is explicit. If any fact is missing or target drifted, report `blocked` with evidence and needed action.
