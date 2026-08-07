---
name: write-orchestrator-coding-plan
description: Use when user requests repository-grounded coding plan or LP dispatches planning for orchestrator-led implementation.
---

# Write Orchestrator Coding Plan

Write implementation-ready Markdown plan. Planner pre-decides coding design from repository evidence so worker translates plan into code instead of designing while coding. Planner performs no implementation, staging, commits, branch/worktree mutation, implementation dispatch, or candidate splitting.

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
- candidate design scope, produced downstream contract, and size check;
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

1. Read repository instructions, source plans, candidate scope, design obligations, and validation workflow.
2. Trace relevant execution paths through code, assets, manifests, settings, and symbols. Record current control flow, state ownership, call sites, serialization, and constraints. Mark claims `observed` or `proposed`.
3. Record branch, worktree root, dirty paths, and exact accepted `baseline_sha`. Preserve unrelated changes.
4. Ask only questions changing scope, behavior, compatibility, architecture, or authority. Record minor assumptions.
5. Unavailable repository evidence -> `blocked` in LP mode; provisional plan with named missing evidence in direct mode.

### Analysis delegation

Use exact `sol_medium` subagents when repository evidence spans separable areas or focused analysis materially improves confidence. Keep small-scope inspection local.

- Dispatch with `fork_turns: "none"`. Give each subagent one self-contained, bounded question with relevant paths, symbols, constraints, and required evidence.
- Require read-only analysis: no edits, implementation, plan drafting, staging, commits, branch/worktree mutation, or project-mutating validation.
- Prompt and result use `$llm-oriented-markdowns`: terse facts, exact paths/symbols/commands, observed gaps, no speculative plan content.
- Parallel dispatch only for independent questions. Planner owns synthesis and plan claims.
- Conflicting or consequential subagent evidence -> planner inspects source directly before recording claim.

## Plan shape

Default: one coherent direct execution plan for assigned candidate. Planner does not decompose into separate plans.

- Tasks use smallest coherent implementation units: one algorithm, state machine, API contract, asset-wiring cluster, or tightly coupled combination.
- Split task when parts require separate design reasoning, can compile/prove at distinct barriers, or contain distinct failure domains. Order shared-path tasks serially.
- Parallel task steps require disjoint writable paths, stable inputs, independent acceptance, and explicit join order.
- Shared files, contracts, generated/serialized assets, migrations, and product decisions stay serialized.
- Default review boundary: one unique checkpoint after each expected implementation worker. Group multiple workers only when joined chunk is more meaningful to review than partial worker states; name covered tasks, join condition, and technical rationale. Reviewer-call reduction is insufficient rationale.
- Parallel workers require one grouped review checkpoint. Downstream dependencies wait for checkpoint verdict/fix disposition.
- Candidate dependencies use accepted SHAs supplied by LP.
- Assigned candidate exceeding detailed design capacity -> decomposition mismatch; LP mode returns `blocked` with `fresh task-breakdown`. Produce no shallow catch-all task.

## Implementation design gate

Before writing artifact, ask what worker would still need to figure out. Resolve choices affecting behavior, contracts, state ownership, other files, or edge cases. Detail stays proportional: direct edit may need one precise line; complex mechanic needs concrete symbols, logic, ordering, math, integration, and lifecycle behavior relevant to that mechanic. Avoid empty checklist fields.

Ready task lets worker follow recorded design using only local coding judgment. Product/architecture choice missing from repository -> `needs_user`. Repository evidence gap -> LP `blocked`. Excess design surface -> decomposition mismatch.

Example: `record walkable hit normal, project velocity along ramp, preserve launch velocity` remains too broad until plan explains concrete contact state, projection/order, ramp-exit handling, and separation from wall handling.

## Plan contract

Every plan contains:

- identity: LP mode includes `run_id`, `plan_id`, `attempt_id`, covered requirements, accepted baseline, and dependencies.
- objective: requested outcome and completion boundary.
- scope: included behavior/files and explicit exclusions.
- findings: repository facts, constraints, gaps, proposed paths.
- decisions: implementation choices, assumptions, and unresolved material questions.
- tasks: bounded ordered work with enough coding detail to remove non-local worker decisions.
- review checkpoints: every task maps to one checkpoint; default per worker; grouped checkpoint records covered tasks/workers, join condition, dependency gate, and technical rationale.
- checks: command/workflow, owner, run point, expected result, evidence, invalidation.
- proof: discriminatory scenario or safe alternate proof.
- review focus: Critical/High regression, safety, contract, evidence risks.
- handoff: exact head requirement, changed paths, residual risks, integration/user-branch authority.

Each task names objective, done condition, dependency, owned/protected paths, focused reads, implementation instructions, validation, proof, and return evidence.

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
- objective: [single bounded implementation outcome]
- covered_requirements: [REQ-* list or direct request slice]
- owner: [identity]
- depends_on: [accepted SHA or None]
- owns: `[exact paths]`
- protected: `[exact paths/symbols]`
- focused_reads: `[exact paths/symbols and reason]`
- implementation: [ordered coding details; include exact symbols, logic, order, integration, and edge handling only where needed]
- done when: [observable acceptance]
- checks: [owner, command/workflow, result, evidence, invalidation]
- proof: [discriminatory evidence]
- review_focus: [Critical/High risks]
- review_checkpoint: [unique checkpoint ID by default; shared ID only for justified grouped review]
- return_evidence: [changed symbols/paths, check output, proof record, residual risk]

## Execution
- workers: [ordered/parallel worker assignment and join order]
- review_checkpoints: [checkpoint ID -> covered tasks/workers -> trigger/join condition -> dependency gate -> grouped rationale or per-worker default]

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
- every task passes implementation design gate;
- every implementation worker maps to one review checkpoint; grouped checkpoints include stronger-boundary rationale;
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
- Run worker-decision audit; unresolved repository-significant choice prevents `ready`.
- Verify LP artifact path is new, complete, and accepted destination was never overwritten.
- Verify direct mode preserves existing repository plans and returns path only.
- Run `git diff --check -- .agents/skills/write-orchestrator-coding-plan/SKILL.md .agents/skills/loop-orchestrator/agents/task-breakdown.md`.
