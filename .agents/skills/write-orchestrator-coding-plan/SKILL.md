---
name: write-orchestrator-coding-plan
description: >-
  Use when user requests repository-grounded coding plan for orchestrator-led
  implementation by coding agents.
---

# Write Orchestrator Coding Plan

Create lean plan executable by orchestrator and coding subagents. Favor clear ownership, short messages, and one flow:

`implement -> review -> fix findings -> next task -> final verification`

## Inspect Before Planning

1. Read repository instructions and source plan or requested scope.
2. Inspect relevant code, assets, manifests, settings, and validation commands.
3. Search exact paths and symbols. Distinguish existing from proposed.
4. Check Git status. Preserve user changes; avoid assigning overlapping writes.
5. Ask user only when unresolved choice materially changes behavior, scope, compatibility, or architecture.
6. State minor assumptions in plan. Do not turn them into gates.

If repository is unavailable, request required files or label plan provisional.

## Scale Orchestration

Choose smallest useful shape.

- tiny or low-risk change: concise direct plan; no fake parallelism
- small change fitting one agent context: one implementation packet -> one review -> findings fix -> final verification
- medium change: few coherent packets; run sequence above for each packet
- large change: parallelize only independent packets with disjoint write ownership, stable inputs, and independent completion

Do not split work by arbitrary layer boundaries when one worker can safely complete coherent change. Do not create separate gates, state ledgers, schemas, worktrees, checkpoints, or convergence tasks without concrete need.

Keep sequential when packets touch same files, shared contracts, central registration, serialized assets, migration order, or unstable upstream interfaces.

Parallel form:

`{P1 implement -> review -> fix || P2 implement -> review -> fix} -> integration if needed -> final verification`

## Design Work Packets

Each packet must give one agent enough context to start without rediscovering task.

Include:

- objective: named behavior or result
- depends on: accepted prior result, if any
- owns: exact paths or tight globs
- reads: only important paths and symbols
- changes: ordered implementation actions
- constraints: relevant repository rules and invariants
- done when: observable acceptance conditions
- validation: smallest relevant command or workflow, sole owner, and run point
- proof: evidence distinguishing intended behavior from pre-change behavior, when needed
- review focus: likely regressions or contract risks

Omit empty fields. Keep one owner per writable shared path. Mark new paths `proposed`.

Prefer behavior slices over generic tasks such as "update backend" or "add tests." Avoid line-level prescriptions when repository patterns leave valid implementation choices.

## Review and Fix Flow

After each coherent implementation packet:

1. Assign inspect-only review against exact changed paths or diff.
2. Ask reviewer for actionable correctness, regression, safety, and validation findings only.
3. Route accepted findings to execution's review-fix worker.
4. Fix findings and continue.

Do not require another review after ordinary fixes. Re-review only when fix changes architecture, public contract, security-sensitive behavior, or broad shared code.

Group review after several small, tightly related changes when no downstream task consumes them first. Do not review every mechanical microstep.

## Test Ownership

One owner per check per code state. Plan role, run point, exact command or workflow, and invalidation. Defaults:

- implementation worker: focused lane checks after implementation, before review
- reviewer: inspect code and supplied evidence only; run no tests
- review-fix worker: rerun only checks invalidated by its fixes, after fixes
- orchestrator: run only distinct final integration checks explicitly assigned to orchestrator

Carry valid evidence forward. Rerun only after named relevant state change. Same-owner red-green or multi-state proof = one protocol.

Owner failure loop: diagnose -> fix root cause in scope -> rerun to pass -> report complete. Blocked or out-of-scope -> report evidence, diagnosis, needed owner or dependency. Preserve valid checks.

No useful or repository-supported tests -> `tests: none — [reason]`. Same ownership for non-test checks.

## Verification

Put main verification after implementation and review fixes settle. Derive commands and Unity workflows from repository instructions; never invent validation claims.

Final verification should cover only relevant checks, such as:

- compile or build
- focused automated checks when repository supports them
- required editor or runtime workflow
- affected asset reopen or diff inspection
- broad regression check only when change warrants it

Final verification accounts for carried evidence plus checks not run earlier. It is not a blanket rerun. Name sole owner, run point, exact command or workflow, expected result, and prior checks excluded as already proven. State manual checks honestly. Do not claim checks ran while writing plan.

## Execution Handoff

Plans own work-packet data, not agent prompt or response formats. Do not embed or duplicate dispatch templates.

During execution, use `$orchestrate-implementation` as canonical source for worker, reviewer, and fix-worker templates. Map packet objective, dependencies, ownership, reads, changes, constraints, acceptance, validation, proof, and review focus into those templates.

## Plan Output

Write AI-facing Markdown using `$llm-oriented-markdowns`. Save at user path. If absent, use clear repository convention such as `plans/<scope>-coding-plan.md`; ask only when no safe convention exists.

Use following structure. Remove irrelevant sections.

```markdown
# [Scope] Coding Plan

Status: proposed
Source: [request or plan path -> heading]
Baseline: [branch/commit or inspection date]

## Objective

[Outcome and completion boundary]

## Scope

- in: [behavior]
- out: [non-goal]

## Repository Findings

- existing: `[path]` -> `[symbol/behavior]`
- gap: [missing behavior]
- constraint: [relevant rule]
- proposed: `[new path/symbol]` -> [purpose]

## Approach

- [design and data/control flow]
- invariant: [rule]
- assumption: [minor assumption]

## Execution

`P1 implement -> R1 review -> F1 fix findings -> P2 -> final verification`

### P1: [coherent result]

- mode: sequential | parallel with [packet]
- depends on: [packet/result]
- owns: `[paths]`
- reads: `[path]` -> `[symbol]`
- changes:
  - [step]
- constraints: [rules]
- done when: [acceptance]
- validation: owner [role]; when [run point]; run `[smallest command or workflow]`; rerun only if [invalidation]
- proof: [discriminatory evidence when needed]
- review: [exact focus]

## Final Verification

- evidence: [carried valid results]
- tests: owner [orchestrator or worker] at [run point]; run `[exact command/workflow]` | none — [reason]
- expect: [result]
- exclude: [checks already proven; no rerun]
- inspect: [diff/assets/runtime behavior when required]

## Risks and Questions

- risk: [failure] -> mitigation: [plan action]
- question: [material unresolved choice]

## Done Criteria

- [observable result]
- [verification result]
```

For one-packet work, keep one packet and one review. For direct tiny work, replace packet ceremony with short ordered steps while preserving review and verification expectations appropriate to risk.

## Final Check

Before saving, confirm:

- repository claims cite real paths or label proposals
- plan covers requested scope and excludes unrelated work
- complexity matches task size
- parallel writes do not overlap
- each packet has outcome, ownership, acceptance, validation, proof needs, and review focus
- each check has one owner and run point; reviewer owns none; reruns name invalidation
- test owners fix owned failures and rerun before completion, or report evidenced blocker and needed owner
- flow stays `implement -> review -> fix -> next`
- final verification uses real repository commands or workflows without repeating valid checks
- plan delegates prompt and response formats to `$orchestrate-implementation`
- plan contains no implementation changes unless user requested them
