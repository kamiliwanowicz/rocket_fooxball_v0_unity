---
name: write-orchestrator-coding-plan
description: Use when user requests repository-grounded coding plan or LP dispatches planning for orchestrator-led implementation.
---

# Write Orchestrator Coding Plan

Write lean executable Markdown plan. Planner inspects repository and writes plan only. Planner performs no implementation, staging, commits, branch/worktree mutation, worker dispatch, or candidate splitting.

## Intake modes

### Direct user request

Required: request, repository scope, requested output location when any.

1. Default output directory: `plans/`. Use user-specified location when given.
2. Create directory if missing. Name file `<scope>-coding-plan.md`; scope uses concise kebab-case.
3. Preserve existing plan files. Existing target -> next suffix: `<scope>-coding-plan-2.md`, then `-3`, onward.
4. Write complete plan using output shape. Create/edit only requested plan file.
5. Final chat response: repository-relative saved path in backticks only. No plan content, reasoning, summary, or extra prose.

### LP-dispatched planning

Required dispatch fields:

- `run_id`, stable `plan_id`, unique `attempt_id`;
- covered `REQ-*` IDs and objective;
- exact accepted 40-character `baseline_sha`;
- dependencies: accepted upstream integration SHAs or `None`;
- forecast owned/protected paths;
- checks and validation boundary;
- reserved artifact path under `<git-common-dir>/loop-orchestrator/<run-id>/plans/<plan-id>/<attempt-id>.md`;
- LP state path.

Missing, conflicting, stale, or invented required fact -> `blocked`. Material product/scope choice -> `needs_user`. User wait is never blocker.

Reserved artifact path is create-once. Confirm destination absent. Write complete bytes to unique same-directory temporary file, flush/close, then atomically rename into reserved path without overwrite. Destination appearing before rename -> `blocked`; preserve existing file unchanged. Never edit accepted artifact.

Result status:

- `ready`: artifact created at exact reserved path; return path, run/plan/attempt IDs, baseline, covered requirements, dependencies, and owned/protected paths.
- `needs_user`: return one material question and safe facts; create no accepted artifact. User response requires fresh attempt ID and reserved path.
- `blocked`: return exact blocker, evidence, and one needed LP action/recheck; create no accepted artifact.
- decomposition mismatch: return `blocked` with needed LP action `fresh task-breakdown`; planner never creates/splits candidates.

After planner stops, LP computes SHA-256 and byte size, records acceptance in state, and rehashes before execution. Planner never claims digest acceptance.

Dependent candidate planning begins only after LP supplies observed accepted upstream integration SHA. Never plan against forecast or invented downstream baseline.

## Inspect

1. Read repository instructions, source plans, candidate scope, and validation workflow.
2. Inspect relevant code, assets, manifests, settings, and symbols. Mark claims `observed` or `proposed`.
3. Record branch, worktree root, dirty paths, and exact accepted `baseline_sha`. Preserve unrelated changes.
4. Ask only questions changing scope, behavior, compatibility, architecture, or authority. Record minor assumptions.
5. Unavailable repository evidence -> `blocked` in LP mode; provisional plan with named missing evidence in direct mode.

## Plan shape

Default: one coherent direct execution plan for assigned candidate. Planner does not decompose into separate plans.

- Tasks may be ordered within candidate.
- Parallel task steps require disjoint writable paths, stable inputs, independent acceptance, and explicit join order.
- Shared files, contracts, generated/serialized assets, migrations, and product decisions stay serialized.
- Candidate dependencies use accepted SHAs supplied by LP.

## Plan contract

Every plan contains:

- identity: LP mode includes `run_id`, `plan_id`, `attempt_id`, covered requirements, accepted baseline, and dependencies.
- objective: requested outcome and completion boundary.
- scope: included behavior/files and explicit exclusions.
- findings: repository facts, constraints, gaps, proposed paths.
- decisions: assumptions and unresolved material questions.
- tasks: coherent ordered work with one owner per writable path.
- checks: command/workflow, owner, run point, expected result, evidence, invalidation.
- proof: discriminatory scenario or safe alternate proof.
- review focus: Critical/High regression, safety, contract, evidence risks.
- handoff: exact head requirement, changed paths, residual risks, integration/user-branch authority.

Each task names objective, done condition, accepted dependency or `None`, exact owned/protected paths, focused reads, ordered changes, validation, proof, and return evidence.

Execution route:

`accepted artifact -> LP-bound isolated worktree -> exact sol_high execution orchestrator using $orchestrate-implementation -> implementation/review/fix/final validation -> clean committed execution SHA -> merging agent`

Use [`$orchestrate-implementation`](../orchestrate-implementation/SKILL.md) as execution contract. Do not duplicate worker/reviewer prompt templates.

## Output shape

```markdown
# [Scope] Coding Plan

Status: proposed
Source: [request or breakdown candidate]
Run ID: [run_id or direct]
Plan ID: [plan_id or direct]
Attempt ID: [attempt_id or direct]
Covered Requirements: [REQ-* list or direct request]
Baseline: [exact 40-character lowercase SHA]
Dependencies: [accepted full SHAs or None]

## Objective
[Outcome and completion boundary]

## Scope
- in: [behavior/files]
- out: [non-goal]

## Repository Findings
- observed: `[path]` -> [symbol/fact]
- gap: [missing behavior]
- constraint: [repository rule]
- proposed: `[path]` -> [purpose]

## Decisions
- assumption: [minor assumption]
- decision: [chosen approach and reason]
- question: [material unresolved choice] | None

## Tasks
### T1: [coherent result]
- owner: [identity]
- depends_on: [accepted SHA or None]
- owns: `[exact paths]`
- protected: `[exact paths/symbols]`
- changes: [ordered actions]
- done when: [observable acceptance]
- checks: [owner, command/workflow, result, evidence, invalidation]
- proof: [discriminatory evidence]
- review_focus: [Critical/High risks]

## Execution
[ordered plan-level implementation sequence and dependency gates]

## Final Verification
- exact head: [clean committed SHA requirement]
- checks: [commands/workflows and expected evidence]
- inspect: [diff/assets/runtime behavior]
- invalidation: [edits requiring rerun]

## Handoff
- changed paths: [list]
- residual risks: [list or None]
- authority: [integration and user-branch approval]

## Done Criteria
- every covered requirement maps to task, owner, check, and proof;
- exact baseline and dependencies are factual;
- execution route uses immutable accepted artifact and `$orchestrate-implementation`;
- final checks bind clean committed head or blocker names needed action.
```

## LP result template

Return exactly this template in LP mode; no prose before or after:

```markdown
# Planner Result
Status: ready | needs_user | blocked
Run ID: [run_id]
Plan ID: [plan_id]
Attempt ID: [attempt_id]
Assigned Agent: [exact identity]
Profile: sol_high
Baseline SHA: [full SHA]
Covered Requirements: [REQ-* list]
Dependencies: [full SHA list or None]
Owned Paths: [exact paths]
Protected Paths: [exact paths]
Artifact Path: [exact path or None]
Question: [one material question or None]
Blocker: [exact blocker or None]
Evidence: [path/command/fact or None]
Needed LP Action or Recheck: [one action/fact or None]
```

## Final check

- Verify every Markdown link and target heading.
- Verify LP artifact path is new, complete, and accepted destination was never overwritten.
- Verify direct mode preserves existing repository plans and returns path only.
- Run `git diff --check -- .agents/skills/write-orchestrator-coding-plan/SKILL.md`.
