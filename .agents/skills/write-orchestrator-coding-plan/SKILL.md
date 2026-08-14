---
name: write-orchestrator-coding-plan
description: Use when user requests repository-grounded coding plan or LP dispatches planning for orchestrator-led implementation.
---

# Write Orchestrator Coding Plan

Write implementation-ready Markdown plan. Planner pre-decides coding design from repository evidence so worker translates plan into code instead of designing while coding. Planner performs no implementation, staging, commits, branch/worktree mutation, implementation dispatch, or candidate splitting.

## Intake modes

### Direct user request

Required: request, repository scope, requested output location when any.

1. Default output directory: active plan directory declared by [`AGENTS.md`](../../../AGENTS.md). Use user-specified location when given.
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
- candidate design scope and produced downstream contract;
- forecast owned/protected paths;
- checks and validation boundary;
- reserved artifact path under `<git-common-dir>/loop-orchestrator/<run-id>/plans/<plan-id>/<attempt-id>.md`;
- LP state path.

Missing, conflicting, stale, or invented required fact -> `blocked`. Material product/scope choice -> `needs_user`. User wait is never blocker.

Reserved artifact path is create-once. Confirm destination absent. Write complete bytes to unique same-directory temporary file, flush/close, then atomically rename into reserved path without overwrite. Destination appearing before rename -> `blocked`; preserve existing file unchanged. Never edit accepted artifact.

Result status:

- `ready`: artifact created at exact reserved path; return path, run/plan/attempt IDs, and owned/protected paths.
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
- Prompt and result are terse AI-to-AI text: exact paths/symbols/commands, observed gaps, no prose, no narration, no speculative plan content.
- Parallel dispatch only for independent questions. Planner owns synthesis and plan claims.
- Conflicting or consequential subagent evidence -> planner inspects source directly before recording claim.
- Subagent returns exactly this template; no text before or after:

```markdown
# Analysis Result

Status: complete | blocked
Assigned Agent: [exact agent identity]
Profile: sol_medium
Question: [bounded dispatched question]
Findings: [`exact path/symbol` -> observed fact]
Gaps: [missing evidence or None]
Blocker: [exact blocker when blocked; otherwise None]
```

## Plan shape

Default: one coherent direct execution plan for assigned candidate. Planner does not decompose into separate plans.

