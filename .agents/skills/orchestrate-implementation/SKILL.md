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
4. Derive unique `codex/<plan-slug>-<attempt-id>` branch and sibling worktree path outside launch checkout. Confirm target parent writable for active agent and child agents. Existing branch/path -> choose new unique names; preserve existing worktrees and branches.
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
- Child roles: implementation worker, reviewer, fix worker only. Active agent retains plan sequencing, worker coordination, result acceptance, Git operations, review gates, finding disposition, and final validation.
- Implementation/fix workers: edit assigned owned paths only; no Git staging, commits, branch/worktree operations, or state edits.
- One writer per path. Parallel writers require disjoint paths and stable inputs. Serialize shared contracts, generated/serialized assets, migrations, and shared validation environments.
- Reviewer: fresh exact `sol_high` per review checkpoint; read-only Git-object inspection at exact frozen SHA, independent of live worktree state.
- Implementation worker: exact profile required by plan/user/AGENTS; otherwise `luna_max`.
- Fix worker: fresh exact profile required by plan/user/AGENTS; otherwise `luna_max`.
- Stuck-child takeover: any child blocker, request for rescue, repeated failed approach, scope drift, confusion, or loss of useful progress -> interrupt immediately. Orchestrator takes over diagnosis: inspect repository/evidence, reproduce failure, run safe checks, determine solution, and remove blocker or improve task contract. Retire old child/result, close lane barrier, restore only verified task-owned edits to dispatch snapshot, then dispatch fresh role-appropriate child with new `execution_id` and blocker-free contract. Never coach, resume, or retry stuck child. Ambiguous edit ownership or unresolved authority/product decision -> `blocked`.
- Proactive context reset: massive implementation chunk or overwhelmed child context -> same stuck-child takeover flow before failure compounds.
- Required profile unavailable -> `blocked`; no silent substitution.

Writer barriers follow ownership: completed disjoint lane closes independently before Git mutation, freeze, or review; unrelated lanes continue. Shared path, contract, or validation environment -> global barrier.

### Candidate and proof environment

Candidate contract carries `read_paths`, `validation_environment`, `unity_mutation`, `expensive_proof_owner`, `expensive_proof_run_point`, and `proof_invalidation_paths`. Candidate may contain multiple workers when ownership remains disjoint and one recovery/proof environment remains coherent. Planner must name one owner for each `production-final` proof after source fan-in, review, and fixes. Expensive-check reduction never changes default review boundaries.

Worker validation defaults to fast/local checks. A worker runs development proof only when task ownership names it. Before project-mutating production-final Unity proof, orchestrator verifies zero writers, clean exact source SHA, one Unity lease, accepted reviews/fixes, and valid review marker. Unity process and project lock checks apply before and after every Unity step.

Review marker contract -> Git-common `architecture-evidence/movement-lab-prebake/reviews/<projectSha>.json`; schema `1`; required `gitSha`, `projectSha`, `sourceSha`, `reviewedSha`, `sourceReviewCompleted`, `criticalHighFixesApplied`, reviewer/checkpoint identities, `completedUtc`, at least one durable `reviewReportPaths`/`reviewReportSha256s` pair, and finding dispositions. Writer requires review/fix flags true, validates current HEAD, source/reviewed ancestry, clean non-generated source, report bytes, and destination; writes atomically without Git mutation.

Probe contract -> when file exists, `schemaVersion: 1` requires typed `gitSha` (current HEAD), `unityVersion`, `manifestStatus`, `staleStages`, `staleReasons`, `lightingInputDigest`, `sourceSignature`, `outputFingerprint`, `bakedProfile`, `fingerprintPaths`, and `fingerprintHashes`; missing probe remains planned/optional until T4 producer exists.

Workflow result contract -> modes `Fast`, `Development`, `ProductionPrepare`, `ProductionValidate`; durable evidence outside project; JSON fields `exactSha`, command arguments/exits/logs/elapsed times, probe records, bake count, before/after generated hashes, changed generated paths, check ledger, evidence-manifest SHA-256, and lock-release proof. Wrapper pins Unity `6000.5.6f1`, uses short absolute project path, private warm `Library`, hidden `Start-Process -Wait -PassThru`, exclusive process/lock lease, and no Git mutation.

## Child dispatch contract

