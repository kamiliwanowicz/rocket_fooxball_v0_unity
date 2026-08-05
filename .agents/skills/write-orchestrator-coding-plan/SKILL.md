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
- review focus: likely regressions or contract risks

Omit empty fields. Keep one owner per writable shared path. Mark new paths `proposed`.

Prefer behavior slices over generic tasks such as "update backend" or "add tests." Avoid line-level prescriptions when repository patterns leave valid implementation choices.

## Review and Fix Flow

After each coherent implementation packet:

1. Assign inspect-only review against exact changed paths or diff.
2. Ask reviewer for actionable correctness, regression, safety, and validation findings only.
3. Send findings to implementation worker as terse fix list.
4. Fix findings and continue.

Do not require another review after ordinary fixes. Re-review only when fix changes architecture, public contract, security-sensitive behavior, or broad shared code.

Group review after several small, tightly related changes when no downstream task consumes them first. Do not review every mechanical microstep.

## Verification

Put main verification after implementation and review fixes settle. Derive commands and Unity workflows from repository instructions; never invent validation claims.

Final verification should cover only relevant checks, such as:

- compile or build
- focused automated checks when repository supports them
- required editor or runtime workflow
- affected asset reopen or diff inspection
- broad regression check only when change warrants it

Name owner, exact command or workflow, and expected result. State manual checks honestly. Do not claim checks ran while writing plan.

## Agent Communication

Use standard Markdown, never JSON. Apply `$llm-oriented-markdowns` to every agent message.

Project minimum context. Do not paste full plan, transcripts, unrelated reports, or repeated repository rules. Send file path plus section name when shared artifact is readable.

### Worker Assignment

```markdown
Task: [outcome]
Depends on: [accepted input; omit if none]
Own: [writable paths]
Read: [focused paths/symbols]
Do:
- [step]
Constraints:
- [packet-specific rule]
Done when:
- [acceptance]
Return:
- Changed: [paths + result]
- Checks: [command/workflow + result]
- Blocker: [exact need; omit if none]
```

### Reviewer Assignment

```markdown
Review: [exact packet/diff/paths]
Check:
- [acceptance, invariant, risk]
Do not edit.
Return:
- Verdict: pass | findings
- Findings: [F1 severity path:line - issue - required fix]
```

### Fix Assignment

```markdown
Fix:
- [F1 exact required outcome]
Own: [affected paths]
Verify: [smallest relevant check]
Return:
- Changed: [paths + result]
- Checks: [result]
- Blocker: [exact need; omit if none]
```

Reports stay terse. No preamble, praise, restated assignment, or speculative notes. Include exact errors when blocked.

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
- review: [exact focus]

## Final Verification

- owner: [orchestrator or worker]
- run: `[exact command/workflow]`
- expect: [result]
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
- each packet has outcome, ownership, acceptance, and review focus
- flow stays `implement -> review -> fix -> next`
- final verification uses real repository commands or workflows
- agent messages use terse standard Markdown, not JSON
- plan contains no implementation changes unless user requested them
