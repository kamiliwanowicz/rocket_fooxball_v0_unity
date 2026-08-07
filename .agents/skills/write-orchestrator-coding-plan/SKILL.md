---
name: write-orchestrator-coding-plan
description: >-
  Use when user requests repository-grounded coding plan for orchestrator-led
  implementation by coding agents.
---

# Write Orchestrator Coding Plan

Write lean, executable Markdown plan. Planner inspects repository and writes
plan only; implementation and Git actions belong to execution owner.

## Inspect

1. Read repository instructions, source plans, requested scope, and relevant
   validation workflow.
2. Inspect relevant code, assets, manifests, settings, and symbols. Search
   exact paths. Mark each claim `observed` or `proposed`.
3. Record current branch, worktree root, dirty paths, and exact 40-character
   `baseline_sha` from Git. Preserve unrelated changes.
4. Ask only questions that change scope, behavior, compatibility, architecture,
   or authority. Record minor assumptions and decisions in plan.
5. If repository evidence is unavailable, mark plan provisional and name missing
   evidence.

## Choose shape

- Default: one coherent direct plan.
- Split only when independent ownership, recovery, elapsed-time value, or an
  accepted upstream dependency makes split worth coordination cost.
- Parallel tasks require disjoint writable paths, stable inputs, independent
  acceptance, and explicit integration order.
- Serialize shared files, contracts, generated or serialized assets, migrations,
  and product decisions.
- Genuine multi-plan work adds ownership/dependency map, accepted SHAs, optional
  facts-only checkpoint for long runs, and exact-head integration order.

## Plan contract

Every plan contains:

- `objective`: requested outcome and completion boundary.
- `scope`: included behavior/files and explicit exclusions.
- `findings`: relevant repository facts, constraints, gaps, and proposed paths.
- `decisions`: assumptions, alternatives rejected, and unresolved material
  questions.
- `tasks`: coherent ordered work with one owner per writable path.
- `checks`: command or workflow, owner, run point, expected result, and evidence.
- `proof`: discriminatory scenario for changed behavior; explain alternate proof
  when negative control is unsafe or impractical.
- `review_focus`: Critical/High regression, safety, contract, and evidence risks.
- `handoff`: exact head, changed paths, residual risks, and authority needed for
  integration or user-branch merge.

Each task names:

- objective and done condition;
- `depends_on`: accepted result or `None`;
- `owns`: exact repository-relative paths or tight globs;
- `protected`: paths/symbols that stay untouched;
- focused reads and ordered changes;
- validation owner, exact command/workflow, and invalidation rule;
- task-specific return evidence only.

Use exact paths and full SHAs. Mark new paths `proposed`. Keep product decisions
and serialized-asset edits ordered under one owner.

## Direct execution

Keep one-plan structure small:

`inspect -> bind one isolated worktree -> implement -> writer barrier -> one read-only review -> fresh fix if needed -> final validation -> handoff`

Use [`$orchestrate-implementation`](../orchestrate-implementation/SKILL.md) for
dispatch, ownership safety, review, fixes, and validation. Plan supplies task
inputs, acceptance, paths, dependencies, checks, and proof; it does not copy
worker or reviewer message templates.

## Multi-plan execution

Use only for real independent work or accepted dependency chains. Add:

- plan map: exact owned/protected paths, owner, baseline, dependencies, and
  acceptance for each plan;
- start condition: dependent plan begins only after named upstream SHA is
  accepted and checks remain valid;
- integration order: integrator applies accepted SHAs in declared order and
  reads target HEAD immediately before integration;
- recovery pointer: use [`$loop-orchestrator`](../loop-orchestrator/SKILL.md)
  [multi-plan path](../loop-orchestrator/SKILL.md#multi-plan-path) and [target
  drift recovery](../loop-orchestrator/references/state-and-recovery.md#target-drift-and-integration-recovery).

For long runs, optional facts-only checkpoint may live under Git common dir.
Keep product tree free of orchestration artifacts.

## Output shape

```markdown
# [Scope] Coding Plan

Status: proposed
Source: [request or repository path -> heading]
Baseline: [exact 40-character lowercase SHA]
Shape: direct | multi-plan

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
- checks: [owner, command/workflow, expected result, invalidation]
- proof: [discriminatory evidence]
- review_focus: [Critical/High risks]

## Execution
[direct sequence or multi-plan map and integration order]

## Final Verification
- exact head: [SHA and clean-state evidence]
- checks: [commands/workflows and observed results]
- inspect: [diff, assets, runtime behavior as relevant]
- invalidation: [post-change edits that require rerun]

## Handoff
- changed paths: [list]
- residual risks: [list or None]
- authority: [integration or user-branch approval needed]

## Done Criteria
- every requirement maps to task, path owner, check, and proof;
- exact baseline and final head are recorded;
- independent review binds pre-fix frozen SHA; accepted finding gets fresh fix proof; final validation binds post-fix SHA; no re-review;
- final checks pass at exact final head, or blocker evidence names needed action.
```

## Final check

- Verify every repository link target and heading.
- Search for removed orchestration concepts within this file; delete stale
  wording and duplicated role contracts.
- Run `git diff --check -- .agents/skills/write-orchestrator-coding-plan/SKILL.md`.
- Trace one coherent docs change as one direct plan and one genuinely disjoint
  change as multi-plan with explicit dependencies and integration order.
