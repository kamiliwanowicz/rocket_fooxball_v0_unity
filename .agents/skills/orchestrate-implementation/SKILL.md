---
name: orchestrate-implementation
description: Use when executing implementation plans or multi-file coding changes through delegated subagents, especially work needing parallel ownership lanes, independent code review, review-fix workers, and evidence-based completion.
---

# Orchestrate Implementation

Act only as orchestrator. Delegate implementation, testing, validation, review, and review fixes. Read enough context to partition work, resolve ownership, monitor progress, and assess evidence completeness. Make no product-code edits. Perform no substantive code review.

Own canonical worker, reviewer, and fix-worker prompt and response formats. When executing plan from `$write-orchestrator-coding-plan`, map work-packet data into templates below. Ignore copied or embedded prompt schemas in plans.

Use standard Markdown, never JSON. Apply `$llm-oriented-markdowns` to every subagent prompt and require same style for responses. Include only task-critical details and exact identifiers.

## Ownership And State

- `LP`: control ledger, orchestration artifacts, global scheduling, recovery, and authorized user-branch merge.
- plan supervisor: sole Git owner for one plan branch/worktree; stages, commits, reconciles cleanliness, and freezes accepted exact head. Its one Git-owner attempt does not overlap another plan-supervisor attempt on that branch/worktree. Creates no nested worker worktrees.
- implementation and review-fix workers: child edit leases; edit owned files only. No Git operations, branch/worktree mutation, staging, or commits. Parallel child leases are allowed only for disjoint owned paths; one active child edit lease per owned path.
- reviewer: inspect frozen state only. No edits or Git operations.
- merging supervisor: sole Git owner for active integration branch/worktree lease. Stops at verified integration head; `LP` alone merges user/original/default branch.

Immutable plan/report artifacts may live under `loop-runs/{run_id}/artifacts/` in control worktree; control worktree holds ledger and orchestration artifacts only. `LP` provisions one writable plan branch/worktree after plan convergence and before plan-supervisor dispatch. Standalone planning may create `P0`; loop-owned planning verifies pre-provisioned exact branch/worktree and does not create a duplicate.

Every dispatch and result carries `{dispatch_id, attempt_id, lease_id, entity_generation, baseline_sha}`. Acceptance binds result to that tuple plus relevant entity state. Unrelated ledger revision changes do not invalidate a result; entity mutation, supersession, lease closure, or baseline change does. `LP` re-reads latest ledger and writes with latest-revision CAS.

Accepted state means committed clean exact full SHA. After freeze, any branch mutation requires a fresh lease and invalidates affected review, validation, and evidence. Evidence names exact frozen head; assertions never substitute for observed Git facts.

## Roles

- implementation: exact `luna_max` profile. Maximum-effort worker.
- review: exact `sol_medium` profile. Independent code reviewer.
- review fixes: fresh `luna_max` worker. Never original worker or reviewer.
- orchestrator: decomposition, dispatch, blocker resolution, evidence accounting, final synthesis only.

Never substitute profiles. Never absorb delegated work when slots, tools, or agents fail. Report blocker or retry delegation.

## Workflow

### 0. Select route

Use direct route when task has one coherent plan context, no cross-plan dependency or parallel-plan gain, one recovery boundary, no long-running external operation, and stable writable ownership:

`one worktree -> one writer lane -> writer barrier -> one review -> fresh fix worker if needed -> final validation -> handoff`

Direct route still names plan supervisor as sole Git owner; `LP` provisions one isolated worktree before worker dispatch.
Direct route keeps no durable ledger; handoff records exact branch, worktree, baseline, frozen head, review, fix proof, validation, and cleanup.

Use durable plan route when multi-plan execution, dependency waves, recovery duration, or integration risk earns ledger/worktree overhead. Do not add durable machinery to direct-route work.

Completion: route, worktree, owner, and review boundary are named before dispatch.

### 1. Partition

Convert request or plan into smallest coherent lanes. Default to one coherent plan-wide lane; split only for disjoint writes, real dependency, independent recovery, or a needed early contract boundary.

For each lane define:

- objective and acceptance criteria
- owned files, symbols, or subsystem
- protected scope
- dependencies and start condition
- required validation
- proof needed to discriminate changed behavior from pre-change behavior

Prefer non-overlapping ownership. Parallel lanes edit only owned files. Serialize shared files, shared contracts, mutable serialized assets, and validation environments. Lane consuming upstream contract waits for accepted upstream review/fix state, not worker completion alone.

Completion: every requirement maps to one named lane; ownership and dependency edges explicit.

### 2. Dispatch implementation

Spawn one `luna_max` worker per ready lane with Worker Prompt Template. Give task-local context only. Include plan ID, exact dispatch identity, baseline SHA, branch/worktree, paths, constraints, acceptance criteria, proof bar, and response schema. Worker owns file edits only; plan supervisor retains all Git ownership.

