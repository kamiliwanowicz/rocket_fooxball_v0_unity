---
name: write-orchestrator-coding-plan
description: >-
  Use when user requests repository-grounded coding plan for orchestrator-led
  implementation by coding agents.
---

# Write Orchestrator Coding Plan

Create lean plan executable by orchestrator and coding agents. Use exact paths, owners, dependencies, acceptance, validation, and proof. Canonical execution:

`implement -> writer barrier -> review -> fix findings -> final-state validation -> handoff`

## Route Gate

Choose route before durable initialization:

- `direct`: one coherent plan context; no cross-plan dependency; no parallel benefit; one recovery boundary; no long external operation; stable writable ownership.
- `durable`: any direct criterion fails, or multi-plan execution, long recovery, dependency waves, or integration risk earns ledger/worktree overhead.

Direct route:

`one task worktree -> implement -> writer barrier -> one review -> fresh fix worker if needed -> final-state validation -> handoff`

Use one orchestrator-owned task branch/worktree. Direct-route owner acts as plan supervisor and sole Git operator for that worktree. Workers edit owned files only. Stop at clean exact task head; user/original-branch merge remains LP-owned and authority-gated.

Durable route uses control, plan, and integration worktrees plus state contracts referenced below. Do not add ledger, convergence, wave, or recovery ceremony when direct route criteria hold.

## Inspect Before Planning

1. Read repository instructions, source plans, and requested scope.
2. Inspect relevant code, assets, manifests, settings, and validation workflows.
3. Search exact paths and symbols. Mark existing, missing, and proposed facts.
4. Inspect Git status and full baseline SHA. Preserve unrelated user changes.
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

Canonical ownership for durable route:

- `LP`: control ledger, immutable orchestration artifacts, global scheduling, plan-worktree provisioning, and authorized user/original-branch merge.
- planner: immutable plan/report artifact under control worktree `loop-runs/{run_id}/artifacts/`; no product edits or Git operations.
- plan supervisor: sole Git owner for one plan branch/worktree; stages, commits, checks clean state, and freezes accepted head.
- child implementation/fix leases: file edits only; parallel leases disjoint; each owned path has one active lease; no child Git, branch, worktree, staging, or commit operation.
- implementation and fix workers: edit owned files only; no Git operations; no foreign-path edits.
- reviewer: inspect frozen state only; no edits; run no tests.
- merging supervisor: sole Git owner for active integration branch/worktree lease; reports verified integration head only. Lease ends on accepted, blocked, interrupted, expired, or cancelled attempt.
- control worktree: ledger and orchestration artifacts only.
- integration worktree: accepted product commits and integration fixes.
- plan worktree: one isolated worktree per plan; no cross-plan sharing.

LP alone merges into user, original, or default branch after explicit authority. Merging supervisor reports `not_authorized` or `authorized_pending_lp` for that boundary. Completion records exact merge outcome and observed user-branch head. Retain control, accepted plan, and integration refs until handoff/recovery retention is recorded; LP owns safe worktree cleanup after all leases close.

## Artifact And Worktree Lifecycle

Durable planner artifact precedes worktree. `P0` worktree binding is required only for a plan accepted as `ready_to_execute` during `PLAN_CONVERGENCE`:

- planner writes immutable plan/report artifact in control worktree; LP records and commits control state.
- after `PLAN_CONVERGENCE` marks a plan `ready_to_execute`, LP provisions one exact plan branch/worktree from accepted baseline before plan-supervisor dispatch.
- `needs_user` or `blocked` plans remain artifact-only; no product branch/worktree or `P0` verification is required.
- loop-owned `P0` verifies pre-provisioned branch/worktree, path, writability, baseline, and ownership. No duplicate creation.
- standalone planning `P0` creates one fresh task branch/worktree from baseline and verifies access.
- plan supervisor owns all plan-worktree staging, commits, clean checks, and accepted-head freeze.
- main checkout stays read-only after setup. Setup failure blocks execution; no fallback edit path.

Lease recovery preserves single-writer invariant:

- replace same-worktree attempt only after lease expiry or confirmed interruption, confirmed process termination, and reconciled Git state.
- if prior agent remains live or termination is unconfirmed, quarantine old branch/worktree; replacement uses new branch/worktree from last accepted SHA.
- preserve reachable commits before cleanup. Late results fail identity checks against closed lease and entity generation.

## Result And Evidence Binding

Result identity:

`{dispatch_id, attempt_id, lease_id, entity_generation, baseline_sha}`

LP accepts parallel reports after latest-revision CAS re-read when active entity generation and relevant baseline remain unchanged. Submitted report revision need not equal latest global revision. Unrelated heartbeat, report, or ledger revision does not invalidate result. Entity mutation, supersession, lease closure, or baseline change invalidates result.

Every accepted artifact names exact frozen state SHA. Evidence stays valid only while checked inputs remain byte/state-equivalent. Name invalidation and rerun owner when state changes.

## Scale Orchestration

