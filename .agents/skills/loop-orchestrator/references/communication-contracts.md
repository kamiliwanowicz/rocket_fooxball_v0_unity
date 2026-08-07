# Communication Contracts

Load immediately before every dispatch and report-acceptance decision. Applies to `LP` direct agents and nested-agent routing.

## Protocol

- agent interchange: strict Markdown
- external interchange: JSON only when boundary enforces JSON Schema
- durable run state: restricted YAML; format lives in [state and recovery](state-and-recovery.md)
- report body: one exact role artifact from this file or linked role contract
- linked contract precedence: linked role contract owns headings, field order, enums, path form, empty markers, and surrounding-prose rule
- local contract scope: Dispatch, Planner Return, Plan Supervisor Return, request, evidence, and accounting fields defined here
- local field order: template order
- local headings/colon fields: exact spelling and case
- local status/decision values: listed enum only
- local empty required field/section: `None`
- local empty accounting sections: `None reported`
- local bullet: one fact
- local path: absolute normalized filesystem path unless placeholder says otherwise
- local attempt: exact active attempt ID
- local branch: exact Git branch name
- local entity generation: positive integer for active entity
- local SHA: full 40-character lowercase Git object ID; `baseline_sha` may be `None` only for blocked task-breakdown attempt with unavailable baseline
- local digest: `sha256:` plus 64 lowercase hexadecimal characters over exact file bytes
- local command: exact executable, arguments, quoting, working directory, and relevant environment
- surrounding prose: absent
- missing heading/field/item, invalid enum, malformed identifier, or extra prose: invalid report

Nested report enums and pointers map only through field-level rules in [state and recovery](state-and-recovery.md). Role, stage, check-kind, and entity context select one deterministic mapping; unmapped combinations reject. Do not substitute durable enums, infer missing gate/result IDs, or treat summary prose as evidence.

Nested agent -> immediate parent -> `LP`. Nested agents address neither user nor durable ledger. Only `LP` requests user decisions and writes durable state.

## Result Identity And Acceptance

Every dispatch and result carries one identity tuple:

`{dispatch_id, attempt_id, lease_id, entity_generation, baseline_sha, input_state_digest, incoming_accepted_sha}`

- `dispatch_id`, `attempt_id`, `lease_id`: exact active IDs
- `entity_generation`: current positive generation for entity; replacement/supersession/lease closure advances or closes generation
- `baseline_sha`: exact committed full SHA, not global ledger revision; `None` only for blocked task-breakdown attempt with unavailable baseline
- `input_state_digest`, `incoming_accepted_sha`: integration-conflict child binding; `None` for every other dispatch

Durable child tuple also carries:

- `Entity: child_task:{child_task_id}`
- `Parent: plan:{plan_id} | integration:{integration_id}`
- `Parent attempt: {active_parent_attempt_id}`
- parent branch/worktree, role, task kind, scope, and review boundary when applicable

Result acceptance:

1. Match tuple against active entity, dispatch, attempt, lease, generation, baseline, and optional conflict fields.
2. For child result, match child identity and active parent attempt/worktree without closing parent lease. Apply conflict digest acceptance from [state and recovery](state-and-recovery.md) when task kind is `integration_conflict`.
3. Reconcile Git and evidence against baseline/frozen state; blocked task-breakdown attempt with `baseline_sha: None` records blocker evidence instead of fabricated Git facts.
4. Re-read latest ledger revision; apply latest-revision CAS and append event.
5. If CAS loses unrelated update, re-read and retry. Keep result eligible.
6. Reject on entity mutation, supersession, lease closure, baseline/binding change, stale generation, or relevant Git drift.

Global revision changes alone never reject result. Two parallel results dispatched from same revision may both accept in either completion order when tuples and relevant baselines remain valid. Late result from closed/expired/interrupted attempt is rejected and preserved as incident evidence.

## Dispatch

### Select contract