Each child dispatch carries unique `execution_id`, invocation mode, assigned identity/profile/role, bounded task/done condition, objective/exclusions, `start_sha`, exact branch/worktree, initial unrelated-status exclusions, owned/protected paths, accepted dependencies, allowed Git operations (`None` for writers; read-only for reviewer), checks, proof/evidence boundary, candidate read paths, validation environment, Unity mutation flag, expensive-proof owner/run point, proof invalidation paths, and bound snapshot identity/path/digest.

Reviewer dispatch also binds `checkpoint_id`, covered worker/task execution IDs, checkpoint task/path slice, `review_base_sha`, and `frozen_sha`. Fix dispatch binds `pre_fix_frozen_sha`, accepted finding IDs, finding-owned paths, and acceptance criteria.

Child return repeats identity and role unchanged:

- `status`: `complete | blocked`;
- implementation/fix: changed paths, checks, evidence, finding disposition when applicable;
- reviewer: reviewed SHA, verdict, Critical/High findings with exact paths/symbols and evidence;
- blocked: exact blocker plus one needed action/recheck.

Reject late, interrupted, replaced, duplicate, foreign, out-of-scope, or Git-inconsistent result. Preserve as evidence only.

## Child lifecycle gate

- Registry: record child agent ID, `execution_id`, role, and state (`running | returned | retired`) at dispatch. One dispatch gets one child turn; follow-up work gets fresh child required by role rules.
- Stuck signal at any lifecycle point -> run stuck-child takeover before more child work. Orchestrator may continue independent diagnosis while child retirement completes; replacement waits for terminal retirement and restored writer boundary.
- Terminal return: collaboration runtime reports child turn finished and child is no longer running. Messages, commentary, partial reports, filesystem changes, or apparent task completion while child remains running -> progress evidence only.
- Returned child: capture immutable report, mark `returned`, then retire immediately. Result acceptance, Git verification, and checkpoint work use captured report; returned agent stays retired.
- Replaced, restarted, cancelled, or no-longer-needed running child: call `interrupt_agent`, wait for terminal state, capture late output as evidence only, then mark `retired`. Finish retirement before replacement dispatch or lane-barrier close.
- Exit drain: before any `complete` or `blocked` return, call `list_agents`; interrupt every running descendant, wait for terminal states, then call `list_agents` again. `complete` requires zero running descendants and every registry entry `retired`. Unresolved descendant -> `blocked` with exact agent ID, role, state, and cleanup attempts.

## Worker -> reviewer barrier

Reviewer dispatch requires all checkpoint-covered implementation workers through this sequence:

1. Receive terminal return from collaboration runtime; confirm covered child no longer running.
2. Capture final report; require `status: complete`; verify identity, scope, files, checks, and Git boundary.
3. Mark child `returned`, then `retired`. Covered worker registry contains zero `running` entries.
4. Close writer barrier; stage only checkpoint paths; commit; resolve exact `frozen_sha`; verify scope and unrelated status.
5. Dispatch reviewer bound to committed `review_base_sha..frozen_sha`.

Per-worker checkpoint covers one worker. Grouped checkpoint covers every named worker. Any covered worker still `running`, lacking terminal return, blocked, or unverified -> reviewer barrier remains open. Unrelated disjoint workers outside checkpoint may keep running.

## Review checkpoints

Default: one checkpoint per implementation worker. Satisfy worker -> reviewer barrier, then review before dependent work.

Accepted plan may group multiple implementation workers into one checkpoint only when combined chunk creates stronger review boundary than partial worker states. Plan must name checkpoint, covered tasks/workers, join condition, and technical rationale. Valid rationale: producer/consumer contract, coordinated code/serialized asset wiring, or another state whose partial review lacks meaningful proof. Throughput or fewer reviewer calls is insufficient. Missing explicit grouped checkpoint -> per-worker review.

Plan fan-out -> launch every ready sibling after shared predecessors. Worker terminal return -> satisfy worker -> reviewer barrier for declared per-worker checkpoint immediately; unrelated disjoint workers continue. Grouped checkpoint waits only for terminal returns from all named members plus join condition. Branch checkpoint gates fan-in. Cross-lane dependency, overlapping paths, or shared validation environment -> serialize.

Review scope: checkpoint task/path slice from `review_base_sha` to `frozen_sha`, plus Critical/High integration risks visible at frozen SHA. Fix result advances accepted head without re-review. Next lane or wave uses post-fix head as `review_base_sha`.

## Execution loop

### Check ledger

Build ledger before dispatch. Each row has `check_id`, `tier` (`fast|development|production-final`), `status` (`pending|executed|reused|deferred|invalidated`), `run_point`, `mutates_project`, `input_paths`, `input_digest`, `environment_fingerprint`, `invalidation_paths`, `subsumes`, `executed_sha`, `validated_sha`, `evidence_path`, `evidence_digest`, and `subsumed_checks`. One owner binds every production-final row. Workers do not claim rows outside task scope.

