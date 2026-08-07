---
name: write-orchestrator-coding-plan
description: >-
  Use when user requests repository-grounded coding plan for orchestrator-led
  implementation by coding agents.
---

# Write Orchestrator Coding Plan

Create lean plan executable by orchestrator and coding agents. Own artifact structure only. Use exact paths, owners, dependencies, acceptance, validation, and proof.

- direct: `implementation -> writer barrier -> review -> fix if needed -> final validation -> handoff`
- durable: `implementation -> writer barrier -> review -> fix if needed -> final plan validation -> integration handoff`

## Route Gate

Choose route before durable initialization:

- `direct`: one coherent plan context; no cross-plan dependency; no parallel benefit; one recovery boundary; no long external operation; stable writable ownership.
- `durable`: any direct criterion fails, or multi-plan execution, long recovery, dependency waves, or integration risk earns ledger/worktree overhead.

Direct route:

`one task worktree -> implement -> writer barrier -> one review -> fresh fix worker if needed -> final-state validation -> handoff`

Record `worktree_binding: create_at_execution`. Planning creates no branch/worktree. At execution, invoking orchestrator/LP acts as direct-route plan supervisor and sole Git owner, then creates one isolated task branch/worktree from exact artifact baseline. Apply direct-owner and child contracts from `$orchestrate-implementation`. Stop at clean exact task head; user/original-branch merge remains LP-owned and authority-gated.

Durable route uses control, plan, and integration worktrees plus state contracts referenced below. Do not add ledger, convergence, wave, or recovery ceremony when direct route criteria hold.

## Inspect Before Planning

1. Read repository instructions, source plans, and requested scope.
2. Inspect relevant code, assets, manifests, settings, and validation workflows.
3. Search exact paths and symbols. Mark existing, missing, and proposed facts.
4. Inspect Git status and exact committed full baseline SHA. Preserve unrelated user changes.
5. Ask user only when unresolved behavior, scope, compatibility, architecture, or authority choice changes work.
6. State minor assumptions in plan. Do not gate on minor assumptions.
7. Apply route gate. Record route and reason.

If repository unavailable, request required files or mark plan provisional.

## Breakdown Input Contract

When plan input comes from task breakdown, validate status-specific evidence:

- `ready`: require full baseline, decomposition, ownership forecast, requirement coverage, and dependency graph.
- `needs_user`: require known evidence, one material question, affected requirements, and safe continuing work. Final plan graph and invented fields not required.
- `blocked`: require exact unavailable fact, blocker evidence, and recheck condition. Unavailable fields use `None`.

User answer or blocker resolution triggers fresh breakdown attempt. Do not turn missing facts into assumptions.

## Ownership And Git Surfaces

Artifact records one ownership forecast covering every writable path, validation owner, dependency boundary, and integration order. Apply durable Git/state ownership from `$loop-orchestrator` [state and recovery](../loop-orchestrator/references/state-and-recovery.md); apply execution roles from `$orchestrate-implementation`. Planner writes immutable artifact under control worktree `loop-runs/{run_id}/artifacts/`; planner performs no product edit or Git operation. LP retains ledger, provisioning, and authorized user/original/default-branch merge. Invoking orchestrator/LP retains direct-route Git ownership.

## Artifact And Worktree Lifecycle

Durable artifact is immutable before product worktree exists:

- `baseline_sha`: exact committed full SHA
- `forecast_ownership`: required writable paths and owners
- `worktree_binding`: `assigned_at_convergence`
- future branch name and absolute worktree path: omitted

Durable convergence: validate graph, ownership, artifact digests, and exact baseline -> provision branch/worktree from accepted baseline -> verify path, branch, full head SHA, cleanliness, and writability -> one atomic ledger transition records accepted gate, observed binding facts, and `ready_to_execute`. Before transition plan remains `awaiting_convergence`; committed `ready_to_execute` requires nonnull observed branch/worktree facts. `needs_user` and `blocked` remain artifact-only.

Standalone/direct artifact records `worktree_binding: create_at_execution`. Planning alone creates no branch/worktree. At execution, direct owner creates one task branch/worktree from exact baseline, verifies binding, then dispatches work.

Apply worktree, baseline, freeze, identity, capacity, and recovery invariants from `$loop-orchestrator` [state and recovery](../loop-orchestrator/references/state-and-recovery.md). Apply dispatch acceptance from [communication contracts](../loop-orchestrator/references/communication-contracts.md).

