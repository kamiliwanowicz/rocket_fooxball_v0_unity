---
name: write-orchestrator-coding-plan
description: Use when user requests repository-grounded, implementation-ready coding plan or LP dispatches planning for orchestrator-led implementation with workers, reviews, and exact validation.
---

# Write Orchestrator Coding Plan

Write implementation-ready Markdown plan. Planning owns heavy reasoning: exact `sol_high` subagents investigate repository slices, challenge contracts, and close design choices before dispatch. Planner synthesizes verified design. Worker translates recorded design into code; worker does not design solution. Accept plan only after task-by-task implementation simulation and independent closure audit. Planner performs no implementation, staging, commits, branch/worktree mutation, implementation dispatch, or candidate splitting.

## Intake modes

### Direct user request

Required: request, repository scope, requested output location when any.

1. Default output directory: active plan directory declared by [`AGENTS.md`](../../../AGENTS.md). Use user-specified location when given.
2. Create directory if missing. Name file `<scope>-coding-plan.md`; scope uses concise kebab-case.
3. Preserve existing plan files. Existing target -> next suffix: `<scope>-coding-plan-2.md`, then `-3`, onward.
4. Write complete plan using output shape. Create/edit only requested plan file.
5. Success response: repository-relative saved path in backticks only. No plan content, reasoning, summary, or extra prose. `needs_user` or `blocked` -> no artifact; return concise reason plus one needed user/repository action.

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

After planner stops, LP records acceptance in state. Execution binds accepted plan artifact in place at reserved create-once path; never edit accepted artifact.

Dependent candidate planning begins only after LP supplies observed accepted upstream integration SHA. Never plan against forecast or invented downstream baseline.

## Inspect

1. Read repository instructions, source plans, candidate scope, design obligations, and validation workflow.
2. Trace relevant execution paths through implementations, callers, assets, manifests, settings, tests, and validation tools. Record current control flow, state ownership, call sites, serialization, constraints, result variants, and every implementation choice exposed by requested change. Mark claims `observed` or `proposed`. Never infer behavior from declaration, name, plan, or policy when implementation exists.
3. Record branch, worktree root, dirty paths, and exact accepted `baseline_sha`. Bind observed claims to baseline blobs; worktree read is valid only when relevant file matches baseline. Relevant dirty owned/read path -> stop until committed baseline or explicit dependency supplies it. Preserve unrelated changes.
4. Ask questions changing scope, behavior, compatibility, architecture, or authority before artifact creation. Record resolved answer as decision; accepted artifact contains no open question.
5. Unavailable repository evidence -> LP `blocked`; direct mode stops without plan artifact and reports exact missing evidence plus needed user/repository action. Never publish provisional accepted plan.

### Planning delegation

Use exact `sol_high` subagents for repository investigation and design closure. Non-trivial plan requires delegation. Non-trivial: multiple paths/symbols, behavior change, API/contract change, state/lifecycle logic, serialization, migration, generated output, validation workflow, or parallel task graph. Single mechanical edit with zero design choice may stay local.

