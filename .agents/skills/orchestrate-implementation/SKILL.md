---
name: orchestrate-implementation
description: Use when executing implementation plans or multi-file coding changes through delegated subagents, especially work needing parallel ownership lanes, independent code review, review-fix workers, and evidence-based completion.
---

# Orchestrate Implementation

Act only as orchestrator. Delegate implementation, testing, validation, review, and review fixes. Read enough context to partition work, resolve ownership, monitor progress, and assess evidence completeness. Make no product-code edits. Perform no substantive code review.

Own canonical worker, reviewer, and fix-worker prompt and response formats. When executing plan from `$write-orchestrator-coding-plan`, map work-packet data into templates below. Ignore copied or embedded prompt schemas in plans.

Use standard Markdown, never JSON. Apply `$llm-oriented-markdowns` to every subagent prompt and require same style for responses. Include only task-critical details and exact identifiers.

## Roles

- implementation: exact `luna_max` profile. Maximum-effort worker.
- review: exact `sol_medium` profile. Independent code reviewer.
- review fixes: fresh `luna_max` worker. Never original worker or reviewer.
- orchestrator: decomposition, dispatch, blocker resolution, evidence accounting, final synthesis only.

Never substitute profiles. Never absorb delegated work when slots, tools, or agents fail. Report blocker or retry delegation.

## Workflow

### 1. Partition

Convert request or plan into smallest coherent lanes.

For each lane define:

- objective and acceptance criteria
- owned files, symbols, or subsystem
- protected scope
- dependencies and start condition
- required validation
- proof needed to discriminate changed behavior from pre-change behavior

Prefer non-overlapping ownership. Dispatch independent lanes in parallel. Serialize only real dependencies or shared-file conflicts.

Completion: every requirement maps to one named lane; ownership and dependency edges explicit.

### 2. Dispatch implementation

Spawn one `luna_max` worker per ready lane with Worker Prompt Template. Give task-local context only. Include paths, constraints, acceptance criteria, proof bar, and response schema.

Monitor reports. Request missing evidence or clarification from same worker when implementation scope stays unchanged.

Completion: every lane reports completed work with proof, or exact blocker.

### 3. Resolve blockers

Treat ownership blockers as healthy scope control. Worker must stop before editing unowned scope and request exact extra file, symbol, or call-site.

Grant narrow, named extra scope when needed and conflict-free. Update ownership map before work resumes. Never grant broad subsystem access when one expression, symbol, or file suffices.

Keep cross-lane findings in owning lane. Route foreign-file work to its owner. When owned provider or API boundary can make untouched consumers safe by default, prefer that local contract hardening over foreign-lane edits.

Completion: each blocker resolved by narrow scope grant, owner routing, safe local boundary change, or explicit unresolved status.

### 4. Dispatch reviews

After implementation lane completes, spawn independent `sol_medium` reviewer with Reviewer Prompt Template. Run reviews in parallel where ownership and dependencies permit.

Reviewer judges correctness, regressions, security, validation gaps, and proof quality. Reviewer must decide whether tests fail against relevant pre-change behavior, not accept passing tests as sufficient proof.

Orchestrator checks review response completeness only. Orchestrator does not inspect code as substitute review.

Completion: each implemented lane has one independent review report.

### 5. Dispatch review fixes

Group compatible in-lane findings. Spawn fresh `luna_max` worker with Fix Worker Prompt Template. Give exact findings, owned scope, acceptance criteria, and required validation.

Do not send review fixes for another review. Fix worker supplies final proof. Report unresolved findings or proof gaps as residual risk.

Completion: every accepted finding maps to proven fix or explicit unresolved status. No second review run.

### 6. Report

Return user-facing outcome from agent evidence. Include:

- delivered lanes and key files
- validation and discriminatory proof
- review findings and fix disposition
- unresolved risks or blockers
- every environment trap encountered
- every wasted subagent run or miscommunication

Write `None reported` for empty trap or waste sections. Never hide failed, duplicated, blocked, wrongly scoped, or preventable runs.

Completion: every requirement, finding, trap, and run accounted for.

## Proof Rules

- assertion is not proof. Require commands, observed output, diffs, screenshots, logs, or other reproducible artifacts.
- bug fix: require red-green evidence where safe and practical. In isolated owned scope, restore relevant pre-change behavior, observe targeted case fail, restore intended change byte-identically, observe pass.
- unsafe or impractical negative control: require reason plus strongest alternate discriminatory evidence.
- test quality: show test would reject relevant pre-change behavior. Passing only on changed code is insufficient.
- final state: prove intended change restored after negative control and unrelated state preserved.
- reviewer: inspect evidence provenance and discrimination, not worker confidence language.

## Run Accounting

Environment trap: environment behavior that blocked, distorted, or slowed work. Capture agent, lane, exact error or symptom, trigger, workaround, impact, and suggested permanent fix. Examples: tool/version mismatch, editor lock, sandbox boundary, path quoting, missing dependency, hidden generated state, flaky command.

Wasted run: run producing no useful implementation or review because of preventable orchestration failure. Capture agent, lane, cause, cost or delay, recovery, and prompt/workflow change. Include duplicate dispatch, wrong profile, missing context, conflicting ownership, ambiguous acceptance criteria, or unusable response format.

Miscommunication: prompt or handoff ambiguity causing rework, wrong scope, or missing proof. Record separately even when run still produced useful work.

## Worker Prompt Template

```text
Role: implementation worker
Lane: {lane_id}
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

Implement and validate owned lane. Preserve unrelated work. Stop before unowned edits; request narrow named scope. Report cross-lane findings without editing foreign scope.

Respond exactly:
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
Lane: {lane_id}
Objective: {objective}
Owned review scope: {exact_files_symbols_or_diff}
Acceptance criteria:
- {criterion}
Worker evidence:
{worker_report_or_artifact_paths}

Review correctness, regressions, security, validation gaps, and proof discrimination. Verify tests reject relevant pre-change behavior. Trace cross-lane impact, but keep findings assigned to owning lane.

Respond exactly:
Verdict: pass | findings
Findings:
- {id} | {critical_high_medium_low} | {owner_scope} | {file:line} | {defect} | {impact} | {evidence} | {required_fix}
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
Lane: {lane_id}
Owned scope: {exact_files_symbols_or_subsystem}
Protected scope: {must_not_edit}
Findings:
- {finding_id}: {required_fix_and_evidence}
Acceptance criteria:
- {criterion}
Required validation:
- {command_or_check}

Fix accepted findings in owned scope. Preserve unrelated work. Stop before unowned edits; request narrow named scope. Report cross-lane findings without editing foreign scope.

Respond exactly:
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
