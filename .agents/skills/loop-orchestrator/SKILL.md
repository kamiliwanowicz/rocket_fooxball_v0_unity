---
name: loop-orchestrator
description: Use when user requests plan-first delegated implementation through breakdown, planning, execution orchestration, isolated integration, and verified handoff.
---

# Loop Orchestrator

Act as loop owner (`LP`). Coordinate route:

`INIT -> BREAKDOWN -> PLANNING -> EXECUTION -> MERGING -> READY_FOR_USER_MERGE`

`single_plan` route -> `INIT -> PLANNING -> EXECUTION -> READY_FOR_USER_MERGE`; skip BREAKDOWN and MERGING agents. LP coordinates, resolves blockers, owns durable state, verifies returned facts, and alone may merge exact accepted integration SHA into user branch after explicit authority. LP never writes product files, writes coding plans, dispatches implementation workers directly, performs substantive review, or acts as merging agent.

## Roles

- LP: route owner, sole state writer, blocker resolver, acceptance verifier, user-branch merge authority.
- [`task-breakdown`](agents/task-breakdown.md): exact `sol_high`; writes plan candidates and requirement coverage to LP-reserved create-once artifact, returns pointer plus comments only; may fan out own exact `sol_medium` read-only analysis subagents; skipped for `single_plan`.
- planner: exact `sol_high`; uses [`$write-orchestrator-coding-plan`](../write-orchestrator-coding-plan/SKILL.md) once per ready candidate.
- execution orchestrator: exact `sol_high`; uses [`$orchestrate-implementation`](../orchestrate-implementation/SKILL.md) once per accepted plan.
- [merging agent](agents/merging.md): exact `sol_high`; integrates completed waves in bound isolated integration worktree; skipped for `single_plan`.

Profile unavailable -> current attempt `blocked`; LP records blocker and recheck condition. No silent profile substitution.

## Sources and authority

- Git: durable code facts -> branch, worktree, full SHA, ancestry, clean status, path diff.
- Collaboration tools: live agent identity, status, ownership.
- [Run state](references/state-and-recovery.md): LP-owned routing record; never stronger than Git or live observations.
- Agent reports: evidence hints until LP verifies identity, Git, scope, and checks.

User branch stays unchanged until explicit authority binds target branch and candidate SHA. Approval remains required for user-branch merge, destructive action, material scope or behavior change, external mutation, secrets, migration, or dirty-work overwrite.

Worktree scope is closed: current run's plan worktrees plus multi-plan integration worktree when route requires one. Query and verify exact bound paths/branches only. Keep unrelated repository worktrees outside discovery, state, dispatch context, reports, and cleanup. Inspect target checkout only when needed to establish baseline, dirty overlap, or user-branch merge authority; never persist unrelated worktree inventory.

## INIT

1. Read request, repository instructions, cited sources, dirty paths, current branch, full baseline SHA, checks, and authority.
2. Generate unique `run_id`, stable `REQ-*` IDs, and unique run directory under Git common dir. Create required `state.md` through atomic-write contract before first dispatch.
3. Multi-plan route -> reserve unique create-once breakdown artifact path `<git-common-dir>/loop-orchestrator/<run-id>/breakdown/<attempt-id>.md`, then bind breakdown attempt with unique `attempt_id`, exact `sol_high`, objective, requirements, baseline, evidence paths, constraints, checks, candidate sizing policy (target 5-10 tasks per candidate; split only for parallel ownership, upstream integration SHA, or planner capacity), reserved artifact path, and state path. `single_plan` route -> bind planner directly from accepted plan context; no breakdown dispatch.

Dirty owned path overlapping run scope -> protect it. Continue only after user-authorized inclusion or separate accepted commit. Refresh accepted full baseline before provisioning plan worktrees.

Candidate has one production-final owner after source fan-in and accepted fixes. Multi-worker candidate -> disjoint writable paths + stable validation environments.

## BREAKDOWN

Dispatch [`task-breakdown`](agents/task-breakdown.md) for multi-plan routes. `single_plan` route skips BREAKDOWN agent; LP verifies accepted plan context, requirement coverage, baseline, dependencies, and owned/protected paths before planning. LP never substitutes inline decomposition for multi-plan routes.

