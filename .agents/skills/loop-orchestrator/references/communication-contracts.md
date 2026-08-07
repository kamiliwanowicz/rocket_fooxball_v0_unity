# Communication Contracts

Load immediately before each dispatch and each report-acceptance decision. Applies to `LP` direct agents and nested-agent routing.

## Protocol

- Agent interchange: strict Markdown.
- External interchange: JSON only when external boundary enforces JSON Schema.
- Durable run state: YAML only; state format lives elsewhere.
- Report body: one exact role artifact from this file or linked role contract.
- Linked contract precedence: linked role contract owns headings, field order, enums, path form, empty markers, and surrounding-prose rule for its artifact.
- Local contract scope: rules below apply only to Dispatch, Planner Return, Plan Supervisor Return, request, evidence, and accounting fields defined here.
- Local field order: template order.
- Local headings and colon fields: exact spelling and case.
- Local status and decision values: listed enum only.
- Local empty required field or section: `None`.
- Local empty `Environment traps` or `Waste or miscommunication`: `None reported`.
- Local bullet content: one fact per bullet.
- Local path: absolute normalized filesystem path unless placeholder states another form.
- Local attempt: exact active attempt ID.
- Local branch: exact Git branch name.
- Local SHA: full 40-character lowercase Git object ID.
- Local digest: `sha256:` plus 64 lowercase hexadecimal characters over exact file bytes.
- Local command: exact executable, arguments, quoting, working directory, and relevant environment.
- Local surrounding prose: absent.
- Local missing heading, field, item, invalid enum, malformed identifier, or extra prose: invalid report.
- Task-breakdown, merging, implementation-worker, reviewer, and review-fix artifacts: preserve linked contract path and empty-marker rules unchanged.

Nested agent -> immediate parent -> `LP`. Nested agents address neither user nor durable ledger. Only `LP` requests user decisions and writes durable state.

## Dispatch

### 1. Select contract

- Task breakdown -> full [task breakdown report](../agents/task-breakdown.md), unchanged.
- Planner -> `$write-orchestrator-coding-plan` plus Planner Return below.
- Plan supervisor -> `$orchestrate-implementation` plus Plan Supervisor Return below.
- Merging supervisor -> full [merging report](../agents/merging.md), unchanged.
- Implementation worker, reviewer, review-fix worker -> canonical prompts and reports in [`$orchestrate-implementation`](../../orchestrate-implementation/SKILL.md). Copying those formats into this file or plan forbidden.

Completion: exactly one role contract and one legal response artifact selected; every requested action falls inside selected role authority.

### 2. Build dispatch

Use exact envelope:

