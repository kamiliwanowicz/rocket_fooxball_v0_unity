# Loop-Orchestrator Workflow Restoration Plan

Status: proposed

Source: user request -> restore mandatory LP workflow

Baseline: `3347042c5028a6577a3f128e0150346fa1fa3d43`

Shape: direct

## Objective

Restore mandatory LP flow without restoring former oversized ledger system:

`task-breakdown -> planner(s) -> execution orchestrator(s) -> merging agent -> handoff`

Every run uses one small durable state document.

## Scope

- in: `.agents/skills/loop-orchestrator/`, `write-orchestrator-coding-plan`, `orchestrate-implementation`
- out: control branches, leases, capacity accounting, closed YAML schemas, deleted communication-contract machinery
- preserve: unrelated dirty work in `plans/core-behaviour.md`
- reconcile before execution: uncommitted `.agents/skills/write-orchestrator-coding-plan/SKILL.md` changes overlap owned scope
- protect: user branch and main checkout; no implementation or merge mutation without bound authority

## Findings

- observed: `96f2135` removed LP workflow phases and durable state model.
- observed: `a3ece5c` removed planner and plan-execution handoff detail.
- observed: `051a6da` was not primary removal; it changed only merger and recovery wording.
- observed: `.agents/skills/loop-orchestrator/SKILL.md` explicitly bypasses task breakdown and merging for single-plan work.
- observed: `.agents/skills/loop-orchestrator/agents/task-breakdown.md` and `agents/merging.md` exist but are not mandatory route stages.
- observed: current workflow has no planner role using `$write-orchestrator-coding-plan` and no execution-orchestrator role using `$orchestrate-implementation`.
- observed: current state is optional checkpoint with no fixed path, plan registry, or progress transitions.
- observed: `.agents/skills/write-orchestrator-coding-plan/SKILL.md` has uncommitted user changes absent from baseline SHA.
- gap: planner writes repository plan files but performs no Git actions; accepted artifact has no immutable custody.
- gap: task-breakdown and planner return `needs_user`; current global return contract and proposed state statuses cannot represent user wait.
- gap: mandatory single-plan merger has no preprovisioned isolated integration branch/worktree.
- constraint: user-branch merge always requires explicit authority, including documentation-only changes.

## Decisions

- decision: restore mandatory role sequence for every task, including single-plan work.
- decision: prefer one large plan; split only for independent ownership, meaningful parallel benefit, or accepted dependency.
- decision: planner dispatch applies to ready work. Dependent candidate planning starts after upstream merge creates factual baseline SHA.
- decision: LP is sole state-document writer. Other agents read it and return facts for LP to record, avoiding concurrent state-file edits.
- decision: state document lives outside product tree at `<git-common-dir>/loop-orchestrator/<run-id>/state.md`.
- decision: LP generates unique `run_id`, stable requirement IDs, stable `plan_id` values, and unique attempt IDs before dispatch.
- decision: LP-dispatched planner artifact lives at `<git-common-dir>/loop-orchestrator/<run-id>/plans/<plan-id>/<attempt-id>.md`; direct user-authored plans keep repository `plans/` convention.
- decision: planner writes unique create-once artifact path and never overwrites it. LP accepts artifact only after planner stops, computes SHA-256, records path/digest/size, and verifies same digest before execution dispatch.
- decision: LP updates `state.md` through same-directory temporary file plus atomic replace. Missing or corrupt state triggers Git/live-agent recovery; no run reuses another run directory.
- decision: `needs_user` result maps to `awaiting_user`; user response starts fresh role attempt with new attempt ID. `blocked` remains separate.
- decision: LP provisions isolated integration branch/worktree from observed accepted baseline before merger dispatch. Merger never targets user branch or main checkout.
- decision: one-plan fast-forward may produce same commit SHA as execution head; integration proof binds branch/worktree, expected pre-merge head, accepted input SHA, and observed post-merge head.
- decision: existing dirty planning-skill change remains protected. Execution waits for user-authorized inclusion or separate accepted commit; LP refreshes plan baseline before worktree creation.
- decision: retain exact-SHA, review/fix, target-drift, and user-branch authority safeguards already present.
- decision: do not restore old control-worktree, ledger-revision, lease, capacity, or communication-contract systems.

