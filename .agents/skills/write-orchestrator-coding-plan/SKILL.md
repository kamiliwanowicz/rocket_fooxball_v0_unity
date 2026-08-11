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

After planner stops, LP computes SHA-256 and byte size and records acceptance in state. Execution binding copies source once into attempt snapshot and verifies accepted digest/size. Planner never claims digest acceptance.

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

- Task is worker-review slice, not edit checklist item. One task -> one implementation worker -> one review checkpoint by default. Keep ordered substeps inside task.
- Size slice by reasoning and proof load, never line or file count. Balanced slice has one dominant behavior or invariant, cohesive execution path, bounded failure domain, and one review risk model. Worker can implement it without context overload; reviewer can judge diff and proof without reconstructing unrelated mechanisms.
- Fold incidental edits into nearest behavior task when they lack independent done condition or proof and share dependencies, lifecycle, paths, or validation. Preserve separate task when small edit carries distinct material risk or independent acceptance.
- Split slice at stable contract, state-ownership, failure-domain, or validation barrier when it contains independently reasoned mechanisms, unrelated edge-case policy, distinct proof workflows, or reviewer risk-model switches. Each resulting slice must remain meaningful and independently reviewable.
- Rebalance after design detail is known: merge thin slices; split overloaded slices. Inseparable candidate still exceeding one worker-review slice capacity -> decomposition mismatch.
- Order shared-path tasks serially.
- Parallel tasks require same launch head, disjoint paths, stable inputs, independent acceptance, and explicit fan-in. Cross-lane dependency or shared validation environment -> serial edge.
- Shared files, contracts, generated/serialized assets, migrations, and product decisions stay serialized.
- Default review boundary: one unique checkpoint after each expected implementation worker. Group multiple workers only when joined chunk is more meaningful to review than partial worker states; name covered tasks, join condition, and technical rationale. Reviewer-call reduction is insufficient rationale.
- Fan-out launches every ready sibling together. Each per-worker checkpoint dispatches immediately when its worker completes; unrelated siblings continue. Branch checkpoint gates fan-in. Group only under rule above.
- Candidate dependencies use accepted SHAs supplied by LP.
- Assigned candidate exceeding detailed design capacity -> decomposition mismatch; LP mode returns `blocked` with `fresh task-breakdown`. Produce no shallow catch-all task.

## Implementation design gate

Before writing artifact, ask what worker would still need to figure out. Resolve choices affecting behavior, contracts, state ownership, other files, or edge cases. Detail stays proportional: direct edit may need one precise line; complex mechanic needs concrete symbols, logic, ordering, math, integration, and lifecycle behavior relevant to that mechanic. Avoid empty checklist fields.

Ready task lets worker follow recorded design using only local coding judgment. Product/architecture choice missing from repository -> `needs_user`. Repository evidence gap -> LP `blocked`. Excess design surface -> decomposition mismatch.

Run worker-review sizing gate after design gate. For each task, state dominant outcome, coupled edits kept inside boundary, independent work kept outside, and one proof boundary. If worker or reviewer must hold unrelated mechanisms in context -> split. If task has no meaningful independent acceptance -> fold into adjacent task.

Example: `record walkable hit normal, project velocity along ramp, preserve launch velocity` remains too broad until plan explains concrete contact state, projection/order, ramp-exit handling, and separation from wall handling.

## Plan contract

Every plan contains:

- identity: LP mode includes `run_id`, `plan_id`, `attempt_id`, covered requirements, accepted baseline, and dependencies.
- objective: requested outcome and completion boundary.
- scope: included behavior/files and explicit exclusions.
- findings: repository facts, constraints, gaps, proposed paths.
- decisions: implementation choices, assumptions, and unresolved material questions.
- execution graph: mandatory task/review/gate dependency graph showing sequential and parallel execution, fan-out, join conditions, and downstream gates.
- tasks: bounded ordered work with enough coding detail to remove non-local worker decisions.
- review checkpoints: every task maps to one checkpoint; default per worker; grouped checkpoint records covered tasks/workers, join condition, dependency gate, and technical rationale.
- checks: command/workflow, owner, run point, expected result, evidence, invalidation.
- proof: discriminatory scenario or safe alternate proof.
- review focus: concrete material Critical/High failure or delivery risks under `$orchestrate-implementation` PoC review filter.
- handoff: exact head requirement, changed paths, residual risks, integration/user-branch authority.

Every candidate also binds `read_paths`, `validation_environment`, `unity_mutation`, `expensive_proof_owner`, `expensive_proof_run_point`, and `proof_invalidation_paths`. Candidate may contain multiple workers only when paths and validation environments are disjoint; one owner must run each production-final proof after fan-in, review, and fixes.

