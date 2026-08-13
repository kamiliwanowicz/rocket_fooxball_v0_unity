---
name: orchestrate-implementation
description: Use when LP dispatches one accepted coding plan for orchestration, or user explicitly invokes this skill to execute one accepted coding plan through bounded workers, independent reviewers, fixes, and exact-SHA validation.
---

# Orchestrate Implementation

Invocation makes active agent execution orchestrator for one accepted coding plan. Active agent coordinates plan directly, remains sole Git owner for bound plan branch/worktree, and retains orchestration through completion or blocker return. Dispatch child roles only: implementation worker, reviewer, fix worker. Active agent owns writer barriers, commits, freezes, scope checks, dependency gates, and final validation.

## Invocation mode

Select one mode before product mutation or child dispatch:

- `lp-dispatched`: invocation contains explicit LP handoff contract and LP state path. LP owns run state, cross-plan coordination, merging-agent dispatch, and user-branch authority.
- `user-direct`: user explicitly invoked this skill without LP handoff. User owns decisions normally routed to LP. Active agent creates isolated branch/worktree, runs plan there, and communicates blockers/results directly to user. LP state, IDs, assignments, messages, waits, and merge coordination remain outside run.

Never switch mode during attempt. Ambiguous or partial LP handoff -> `blocked`; ask invoking user whether to use `user-direct` or obtain complete LP dispatch.

## LP handoff contract

`lp-dispatched` handoff binds:

- `run_id`, stable `plan_id`, unique `attempt_id`;
- assigned active-agent identity, role `execution orchestrator`, profile `sol_high`;
- accepted plan source path as provenance plus attempt-bound plan snapshot absolute path, SHA-256 digest, byte size;
- covered requirement IDs and objective;
- accepted dependency SHAs;
- immutable execution `start_sha`, exact plan branch, and isolated worktree;
- owned/protected paths;
- checks, proof boundary, evidence locations;
- allowed Git operations limited to plan branch/worktree;
- LP state-file path, read-only for active execution orchestrator.

Missing/mismatched field, including active-agent identity or role -> `blocked` before product mutation.

## User-direct bootstrap

`user-direct` requires accepted plan artifact explicitly identified by user or current context. Before child dispatch:

1. Resolve repository root and accepted plan source absolute path. Generate stable local `plan_id` plus unique `attempt_id`.
2. Read source once into unique create-once snapshot at `<git-common-dir>/orchestrate-implementation/<plan-id>/executions/<attempt-id>.md`. Reopen snapshot; capture SHA-256 and byte size. Record source path as provenance; bind snapshot as sole plan authority.
3. Capture launch checkout absolute path, current branch, exact `HEAD` as `launch_head_sha`, and status. Preserve launch checkout and all existing changes unchanged.
4. Derive unique `codex/<plan-slug>-<attempt-id>` branch and short worktree path per `AGENTS.md` `Unity execution`. Confirm target parent writable for active agent and child agents. Existing branch/path -> choose new unique names; preserve existing worktrees and branches.
5. Create new branch/worktree from exact `launch_head_sha`. Verify worktree root, branch, and initial `HEAD`; bind that full SHA as immutable execution `start_sha`.
6. Bind objective, requirements, owned/protected paths, dependencies, checks, proof boundary, and evidence locations from snapshot. Missing execution-critical boundary -> `blocked` with one needed user decision.

All product reads, plan work, child dispatch, Git mutation, and validation use created worktree plus bound snapshot. Active agent owns created plan branch only. Launch checkout leaves attempt authority after snapshot binding. Keep completed worktree/branch for user inspection; integrate into launch branch only when user explicitly requested integration.

Worktree creation failure -> `blocked` before product mutation. Return command/error, attempted branch/path, and one needed user action.

## Artifact gate

Before first worker dispatch:

- Current plan-worktree `HEAD` -> authoritative `start_sha`. Plan-declared repository revision metadata never gates execution.
- Capture initial worktree status. Pre-existing changes outside owned paths -> preserve untouched, exclude from staging/review evidence, continue. Pre-existing change overlapping owned path -> `blocked` with exact overlap and one needed authority action.
- `lp-dispatched`: read LP state; verify assigned identity, role `execution orchestrator`, profile `sol_high`, bound snapshot path/digest/size, dependencies, worktree/branch, ownership, and allowed Git operations.
- `user-direct`: verify generated attempt identity, bound snapshot path/digest/size, dependencies, created worktree/branch, ownership, and branch-only Git boundary.

Snapshot digest, identity, or mode-contract mismatch -> `blocked` with observed digest/size and needed authority action. Perform no product mutation or child dispatch. Rehash bound snapshot before final return; snapshot mismatch invalidates attempt.

After snapshot gate passes, source plan path leaves attempt observation, recovery, and completion gates. Later changes there have no effect on running attempt.

## Frozen code boundary

- source branch and `launch_head_sha`: bootstrap provenance only;
- `start_sha`: immutable code baseline for full execution attempt;
- plan branch `HEAD`: moving execution result owned by orchestrator;
- checkpoint comparison: exact `review_base_sha..frozen_sha`;
- final ancestry: `git merge-base --is-ancestor <start_sha> <final_sha>`;
- final scope/content comparison: `git diff <start_sha>..<final_sha>` plus index/worktree status.

After plan worktree creation, all execution, review, recovery, and completion Git checks use bound worktree, plan branch, and exact SHAs. Source branch movement has no effect on attempt. Compare no execution result against moving source-branch ref. Only explicit user/LP cancellation, bound snapshot corruption, plan-worktree drift, or normal execution blockers can stop product work.

## Ownership and profiles