Final verification runs pending or invalidated rows only. Reuse executed evidence at exact SHA. Pure checks may reattest at descendant SHA only when ancestry holds, declared input digest and environment match, and `git diff` across every invalidation path is empty. Bake, capture, and manual proof never reattest after render or lighting input changes. Merge/fix edits invalidate rows whose paths intersect `invalidation_paths`; unchanged one-plan fast-forward reuses valid plan evidence after cheap SHA/content attestation.

Ledger traces:

- validator-only diff -> `compile` and read-only `validate`; bake/capture rows remain valid.
- shader-only diff -> `compile` and shader-message check; lighting rows remain valid unless declared render input.
- lighting-input diff -> invalidate production bake/capture/manual rows; retain unrelated fast rows.
- unchanged fast-forward -> exact-SHA proof reuse after ancestry, input digest, environment, and empty invalidation-path diff checks.
- multi-wave merge -> merge/scope/downstream rows per wave; final wave executes union of pending production-final rows once.
- post-proof fix -> invalidate only rows whose declared paths intersect fix; render/lighting intersection forces bake/capture rerun.

1. Parse graph, tasks, and checkpoints. Dispatch every ready fan-out worker together; otherwise dispatch next serial worker.
2. Monitor running children for stuck signals. Signal -> interrupt, diagnose directly, remove blocker or improve contract, restore writer boundary, then dispatch fresh child. Process each worker terminal return immediately. Capture final report, retire child, then verify report against files, Git, scope, checks, and identity. No retry on old worker.
3. Per-worker checkpoint -> satisfy worker -> reviewer barrier; dispatch fresh exact `sol_high` reviewer. Keep unrelated disjoint workers running. Grouped checkpoint -> wait for terminal returns from all named workers plus join condition, then satisfy same barrier.
4. Reviewer inspects bound Git objects at frozen SHA, reports Critical/High findings only, performs no edits/tests unless explicitly assigned.
5. No accepted finding -> mark checkpoint accepted. Accepted finding -> one fresh fix worker with narrow finding-owned scope.
6. Stop fix writer, close lane barrier, verify scope, stage, commit, require owned paths clean, and freeze new full SHA. Do not re-review fix. Rerun checks invalidated by fix; pre-fix review does not prove post-fix behavior. Advance checkpoint from post-fix head.
7. Fan-in waits for every branch checkpoint, not unrelated worker completion alone. Repeat until every checkpoint has verdict and finding disposition. Run final checks at exact committed `HEAD`. Rehash bound snapshot. Verify `HEAD` descends from `start_sha`; calculate owned path/content diff from `start_sha..HEAD`; verify clean index/worktree, initial unrelated status, branch, dependencies, and requirements.

Required unowned edit, plan decomposition change, dependency drift, bound snapshot mismatch, or product decision outside accepted plan -> `blocked`. Route one needed action to LP in `lp-dispatched`; route it to user in `user-direct`. Active agent preserves accepted plan boundary and authority state.

## Completion routing

Return common facts:

- invocation mode, `plan_id`, `attempt_id`, active-agent identity, role `execution orchestrator`, profile;
- `status: complete | blocked`;
- source plan path as provenance; bound snapshot path, accepted digest/size, observed final digest/size;
- exact `start_sha`, dependency SHAs, branch, worktree, initial unrelated-status snapshot;
- clean committed exact plan SHA when complete;
- changed paths and scope proof from exact `start_sha..final_sha`;
- each checkpoint ID, covered executions, review base/frozen SHA, verdict, accepted finding dispositions;
- checks: command/workflow, working directory, observed result, evidence, exact SHA;
- blocker, evidence, and one needed authority action/recheck when blocked.

`lp-dispatched` also returns same `run_id` and LP-bound identity fields to LP. LP verifies Git/artifact facts and dispatches merging agent. Active agent returns plan SHA only.

`user-direct` returns facts directly to user, including launch checkout path/branch/status snapshot and created branch/worktree. Result remains isolated unless user explicitly authorized integration.

`complete` requires bound snapshot match, exact committed head descending from `start_sha`, owned paths clean, initial unrelated status preserved, owned-only `start_sha..final_sha` diff, every worker covered by completed checkpoint review/fix flow, passing final checks, and satisfied child lifecycle gate.