Each task names objective, done condition, dependency, owned/protected paths, focused reads, implementation instructions, validation, proof, and return evidence.

### Check contract

Planner checks use machine-readable rows. Required fields: `check_id`, `tier` (`fast|development|production-final`), `mutates_project`, `input_paths`, `input_digest`, `environment_fingerprint`, `invalidation_paths`, `subsumes`, `run_point`, and `evidence`. Include `executed_sha`, `validated_sha`, `status`, and evidence path/digest in execution state. Require one owner for every production-final row after source fan-in and accepted fixes. A review never proves bake or capture rerun.

Execution route:

`accepted source artifact -> LP-bound attempt snapshot + isolated worktree -> exact sol_high execution orchestrator using $orchestrate-implementation -> implementation/review/fix/final validation -> clean committed execution SHA -> merging agent`

Use [`$orchestrate-implementation`](../orchestrate-implementation/SKILL.md) as execution contract. Do not duplicate worker/reviewer prompt templates.

## Output shape

Every written plan uses `Status: accepted`. Document status does not claim LP digest acceptance.

```markdown
# [Scope] Coding Plan

Status: accepted
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

## Execution Graph
`START -> T1 -> CP1 -> {T2 -> CP2 || T3 -> CP3} -> JOIN1 -> T4 -> CP4 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in; `+` requires every named predecessor
- gates: `START` -> [entry condition]; `JOIN1` -> [join condition]; `FINAL` -> [completion condition]
- rule: include every task and review checkpoint exactly once; use only IDs defined in this plan
- rule: fan-out launches every branch when predecessor passes; branch checkpoint gates join, not sibling launch
- rule: parallel branches require disjoint paths, stable inputs, independent acceptance, and explicit join gate
- rule: shared paths/contracts/assets, generated or serialized outputs, migrations, and product decisions remain sequential
- rule: a single-task plan still includes `START -> T1 -> CP1 -> FINAL`

## Tasks
### T1: [coherent result]
- objective: [single bounded implementation outcome]
- slice_boundary: [dominant behavior/invariant; coupled edits included; independent work excluded; one proof boundary]
- covered_requirements: [REQ-* list or direct request slice]
- owner: [identity]
- depends_on: [accepted SHA or None]
- owns: `[exact paths]`
- protected: `[exact paths/symbols]`
- read_paths: `[exact paths/symbols]`
- validation_environment: `[bounded environment and lease]`
- unity_mutation: `true | false`
- expensive_proof_owner: `[one identity or None]`
- expensive_proof_run_point: `[checkpoint/final boundary or None]`
- proof_invalidation_paths: `[paths that invalidate proof]`
- focused_reads: `[exact paths/symbols and reason]`
- implementation: [ordered coding details; include exact symbols, logic, order, integration, and edge handling only where needed]
- done when: [observable acceptance]
- checks: [owner, command/workflow, result, evidence, invalidation; include full check contract fields]
- proof: [discriminatory evidence]
- review_focus: [concrete trigger, harmful outcome, and evidence target for material Critical/High failure or delivery risks]
- review_checkpoint: [unique checkpoint ID by default; shared ID only for justified grouped review]
- return_evidence: [changed symbols/paths, check output, proof record, residual risk]

## Execution Assignments
- workers: [task ID -> worker identity -> bounded outcome; parallel lane when any]
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
- every task passes worker-review sizing gate: one meaningful outcome, cohesive reasoning, bounded failure domain, one proof boundary, and no incidental standalone slice;
- Execution Graph includes every task and review checkpoint exactly once and makes every sequential dependency, parallel lane, and join gate explicit;
- every implementation worker maps to one review checkpoint; grouped checkpoints include stronger-boundary rationale;
- exact baseline and dependencies are factual;
- execution route uses immutable attempt-bound snapshot and `$orchestrate-implementation`;
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
- Run worker-review sizing audit after design detail: fold tasks lacking independent acceptance; split tasks spanning unrelated reasoning, failure, or proof boundaries; return decomposition mismatch when no stable internal split exists.
- Verify `## Execution Graph` matches dependencies, launches fan-out siblings together, and never parallelizes overlapping paths, unstable inputs, or shared validation environments.
- Verify LP artifact path is new, complete, and accepted destination was never overwritten.
- Verify direct mode preserves existing repository plans and returns path only.
- Run `git diff --check -- .agents/skills/write-orchestrator-coding-plan/SKILL.md .agents/skills/loop-orchestrator/agents/task-breakdown.md`.