## Result And Evidence Binding

Plan names required identity and evidence outputs; canonical tuple, acceptance, and recovery live in `$loop-orchestrator` [state and recovery](../loop-orchestrator/references/state-and-recovery.md) and [communication contracts](../loop-orchestrator/references/communication-contracts.md). Every accepted artifact names exact frozen state SHA. Plan names invalidation and rerun owner for each check.

## Scale Orchestration

- tiny/low-risk: direct route; concise ordered steps; no fake parallelism.
- one-agent change: one packet -> writer barrier -> one review -> fresh fix if needed -> final validation.
- medium change: few coherent packets; serialize shared files, contracts, registration, serialized assets, migrations, and unstable interfaces.
- large change: parallelize only independent packets with disjoint owned files, stable inputs, and independent acceptance.

Parallel packet form:

`P1 implementation || P2 implementation -> writer barrier -> one plan-wide review -> fix if needed -> final plan validation -> integration handoff`

Parallel implementation permits owned-file edits only under disjoint child edit leases; one active lease per owned path. Close every active edit lease before review or shared-project validation. Plan supervisor reconciles diff, commits, and freezes exact head. Reviewers inspect that frozen head; repository-wide validation starts after barrier. Downstream lane consuming upstream contract waits for accepted upstream review/fix state.

One plan-wide review per frozen plan by default. Early lane review is allowed only before downstream contract consumption; do not review same state again later.

## Design Work Packets

Each packet gives one agent enough context:

- objective: one named behavior/result
- depends on: accepted prior result or `None`
- owns: exact paths or tight globs; one owner per writable path
- reads: focused paths and symbols
- changes: ordered implementation actions
- constraints: repository rules and invariants
- done when: observable acceptance conditions
- validation: sole owner, exact run point, command/workflow, invalidation
- proof: discriminatory evidence when behavior changes
- review focus: regression, safety, contract, or evidence risks
- requested slots: peak concurrent slot count

Mark new paths `proposed`. Keep packet data in plan; use `$orchestrate-implementation` for worker, reviewer, and fix-worker prompts/reports. Workers receive explicit no-Git boundary.

## Writer Barrier, Review, And Fix

Plan artifact includes `WB`, one `R1` per required boundary, optional `F1`, and final validation. Use `$orchestrate-implementation` for role actions, profiles, prompts, reports, one-review rule, fix proof, and freeze behavior. Artifact names exact owners, run points, frozen-state bindings, completion criteria, and invalidations.

## Check Ownership And Proof

One owner per check per code state:

- implementation worker: focused checks before barrier/review
- reviewer: evidence inspection only; no tests
- fix worker: rerun checks invalidated by fix, after fix
- plan supervisor: plan-head compile/editor/asset checks and clean-head reconciliation
- merging supervisor: integration checks and final integration-stage checks
- LP: ledger/state acceptance and final handoff accounting

Carry valid evidence. Rerun only named invalidated checks. Owner failure loop: diagnose -> fix in scope -> rerun -> report. Blocked/out-of-scope: report evidence, diagnosis, owner, and dependency.
Same-owner red/green or multi-state proof counts one protocol.

Bug proof requires red/green negative control when safe and useful. Avoid temporary serialized-asset mutation. If unsafe or impractical, record reason plus strongest alternate discriminatory evidence. Preserve final intended state after control.

Use `tests: none - [reason]` only when no repository-supported check exists. Do not claim commands or Unity workflows ran while writing plan.
Final validation covers relevant compile/build, focused checks, required editor/runtime workflow, and affected asset reopen/diff inspection only; broad regression check earns named risk.

## Final Integration Boundary