## Tasks

### T1: Restore LP route and roles

- owner: implementation agent
- depends_on: None
- owns: `.agents/skills/loop-orchestrator/SKILL.md`, `.agents/skills/loop-orchestrator/agents/openai.yaml`
- protected: product files and unrelated skill contracts
- changes:
  1. Replace LP direct-worker route with `INIT -> BREAKDOWN -> PLANNING -> EXECUTION -> MERGING -> READY_FOR_USER_MERGE`.
  2. Define roles: LP, `task-breakdown` (`sol_high`), planner (`sol_high`), execution orchestrator (`sol_high`), merging agent (`sol_high`).
  3. Require every run, including `single_plan`, to dispatch breakdown, planner, execution orchestrator, and merger.
  4. Permit parallel planner/execution dispatch only for breakdown-approved disjoint plans.
  5. Keep LP as coordinator, blocker resolver, state owner, and sole user-branch merge authority.
  6. Scope generic `complete | blocked` return contract to implementation/review/fix/merge roles. Route task-breakdown and planner through strict role-specific statuses.
  7. Update skill display text to describe plan-first orchestration.
- done when: main skill makes all four delegated stages mandatory and links their source documents and skills.
- checks: documentation owner -> inspect route, role references, and links; rerun after cross-skill contract edits.
- proof: trace one-plan request from breakdown through merger without LP directly dispatching implementation workers.
- review_focus: skipped stage, contradictory ownership, accidental restoration of heavyweight legacy machinery.

### T2: Define strict task-breakdown result

- owner: implementation agent
- depends_on: T1
- owns: `.agents/skills/loop-orchestrator/agents/task-breakdown.md`
- protected: implementation-plan and execution-worker contracts
- changes:
  1. Keep exact `sol_high` profile and LP-only invocation.
  2. Require one strict Markdown result template with no prose before or after it.
  3. Include `Status`, `Decision`, baseline SHA, stable requirement IDs, evidence, plan candidates, covered requirement IDs, dependencies/waves, forecast owned/protected paths, validation boundary, integration order, split rationale, and one material question or blocker when needed.
  4. Limit output to plan candidates; prohibit implementation-task detail and recursive breakdown.
  5. Keep choices `single_plan`, `multi_sequential`, `multi_parallel`, and `hybrid`.
- done when: LP can determine exact planner dispatches and requirement coverage solely from one accepted result.
- checks: documentation owner -> validate each status template and route from `ready`, `needs_user`, and `blocked`.
- proof: small coherent task emits exactly one plan candidate; valid parallel case emits disjoint candidates with deterministic merge order.
- review_focus: ambiguous format, micro-plan pressure, duplicate or overlapping path ownership.

### T3: Restore planner phase and artifacts

- owner: implementation agent
- depends_on: T1, T2
- owns: `.agents/skills/write-orchestrator-coding-plan/SKILL.md`, `.agents/skills/loop-orchestrator/SKILL.md`
- protected: task-breakdown decision authority and worker/reviewer prompt details
- changes:
  1. Add LP-dispatched planner work to `$write-orchestrator-coding-plan` trigger and intake.
  2. Require `run_id`, `plan_id`, `attempt_id`, covered requirement IDs, baseline SHA, dependencies, owned/protected paths, and reserved artifact path.
  3. Add LP-dispatched artifact mode: create-once path under Git common run directory; repository `plans/` output remains direct user-request mode only.
  4. Require planner result status `ready`, `needs_user`, or `blocked`. Never encode user wait as blocker.
  5. Require planner to write artifact through same-directory temporary file plus atomic rename and never mutate accepted artifact.
  6. Require planner to return decomposition changes to LP/task-breakdown instead of splitting work itself.
  7. Require LP to stop planner, compute SHA-256, record artifact path/digest/size in state, and rehash before execution dispatch.
  8. For dependent candidates, defer planner dispatch until upstream merger records accepted integration SHA.