- Dispatch with `fork_turns: "none"`. Give each subagent one self-contained concern: exact question, paths, symbols, known constraints, downstream contract, and choices requiring resolution.
- Parallelize independent concerns. Keep shared contract or conflicting design questions with one owner or serialize them.
- First wave: domain investigators trace current code and propose closed design. Add cross-cutting investigator when concern spans lifecycle/state, generated outputs, or validation workflow.
- Require read-only work: inspect repository, trace behavior, compare viable designs, select evidence-backed proposal, enumerate edge cases and downstream effects. No edits, implementation, plan drafting, staging, commits, branch/worktree mutation, or project-mutating validation.
- Require exact evidence for observed claims. Require proposed design details precise enough for task recipe: symbols, signatures, algorithm/order, lifecycle, fallbacks, callers, validation, and rejected alternatives where material.
- Planner owns synthesis and plan claims. Consequential or conflicting result -> planner inspects source directly, resolves conflict, then records one design.
- After draft, dispatch fresh exact `sol_high` closure auditor. Provide draft plus relevant repository paths, not planner conclusions. Auditor simulates each worker task and reports repository-significant remaining design choices, missing repository facts, cross-task mismatches, and unverifiable checks. Ignore worker-local syntax, formatting, and local names.
- Multi-domain plan, generated-output/migration plan, or cross-task proof workflow with different mutation/review/final-validation owners -> run two fresh auditors in parallel: `design-closure-auditor` for task/API/state simulation; `execution-proof-auditor` for graph/review/generated-output/validation simulation. Small non-trivial plan may use one auditor covering both.
- Resolve concrete findings. Each audit role gets initial audit plus at most one targeted fresh re-audit with revised draft and prior finding evidence; report only surviving or new evidence-backed findings. Remaining material gap -> `needs_user`, `blocked`, or decomposition mismatch, never another audit loop. Accept only when every role reports `Worker Decisions Remaining: None` and `Gaps: None`. Do not add process or abstraction without identified failure it prevents.
- Required `sol_high` unavailable -> stop. Never downgrade planning analysis or transfer design work to implementation worker.
- Prompt and result use terse AI-to-AI language. Subagent returns exactly this template; no text before or after:

```markdown
# Planning Analysis

Status: complete | blocked
Assigned Agent: [exact agent identity]
Profile: sol_high
Role: investigator | design-closure-auditor | execution-proof-auditor
Question: [bounded dispatched question]
Observed: [`exact path/symbol` -> fact]
Proposed Design: [exact evidence-backed design or None]
Rejected Alternatives: [alternative -> rejection reason or None]
Worker Decisions Remaining: [unresolved implementation choice or None]
Gaps: [missing evidence or None]
Blocker: [exact blocker when blocked; otherwise None]
```

## Contract simulation

Run after first complete draft, before acceptance. Keep analysis scratch-only; encode resolved outcomes in task recipes and graph.

1. Build contract ledger for every cross-task API, shared state, generated artifact, workflow row, and review gate: producer, consumer, exact shape/value, owner, lifetime, precondition, result variants, fallback/error behavior.
2. Simulate each task in graph order as assigned worker. Require every input already observed or produced by accepted predecessor. Require every consumer use exact producer value and semantics. Missing seam, caller, ownership, or ordering -> revise plan.
3. Simulate key runtime paths end to end: normal path, reset/retry, pause/disable, invalid or absent input, and legacy/public entry points. Distinguish freeze from destructive disable/reset. State preservation or clearing must be explicit.
4. Simulate proof path end to end: writer mutation -> generated-output gate when applicable -> freeze/commit -> review -> fix -> re-gate/refreeze/re-review -> final validation. Every checkpoint names real writer task, covered slice, runtime base/head derivation, reviewer profile, and downstream gate. Product mutation always belongs to named worker task and checkpoint. Orchestrator-only non-product evidence/transition write -> final orchestrator gate, not fictional worker checkpoint.
5. Read validation and comparator implementations plus harness expectations. Record actual conditional statuses and output classes. When workflow has valid result variants, define deterministic pre-invocation selector resolving one `expected_status` plus branch-specific evidence. Never declare multi-value expected status. Apply asset/metadata rules only to path classes they govern.
6. For generated output, bind source owner -> authoritative inventory -> exact declared paths -> sole mutation owner/command -> comparator coverage -> commit transition -> persisted reload validation. Include every produced file. Source-only tasks declare affected outputs but do not own them. Generation worker owns output paths and runs mutating proof in `same_dispatch`; later `orchestrator_phase` proof is non-mutating. Pair `.meta` only where repository rules require it. Binary/metadata provenance does not imply semantic parsing.
7. Recompute graph after design closure. Add dependency only for data, API, ownership, mutation environment, review, or proof need. Remove stale edges. Consumer begins only after required producer checkpoint.

Common mismatch probes:

- decision computes candidate-specific value but action later uses shared/recomputed value
- pause routes through ordinary disable path that clears active state
- legacy/public method bypasses new state guard
- workflow status selector cannot resolve exactly one pre-run expectation
- generated policy applied to unsupported path class or unreported output
- final validator consumes provisional payload instead of finalized ledger/evidence
- checkpoint exists without actual writer execution or reviewable slice

## Plan shape

Default: one coherent direct execution plan for assigned candidate. Planner does not decompose into separate plans.

- `START` requires execution `start_sha == Baseline`; dependency SHAs must already be integrated. Never dispatch stale plan against different source.
- Baseline = execution start_sha; planning/docs paths may differ from product ancestor.
- Build the dependency/ownership graph before drafting task order. Identify stable contracts, shared files/symbols, generated outputs, mutation environments, review boundaries, and final proof consumers.
- Maximize safe parallel implementation. Extract independent core work into sibling lanes, then assign shared integration, wiring, or aggregation to a named fan-in task that alone owns the shared surface. Keep coupled work together when no stable contract separates it.
- Give parallel sibling tasks disjoint owned files and symbols plus stable, predeclared input/output contracts. Overlapping ownership, shared generated output, migration state, Unity/project mutation environment, or shared validation environment requires serialization unless the repository supplies an isolation mechanism.
- Justify every sequential edge with a concrete data, contract, ownership, mutation, review, or validation dependency. Do not serialize an unrelated lane behind another lane's checkpoint; let its per-worker review proceed independently and join only where a consumer or final gate needs every predecessor.
- Fan in all source producers and accepted reviews/fixes before the production-final owner starts the shared final phase. Within that sole-owner phase, order producer mutation before its generated-output gate, then run the remaining exact-SHA proof; never require an output gate before the mutation that creates its outputs.
- Size primarily by reasoning and proof load: one dominant behavior or invariant, cohesive path, bounded failure domain, one review risk model, one proof boundary. Volume is coarse tripwire, not the rule.
- Tripwire: task expected >600 changed LOC, or >3 named behaviors, or `implementation` recipe >4k chars -> split or record explicit rationale in task objective.
- Fold incidental edits sharing dependencies, lifecycle, paths, or validation when no independent done condition/proof. Keep separate only for distinct material risk or independent acceptance.
- Split at stable contract, state ownership, failure domain, or validation barrier for independently reasoned mechanisms, unrelated edge policy, distinct proof workflow, or reviewer risk model. Merge thin slices; split overloaded slices. Task obviously containing two separable builds -> prefer split. No stable meaningful split within worker-review capacity -> decomposition mismatch; LP mode -> `blocked`, needed action `fresh task-breakdown`.
- New gameplay mechanic default seam: `pure logic + types -> lifecycle/integration -> scene/prefab composition`. Default, not mandatory.
- Execution graph and checkpoints must satisfy [`$orchestrate-implementation`](../orchestrate-implementation/SKILL.md#review-checkpoints). Encode named tasks/workers, dependencies, serial/parallel lanes, joins, review gates, and any grouped-review rationale.
- Candidate dependencies: accepted SHAs supplied by LP.

Split anchors: prefab/scene wiring + few call sites -> usually one task; new MonoBehaviour + one-system integration -> cohesive task; full mechanic with separable state/lifecycle/integration -> split; whole subsystem -> split; bulk repetitive generation/config -> one task when split harms execution.

## Worker-decision gate

Before writing artifact, simulate each task from dispatch through proof. Identify every choice worker would encounter. Resolve each repository-significant choice in plan from observed evidence or explicit proposed design.

Predefine applicable details:

- exact paths, symbols, signatures, types, data shapes, owners, producers, and consumers
- algorithm, formulas, constants, ordering, ties, fallbacks, invalid-input behavior, and edge cases
- state transitions, lifecycle timing, frame ownership, reset/disable behavior, and error handling
- call-site changes, integration sequence, serialization, wiring, migrations, generated outputs, and unchanged contracts
- validation command, expected discriminatory evidence, and invalidation boundary
- cross-task producer/consumer value identity, valid workflow result variants, and executable writer/reviewer route

Worker freedom: syntax, formatting, local names, and mechanical adaptation needed to express recorded design in observed codebase. Planner owns every choice affecting behavior, architecture, API/contract shape, state ownership, dependencies, compatibility, persistence, safety, scope, or proof.

Never delegate design with phrases such as `choose`, `decide`, `determine`, `design`, `figure out`, `investigate and implement`, `as appropriate`, `if needed`, `use best judgment`, or `update callers as necessary`. Replace each with exact choice, trigger, target, and behavior. Select worker profile from user/`AGENTS.md` rules. Accepted task is fully designed; never select profile defined for tasks lacking detailed plan or use stronger reasoning profile to compensate for missing design.

Unresolved product/architecture choice -> `needs_user`. Missing repository evidence -> LP `blocked`; direct mode stops without accepted artifact and reports exact evidence needed. Design too large to pre-resolve within one worker task -> decomposition mismatch. Never defer unresolved design to implementation worker.

After design detail, apply `Plan shape` splitting rules. No meaningful independent acceptance -> fold.

Example: `record walkable hit normal, project velocity along ramp, preserve launch velocity` remains too broad until plan defines stored contact fields, projection formula and order, ramp-exit clearing trigger, launch-velocity preservation rule, and wall-contact separation.

## Plan contract

`Output shape` is sole source for plan fields, placement, and task metadata. Every plan must satisfy this skill's task/graph rules and check contract.

### Check contract

Ordinary task checks (`fast|development`) use exactly one line: `proof: <command> -> <expected discriminatory evidence>`. Do not require full ledger fields for ordinary checks. Production row grammar: `check_id=<id>; tier=production-final; owner=<task-slot|execution-orchestrator>; expected_status=<single status or deterministic pre-run selector>; command=<command/workflow>; mutates_project=<true|false>; input_paths=[exact paths]; input_digest=<recipe>; environment_fingerprint=<recipe>; invalidation_paths=[exact paths]; subsumes=[ids]; run_point=<named gate>; evidence=<predicate>`. Selector resolves exactly one status before invocation. Workflow-owned ledger resolves concrete digest/fingerprint; execution adds `executed_sha`, `validated_sha`, `status`, and evidence path/digest. Never invent plan-time values only knowable after execution. Require one owner and [`AGENTS.md`](../../../AGENTS.md)-compliant run point for every production-final row after source fan-in and accepted fixes. Review never substitutes for required project validation.

### Validation authoring rules

- Plans follow `AGENTS.md` visual-proof policy. Task-specific source-asset previews required by applicable skills, including [`$use-blender`](../use-blender/SKILL.md), remain allowed as supplementary proof.
- Plan Unity checks from [`AGENTS.md`](../../../AGENTS.md) -> `Unity execution`; `Validation`, including required pre-gates and generated-output proof policy.
- Builder-affecting plan validation order = `pure source fan-in -> targeted EditMode rules tests -> Editor compile-only gate -> ProductionPrepare -> generated review -> final full EditMode -> ProductionValidate`; plan never prohibits pre-`ProductionPrepare` compile; `ProductionPrepare` never first compiler or first test runner; comparator selects zero generated paths -> plan requires no regeneration commit, `sourceFreezeSha` is generated boundary.
- Builder-affecting work -> trace authoritative builder inventory before plan write. Source-only tasks list exact affected outputs in `implementation` but own source only. One generation task owns every produced output, exact path per output; include derived outputs such as cue-mesh assets. Do not hide outputs behind broad inventory glob. Unknown output path -> LP `blocked`; direct mode stops without plan artifact. Classifier combines inventory + exact task declarations; declaration absent inventory requires builder-source evidence as `declared-new`.
- Expensive proof contract: `same_dispatch` -> task worker runs proof before return; `expensive_proof_owner` derives to task owner. `orchestrator_phase` -> execution orchestrator runs shared proof after `checks` names exact checkpoint/final trigger, declared source fan-in, declared outputs, accepted review/fixes; `expensive_proof_owner` derives to `execution-orchestrator`. `None` -> owner `None`. Never use narrative run-point wording or workflow path-selection flag.

Execution route:

`accepted plan artifact -> LP-bound in place at reserved path + isolated worktree -> exact sol_high execution orchestrator using $orchestrate-implementation -> implementation/review/fix/final validation -> clean committed execution SHA -> merging agent`

Use [`$orchestrate-implementation`](../orchestrate-implementation/SKILL.md) as execution contract. Do not duplicate worker/reviewer prompt templates. Reference review/fix gates by link only; never copy thresholds or numeric constants into plan.

## Output shape

Every written plan uses `Status: accepted`. Document status does not claim LP acceptance.

Plan `owner`/assignment identity means stable task slot plus required profile, such as `worker-T1 (luna_max)`, not future runtime agent ID.

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
- question: None

## Execution Graph
`START -> T1 -> CP1 -> {T2 -> CP2 || T3 -> CP3} -> JOIN1 -> T4 -> CP4 -> FINAL`

- notation: `->` sequential; `||` parallel; `{...}` parallel fan-out/fan-in; `+` requires every named predecessor
- gates: `START` -> [entry condition]; `JOIN1` -> [join condition]; `FINAL` -> [completion condition]

## Tasks
### T1: [coherent result]
- objective: [single bounded implementation outcome; dominant behavior/invariant; coupled edits included; independent work excluded; one proof boundary]
- covered_requirements: [REQ-* list or direct request slice]
- owner: [stable task slot (required profile)]
- dependencies: `[predecessor task/checkpoint -> concrete reason work cannot start earlier]` or `None`
- parallel_contract: `[stable input/output symbols plus disjointness from sibling ownership]` or `None`
- owns: `[exact repository-relative paths]`
- protected: `[exact repository-relative paths; symbol boundaries belong in implementation/review_focus]`
- read_paths: `[exact path/symbol -> reason]`
- validation_environment: `[bounded environment and lease]`
- unity_mutation: `true | false`
- expensive_proof_owner: `[one identity or None]`
- expensive_proof_execution: `same_dispatch | orchestrator_phase | None`
- implementation: [complete ordered coding recipe; exact symbols, signatures, logic, order, integration, lifecycle, fallbacks, edge handling, and caller changes; no worker-owned design choices]
- done when: [observable acceptance]
- checks: ordinary -> `proof: <command> -> <expected discriminatory evidence>`; `production-final` -> owner, command/workflow, input digest/fingerprint recipes, invalidation, one pre-run `expected_status` selector, evidence predicates, and remaining plan-time check contract fields
- review_focus: [concrete trigger, harmful outcome, and evidence target for material Critical/High failure or delivery risks]
- review_checkpoint: [unique checkpoint ID by default; shared ID only for justified grouped review]

## Execution Assignments
- workers: [task ID -> stable task slot (required profile) -> bounded outcome; parallel lane when any]
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
- Verify required `sol_high` investigations completed. Verify required fresh audit role(s) report `Worker Decisions Remaining: None` plus `Gaps: None`.
- Verify contract ledger and end-to-end simulations cover every cross-task API/state/artifact/checkpoint plus normal, reset, pause/disable, invalid-input, legacy-entry, generated-output, and final-proof paths when applicable.
- Run worker-decision audit task by task. Search delegated-design phrases from `Worker-decision gate`; resolve each occurrence. Any repository-significant worker choice prevents `ready`.
- Verify template completeness plus `Plan shape` and `Worker-decision gate`.
- Verify `question: None`, complete repository evidence, and zero provisional or unresolved task design.
- Verify every observed claim matches `Baseline`, `START` requires execution `start_sha == Baseline`, and no relevant worktree-only fact leaks into plan.
- Verify every sequential edge has explicit dependency reason, every parallel lane has disjoint ownership and stable contract, every checkpoint has real writer/reviewer route, and shared integration plus final proof occur only after required fan-in.
- Verify LP artifact path is new, complete, and accepted destination was never overwritten.
- Verify direct mode preserves existing repository plans. Success returns path only; `needs_user`/`blocked` returns no artifact and one reason/action.