Artifact hands only validated frozen plan head to integration. Point final-stage mapping, gates, and LP merge boundary to `$loop-orchestrator` [state and recovery](../loop-orchestrator/references/state-and-recovery.md#canonical-report-transitions); point merger procedure to [merging](../loop-orchestrator/agents/merging.md). Add no parallel state enum or copied mapping.

## Execution Handoff

Plans own packet data, not agent prompt or response schemas. Use `$orchestrate-implementation` as canonical worker/reviewer/fix-worker source. Use `$loop-orchestrator` communication and state/recovery contracts for dispatch, report identity, entity generations, leases, evidence, and final-stage gates. Do not copy templates or create parallel enums.

## Plan Output

Write AI-facing Markdown using `$llm-oriented-markdowns`. Durable route saves immutable artifact under control worktree `loop-runs/{run_id}/artifacts/`. Direct route saves at requested path or `plans/<scope>-coding-plan.md`; ask only when no safe convention exists.

Required structure; remove irrelevant sections:

```markdown
# [Scope] Coding Plan

Status: proposed
Route: direct | durable
Source: [request or source path -> heading]
Baseline: [exact 40-character lowercase commit SHA]
worktree_binding: assigned_at_convergence | create_at_execution

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

## Ownership Forecast

- `[writable path or tight glob]`: [single owner]
- validation: [single owner per check and code state]
- integration order: [dependency-safe order]
- requested slots: [peak concurrent count]

## Execution

`direct: create binding at execution -> implementation -> writer barrier -> review -> fresh fix if needed -> final validation -> handoff`

or

`durable: convergence assigns binding -> implementation packets -> writer barrier -> review -> fresh fix if needed -> final plan validation -> integration handoff`

### P0: Worktree binding

- worktree_binding: `assigned_at_convergence` | `create_at_execution`
- baseline: `[same exact 40-character lowercase commit SHA]`
- owner: durable -> LP provisions and plan supervisor verifies; direct -> invoking orchestrator/LP creates and verifies
- action: durable -> convergence validates graph/ownership/digests/baseline, provisions from baseline, verifies observed binding, then atomically records accepted gate + binding facts + `ready_to_execute`; direct -> owner creates binding at execution before dispatch
- done when: one observed clean writable branch/worktree matches exact baseline; durable ledger or direct transient handoff records facts
- blocked: report setup evidence and required authority; keep main checkout read-only

### P1: [coherent result]

- mode: sequential | parallel with [packet]
- depends on: [accepted result] | None
- owns: `[paths]`
- reads: `[path]` -> `[symbol]`
- changes:
  - [step]
- constraints: [rules]
- done when: [acceptance]
- validation: owner [role]; run point [point]; command/workflow `[exact]`; rerun only if [invalidation]
- proof: [discriminatory evidence]
- review: [exact focus]

### WB: Writer barrier and frozen head

- owner: plan supervisor
- actions: close edit leases; reconcile diff; stage/commit; check clean state; record `writer_barrier` gate, exact head, and evidence
- done when: no active writer remains and review state is immutable exact SHA

### R1: Review

- owner: independent reviewer; inspect-only against writer-barrier frozen full SHA/diff; run no tests; report `critical` or `high` only
- done when: one review result accepted with findings or pass

### F1: Fix accepted findings

- owner: fresh fix worker; no later review
- actions: fix owned findings; supply final proof; plan supervisor commits and freezes new head
- done when: every accepted finding fixed or explicitly blocked; invalidated checks named

## Final Verification

- evidence: [carried valid results and frozen SHA]
- tests: owner [direct owner or plan supervisor] at [run point before handoff]; run `[exact command/workflow]` | none - [reason]
- expect: [result]
- exclude: [checks already proven; no rerun]
- inspect: [diff/assets/runtime behavior when required]

## Risks and Questions

- risk: [failure] -> mitigation: [plan action]
- question: [one material unresolved choice] | None

## Done Criteria

- route and ceremony match task size
- every writable path has one owner; execution allocates exactly one route-owned worktree
- plan head is committed, clean, exact, and frozen before review evidence
- review runs once per boundary; fix worker supplies final proof; final checks cover invalidations
- durable integration receives only validated frozen plan head; LP merge boundary is explicit
```

Direct route may replace packet ceremony with short ordered steps, but retain ownership, writer barrier, one review, fix proof, and final validation.

## Final Check

Before save, confirm:

- repository claims cite real paths or mark proposals
- route matches task size; no unearned durable ceremony
- breakdown status matrix honored; no invented `needs_user`/`blocked` fields
- baseline is exact committed full SHA
- binding is `assigned_at_convergence` for durable or `create_at_execution` for direct; artifact contains no future branch or absolute product-worktree path
- ownership forecast covers every writable path
- one owner per Git surface, file, worktree, check, lease, and evidence item
- requested slots do not exceed reconciled `available_count`; zero or insufficient capacity queues/waits before reservation or spawn
- parallel writers are disjoint and finish before barrier/review/validation
- review targets frozen exact head; no second review branch exists
- identity, recovery, final mapping, baseline, and capacity use canonical pointers
- packet data delegates prompt/report formats to `$orchestrate-implementation`
- no implementation changes appear in plan