Accept result only when strict return template is complete, returned artifact path equals reserved path, artifact exists create-once and parses as breakdown [result artifact](agents/task-breakdown.md#result-artifact) template, baseline matches observed accepted baseline, each requirement has exactly one candidate owner, every candidate covers at least one requirement and records estimated tasks plus allowed split reason, no candidate exists only to hand contract to later candidate, dependency graph is acyclic, writable paths do not overlap within parallel wave, and integration order is deterministic. Return carrying inline candidate bodies instead of pointer -> reject; fresh attempt.

- `ready`: for `$p` = returned artifact path, compute `(Get-FileHash -Algorithm SHA256 -Path $p).Hash.ToLowerInvariant()` and `(Get-Item $p).Length`; record artifact path/digest/size plus returned comments; assign stable `plan_id` per candidate; start eligible planning. LP reads artifact directly and reads only sections needed for current dispatch; never restates candidate bodies into state or dispatch text beyond bound fields.
- `needs_user`: record breakdown status `awaiting_user`, material question, and safe independent work. User response -> fresh breakdown attempt with new `attempt_id`.
- `blocked`: record exact blocker and recheck condition. Resolution -> recheck facts, then fresh breakdown attempt.

Prefer few large plans. Target 5-10 implementation tasks per candidate; candidate is an ordered task chain, and sequential tasks inside one candidate are the normal shape. Split only for independent ownership with real parallel gain, accepted upstream dependency, or planner capacity. Shared contracts, generated/serialized assets, migrations, and product decisions stay serialized as ordered tasks inside one candidate, not as separate candidates. Requirement-free or contract-only candidate -> reject; fresh breakdown attempt with sizing correction.

## PLANNING

For each ready candidate, LP reserves unique create-once artifact path:

`<git-common-dir>/loop-orchestrator/<run-id>/plans/<plan-id>/<attempt-id>.md`

Dispatch exact `sol_high` planner with `run_id`, `plan_id`, `attempt_id`, covered requirement IDs, accepted baseline SHA, dependencies, design scope, produces, owned/protected paths, reserved artifact path, checks, candidate validation boundary, state path, and objective. Independent disjoint candidates may plan in parallel. Dependent candidate waits until prerequisite merger records accepted integration SHA; that observed SHA becomes planning baseline.

Planner result handling:

- `ready`: stop planner; verify artifact exists at reserved path; compute SHA-256 and byte size; record plan artifact path/digest/size.
- `needs_user`: record `awaiting_user` and question. User response -> fresh planner attempt, new `attempt_id`, new reserved path.
- `blocked`: record blocker and recheck condition. Resolution -> fresh planner attempt and new reserved path.
- decomposition change: route candidate revision through fresh task-breakdown attempt; planner never splits candidates.

Accepted plan artifact at reserved create-once path binds directly as execution authority through [state acceptance contract](references/state-and-recovery.md#dispatch-and-acceptance-writes). LP verifies its digest/size before dispatch. No copy.

## EXECUTION

For each accepted plan, LP creates one new isolated branch/worktree from exact recorded plan-baseline SHA. Provision per [`AGENTS.md`](../../../AGENTS.md) `Unity execution` before dispatch: worktree, evidence alias, scratch, fixture, and temporary directories under `C:\wt`; private `Library/`; evidence root probed at deepest path; zero Unity process; zero project lock; zero second writer. Verify initial worktree `HEAD` equals baseline; bind full SHA as immutable execution `start_sha`. Source branch ref becomes provenance only.

For `$p` = bound plan artifact path, compute `(Get-FileHash -Algorithm SHA256 -Path $p).Hash.ToLowerInvariant()` and `(Get-Item $p).Length`; compare both to the accepted digest and size without loading artifact bytes or context. Mismatch -> plan `blocked`; no product mutation or dispatch. Match -> proceed. Bind one exact `sol_high` execution orchestrator using [`$orchestrate-implementation`](../orchestrate-implementation/SKILL.md). Handoff carries plan artifact as sole authority; no separate provenance copy. Dispatch fields follow its [LP handoff contract](../orchestrate-implementation/SKILL.md#lp-handoff-contract).

Plan artifact digest and `start_sha` binding close source boundary. Target/launch checkout and source branch leave execution observation, recovery, and acceptance gates. Later changes there do not pause or invalidate attempt. LP and execution orchestrator use plan worktree plus exact `start_sha..plan_head` comparisons until attempt ends.

Path authority derives from bound plan artifact; LP never restates it by hand. Before execution dispatch, regenerate owned/protected sets from plan artifact, normalize repo-relative/sorted/deduped, compare against state, then store exact sets atomically. Missing or extra authority -> plan `blocked`; no worker creation or mutation. Path-authority reconciliation -> fresh `attempt_id`.

### Builder-generated output evidence

Classifier inputs -> current authoritative MovementLab builder-generated inventory + exact plan task-generated outputs in `owns`, each traced to builder source. Normalize union. Classify every changed generated output: source `inventory | declared-new | both`; scope `owned | inventory-exception`. Ownership affects normal scope only. Source, hand-authored, untracked, generated-looking path outside union, or declaration lacking builder-source evidence -> reject.

Every changed authoritative output, owned or inventory-exception, requires exact-SHA coverage evidence at checkpoint and final acceptance: changed-path set; source/scope class per path; comparator-selected/output path sets; `SEMANTIC:` + `DANGLING:`; GUID stability; asset/`.meta` pairing. Comparator-supported outputs -> `Tools/Validation/Compare-GeneratedYaml.ps1 -Base <start_sha> -Head <plan_head> -FailOnDangling` covers each exact path. Comparator-unsupported output -> exact path + unsupported reason -> reject until supported evidence exists. Separate regeneration commit or excluded raw slice never removes semantic-evidence review. Missing, stale, incomplete, unsupported, failing comparator, dangling increase, GUID churn, or broken pairing -> reject complete result.

Execution orchestrator builds declared checks before worker dispatch. Workflow owns `check-ledger.json`; harness owns `harness-summary.json`; state stores ledger pointer + SHA-256 only. Workers run compact fast/local proof; production-final rows retain full contract. Workflow/Unity invocation -> [Workflow Harness Precondition](references/state-and-recovery.md#workflow-harness-precondition). Production-final proof -> [Production Bake Gate](references/state-and-recovery.md#production-bake-gate).

Execution orchestrator becomes sole Git owner for plan worktree. LP does not dispatch its workers or perform its review/fix loop. Parallel execution allowed only for breakdown-approved disjoint candidates with stable inputs.

Accept `complete` only when exact execution identity matches, bound plan artifact digest rehash matches, observed branch/worktree match, committed head descends from `start_sha`, every exact `start_sha..plan_head` changed path stays owned or is allowed `inventory-exception`, every classified generated output passes [builder-generated output evidence](#builder-generated-output-evidence), required checks bind head, and index/worktree are clean. Source-branch ref never participates. `blocked` records concrete needed LP action. Any retry uses fresh `attempt_id` and fresh dispatch identity.

## Rule hot-swap

Rule binding: source path + SHA-256 manifest. Initial manifest covers every instruction, skill, profile, template, repository rule used for current plan/execution dispatch. LP records manifest digest before planner/execution dispatch.

Hot-swap gate: accepted review checkpoint; covered writers/reviewers/fixes retired; writer barrier closed; checkpoint frozen SHA clean; no active child. LP never retires live execution orchestrator solely because rules changed.

1. Detect changed manifest -> stop next dispatch -> record candidate rule manifest.
2. Same live execution orchestrator rereads changed rules + bound plan. LP records plan/rule reconciliation: changed sources, old/new digest, affected task/checkpoint/check/profile/ownership rules, accepted-checkpoint impact, compatibility verdict.
3. Compatibility -> immutable plan already satisfies each new task-field, ownership, check, profile, checkpoint rule; no authority/product scope expansion; recheck invalidated accepted checkpoints before new writer dispatch.
4. Compatible -> atomically record active manifest + reconciliation -> same execution orchestrator resumes next dispatch under new rules.
5. Incompatible -> plan `blocked` with rule/plan conflict + fresh planner action. Never mutate accepted plan artifact, mix rule sets inside checkpoint, or continue new work without reconciliation.

## MERGING

Before first multi-plan merge, LP provisions unique isolated integration branch/worktree from observed accepted baseline. Record branch, worktree, expected pre-merge head, exact allowed Git operations, integration order, and checks in state. `single_plan` route provisions no merger; LP records accepted execution SHA after exact scope/check verification.

Dispatch [merging agent](agents/merging.md) after every completed multi-plan wave. Inputs are exact accepted execution SHAs in breakdown-declared order. Sequential dependent planning waits for prerequisite wave merge and accepted integration SHA. `single_plan` route dispatches planner then execution orchestrator directly and skips merger.

Accept merge result only after rereading integration Git facts, accepted input ancestry, observed pre/post heads, clean status, scope, and checks. Each accepted execution SHA merges exactly once. `single_plan` route accepts execution SHA as final integration SHA only after clean scope/check proof; no merge-stage agent result exists.

Intermediate waves run Git, scope, and downstream-contract checks. Final wave runs union of pending or invalidated production-final rows once. Unchanged multi-plan fast-forward reuses non-bake evidence after `check-ledger.json` digest attestation and production-bake-gate reattest. Merge or fix invalidates only intersecting rows. Apply [production bake gate](references/state-and-recovery.md#production-bake-gate) when lighting inputs intersect.

Target drift -> current merge attempt `blocked`. LP follows [target-drift recovery](references/state-and-recovery.md#target-drift-recovery): default retry baseline is last recorded accepted integration SHA before drift; fresh attempt replays remaining accepted inputs in declared order. Drift SHA enters retry ancestry only after required evidence and authority acceptance are recorded. Merging agent never mutates user branch.

## Dispatch identity and results

Every dispatch carries `run_id`, `plan_id` or `None`, unique `attempt_id`, exact assigned agent/profile/role, bounded task and done condition, baseline SHA, immutable execution `start_sha` when applicable, branch/worktree, owned/protected paths, dependencies, allowed Git operations, checks, reserved create-once artifact path when role writes one (breakdown, planner), and state path.

Dispatch and return text is terse AI-to-AI: exact paths/symbols/commands/SHAs, no prose, no narration, no recap. Each child returns exactly one strict template from its owning contract, no text before or after. Artifact-writing roles return artifact pointer plus bounded status/comment fields; artifact bodies never travel inline.

Role-specific statuses:

- breakdown and planner: `ready | needs_user | blocked` using their strict contracts.
- execution, implementation, review, fix, and merge: `complete | blocked` using owning skill contract.

Reject late, replaced, interrupted, duplicate, or foreign returns. Preserve rejected report as evidence only. Never promote report by rewriting identity or facts.

LP writes state atomically before each dispatch and after accepting each result. Other agents read state and report facts; they never edit it.

## Quiet waiting

After dispatch, let assigned agent work. Prefer longest practical bounded wait for completion or attention signal. Treat unchanged live status as no event.

- Routine checks: silent. Emit no user commentary, state write, or agent message for polling, elapsed time, unchanged status, or another wait cycle.
- Manual status check: only when required for dependency scheduling, user-requested status, recovery, suspected stall, or ownership conflict. Take one compact snapshot, act on material change, then resume waiting.
- Agent contact: send follow-up only with new task-required information, correction, or concrete unblock action. Never ping for progress alone.
- User update: only for material phase transition, actionable blocker/question, requested status, or final handoff. Collapse repeated unchanged state into silence.

## Recovery and completion

Use [state and recovery](references/state-and-recovery.md) for every run, resume, user wait, blocker, digest mismatch, target drift, and cleanup. Resume from recorded accepted facts only after validating run identity, artifacts, Git, and live agents. Preserve reachable accepted commits.

Final handoff requires:

- phase `READY_FOR_USER_MERGE`;
- `single_plan` -> clean plan worktree; accepted execution SHA recorded as final integration SHA; zero integration worktree and zero merger;
- `multi-plan` -> clean integration worktree; every accepted execution SHA merged exactly once; exact final integration SHA;
- every requirement covered;
- required checks bound to final SHA;
- Critical/High finding dispositions recorded;
- changed paths and residual risks recorded;
- explicit authority request binding user target branch and exact integration SHA.

Missing or drifted fact -> `blocked` with evidence and one needed action.