- Active execution orchestrator: sole Git owner for plan worktree and coordinator for every checkpoint. Creates no additional plan/integration worktrees.
- Child roles: implementation worker, reviewer, fix worker, investigator only. Active agent retains plan sequencing, worker coordination, result acceptance, Git operations, review gates, finding disposition, and final validation.
- Implementation/fix workers: edit assigned owned paths only; no Git staging, commits, branch/worktree operations, or state edits.
- One writer per path. Parallel writers require disjoint paths and stable inputs. Serialize shared contracts, generated/serialized assets, migrations, and shared validation environments.
- Reviewer: fresh exact `sol_high` per review dispatch; read-only Git-object inspection at exact frozen SHA, independent of live worktree state.
- Implementation worker: exact profile required by plan/user/AGENTS; otherwise `luna_max`.
- Fix worker: fresh exact profile required by plan/user/AGENTS; otherwise `luna_max`.
- Repeated-struggle takeover: same material issue survives two failed approaches or rechecks by current child -> orchestrator diagnoses repository/evidence, reproduces failure, and supplies targeted guidance. Issue survives guided recheck -> interrupt child, retire old result, close lane barrier, restore only verified task-owned edits to dispatch snapshot, then dispatch fresh role-appropriate child with new `execution_id` and blocker-free contract. Isolated blocker, rescue request, scope correction, confusion, large task, or large context -> keep current child assigned. Ambiguous edit ownership or unresolved authority/product decision -> `blocked`.
- Recovery bounds: investigator read-only; recovery preserves bound objective, requirements, owned paths, dependencies, and Git ownership. Production-bake authority -> [loop-orchestrator/references/state-and-recovery.md#production-bake-gate](../loop-orchestrator/references/state-and-recovery.md#production-bake-gate): require explicit user authority only when changed lighting-input digest predicts replacement bake; builder-proved reuse and replacement build/validation need none. Out-of-bounds recovery -> `blocked`.
- Required profile unavailable -> `blocked`; no silent substitution.

Writer barriers follow ownership: completed disjoint lane closes independently before Git mutation, freeze, or review; unrelated lanes continue. Shared path, contract, or validation environment -> global barrier.

### Candidate and proof environment

Candidate contract carries `read_paths`, `validation_environment`, `unity_mutation`, `expensive_proof_owner`, `expensive_proof_run_point`, and `proof_invalidation_paths`. Candidate may contain multiple workers when ownership remains disjoint and one recovery/proof environment remains coherent. Planner must name one owner for each `production-final` proof after source fan-in, review, and fixes. Expensive-check reduction never changes default review boundaries.

Worker validation defaults to fast/local checks. A worker runs development proof only when task ownership names it. Before project-mutating production-final Unity proof, orchestrator verifies zero writers, clean exact source SHA, one Unity lease, and accepted checkpoint/fix state. Unity workflow pre-gates and process/lock requirements -> [`AGENTS.md`](../../../AGENTS.md) `Unity execution`; `Validation`. Guard paths -> `Tools/Tests/**`, `Tools/Validation/*.ps1`, `Assets/_Game/Editor/MovementLab/*.cs`. Red harness -> `blocked`; repair guard paths, then satisfy repository pre-gate before retry.

Production-final invocation gate -> declare `expected_status` before every invocation. Compare observed workflow status after completion. Two consecutive mismatches -> halt `blocked`, mark comparator suspect, and run no further Unity.

Probe contract -> consume current workflow `schemaVersion: 1`; missing or invalid probe follows workflow failure contract.

Workflow result contract -> modes `Fast`, `Development`, `ProductionPrepare`, `ProductionValidate`; `ProductionValidate` runs repository semantic validator. Durable evidence stays under Git-common destination outside product worktree. JSON fields: `exactSha`, command arguments/exits/logs/elapsed times, probe records, bake count, same-run generated inventory/hashes as provenance, changed generated paths, check ledger, evidence-manifest SHA-256, lock-release proof. Wrapper pins Unity `6000.5.6f1`, verifies `ProjectSettings/ProjectVersion.txt`, satisfies `AGENTS.md` path/cache/process/lock rules, and performs no Git mutation.

Invocation contract -> after satisfying `AGENTS.md` pre-gate, run `Tools/Validation/Invoke-MovementLabWorkflow.ps1 -Mode <...> -ProjectPath <...>` with applicable `-PlanOnly`, `-LedgerPath`, and `-EvidenceRoot`. Carry prior accepted `-LedgerPath` across retries and dependent invocations. Never pass workflow arguments to test runner.

Production bake gate -> [loop-orchestrator/references/state-and-recovery.md#production-bake-gate](../loop-orchestrator/references/state-and-recovery.md#production-bake-gate). `bakeCount` budget applies only to `ProductionPrepare`.

### Gate remediation

- Unblock/relax change touching predicate family -> net-subtractive in that gate family: deletions > insertions.
- No compensating allowlist, replacement hard gate, or new fail-closed predicate in same change.
- Two-fix rule -> second corrective change to same predicate family deletes family wholesale. Retain only separately named safety invariant requiring authority to remove: GUID/meta, path, process/lease, atomic write, source/input digest, or orchestration artifact/evidence integrity.
- Source/input digests and orchestration artifact/evidence hashes remain allowed integrity checks; builder-owned output byte/hash equality never gates.

## Child dispatch contract

Each child dispatch carries unique `execution_id`, invocation mode, assigned identity/profile/role, bounded task/done condition, objective/exclusions, `start_sha`, exact branch/worktree, initial unrelated-status exclusions, owned/protected paths, accepted dependencies, allowed Git operations (`None` for writers; read-only for reviewer/investigator), checks, proof/evidence boundary, candidate read paths, validation environment, Unity mutation flag, expensive-proof owner/run point, proof invalidation paths, and bound snapshot identity/path/digest. Worker dispatch carries exact assigned files/symbols and bounded task only; never full plan dump.

Reviewer dispatch also binds `checkpoint_id`, unique `review_cycle_id`, review kind (`initial | fix-re-review`), covered worker/task execution IDs, checkpoint task/path slice, `review_base_sha`, and `frozen_sha`. Fix dispatch binds originating `review_cycle_id`, `pre_fix_frozen_sha`, accepted finding IDs, finding-owned paths, and acceptance criteria. Investigator dispatch binds recurring issue evidence, attempted orchestrator fixes, failed verification, affected task/path slice, and decision contract `fix_found | no_reasonable_fix`.

Child return repeats identity and role unchanged:

- `status`: `complete | blocked`;
- implementation/fix: changed paths, checks, evidence, finding disposition when applicable;
- reviewer: reviewed SHA, verdict, Critical/High findings using `Review checkpoints` format;
- investigator: decision, reproduced evidence, hypotheses checked, and either precise worker fix contract or reason no reasonable fix remains;
- blocked: exact blocker plus one needed action/recheck.

Reject late, interrupted, replaced, duplicate, foreign, out-of-scope, or Git-inconsistent result. Preserve as evidence only.

## Child lifecycle gate

- Registry: record child agent ID, `execution_id`, role, and state (`running | returned | retired`) at dispatch. One dispatch gets one child turn; follow-up work gets fresh child required by role rules.
- Repeated-struggle gate may trigger at any lifecycle point. Current child keeps assignment through diagnosis and guided recheck. Replacement starts only after gate completes; wait for terminal retirement and restored writer boundary before dispatch.
- Terminal return: collaboration runtime reports child turn finished and child is no longer running. Messages, commentary, partial reports, filesystem changes, or apparent task completion while child remains running -> progress evidence only.
- Returned child: capture immutable report, mark `returned`, then retire immediately. Result acceptance, Git verification, and checkpoint work use captured report; returned agent stays retired.
- Replaced, restarted, cancelled, or no-longer-needed running child: call `interrupt_agent`, wait for terminal state, capture late output as evidence only, then mark `retired`. Finish retirement before replacement dispatch or lane-barrier close.
- Hung child: no terminal return and no new progress evidence within bounded wait -> inspect worktree directly, preserve verified task-owned edits, `interrupt_agent`, wait for terminal state, retire, restore writer boundary, then dispatch fresh child. Never stack second writer over same paths while first still running.
- Exit drain: before any `complete` or `blocked` return, call `list_agents`; interrupt every running descendant, wait for terminal states, then call `list_agents` again. `complete` requires zero running descendants and every registry entry `retired`. Unresolved descendant -> `blocked` with exact agent ID, role, state, and cleanup attempts.

- Quiet reporting: update user only on kickoff, material checkpoint/fix/validation/blocker/completion change, or required one-line heartbeat; unchanged waits and routine child state -> silent; batch concurrent changes; full roster only on request or final return.

## Worker -> reviewer barrier

Reviewer dispatch requires all checkpoint-covered implementation workers through this sequence:

1. Receive terminal return from collaboration runtime; confirm covered child no longer running.
2. Capture final report; require `status: complete` plus terminal changed-path list; verify identity, checks, and Git boundary; compare reported paths against `git diff --name-only` for worker slice. Path outside owned set -> reject report as evidence only, keep barrier open.
3. Mark child `returned`, then `retired`. Covered worker registry contains zero `running` entries.
4. Close writer barrier; stage only checkpoint paths; commit; resolve exact `frozen_sha`; verify scope and unrelated status.
5. Dispatch reviewer bound to committed `review_base_sha..frozen_sha`.

Per-worker checkpoint covers one worker. Grouped checkpoint covers every named worker. Any covered worker still `running`, lacking terminal return, blocked, or unverified -> reviewer barrier remains open. Unrelated disjoint workers outside checkpoint may keep running.

Conditional fix re-review uses same terminal-return, retirement, writer-barrier, commit, frozen-SHA, scope, and unrelated-status gates for covered fix worker.

## Review checkpoints

Default: one checkpoint per implementation worker. Satisfy worker -> reviewer barrier, then review before dependent work.

Accepted plan may group multiple implementation workers into one checkpoint only when combined chunk creates stronger review boundary than partial worker states. Plan must name checkpoint, covered tasks/workers, join condition, and technical rationale. Valid rationale: producer/consumer contract, coordinated code/serialized asset wiring, or another state whose partial review lacks meaningful proof. Throughput or fewer reviewer calls is insufficient. Missing explicit grouped checkpoint -> per-worker review.

Plan fan-out -> launch every ready sibling after shared predecessors. Worker terminal return -> satisfy worker -> reviewer barrier for declared per-worker checkpoint immediately; unrelated disjoint workers continue. Grouped checkpoint waits only for terminal returns from all named members plus join condition. Branch checkpoint gates fan-in. Cross-lane dependency, overlapping paths, or shared validation environment -> serialize.

Review scope: checkpoint task/path slice from `review_base_sha` to `frozen_sha`, plus material Critical/High integration risks visible at frozen SHA. Finding qualifies only with concrete trigger, harmful outcome, and code/evidence showing realistic risk. Harmful outcome must break scoped behavior, correctness, safety, security, data/asset integrity, required contract, build/integration/validation, or materially slow runtime or team iteration.

PoC review filter: prioritize failures blocking playtest learning or reliable iteration. Omit style, naming, formatting, comment preference, optional cleanup, speculative refactor, production hardening, theoretical out-of-scope edge case, and test-coverage suggestion without demonstrated material failure risk. Reviewer returns `no findings` when no qualifying issue exists. Builder-owned generated-YAML reserialization — `fileID` reorder, whitespace, imported-model records — is not a finding; qualify only through canonical object/reference-graph change.

Fix re-review gate: after fix commit, calculate `fix_loc` from `git diff --numstat <pre_fix_frozen_sha>..<post_fix_frozen_sha>` by summing added+deleted counts for text rows. Binary rows add zero LOC but remain in review scope. Count all Critical/High findings reported by originating review before disposition. `fix_loc > 200` or reported finding count `> 3` -> mandatory fresh exact `sol_high` re-review. Bind `review_base_sha = pre_fix_frozen_sha`, `frozen_sha = post_fix_frozen_sha`, kind `fix-re-review`, and same checkpoint task/path slice. Apply normal review scope, materiality, and PoC filters. Otherwise fix advances accepted head without re-review. Re-review findings -> fresh fix worker, then apply gate again to that review/fix cycle. Next lane or wave uses accepted post-fix head as `review_base_sha`.

Reviewer finding format: one block per finding, exactly three fields:

```markdown
location: [exact paths/symbols]
issue: [concrete trigger, harmful outcome, and code/evidence proving realistic material risk]
proposed fix: [narrow remediation plus acceptance boundary and contracts/safeguards to preserve]
```

Orchestrator assigns checkpoint-scoped finding IDs during disposition. Reviewer keeps reviewed SHA and verdict outside finding blocks.

## Execution loop

### Check ledger

Build declared checks before dispatch. Authored row schema -> [`$write-orchestrator-coding-plan`](../write-orchestrator-coding-plan/SKILL.md#check-contract). `Tools/Validation/Invoke-MovementLabWorkflow.ps1` writes sole executed ledger `check-ledger.json`; state stores absolute ledger path plus SHA-256 only. Execution adds `status`, `executed_sha`, `validated_sha`, `evidence_path`, `evidence_digest`, and `subsumed_checks`. One owner binds every production-final row. Workers do not claim rows outside task scope.

Final verification runs pending or invalidated rows only. Reuse executed evidence at exact SHA. Production bake follows `AGENTS.md` gate; consume workflow-declared lighting-input and invalidation paths instead of duplicating them here. Merge/fix edits invalidate rows whose paths intersect `invalidation_paths`; unchanged multi-plan fast-forward reuses valid non-bake evidence after SHA/content attestation.

Ledger traces:

- validator-only diff -> direct semantic `validate`; compile proof is subsumed per `AGENTS.md`; production bake remains valid after builder-gate reattest.
- shader-only diff -> `compile` and shader-message check; production bake remains valid unless shader path is declared lighting input.
- lighting-input diff in `production-bake lighting-input set` -> invalidate production bake only; retain unrelated fast rows.
- unchanged fast-forward -> exact-SHA proof reuse after ancestry, input digest, environment, and empty invalidation-path diff checks.
- multi-wave merge -> merge/scope/downstream rows per wave; final wave executes union of pending production-final rows once.
- post-proof fix -> invalidate only rows whose declared paths intersect fix; lighting-input intersection invalidates production-final proof and requires authority before any replacement bake.

1. Parse graph, tasks, and checkpoints. Dispatch every ready fan-out worker together; otherwise dispatch next serial worker.
2. Keep running child assigned through normal difficulty. Same material issue survives two failed approaches or rechecks -> diagnose and guide current child. Guided recheck fails -> interrupt, retire, restore writer boundary, then dispatch fresh child. Recurring issue unresolved by orchestrator -> run recurring-issue escalation; `fix_found` dispatches fresh standard worker from investigator contract. Wider in-scope recovery issue exposed by that worker -> dispatch fresh exact `sol_high` recovery worker under escalation bounds. `no_reasonable_fix` or failed/out-of-bounds `sol_high` recovery -> stop execution as `blocked`. Process each worker terminal return immediately. Capture final report, retire child, then verify report against files, Git, scope, checks, and identity.
3. Per-worker checkpoint -> satisfy worker -> reviewer barrier; dispatch fresh exact `sol_high` reviewer. Keep unrelated disjoint workers running. Grouped checkpoint -> wait for terminal returns from all named workers plus join condition, then satisfy same barrier.
4. Reviewer inspects bound Git objects at frozen SHA, applies review-scope materiality and PoC filters, reports qualifying Critical/High findings only, and performs no edits/tests unless explicitly assigned.
5. No accepted finding -> mark checkpoint accepted. Accepted finding -> one fresh fix worker with narrow finding-owned scope.
6. Stop fix writer, close lane barrier, verify scope, stage, commit, require owned paths clean, and freeze new full SHA. Rerun checks invalidated by fix; pre-fix review does not prove post-fix behavior. Apply fix re-review gate. Triggered -> dispatch fresh exact `sol_high` reviewer and route results through steps 4-6. Not triggered -> advance checkpoint from post-fix head.
7. Fan-in waits for every branch checkpoint, not unrelated worker completion alone. Repeat until every checkpoint has verdict and finding disposition. Run final checks at exact committed `HEAD`. Rehash bound snapshot. Verify `HEAD` descends from `start_sha`; calculate owned path/content diff from `start_sha..HEAD`; verify clean index/worktree, initial unrelated status, branch, dependencies, and requirements.

Required unowned edit, plan decomposition change, dependency drift, bound snapshot mismatch, or product decision outside accepted plan -> `blocked`. In-scope attempt-mechanics revision authorized by recurring-issue escalation is recovery, not plan decomposition change. Route one needed action to LP in `lp-dispatched`; route it to user in `user-direct`. Active agent preserves accepted plan boundary and authority state.

## Completion routing

Return common facts:

- invocation mode, `plan_id`, `attempt_id`, active-agent identity, role `execution orchestrator`, profile;
- `status: complete | blocked`;
- source plan path as provenance; bound snapshot path, accepted digest/size, observed final digest/size;
- exact `start_sha`, dependency SHAs, branch, worktree, initial unrelated-status snapshot;
- clean committed exact plan SHA when complete;
- changed paths and scope proof from exact `start_sha..final_sha`;
- each checkpoint ID, covered executions, each review cycle/kind/base/frozen SHA, verdict, reported finding count, fix LOC, re-review decision, and accepted finding dispositions;
- checks: command/workflow, working directory, observed result, evidence, exact SHA;
- blocker, evidence, and one needed authority action/recheck when blocked.

`lp-dispatched` also returns same `run_id` and LP-bound identity fields to LP. LP verifies Git/artifact facts and dispatches merging agent. Active agent returns plan SHA only.

`user-direct` returns facts directly to user, including launch checkout path/branch/status snapshot and created branch/worktree. Result remains isolated unless user explicitly authorized integration.

`complete` requires bound snapshot match, exact committed head descending from `start_sha`, owned paths clean, initial unrelated status preserved, owned-only `start_sha..final_sha` diff, every worker covered by completed checkpoint review/fix/re-review flow, every triggered re-review resolved, passing final checks, and satisfied child lifecycle gate.