Monitor reports. Request missing evidence or clarification from same worker when implementation scope stays unchanged.

Completion: every lane reports completed work with proof or exact blocker; no writer lease remains active for a lane entering review.

### 3. Resolve blockers

Treat ownership blockers as healthy scope control. Worker must stop before editing unowned scope and request exact extra file, symbol, or call-site.

Grant narrow, named extra scope when needed and conflict-free. Update ownership map before work resumes. Never grant broad subsystem access when one expression, symbol, or file suffices.

Keep cross-lane findings in owning lane. Route foreign-file work to its owner. When owned provider or API boundary can make untouched consumers safe by default, prefer that local contract hardening over foreign-lane edits.

Completion: each blocker resolved by narrow scope grant, owner routing, safe local boundary change, or explicit unresolved status.

### Pre-review semantic conflicts

If implementation evidence exposes a semantic conflict before a review boundary completes (behavior, contract, architecture, or acceptance mismatch), do not dispatch a Fix Worker. Close the current child edit lease, record the conflict, and dispatch an implementation attempt with the canonical Worker Prompt Template, fresh identity tuple, and exact new baseline. Treat conflict resolution as implementation work; run the writer barrier and normal review after it. Fix Worker dispatch is legal only for accepted `critical` or `high` findings from a completed review boundary.

Completion: every pre-review semantic conflict maps to an implementation attempt or explicit blocker; no Fix Worker is used before completed review evidence.

### Lease recovery

Keep one live plan-supervisor Git-owner attempt per branch/worktree. Permit parallel child edit leases in that worktree only when owned paths are disjoint; reject overlapping path leases. Same-worktree replacement for a plan-supervisor attempt or child edit lease starts only after confirmed termination and reconciled Git state. If an old writer remains unconfirmed, quarantine its branch/worktree and start replacement on a new branch/worktree from last accepted SHA. Reject late old results by closed lease and entity generation. Preserve reachable commits before cleanup.

Completion: no two live plan-supervisor attempts share one branch/worktree; no two live child leases share one owned path; every replacement and preserved commit is recorded.

### 4. Writer barrier

Close every active edit lease before review or shared-project validation. Plan supervisor then:

- reconciles owned paths and Git state;
- stages and commits implementation (or accepted fixes);
- records exact full frozen head SHA, commit list, and clean status;
- prevents further writer mutation until a fresh lease is authorized.

Bind review evidence, validation evidence, and requirement claims to frozen head SHA. Repository-wide validation starts only after barrier. Downstream lane needing upstream contract waits for accepted upstream review/fix state and frozen evidence. Any post-freeze mutation invalidates affected evidence and requires a new barrier.

Completion: all writers closed; one observed clean committed frozen head exists; no review or shared validation starts before this point.

### 5. Dispatch reviews

After writer barrier, spawn independent `sol_medium` reviewer with Reviewer Prompt Template against exact frozen head. Default to one plan-wide review. Add early lane review only when downstream contract consumption requires it; each review boundary runs once.

Reviewer judges correctness, regressions, security, validation gaps, and proof quality. Report only `critical` or `high` findings. Reviewer must decide whether tests fail against relevant pre-change behavior, not accept passing tests as sufficient proof.

Orchestrator checks review response completeness only. Orchestrator does not inspect code as substitute review.

Completion: every review boundary has one independent report naming frozen head; no reviewer observes mutable writer state.

### 6. Dispatch review fixes

Group compatible in-lane findings. Spawn fresh `luna_max` worker with Fix Worker Prompt Template. Give exact findings, owned scope, acceptance criteria, and required validation.

Fix worker edits only. Run at most one accepted fix cycle per review boundary. Do not send fixes for another review. After fixes, repeat writer barrier: plan supervisor stages, commits, reconciles clean status, and freezes exact post-fix head. Fix worker reports the pre-fix frozen head and `Final frozen head: pending_plan_supervisor_freeze`; plan supervisor binds final behavior proof to the exact post-fix frozen head. Final validation covers invalidated behavior. Report unresolved findings or proof gaps as residual risk.

Completion: every accepted finding maps to proven fix or explicit unresolved status; one review pass per boundary; no fix re-review.

### 7. Final-state validation

Run assigned final checks only after final writer barrier. Bind every check to exact final frozen head SHA and record command, working directory, observed result, and artifact. Re-run only checks invalidated by a named post-freeze change. Plan supervisor accepts only committed clean exact head with no live lease able to mutate it.

Completion: final checks pass at one frozen head, or exact blocker and invalidated evidence are recorded; branch, worktree, and evidence reconcile.

### 8. Report

Return user-facing outcome from agent evidence. Include:

- selected route and exact final frozen head
- delivered lanes and key files
- validation and discriminatory proof
- review findings and fix disposition
- unresolved risks or blockers
- every environment trap encountered
- every wasted subagent run or miscommunication