```text
Role: task breakdown | planner | plan supervisor | merging supervisor
Objective: {single_observable_outcome}
Attempt: {active_attempt_id}
Lease: {lease_id}; {expires_at}
Baseline: {full_sha}
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

Each action, writable target, Git operation, input state, authority boundary, output artifact contract, and done condition must appear once. Plan supervisor dispatch also includes plan ID, accepted plan digest, requirement IDs, slot budget, validation boundary, and exact branch/worktree. Merging dispatch also includes integration ID, ordered accepted plan heads, allowed conflict policy, required joint checks, and user-branch authority state.

Completion: dispatch parses against envelope; all writable and protected targets are exact; active attempt, lease, baseline, branch, worktree, inputs, authority, capacity, evidence demand, and exhaustive done condition are present.

### 3. Send and record

Send dispatch only to assigned role. Record recipient, attempt, lease, contract, and dispatch artifact location through state workflow.

Completion: one live recipient owns attempt; dispatch artifact is recoverable; no overlapping active lease or unassigned action exists.

## Task Breakdown Report

Return one artifact: full exact [task breakdown report](../agents/task-breakdown.md). No wrapper, extension, summary envelope, or local empty/path override applies. Linked report remains canonical for attempt, lease, status, baseline, digest, decision, plan IDs, requirement coverage, blockers, path form, empty markers, and acceptance. `LP` validates those fields in place.

Completion: one canonical report exists; every canonical acceptance rule passes; attempt, lease, plan, and requirement accounting comes from canonical report only.

## Planner Return

Planner must use `$write-orchestrator-coding-plan`. Plan format remains canonical there.

```text
Role: planner
Status: complete | blocked
Plan: {plan_id}
Attempt: {attempt_id}
Baseline: {full_sha}
Plan path: {absolute_path}
Digest: {sha256_digest}
Requirement IDs:
- {requirement_id}
Forecast ownership:
- {absolute_path_or_exact_symbol}: {exclusive | shared_serialized}; {owner_plan_id}
Dependencies:
- {plan_id_or_external_input}: {satisfied | pending | blocked}; {evidence_or_condition}
Planning timing: now | just_in_time_after_integration
Baseline rule: exact_input_baseline | latest_integration_head_after_dependencies
Blockers:
- {exact_blocker_and_needed_owner_or_action | None}
Environment traps:
- {accounting_item | None reported}
Waste or miscommunication:
- {accounting_item | None reported}
```

`now` pairs with `exact_input_baseline`. `just_in_time_after_integration` pairs with `latest_integration_head_after_dependencies`. `complete` requires exact plan artifact, digest, full requirement coverage, forecast ownership, confirmed dependencies, mandatory single `P0` worktree step, and no blocker.

## Plan Supervisor Return

Plan supervisor must use `$orchestrate-implementation`. Worker, reviewer, fix-worker, proof, negative-control, review, and run-accounting formats remain canonical there.

```text
Role: plan supervisor
Status: complete | blocked
Plan: {plan_id}
Attempt: {attempt_id}
Baseline: {full_sha}
Branch: {branch}
Head: {full_sha | None}
Clean worktree: true | false
Commits:
- {full_sha | None}
Requirements:
- {requirement_id}: delivered | blocked; {evidence_location_and_state_sha}
Reviews:
- {lane_id}: pass | findings_resolved | blocked; {evidence_location_and_state_sha}
Validation:
- {check_id}: pass | fail | not_run; {state_sha_or_None}; {evidence_location_or_reason}
Blockers:
- {exact_blocker_and_needed_owner_or_action | None}
Environment traps:
- {accounting_item | None reported}
Waste or miscommunication:
- {accounting_item | None reported}
```

`complete` requires non-`None` head, clean worktree, every planned commit, every requirement `delivered`, every review `pass` or `findings_resolved`, and every required validation `pass` at exact head. `blocked` preserves every completed disposition and names remaining owner/action.

## Merging Report

Return one artifact: full exact [merging report](../agents/merging.md). No wrapper, extension, summary envelope, or local empty/path override applies. Canonical report supplies identity, status, attempt and lease, integration branch, baseline, final head, clean status, evidence, blockers, traps, and waste accounting.

Validate `Status`, `Stage`, `Combined review`, `Final validation`, per-check status, and all pairings only against canonical merging contract at acceptance time. Local enums do not apply.

Completion: one canonical report exists; every canonical report-completion rule passes; `LP` validates canonical fields in place against lease, Git, evidence, requirements, and findings.

## Acceptance Workflow

### 1. Validate shape

Select one legal role artifact. Apply linked contract syntax to linked artifact and local syntax to locally defined artifact. Validate headings, order, enums, identifiers, completeness, path form, empty markers, and surrounding-prose rule from selected contract. Recompute digest by selected contract semantics.

Completion: exactly one artifact passes selected contract; every required field passes; zero duplicate envelopes, extensions, unknown fields, missing fields, malformed values, or contradictory values remain.

### 2. Validate attempt

Match report attempt and role against active lease. Confirm lease unexpired, unsuperseded, and owned by reporting agent.

Completion: exactly one active matching attempt exists; no stale, duplicate, superseded, or foreign result remains eligible.

### 3. Validate Git facts

Inspect repository. Match baseline, branch, head, ancestry, commits, and clean status. Use reported absolute worktree from active dispatch. Agent assertion has zero acceptance weight.

Completion: every reported Git fact equals observed Git fact; head is immutable for evidence review; no uncommitted or unexpected state exists.

### 4. Validate evidence

Open evidence artifacts. Match check owner, exact command or workflow, observed result, artifact, and exact state SHA. Apply Evidence Contract. Identify named invalidations caused by later commits or integration.

Completion: every claimed disposition has reproducible proof bound to observed state SHA; every invalid evidence item names exact invalidation and rerun owner.

### 5. Validate accounting

Map every requirement, review finding, accepted fix, validation check, blocker, environment trap, wasted run, and miscommunication once. Compare artifact fields against canonical contract and evidence artifacts.

Completion: zero uncovered, duplicated, conflicting, or silently dropped items remain.

### 6. Decide

- Accept once: all prior completion criteria pass -> record one acceptance for exact attempt and state SHA.
- Reject: send one correction listing exact invalid field, observed mismatch, required value or proof, and response envelope. Preserve valid evidence unaffected by named invalidation.

Completion: report has exactly one `accepted` or `rejected` result; rejection is actionable field-by-field; accepted attempt cannot mutate accepted state.

## Evidence Contract

- Assertion: never proof.
- Evidence item: one owner, one check, one exact state SHA.
- Required facts: owner; exact command or manual workflow; working directory; observed result; artifact absolute path or `None`; exact state SHA.
- Artifact: stable, readable, and attributable to command or workflow.
- Manual workflow: exact setup, actions, observation, and captured artifact.
- Discrimination and negative control: inherit Proof Rules from `$orchestrate-implementation`; linked reports retain canonical negative-control field.
- Unsafe or impractical negative control: record reason plus strongest alternate discriminatory proof.
- Carried evidence: valid while checked behavior and dependencies remain byte/state-equivalent.
- Invalidation: name exact changed input, dependency, merge, fix, environment, or check contract.
- Rerun: only invalidated check; retain unaffected accepted evidence.
- Final proof: match intended state after any negative control; bind to accepted head.

Evidence acceptance completion: every requirement or finding disposition has discriminatory, reproducible evidence at applicable accepted state; every absent negative control has accepted reason and alternate proof; no check has multiple owners or state SHAs.

## Scope and Decision Requests

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

Parent or `LP` returns exact grant or rejection. Grant names target, operation, attempt, lease duration, and any ownership-map change. Agent resumes only after matching grant.

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

Reporter preserves foreign file. Parent routes finding to recorded owner, requests narrow ownership change, or blocks dependent item.

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

Nested agent sends request to parent. Parent routes to `LP`. `LP` verifies gate, asks user once, records answer through state workflow, then sends scoped decision downstream.

Request handling completion: request reaches authorized decider; exact affected item stays paused; every safe independent item continues or has recorded reason; decision returns to original attempt or named replacement.

## Run Accounting

Planner Return and Plan Supervisor Return include both local sections. Use `None reported` only after checking full owned attempt tree. Linked task-breakdown, merging, implementation-worker, reviewer, and review-fix reports use linked accounting fields and empty markers unchanged.

Environment trap item:

```text
- ID: {trap_id}
  - Trigger: {exact_command_action_or_condition}
  - Symptom: {exact_error_or_observed_behavior}
  - Workaround: {action_or_None}
  - Impact: {blocked_scope_delay_or_evidence_effect}
  - Permanent fix: {owner_and_change_or_None}
```

Waste or miscommunication item:

```text
- ID: {incident_id}
  - Type: waste | miscommunication
  - Cause: {dispatch_handoff_or_workflow_defect}
  - Impact: {lost_run_rework_delay_or_missing_proof}
  - Recovery: {action_and_owner}
  - Prevention: {prompt_contract_or_workflow_change}
```

Local accounting completion: every owned attempt, retry, rejection, tool/environment failure, ownership error, duplicated run, unusable result, rework cause, and proof gap maps to one item or verified `None reported`; no incident hidden by successful final result. Linked-role accounting completion follows linked contract.