- Size primarily by reasoning and proof load: one dominant behavior or invariant, cohesive path, bounded failure domain, one review risk model, one proof boundary. Volume is coarse tripwire, not the rule.
- Fold incidental edits sharing dependencies, lifecycle, paths, or validation when no independent done condition/proof. Keep separate only for distinct material risk or independent acceptance.
- Split at stable contract, state ownership, failure domain, or validation barrier for independently reasoned mechanisms, unrelated edge policy, distinct proof workflow, or reviewer risk model. Merge thin slices; split overloaded slices. Task obviously containing two separable builds -> prefer split. No stable meaningful split within worker-review capacity -> decomposition mismatch; LP mode -> `blocked`, needed action `fresh task-breakdown`.
- New gameplay mechanic default seam: `pure logic + types -> lifecycle/integration -> scene/prefab composition`. Default, not mandatory.
- Execution graph and checkpoints must satisfy [`$orchestrate-implementation`](../orchestrate-implementation/SKILL.md#review-checkpoints). Encode named tasks/workers, dependencies, serial/parallel lanes, joins, review gates, and any grouped-review rationale.
- Candidate dependencies: accepted SHAs supplied by LP.

Split anchors: prefab/scene wiring + few call sites -> usually one task; new MonoBehaviour + one-system integration -> cohesive task; full mechanic with separable state/lifecycle/integration -> split; whole subsystem -> split; bulk repetitive generation/config -> one task when split harms execution.

## Implementation design gate

Before writing artifact, ask what worker would still need to figure out. Resolve choices affecting behavior, contracts, state ownership, other files, or edge cases. Detail stays proportional: direct edit may need one precise line; complex mechanic needs concrete symbols, logic, ordering, math, integration, and lifecycle behavior relevant to that mechanic. Avoid empty checklist fields.

Ready task lets worker follow recorded design using only local coding judgment. Product/architecture choice missing from repository -> `needs_user`. Repository evidence gap -> LP `blocked`. Excess design surface -> decomposition mismatch.

After design detail, apply `Plan shape` splitting rules. No meaningful independent acceptance -> fold.

Example: `record walkable hit normal, project velocity along ramp, preserve launch velocity` remains too broad until plan explains concrete contact state, projection/order, ramp-exit handling, and separation from wall handling.

## Plan contract

`Output shape` is sole source for plan fields, placement, and task metadata. Every plan must satisfy this skill's task/graph rules and check contract.

### Check contract

Ordinary task checks (`fast|development`) use exactly one line: `proof: <command> -> <expected discriminatory evidence>`. Do not require full ledger fields for ordinary checks. `production-final` checks use full machine-readable rows: `check_id`, `tier`, `mutates_project`, `input_paths`, `input_digest`, `environment_fingerprint`, `invalidation_paths`, `subsumes`, `run_point`, and `evidence`; execution state adds `executed_sha`, `validated_sha`, `status`, and evidence path/digest. Require one owner and [`AGENTS.md`](../../../AGENTS.md)-compliant run point for every production-final row after source fan-in and accepted fixes. Review never substitutes for required project validation.

### Validation authoring rules

- Plans follow `AGENTS.md` visual-proof policy. Task-specific source-asset previews required by applicable skills, including [`$use-blender`](../use-blender/SKILL.md), remain allowed as supplementary proof.
- Plan Unity checks from [`AGENTS.md`](../../../AGENTS.md) -> `Unity execution`; `Validation`, including required pre-gates and generated-output proof policy.

Execution route:

`accepted source artifact -> LP-bound attempt snapshot + isolated worktree -> exact sol_high execution orchestrator using $orchestrate-implementation -> implementation/review/fix/final validation -> clean committed execution SHA -> merging agent`

Use [`$orchestrate-implementation`](../orchestrate-implementation/SKILL.md) as execution contract. Do not duplicate worker/reviewer prompt templates. Reference review/fix gates by link only; never copy thresholds or numeric constants into plan.

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

## Decisions
- assumption: [minor assumption]
- decision: [chosen approach and reason]
- question: [material unresolved choice] | None

## Execution Graph
`START -> T1 -> CP1 -> {T2 -> CP2 || T3 -> CP3} -> JOIN1 -> T4 -> CP4 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in; `+` requires every named predecessor
- gates: `START` -> [entry condition]; `JOIN1` -> [join condition]; `FINAL` -> [completion condition]

## Tasks
### T1: [coherent result]
- objective: [single bounded implementation outcome; dominant behavior/invariant; coupled edits included; independent work excluded; one proof boundary]
- covered_requirements: [REQ-* list or direct request slice]
- owner: [identity]
- owns: `[exact paths or tight globs]`
- protected: `[exact paths/symbols]`
- read_paths: `[exact path/symbol -> reason]`
- validation_environment: `[bounded environment and lease]`
- unity_mutation: `true | false`
- expensive_proof_owner: `[one identity or None]`
- expensive_proof_run_point: `[checkpoint/final boundary or None]`
- implementation: [ordered coding details; include exact symbols, logic, order, integration, and edge handling only where needed]
- done when: [observable acceptance]
- checks: ordinary -> `proof: <command> -> <expected discriminatory evidence>`; `production-final` -> owner, command/workflow, result, evidence, invalidation, and full check contract fields
- review_focus: [concrete trigger, harmful outcome, and evidence target for material Critical/High failure or delivery risks]
- review_checkpoint: [unique checkpoint ID by default; shared ID only for justified grouped review]

## Execution Assignments
- workers: [task ID -> worker identity -> bounded outcome; parallel lane when any]
- review_checkpoints: [checkpoint ID -> covered tasks/workers -> trigger/join condition -> dependency gate -> grouped rationale or per-worker default]

## Final Verification
- exact head: [clean committed SHA requirement]
- checks: [commands/workflows and expected evidence]
- inspect: [diff/assets/runtime behavior]
- invalidation: [edits requiring rerun]

## Handoff
- residual risks: [list or None]
- authority: [integration and user-branch approval]
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
- Verify template completeness plus `Plan shape` and `Implementation design gate`.
- Verify LP artifact path is new, complete, and accepted destination was never overwritten.
- Verify direct mode preserves existing repository plans and returns path only.