Write exact `None reported` for empty trap or waste sections. Keep empty markers compact; do not invent verbose `None` trees. Never hide failed, duplicated, blocked, wrongly scoped, or preventable runs.

Completion: every requirement, finding, trap, and run accounted for.

## Proof Rules

- assertion is not proof. Require commands, observed output, diffs, screenshots, logs, or other reproducible artifacts.
- bug fix: require red-green evidence where safe and practical. In isolated owned scope, restore relevant pre-change behavior, observe targeted case fail, restore intended change byte-identically, observe pass.
- negative control: use only when safe. Do not mutate serialized assets temporarily; use alternate discriminatory evidence when rollback is unsafe or impractical, and record reason.
- test quality: show test would reject relevant pre-change behavior. Passing only on changed code is insufficient.
- final state: prove intended change restored after negative control and unrelated state preserved at named frozen head.
- reviewer: inspect evidence provenance and discrimination, not worker confidence language.

## Run Accounting

Environment trap: environment behavior that blocked, distorted, or slowed work. Capture agent, lane, exact error or symptom, trigger, workaround, impact, and suggested permanent fix. Examples: tool/version mismatch, editor lock, sandbox boundary, path quoting, missing dependency, hidden generated state, flaky command.

Wasted run: run producing no useful implementation or review because of preventable orchestration failure. Capture agent, lane, cause, cost or delay, recovery, and prompt/workflow change. Include duplicate dispatch, wrong profile, missing context, conflicting ownership, ambiguous acceptance criteria, or unusable response format.

Miscommunication: prompt or handoff ambiguity causing rework, wrong scope, or missing proof. Record separately even when run still produced useful work.

## Worker Prompt Template

Context mapping is strict: `Context: plan` uses `Entity: plan:{plan_id}`, `Plan: {plan_id | direct_route}`, and `Lane: {lane_id | plan-wide}`; `Context: integration` uses `Entity: integration:{integration_id}`, `Plan: None`, and `Lane: integration-wide`. Reviewer and fix-worker `Review boundary` uses `lane-contract`, `plan-wide`, or `integration-wide`. Each applicable response repeats context, plan, lane, entity, and boundary fields unchanged so role and durable outcome mapping stay deterministic.

Raw response mapping is strict: implementation `Status: complete` -> `implementation_complete`; implementation `Status: blocked` -> `blocked`; reviewer `Verdict: pass` -> `review_passed`; reviewer `Verdict: findings` -> `changes_required`; fix-worker `Status: complete` -> `fix_complete`; fix-worker `Status: blocked` -> `blocked`. Context does not alter mapping. An `unresolved` finding remains open and blocks review acceptance; it does not change the raw fix-worker mapping. Pre-review semantic conflicts always use the Implementation Worker mapping, never `fix_complete`.

```text
Role: implementation worker
Context: plan | integration
Plan: {plan_id | direct_route | None for integration}
Lane: {lane_id | plan-wide | integration-wide}
Entity: plan:{plan_id} | integration:{integration_id}
Dispatch: {dispatch_id}
Attempt: {attempt_id}
Lease: {lease_id}
Entity generation: {positive_integer}
Baseline: {full_sha}
Branch: {exact_branch}
Worktree: {absolute_path}
Objective: {objective}
Owned scope: {exact_files_symbols_or_subsystem}
Protected scope: {must_not_edit}
Dependencies: {inputs_and_start_state}
Read scope: {focused_paths_and_symbols}
Required changes:
- {implementation_action}
Constraints:
- {lane_specific_rule}
Acceptance criteria:
- {criterion}
Required validation:
- {command_or_check}
Required proof:
- {discriminatory_evidence}

Implement and validate owned lane. Edit owned files only. Do not run Git operations or mutate branch/worktree, stage, commit, or freeze. Preserve unrelated work. Stop before unowned edits; request narrow named scope. Report cross-lane findings without editing foreign scope.

Respond exactly:
Context: plan | integration
Plan: {plan_id | direct_route | None for integration}
Lane: {lane_id | plan-wide | integration-wide}
Entity: plan:{plan_id} | integration:{integration_id}
Dispatch: {dispatch_id}
Attempt: {attempt_id}
Lease: {lease_id}
Entity generation: {positive_integer}
Baseline: {full_sha}
Status: complete | blocked
Changed:
- {file_or_symbol}: {change}
Proof:
- {command_or_artifact}: {observed_result}
Negative control:
- {pre_change_failure_and_post_change_restoration | reason_not_run_and_alternate_evidence}
Validation:
- {check}: pass | fail — {result}
Blockers or scope requests:
- {exact_scope_and_reason | None}
Cross-lane findings:
- {owner_or_scope}: {finding | None}
Environment traps:
- {trigger; exact symptom; workaround; impact; permanent_fix | None}
Waste or miscommunication:
- {cause; impact; recovery; prevention | None}
```