- task breakdown -> full [task breakdown report](../agents/task-breakdown.md)
- planner -> `$write-orchestrator-coding-plan` plus Planner Return below
- plan supervisor -> `$orchestrate-implementation` plus Plan Supervisor Return below
- merging supervisor -> full [merging report](../agents/merging.md)
- implementation/reviewer/review-fix worker -> canonical prompts/reports in [`$orchestrate-implementation`](../../orchestrate-implementation/SKILL.md) plus durable child identity overlay below; copy no worker schema here
- final-stage combined reviewer/integration fixer -> same linked templates plus `Context: integration`, `Plan: None`, `Lane: integration-wide`, `Entity: child_task:{child_task_id}`, `Parent: integration:{integration_id}`, and `Review boundary: integration-wide`; merging supervisor owns child dispatch and Git operations
- direct route -> canonical linked worker/reviewer/fix contracts plus transient direct child identity; no durable multi-plan ceremony

Exactly one role contract and one legal response artifact per dispatch. Every action, writable target, Git operation, input state, authority boundary, output artifact contract, and done condition appears once.

Source-outcome and ledger-field mappings live only in [Canonical Report Transitions](state-and-recovery.md#canonical-report-transitions). Dispatch supplies raw linked-contract enum; acceptance applies exact state mapping.

### Build dispatch

Use exact envelope:

```text
Role: breaker | planner | plan_supervisor | merging_supervisor
Phase: {phase_enum}
Objective: {single_observable_outcome}
Dispatch: {dispatch_id}
Attempt: {active_attempt_id}
Lease: {lease_id}; {expires_at}
Entity: {entity_kind}:{entity_id}
Entity generation: {positive_integer}
Baseline: {full_sha | None when blocked task-breakdown baseline is unavailable}
Input state digest: {sha256_digest | None}
Incoming accepted SHA: {full_sha | None}
Branch: {exact_branch | None}
Worktree: {absolute_path | None}
Owned scope:
- {absolute_path_or_exact_symbol_or_named_git_operation}
Protected scope:
- {absolute_path_or_exact_symbol_or_named_git_operation | None}
Authorization:
- {granted_action_or_boundary}
Inputs:
- {absolute_path_or_exact_identifier}: {digest_or_state_sha_or_value}
Required output: {exact_role_artifact_contract}
Completion criterion:
- {observable_exhaustive_role_result}
```

Plan-supervisor dispatch also includes plan ID, accepted plan digest, requirement IDs, slot budget, validation boundary, exact branch/worktree, and `P0: verify_preprovisioned`. Standalone planning may use `P0: create_standalone`. Merging dispatch also includes integration ID, ordered accepted plan heads, conflict policy, required joint checks, and user-branch authority state.

Durable nested child dispatch uses linked `$orchestrate-implementation` role template plus exact identity fields:

```text
Entity: child_task:{child_task_id}
Parent: plan:{plan_id} | integration:{integration_id}
Parent attempt: {active_parent_attempt_id}
Task kind: implementation | review | review_fix | integration_conflict | combined_review | integration_fix
Input state digest: {sha256_digest | None}
Incoming accepted SHA: {full_sha | None}
```

Child report repeats identity fields unchanged. Child branch/worktree equal active parent facts. Integration-conflict dispatch/report uses nonnull digest and incoming SHA; other child dispatches/reports use `None`. Direct route allocates transient direct plan/child IDs by [Direct Route And Canonical Pointers](state-and-recovery.md#direct-route-and-canonical-pointers); no new phase or durable record.

Plan-supervisor authorization: Git operations only on own plan branch/worktree; stage, commit, clean check, and freeze accepted head. Planner writes immutable plan artifact under control worktree and performs no product Git operation. Workers/reviewers/fix workers edit owned files only and perform no Git operations.

Merging-supervisor authorization: sole Git owner for active integration branch/worktree and lease. Authority ends on accepted, blocked, interrupted, expired, or cancelled attempt. User-branch merge is never inside this grant; `LP` alone performs it after explicit authority.

Completion: envelope parses; identity tuple, writable/protected targets, authority, capacity, evidence demand, and exhaustive done condition present once.

## Task breakdown report

Return one artifact: full exact [task breakdown report](../agents/task-breakdown.md). Linked report remains canonical for headings, attempt/lease, status, baseline, digest, decision, plan IDs, requirement coverage, path form, empty markers, and acceptance. Copy no breakdown schema here.

Status-specific acceptance matrix:

- `ready`: full baseline, decomposition, ownership, requirement coverage, dependency graph, validation boundaries
- `needs_user`: known evidence, one material question, affected requirements, safe continuing work; final plan graph may be absent
- `blocked`: exact unavailable fact, blocker evidence, recheck condition; unavailable fields use `None`

User answer or blocker resolution -> fresh breakdown attempt. Truthful `needs_user` or `blocked` report never invents final decomposition.

Completion: linked canonical report passes status-specific acceptance; one status and one decision; no wrapper or extension.

## Planner Return

Planner must use `$write-orchestrator-coding-plan`. Plan format remains canonical there. Planner artifact path must resolve inside control worktree `loop-runs/{run_id}/artifacts/` and become immutable after digest.

```text
Role: planner
Status: complete | blocked
Plan: {plan_id}
Dispatch: {dispatch_id}
Attempt: {attempt_id}
Lease: {lease_id}
Entity generation: {positive_integer}
Baseline: {full_sha}
Plan path: {absolute_control_worktree_artifact_path | None when blocked}
Digest: {sha256_digest | None when blocked}
P0: create_standalone | verify_preprovisioned | None when blocked
Requirement IDs:
- {requirement_id}
Forecast ownership:
- {absolute_path_or_exact_symbol}: {exclusive | shared_serialized}; {owner_plan_id}
Dependencies:
- {plan_id_or_external_input}: {satisfied | pending | blocked}; {evidence_or_condition}
Planning timing: now | just_in_time_after_integration | None when blocked
Baseline rule: exact_input_baseline | latest_integration_head_after_dependencies | None when blocked
Blockers:
- {exact_blocker_and_needed_owner_or_action | None}
Environment traps:
- {accounting_item | None reported}
Waste or miscommunication:
- {accounting_item | None reported}
```

`now` pairs with `exact_input_baseline`; `just_in_time_after_integration` pairs with `latest_integration_head_after_dependencies`. `complete` requires exact immutable control artifact, digest, full requirement coverage, forecast ownership, confirmed dependencies, one `P0` action, no blocker, and no product Git mutation.
`blocked` may use `None` for unavailable artifact, requirement, ownership, dependency, or `P0` fields; baseline remains required from accepted plan input. Record exact blocker and needed owner/action.

## Plan Supervisor Return

Plan supervisor uses `$orchestrate-implementation`. Worker, reviewer, fix-worker, proof, negative-control, review, and run-accounting formats remain canonical there. This return owns only plan-level aggregation.

```text
Role: plan supervisor
Status: complete | blocked
Plan: {plan_id}
Dispatch: {dispatch_id}
Attempt: {attempt_id}
Lease: {lease_id}
Entity generation: {positive_integer}
Baseline: {full_sha}
Branch: {branch}
Worktree: {absolute_path}
Head: {full_sha | None}
Reviewed head: {full_sha | None}
Final frozen head: {full_sha | None}
Clean worktree: true | false
Writer barrier: closed | blocked
Commits:
- {full_sha | None}
Requirements:
- {requirement_id}: delivered | blocked; {evidence_location_and_state_sha}
Reviews:
- {review_boundary_id_or_lane_id}: pass | findings_resolved | blocked; reviewed {full_sha}; review evidence {evidence_location_and_state_sha}; fix proof {evidence_location_and_state_sha | None}
Validation:
- {check_id}: pass | fail | not_run; {state_sha_or_None}; {evidence_location_or_reason}
Blockers:
- {exact_blocker_and_needed_owner_or_action | None}
Environment traps:
- {accounting_item | None reported}
Waste or miscommunication:
- {accounting_item | None reported}
```

`complete` requires non-`None` `Head`, `Reviewed head`, and `Final frozen head`; `Head` and `Final frozen head` equal observed final branch `HEAD`; clean worktree; `Writer barrier: closed`; every planned commit; every requirement `delivered`; every review `pass` or `findings_resolved`; and every required validation `pass` at `Final frozen head`. Without a post-review fix, `Reviewed head` equals `Final frozen head`. `findings_resolved` permits a different `Final frozen head` only when each accepted finding has fix proof and invalidated final validation binds to that head; child `Final frozen head: pending_plan_supervisor_freeze` never counts as completion evidence; never rewrite review evidence or dispatch a fix re-review. Writer barrier closes all active edit leases before review or shared-project validation. Review evidence names `Reviewed head`; final validation and accepted fix proof name `Final frozen head`. Default one plan-wide review; add early lane review only before downstream contract consumption. One pass per review boundary; fresh fix worker supplies proof; no fix re-review. `blocked` preserves completed dispositions and names remaining owner/action.

Plan Supervisor Return acceptance uses [Canonical Report Transitions](state-and-recovery.md#canonical-report-transitions). Pointers must name exact evidence and state SHA.

## Merging report

Return one artifact: full exact [merging report](../agents/merging.md). No wrapper or local schema override. Canonical report owns identity, stage, status, merge results, final substates, checks, clean state, blockers, and accounting.

Merger procedure owns stage order. Durable gate/nullability/field writes use [Canonical Report Transitions](state-and-recovery.md#canonical-report-transitions); aggregate report references accepted child fields without rewriting them.

User branch: merger reports integration head only plus `not_authorized` or `authorized_pending_lp`. `LP` alone merges exact SHA into explicitly authorized user/original/default branch and records observed before/after heads. Merger cannot claim user-branch merge complete.

Completion: validate canonical merging report against active lease, integration Git facts, final-substate gates, per-check matrix, and user-branch authority. Nested `Combined review`, `Final validation`, and `Requirement accounting` values map only through field-level rules in the merging/state contracts. `wave_complete` is intermediate only; final `complete` is READY transition only.

## Acceptance workflow

### 1. Validate shape

Select one legal role artifact. Apply linked syntax to linked artifact and local syntax here. Validate headings, order, enums, identity tuple, identifiers, completeness, path form, empty markers, and surrounding-prose rule. Validate digests by owning contract; integration-conflict input digest uses stored-binding acceptance from [state and recovery](state-and-recovery.md), not current-worktree recomputation.

Done: one artifact passes; no duplicate envelope, extension, unknown field, missing field, malformed value, or contradiction.

### 2. Validate identity and lease

Match result tuple against active dispatch, attempt, lease, entity generation, and relevant baseline. Confirm lease valid and owned by reporting agent. Reject stale, duplicate, superseded, closed, interrupted, expired, or foreign result.

Replacement and quarantine follow child/parent recovery in [state and recovery](state-and-recovery.md#idempotency). Acceptance rejects late result; preserve it as incident evidence.

Done: exactly one active matching tuple; no stale result eligible.

### 3. Validate Git facts

Inspect repository. Match baseline, branch, worktree, head, commits, ancestry, and clean status against active dispatch. Agent assertion has zero acceptance weight. For plan results, exact frozen head must match plan branch. For integration results, exact integration head must match merger report.

Done: every reported Git fact equals observed fact; evidence head immutable for review.

### 4. Validate evidence

Open evidence artifacts. Match owner, exact command/workflow, working directory, observed result, artifact path, and exact state SHA. Writer barrier must be closed before review or shared-project validation. Evidence remains valid only while behavior/dependencies remain state-equivalent; later mutation names exact invalidation and rerun owner.

One review per review boundary. Findings route to fresh fix worker. Fix worker supplies final proof. No fix re-review. Final validation reruns invalidated checks and binds result to final head. Final integration fixes close child edit lease and freeze candidate head before checks.

Done: every disposition has reproducible proof at applicable frozen SHA; absent negative control has reason plus alternate discrimination.

### 5. Validate accounting

Map every requirement, finding, accepted fix, check, blocker, environment trap, wasted run, and miscommunication once. Compare artifact fields against canonical contract and evidence. Do not duplicate worker/reviewer/fix schemas; follow `$orchestrate-implementation` pointer.

Done: no uncovered, duplicated, conflicting, or silently dropped item.

### 6. Decide

Re-read latest durable ledger. Apply latest-revision CAS. Accept matching identity once. Child acceptance closes child lifecycle only. If unrelated revision changed, retry CAS without invalidating result. If relevant entity/baseline/binding/lease changed, reject result and preserve evidence. Record one acceptance or one actionable rejection event.

Done: accepted result cannot mutate accepted state; rejection names exact field, observed mismatch, required value, and proof.

## Evidence contract

- assertion: never proof
- evidence item: one owner, one check, one exact state SHA
- required facts: owner, exact command/workflow, working directory, observed result, artifact absolute path or `None`, exact state SHA
- artifact: stable, readable, attributable
- manual workflow: exact setup, actions, observation, captured artifact
- negative control: inherit Proof Rules from `$orchestrate-implementation`; unsafe/impractical case records reason plus strongest alternate proof
- carried evidence: valid while checked behavior/dependencies remain byte/state-equivalent
- invalidation: exact changed input, dependency, merge, fix, environment, or contract
- rerun: only invalidated check

## Scope and decision requests

### Narrow authorization or scope grant

```text
Role: authorization request
Status: requested
Attempt: {attempt_id}
Request: {request_id}
Type: scope | authorization
Target: {absolute_path_or_exact_symbol_or_named_action}
Requested operation: {read | write | execute | commit | merge | external_mutation}
Reason: {blocked_requirement_or_finding}
Current owner: {role_and_attempt | None}
Conflict check: clear | conflict
Evidence: {absolute_path_or_exact_observation}
Minimum grant: {narrow_permission_and_duration}
```

Parent or `LP` returns exact grant/rejection. Grant names target, operation, attempt, lease duration, and ownership-map change. Agent resumes only after matching grant.

### Foreign-file routing

```text
Role: foreign-file finding
Status: routed
Attempt: {attempt_id}
Finding: {finding_id}
Target: {absolute_path_and_exact_symbol}
Owning plan: {plan_id | None}
Required action: {single_scoped_change_or_investigation}
Blocked item: {requirement_id_or_finding_id}
Evidence: {absolute_path_or_exact_observation}; {state_sha}
```

Reporter preserves foreign file. Parent routes finding to owner, requests narrow ownership change, or blocks dependent item.

### User decision request

```text
Role: user decision request
Status: requested
Attempt: {attempt_id}
Decision: {decision_id}
Blocked item: {requirement_id_or_plan_id_or_finding_id}
Question: {single_material_choice}
Options:
- {option_id}: {outcome_and_tradeoff}
Required authority: {exact_user_grant_or_None}
Safe independent work:
- {continuing_item_or_None}
Evidence:
- {absolute_path_or_exact_observation}
```

Nested agent sends request to parent. Parent routes to `LP`. `LP` asks user once, records answer, and sends scoped decision downstream. Affected item stays paused; safe independent work continues or records reason.

## Run accounting

Planner Return and Plan Supervisor Return include local accounting sections. Linked task-breakdown, merging, implementation-worker, reviewer, and review-fix reports retain linked accounting fields and empty markers. Use `None reported` only after checking full owned attempt tree.

Environment trap item:

```text
- ID: {trap_id}
  - Trigger: {exact_command_action_or_condition}
  - Symptom: {exact_error_or_observed_behavior}
  - Workaround: {action_or_None}
  - Impact: {blocked_scope_delay_or_evidence_effect}
  - Permanent fix: {owner_and_change_or_None}
```

Waste/miscommunication item:

```text
- ID: {incident_id}
  - Type: waste | miscommunication
  - Cause: {dispatch_handoff_or_workflow_defect}
  - Impact: {lost_run_rework_delay_or_missing_proof}
  - Recovery: {action_and_owner}
  - Prevention: {prompt_contract_or_workflow_change}
```

Completion: every owned retry, rejection, tool/environment failure, ownership error, duplicate run, unusable result, rework cause, and proof gap maps once or is verified `None reported`.