- done when: every executable plan has one accepted coding plan before branch/worktree execution begins.
- checks: documentation owner -> inspect plan template and LP handoff fields; rerun after state-contract edits.
- proof: independent candidates receive parallel plan artifacts; dependent candidate cannot use invented SHA.
- review_focus: planner assumes execution authority, stale baseline, duplicated planning/decomposition.

### T4: Add execution-orchestrator handoff

- owner: implementation agent
- depends_on: T3
- owns: `.agents/skills/orchestrate-implementation/SKILL.md`, `.agents/skills/loop-orchestrator/SKILL.md`
- protected: LP state authority, task-breakdown format, user-branch merge boundary
- changes:
  1. Define exact `sol_high` execution-orchestrator role.
  2. LP binds accepted plan path, SHA-256 digest, size, baseline, branch, worktree, owned/protected paths, dependencies, checks, and state-file path before dispatch.
  3. Execution orchestrator becomes sole Git owner for its plan worktree.
  4. Execution orchestrator rehashes artifact before first worker dispatch; mismatch returns `blocked` without product mutation.
  5. Execution orchestrator uses existing `$orchestrate-implementation` implementation-worker, reviewer, review-fix, writer-barrier, and final-validation loop.
  6. Require return of clean committed exact plan SHA or concrete blocker with needed LP action.
- done when: LP delegates each accepted plan once and does not run its implementation/review loop itself.
- checks: documentation owner -> trace dispatch fields through worker, reviewer, fix, and final return contracts.
- proof: execution result binds immutable plan artifact to one validated commit SHA.
- review_focus: unclear Git ownership, missing plan identity, direct LP worker dispatch.

### T5: Restore merging agent for each completed wave

- owner: implementation agent
- depends_on: T4
- owns: `.agents/skills/loop-orchestrator/agents/merging.md`, `.agents/skills/loop-orchestrator/SKILL.md`
- protected: plan-level review/fix contract and LP user-branch authority
- changes:
  1. Rename/reframe integration worker as LP-dispatched merging agent using exact `sol_high`.
  2. Remove multi-plan-only limitation: one completed plan also reaches merger.
  3. Require LP to provision isolated integration branch/worktree from observed accepted baseline before first merger dispatch; record expected target head and exact allowed Git operations.
  4. Fast-forward/integrate one accepted plan head when ancestry permits. Accept unchanged commit identity after fast-forward.
  5. Merge multi-plan heads in breakdown-declared order.
  6. Keep existing exact-SHA target reread, target-drift blocking, integration validation, and user-branch boundary.
  7. For sequential work, merge prerequisite wave before LP plans/executes dependent wave.
  8. Return integration branch/worktree, expected pre-merge head, accepted input SHAs, observed final head, checks, and clean status.
- done when: every accepted execution head is merged exactly once or blocked with evidence.
- checks: documentation owner -> trace one-plan fast-forward, parallel ordered merge, and target-drift block.
- proof: final handoff names observed integration-branch SHA; one-plan fast-forward may equal execution SHA but must prove isolated target identity and pre/post heads.
- review_focus: merging skipped for one plan, product-choice conflict silently resolved, user branch changed without authority.

### T6: Add lean durable run state