## Reviewer Prompt Template

```text
Role: code reviewer. Review only; make no edits.
Context: plan | integration
Plan: {plan_id | direct_route | None for integration}
Lane: {lane_id | plan-wide | integration-wide}
Entity: plan:{plan_id} | integration:{integration_id}
Dispatch: {dispatch_id}
Attempt: {attempt_id}
Lease: {lease_id}
Entity generation: {positive_integer}
Baseline: {full_sha}
Frozen review head: {full_sha}
Review boundary: {lane-contract | plan-wide | integration-wide}
Objective: {objective}
Owned review scope: {exact_files_symbols_or_diff}
Acceptance criteria:
- {criterion}
Worker evidence:
{worker_report_or_artifact_paths}

Review exact frozen review head only. Run no Git operations, tests, or edits. Review correctness, regressions, security, validation gaps, and proof discrimination. Report only `critical` or `high` findings. Verify supplied tests would reject relevant pre-change behavior. Trace cross-lane impact, but keep findings assigned to owning lane. Plan supervisor records `Frozen review head` as `Reviewed head`; post-fix barrier records separate `Final frozen head`.

Respond exactly:
Context: plan | integration
Plan: {plan_id | direct_route | None for integration}
Lane: {lane_id | plan-wide | integration-wide}
Entity: plan:{plan_id} | integration:{integration_id}
Dispatch: {dispatch_id}
Attempt: {attempt_id}
Lease: {lease_id}
Entity generation: {positive_integer}
Baseline: {full_sha}
Frozen review head: {full_sha}
Review boundary: {lane-contract | plan-wide | integration-wide}
Verdict: pass | findings
Findings:
- {id} | {critical | high} | {owner_scope} | {file:line} | {defect} | {impact} | {evidence} | {required_fix}
Proof assessment:
- {evidence}: discriminates | does_not_discriminate — {reason}
Cross-lane findings:
- {target_owner_or_scope}: {finding | None}
Environment traps:
- {trigger; exact symptom; workaround; impact; permanent_fix | None}
Waste or miscommunication:
- {cause; impact; recovery; prevention | None}
```

## Fix Worker Prompt Template

```text
Role: review-fix worker. No later review run follows; supply complete final proof.
Context: plan | integration
Plan: {plan_id | direct_route | None for integration}
Lane: {lane_id | plan-wide | integration-wide}
Entity: plan:{plan_id} | integration:{integration_id}
Dispatch: {dispatch_id}
Attempt: {attempt_id}
Lease: {lease_id}
Entity generation: {positive_integer}
Baseline: {full_sha}
Pre-fix frozen head: {full_sha}
Review boundary: {lane-contract | plan-wide | integration-wide}
Branch: {exact_branch}
Worktree: {absolute_path}
Owned scope: {exact_files_symbols_or_subsystem}
Protected scope: {must_not_edit}
Findings:
- {finding_id}: {required_fix_and_evidence}
Acceptance criteria:
- {criterion}
Required validation:
- {command_or_check}

For fixes, `Baseline` must equal `Pre-fix frozen head`; both name exact reviewed state before fix edits. Plan supervisor records that reviewed head separately from the post-fix final frozen head. Fix accepted findings in owned scope. Edit files only. Do not run Git operations or mutate branch/worktree, stage, commit, or freeze. Preserve unrelated work. Stop before unowned edits; request narrow named scope. Report cross-lane findings without editing foreign scope. Plan supervisor freezes post-fix state before final validation.

Respond exactly:
Context: plan | integration
Plan: {plan_id | direct_route | None for integration}
Lane: {lane_id | plan-wide | integration-wide}
Entity: plan:{plan_id} | integration:{integration_id}
Dispatch: {dispatch_id}
Attempt: {attempt_id}
Lease: {lease_id}
Entity generation: {positive_integer}
Baseline: {full_sha}
Pre-fix frozen head: {full_sha}
Review boundary: {lane-contract | plan-wide | integration-wide}
Final frozen head: {full_sha | pending_plan_supervisor_freeze}
Status: complete | blocked
Finding disposition:
- {finding_id}: fixed | unresolved — {change_or_reason}
Changed:
- {file_or_symbol}: {change}
Proof:
- {command_or_artifact}: {observed_result}
Negative control:
- {pre_fix_failure_and_final_restoration | reason_not_run_and_alternate_evidence}
Validation:
- {check}: pass | fail — {result}
Residual risks:
- {risk | None}
Blockers or scope requests:
- {exact_scope_and_reason | None}
Environment traps:
- {trigger; exact symptom; workaround; impact; permanent_fix | None}
Waste or miscommunication:
- {cause; impact; recovery; prevention | None}
```