- tiny/low-risk: direct route; concise ordered steps; no fake parallelism.
- one-agent change: one packet -> writer barrier -> one review -> fix -> final validation.
- medium change: few coherent packets; serialize shared files, contracts, registration, serialized assets, migrations, and unstable interfaces.
- large change: parallelize only independent packets with disjoint owned files, stable inputs, and independent acceptance.

Parallel packet form:

`P1 implement || P2 implement -> writer barrier -> one plan-wide review -> fix findings -> validation/integration`

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

Mark new paths `proposed`. Keep packet data in plan; use `$orchestrate-implementation` for worker, reviewer, and fix-worker prompts/reports. Workers receive explicit no-Git boundary.

## Writer Barrier, Review, And Fix

After all writers finish:

1. Plan supervisor closes every edit lease, reconciles owned diff and unrelated state, stages, commits, checks clean worktree, and records `writer_barrier` gate with exact frozen head.
2. Assign one independent inspect-only review against exact frozen diff/head. Reviewer runs no tests and reports critical/high correctness, regression, safety, and proof findings.
3. Route accepted findings to fresh fix worker. Fix worker edits owned scope only, supplies final proof, and returns to plan supervisor for commit and clean-head freeze.
4. Do not dispatch second review. Final-state validation covers behavior and evidence invalidated by fixes.

One review pass applies per review boundary. No architecture, public-contract, security, or broad-code exception. Keep review/fix/validation records tied to exact state SHA.

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

Use one final-stage model across plan, merge, communication, and state contracts:

Final-stage marker: `FINAL_STAGE` (one bounded `WAVE_INTEGRATION` attempt; not a run-state enum).
Substates: `combined_review` -> `integration_fix` (optional, at most once) -> `final_verify`.

`WAVE_INTEGRATION -> merge -> combined review when needed -> integration fix -> final verification -> READY_FOR_USER_MERGE`

Record review, fix, and verification as final integration substates/gates, not separate top-level plan transitions. Final merging supervisor owns one bounded final-stage attempt and delegates review/fix as needed. At most one accepted integration-fix cycle.

Single-plan optimization: reuse accepted plan-wide review when merge adds no changes and evidence remains valid. Run combined review only for multi-plan integration, conflict resolution, integration fixes, or invalidated cross-plan evidence. Final merger stops at verified integration head; LP performs user-branch merge once when authorized.

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

`direct: one worktree -> implement -> writer barrier -> review -> fresh fix if needed -> final validation -> handoff`

or

`durable: P0 -> implementation packets -> writer barrier -> review -> fix -> wave/integration handoff -> final validation`

### P0: Worktree and artifact binding (durable route)

- owner: LP; plan supervisor verifies exact binding
- gate: required only when `PLAN_CONVERGENCE` accepts plan as `ready_to_execute`; `needs_user` or `blocked` -> `None`
- artifact: `[control-worktree]/loop-runs/{run_id}/artifacts/[plan-file]`
- mode: `create_standalone` | `verify_preprovisioned`
- baseline: `[full SHA]`
- branch: `[exact plan branch]`
- worktree: `[absolute plan worktree]`
- actions: standalone create one fresh worktree; loop-owned verify LP-provisioned worktree; confirm path, writability, baseline, and no duplicate
- done when: one plan worktree exists, exact branch/baseline match, all later paths resolve inside it, and control artifact is immutable
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

- owner: independent reviewer; inspect-only against `[frozen SHA/diff]`; run no tests; report `critical` or `high` only
- done when: one review result accepted with findings or pass

### F1: Fix accepted findings

- owner: fresh fix worker; no later review
- actions: fix owned findings; supply final proof; plan supervisor commits and freezes new head
- done when: every accepted finding fixed or explicitly blocked; invalidated checks named

## Final Verification

- evidence: [carried valid results and frozen SHA]
- tests: owner [plan supervisor, merging supervisor, or LP] at [run point]; run `[exact command/workflow]` | none - [reason]
- expect: [result]
- exclude: [checks already proven; no rerun]
- inspect: [diff/assets/runtime behavior when required]

## Risks and Questions

- risk: [failure] -> mitigation: [plan action]
- question: [one material unresolved choice] | None

## Done Criteria

- route and ceremony match task size
- every writable path has one owner and one worktree
- plan head is committed, clean, exact, and frozen before review evidence
- review runs once per boundary; fix worker supplies final proof; final checks cover invalidations
- final integration and LP merge boundaries are explicit
```

Direct route may replace packet ceremony with short ordered steps, but retain ownership, writer barrier, one review, fix proof, and final validation.

## Final Check

Before save, confirm:

- repository claims cite real paths or mark proposals
- route matches task size; no unearned durable ceremony
- breakdown status matrix honored; no invented `needs_user`/`blocked` fields
- artifact path legal; P0 mode matches standalone or loop-owned lifecycle
- one owner per Git surface, file, worktree, check, lease, and evidence item
- parallel writers are disjoint and finish before barrier/review/validation
- review targets frozen exact head; no second review branch exists
- result identity includes dispatch, attempt, lease, entity generation, and baseline
- lease replacement quarantine prevents concurrent writers
- final-stage model and LP user-merge boundary use canonical pointers
- packet data delegates prompt/report formats to `$orchestrate-implementation`
- no implementation changes appear in plan