- owner: implementation agent
- depends_on: T1, T2, T3, T4, T5
- owns: `.agents/skills/loop-orchestrator/references/state-and-recovery.md`
- protected: product tree and Git facts as runtime truth
- changes:
  1. Define unique run directory and state path `<git-common-dir>/loop-orchestrator/<run-id>/state.md`.
  2. Give LP sole write ownership; all other agents read it and report facts for LP updates.
  3. Define compact required fields: run objective, phase, baseline SHA, authority, stable requirement IDs, breakdown result, plan registry, artifact path/digest/size, dependencies, branches, worktrees, accepted SHAs, merge status/final SHA, checks, question, and blocker.
  4. Define plan statuses: `pending`, `planning`, `awaiting_user`, `planned`, `executing`, `done`, `blocked`, `merged`.
  5. Define transitions: `needs_user -> awaiting_user -> fresh attempt`; `blocked -> blocked -> fresh attempt after recheck`; never conflate both.
  6. Require LP to write same-directory temporary state then atomically replace `state.md` before dispatch and after accepting result.
  7. Define resume rule: validate state readability, run identity, artifact digests, Git facts, and live-agent observations. Git/live facts override stale claims.
  8. Remove optional-checkpoint framing; do not add versioned ledger, event history, control branch, leases, or capacity accounting.
- done when: resumed LP identifies current phase, branches/worktrees, active plan, accepted heads, and blocker from one document plus Git facts.
- checks: documentation owner -> inspect sample state for every allowed transition, corrupt-state recovery, and artifact digest mismatch.
- proof: interrupted multi-plan run resumes from recorded accepted integration SHA without repeating completed work; `needs_user` resumes through fresh attempt instead of blocker route.
- review_focus: concurrent state writers, missing branch/head data, state file treated as stronger than Git.

## Execution

`T1 -> T2 -> T3 -> T4 -> T5 -> T6`

All tasks touch shared orchestration contracts. Serialize them in one implementation worktree; parallel work would create needless merge and terminology conflicts.

Execution precondition: resolve overlapping dirty planning-skill change through user-authorized inclusion or separate accepted commit. Refresh `Baseline` to accepted full SHA before isolated worktree binding.

## Final Verification

- owner: execution orchestrator at clean committed candidate head
- bind: set `$baselineSha` to refreshed accepted 40-character baseline; set `$finalSha = (git rev-parse HEAD).Trim()`; record both values
- clean: `git status --porcelain` -> no output
- committed-diff: `git diff --check "$baselineSha..$finalSha" -- .agents/skills/loop-orchestrator .agents/skills/write-orchestrator-coding-plan .agents/skills/orchestrate-implementation` -> exit `0`, no output
- validator: `python "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" ".agents\skills\loop-orchestrator"` -> `Skill is valid!`
- validator: `python "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" ".agents\skills\write-orchestrator-coding-plan"` -> `Skill is valid!`
- validator: `python "$env:USERPROFILE\.codex\skills\.system\skill-creator\scripts\quick_validate.py" ".agents\skills\orchestrate-implementation"` -> `Skill is valid!`
- links: inspect every changed Markdown link and target heading at `$finalSha`; record checked source/target pairs
- traces: record input, expected states, observed states, artifact path/digest, branch/worktree, SHAs, and result for one-plan, parallel, sequential, `needs_user`, blocked, digest-mismatch, and target-drift scenarios
- invalidation: any post-check change to changed paths, role/status mapping, artifact/state custody, or merge contract invalidates all checks and traces

## Handoff

- changed paths: skill documents listed in T1 through T6 only.
- residual risks: prompt-level workflow requires real delegated run validation after documentation implementation.
- authority: merger returns exact isolated integration SHA. Explicit user approval required before LP merges that SHA into `core_mechanics`; documentation-only scope does not waive approval.

## Done Criteria

- every LP run starts with `task-breakdown` and includes planner, execution orchestrator, and merger stages.
- task breakdown output is strict and sufficient to route planners.
- planners use `$write-orchestrator-coding-plan`; execution orchestrators use `$orchestrate-implementation`.
- accepted planner artifact is create-once, digest-bound, recoverable, and outside product tree.
- state document tracks requirement IDs, user wait, blocker, artifact digest, branch, worktree, SHA, and merge state.
- merger operates only in bound isolated integration worktree; user branch stays unchanged without explicit approval.
- final checks bind clean committed candidate SHA and exact recorded commands.
- restored route stays lean and excludes former heavyweight durable-control system.
