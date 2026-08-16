---
name: orchestrate-implementation
description: Use when LP dispatches one accepted coding plan for orchestration, or user explicitly invokes this skill to execute one accepted coding plan through bounded workers, independent reviewers, fixes, and exact-SHA validation.
---

# Orchestrate Implementation

Invocation -> active agent becomes execution orchestrator for one accepted plan. Orchestrator owns plan branch/worktree Git, barriers, freezes, scope checks, recovery, validation, completion. Child roles: writer, reviewer, investigator.

## Invocation mode

- `lp-dispatched`: complete LP handoff required. LP owns run state, coordination, merge dispatch, user-branch authority.
- `user-direct`: explicit user invocation. User owns LP-equivalent decisions. Orchestrator creates isolated branch/worktree; no LP state or merge authority.

Never switch mode. Partial/ambiguous LP handoff -> `blocked` before mutation.

## LP handoff contract

`lp-dispatched` binds:

- `run_id`, stable `plan_id`, unique `attempt_id`
- identity, role `execution orchestrator`, profile `sol_high`
- plan artifact absolute path, lowercase SHA-256, byte size; no separate provenance copy in this mode
- requirement IDs, objective, dependency SHAs
- immutable `start_sha`, branch, isolated worktree
- owned/protected paths, checks, proof boundary, evidence locations
- allowed Git operations limited to plan branch/worktree
- LP state path read-only

Missing/mismatched field -> `blocked` before child dispatch or product mutation.

## Pinned plan artifact

Plan artifact = authoritative plan file, both modes. "Snapshot" = user-direct pinned copy only. Mode asymmetry deliberate.

`lp-dispatched` -> no copy. LP-bound plan artifact is authority; its path is create-once + unique per attempt, so bound digest alone enforces immutability. Verify in place: recompute hash + size with `$p` = plan artifact path; compare to bound values.

`user-direct` -> copy plan artifact exactly once into the absent executions path, because the user-supplied source is mutable and not create-once:

1. Destination must be absent immediately before copy.
2. Copy exact bytes once; never overwrite.
3. Source path leaves execution authority after the copy binds.

Copy/hash/size commands (`Copy-Item` user-direct only):

```powershell
Copy-Item -LiteralPath $sourcePath -Destination $snapshotPath -ErrorAction Stop
(Get-FileHash -Algorithm SHA256 -Path $p).Hash.ToLowerInvariant()
(Get-Item $p).Length
```

`$p` is the plan artifact integrity target (`lp-dispatched`) or each source/snapshot integrity target (`user-direct`). Integrity rechecks use hash + item metadata only; never load bytes into context.

Plan artifact digest/size mismatch, either mode -> `blocked`.

## User-direct bootstrap

1. Resolve repository root + accepted plan source. Create stable `plan_id`, unique `attempt_id`.
2. User-supplied plan source is mutable, not create-once -> pin it. Copy into absent `<git-common-dir>/orchestrate-implementation/<plan-id>/executions/<attempt-id>.md` through the pinned plan artifact procedure; that snapshot becomes the plan artifact. Bind source only as provenance.
3. Capture launch checkout path, branch, exact `HEAD` as `launch_head_sha`, status. Preserve launch checkout.
4. Create unique `codex/<plan-slug>-<attempt-id>` branch + short isolated worktree from exact `launch_head_sha`; confirm child writability and worktree root/branch/`HEAD`.
5. Bind full `HEAD` as immutable `start_sha`; derive/bind objective, requirements, exact dependency SHAs, ownership, checks, proof boundary, evidence from snapshot.

Missing critical boundary or creation failure -> `blocked` with evidence + one needed action. Keep completed worktree/branch for inspection; integration requires explicit user authority.

## Artifact gate

Before first child dispatch:

- plan-worktree `HEAD` is `start_sha`; capture status
- unowned pre-existing changes -> preserve/exclude; overlap with owned path -> `blocked`
- `lp-dispatched` -> verify LP identity/role/profile, plan artifact path/digest/size in place, dependencies, worktree/branch, ownership, Git authority
- `user-direct` -> verify attempt identity, pinned snapshot through pinned integrity procedure, created worktree/branch, ownership, branch-only Git boundary
- verify bound dependency list; for each SHA run `git merge-base --is-ancestor <dependency_sha> <start_sha>`

Plan artifact identity/digest/size mismatch, missing dependency, or non-ancestor dependency -> `blocked`; no child dispatch/product mutation. Final return rechecks the active-mode plan artifact through pinned integrity procedure.

## Frozen code boundary

- source branch + `launch_head_sha` -> bootstrap provenance only
- `start_sha` -> immutable attempt baseline
- plan branch `HEAD` -> orchestrator-owned result
- checkpoint -> exact `review_base_sha..frozen_sha`
- final -> `git merge-base --is-ancestor <start_sha> <final_sha>` + `git diff <start_sha>..<final_sha>` + status

After creation all Git checks use bound worktree/branch/exact SHA. Moving source branch never gates execution.

## Ownership and recovery

- orchestrator: sole Git owner; creates no extra plan/integration worktrees
- writer: implementation/fix product writes inside assigned owned paths; builder-generated outputs -> generated output gate; no Git/state/worktree/branch operations
- reviewer: read-only frozen Git inspection; findings only; [`code-reviewer`](agents/code-reviewer.md)
- investigator: read-only recurring-issue diagnosis
- one writer/path; disjoint writers parallel only with stable inputs. Shared contract, generated asset, migration, validation environment -> serialize
- writer profile -> exact plan/user/`AGENTS.md` requirement; otherwise `luna_max`. Reviewer -> fresh exact `sol_medium`
- required profile unavailable, unresolved ownership, authority, or product decision -> `blocked`

Recovery preserves bound objective, requirements, ownership, dependencies, Git authority. Production bake authority -> [production-bake gate](../loop-orchestrator/references/state-and-recovery.md#production-bake-gate). Out-of-bounds recovery -> `blocked`.

### Repeated-struggle takeover

Same material issue survives two failed approaches/rechecks by current child -> orchestrator diagnoses repository/evidence, reproduces failure, gives targeted guidance. Guided recheck fails -> interrupt child, wait terminal, retire result, close lane barrier, restore only verified task-owned edits outside `protected_paths` and every slice in `bound_concurrent_fanout_peer_owned_paths` with `git restore --source=<dispatch_snapshot_sha> -- <exact task-owned paths>`, dispatch fresh role-appropriate child with new `execution_id`.

Current child stays assigned for isolated blocker, rescue, scope correction, confusion, large task/context. Wider recurring issue -> investigator; same unowned-churn path set across two dispatches is a repository defect -> dispatch investigator directly, never retire and replace the writer; `fix_found` -> fresh standard writer from its contract; exposed wider in-scope recovery -> fresh exact `sol_high` recovery writer. `no_reasonable_fix`, failed/out-of-bounds recovery -> `blocked`.

## Proof environment

Writer contract carries `read_paths`, `validation_environment`, `unity_mutation`, `expensive_proof_owner`, `expensive_proof_execution`. Enum: `same_dispatch | orchestrator_phase | None`. `same_dispatch` -> named task owner runs proof before return. `orchestrator_phase` -> execution orchestrator runs shared proof only after plan `checks` names checkpoint/final trigger, every declared source producer reaches trigger, generated outputs reach generated-output gate, checkpoint review/fixes accepted. `None` -> no expensive proof. Trigger derives from plan checks plus exact declared outputs; never narrative run-point or workflow path-selection flag. Planner names one production-final owner after source fan-in, review, fixes. Worker checks default fast/local unless task owns development proof. Expensive-proof reduction never relaxes review.

Source-only writer for `unity_mutation: true` -> return Unity compile proof before terminal return unless `expensive_proof_execution: orchestrator_phase`. `orchestrator_phase` -> writer runs fast/local checks only; execution orchestrator runs sole declared Unity proof after trigger. Relevant Unity test/import or compile-only Unity batch qualifies; `dotnet build` does not.

Unity command -> orchestrator supplies exact [`AGENTS.md`](../../../AGENTS.md)-compliant command using `Start-Process -Wait -PassThru` + `.ExitCode`. Direct `Unity.exe` invocation or polling instead of `-Wait` -> reject before launch.

Production-final gate: declare `expected_status`; compare observed status. Two consecutive mismatches -> `blocked`, comparator suspect, no further Unity. Probe requires valid workflow `schemaVersion: 1`; failure follows workflow failure contract.

## Workflow invocation

Workflow invocation/order -> [workflow harness precondition](../loop-orchestrator/references/state-and-recovery.md#workflow-harness-precondition).

Success extraction only: `status`, `exactSha`, evidence `result`/`path`, `evidenceManifestSha256`, `bakeCount`, `lockReleaseProof`. Failure evidence may retain full JSON + exit + log. `ProductionPrepare` bake budget -> [production-bake gate](../loop-orchestrator/references/state-and-recovery.md#production-bake-gate).

## Gate remediation

- unblock/relax predicate-family change -> net-subtractive: deletions > insertions
- no compensating allowlist, replacement hard gate, fail-closed predicate in same change
- second correction to same family -> delete family wholesale; retain named authority-required invariant only: GUID/meta, path, process/lease, atomic write, source/input digest, orchestration artifact/evidence integrity
- source/input and artifact/evidence hashes allowed integrity checks; generated-output byte/hash equality never gates

## Generated output gate

Classifier -> union current inventory + task-declared outputs, each builder-traced; source `inventory | declared-new | both`, scope `owned | inventory-exception`. Outside union, untraced declaration, inventory-unknown unowned -> reject/`blocked`. Coverage -> exact changed set; per-path source/scope; selected set; matching headers; `SEMANTIC:`, `DANGLING:`, `GUID:`, `PAIRS:`, `UNSUPPORTED:`. Run `Tools/Validation/Compare-GeneratedYaml.ps1 -Base <writer-slice-base-sha> -Head WORKTREE -FailOnDangling`. Builder-traced task-declared semantic changes are valid review evidence. Accept only complete coverage, zero unknown types, no dangling increase, stable existing GUIDs, intact asset/`.meta` pairs. Binary hashes bind provenance only. Never infer coverage from glob, raw-diff exclusion, separate-regeneration commit. Comparator failure, incomplete coverage, unknown type, dangling increase, GUID churn, broken pair -> reject/`blocked`.

## Child dispatch contract

Every dispatch gets unique `execution_id`. Dispatch/return text: terse AI-to-AI, exact paths/symbols/commands/SHAs, no narration. Worker receives bounded task only; never full plan dump.

### Writer

Every implementation/fix writer contract carries:

- `execution_id`; identity/profile/role
- `dispatch_snapshot_sha`; exact immutable full SHA captured immediately before this writer dispatch, used for writer-scope restoration
- `pre_dispatch_tracked_dirty_paths`; exact repo-relative tracked path set already dirty against `dispatch_snapshot_sha`, captured immediately before dispatch
- `bound_concurrent_fanout_peer_owned_paths`; exact `execution_id -> owned/generated-output path set` for every peer that can run concurrently with this writer: all already-active potentially overlapping writers plus every sibling in this ready fan-out group. Before the group's first writer dispatch, bind that complete map into every sibling contract, including later-dispatched siblings; `None` only when no such peer exists. Protected paths remain an exact bound set
- `protected_paths`; exact repo-relative protected path set bound at dispatch
- `prior_unowned_churn_path_sets`; exact `execution_id -> sorted unowned-churn path set` from every earlier writer dispatch in this run, or `None`
- bounded task + done condition; objective + exclusions
- exact worktree; files/symbols; owned paths
- product writes: owned paths; builder-generated outputs -> generated output gate
- scope self-check before every expensive proof: `git status --porcelain` -> each changed path inside owned set or [generated output gate](#generated-output-gate). For other unowned paths, first record the exact path set, changed-line count, and recurrence against bound `prior_unowned_churn_path_sets` before any restore or proof. Restore only exact paths proven newly introduced by this writer since the pre-dispatch baseline: proof requires the path absent from `pre_dispatch_tracked_dirty_paths` and recorded in this writer's change ledger when it first wrote the path; missing or ambiguous proof fails. It must also be outside `protected_paths` and every slice in `bound_concurrent_fanout_peer_owned_paths`. Revert only when that eligible set is small and non-recurring: no more than 10 paths, no file above 200 changed lines, and no identical path set in a prior dispatch; use `git restore --source=<dispatch_snapshot_sha> -- <exact unowned paths>`. Any unproven, pre-existing, protected, concurrent/fan-out-peer-owned, oversized, or recurring path -> stop and return `blocked` as `systemic-repo-defect`; never restore it. Phantom churn is a repository defect surfaced through the worker, never a worker scope error. Always report it; never silently absorb it or spend Unity/workflow proof on the out-of-scope tree
- Git/state: `None`
- applicable checks; proof boundary; `read_paths`; `validation_environment`; `unity_mutation`; `expensive_proof_owner`; `expensive_proof_execution`; `orchestrator_phase` -> exact plan-check trigger + declared producer/output set; source-only `unity_mutation: true` -> compile-proof command/result/evidence

Fix adds: `review_cycle_id`, `pre_fix_frozen_sha`, accepted finding IDs, finding-owned paths, acceptance criteria.

Omit plan artifact identity/path/digest, unrelated status, dependency SHAs, orchestrator Git facts.

### Reviewer

Reviewer contract:

- `execution_id`; identity/profile/role; repository + exact worktree; read-only Git
- `checkpoint_id`, `review_cycle_id`, kind `initial | fix-re-review`, covered execution IDs, task/path slice, `review_base_sha`, `frozen_sha`
- bound plan artifact path + exact covered-task `implementation` locator
- generated review: `separate-commit` or `excluded-slice`; exact changed generated-output coverage evidence always attached; excluded raw slice -> exact paths + same evidence
- unique scratch root outside product worktree + scratch rules

Omit plan artifact digest and unrelated fields. Reviewer behavior/result -> [`code-reviewer`](agents/code-reviewer.md).

### Investigator

Investigator contract:

- `execution_id`; identity/profile/role; repository + exact worktree; `frozen_sha`; read-only Git
- recurring evidence, attempted orchestrator fixes, failed verification, affected task/path slice, objective/exclusions
- decision: `fix_found | no_reasonable_fix`
- unique scratch root outside product worktree + scratch rules

Omit plan artifact and unrelated fields.

### Scratch rules

Scratch root unique under `C:\wt` and outside product worktree per reviewer/investigator. No `C:\<name>` root or Windows temp path. Allow temporary scripts only in root + non-mutating commands against frozen SHA. Bar product writes, test/build outputs, Git mutation, branches, worktrees, state edits, Unity, `Library`, lease. Unity reproduction -> request orchestrator; requires zero writers + one lease.

Reviewer/investigator return acceptance -> verify exact `frozen_sha` object still resolves; reviewer also verifies bound `review_base_sha..frozen_sha` range. Inspect covered task/path slice at `frozen_sha`; verify role made no product/Git mutation. Do not require globally clean status or current `HEAD == frozen_sha`; unrelated parallel lanes may write/commit. Reserve global clean/current-`HEAD` checks for final barrier.

## Child return contract

Child returns exact template only; unrepresentable fact -> `Blocker`. Writer/reviewer return metadata or format defect -> preserve immutable original report, then use `followup_task` on same child for return-only correction. Correction preserves original status, verdict/findings, changed paths, checks, blockers, and evidence verbatim; no implementation, review, or check rerun. Accept only when corrected identity/scope matches registry; otherwise reject and preserve evidence only. Reject late, interrupted, replaced, duplicate, foreign, out-of-scope, role-mutation-inconsistent result. Reviewer/investigator result remains eligible when unrelated lanes move status/`HEAD` after its frozen range was bound.

Writer:

```markdown
# Worker Result

Status: complete | blocked
Execution ID: [execution_id]
Assigned Agent: [exact agent identity]
Role: implementation worker | fix worker
Profile: [exact profile]
Changed Paths: [exact paths or None]
Unowned Churn: [path count -> line count -> action taken -> recurrence] or None
Checks: [command -> observed result -> evidence path or None]
Findings Fixed: [finding IDs or None]
Blocker: [exact blocker when blocked; otherwise None]
Evidence: [observable evidence or None]
Needed Action or Recheck: [one action/fact or None]
```

Investigator:

```markdown
# Investigator Result

Status: complete | blocked
Execution ID: [execution_id]
Assigned Agent: [exact agent identity]
Role: investigator
Profile: [exact profile]
Decision: fix_found | no_reasonable_fix
Reproduced: [exact command/path -> observed failure]
Hypotheses Checked: [hypothesis -> ruled in | ruled out -> evidence]
Fix Contract: [exact paths/symbols + required change when fix_found; otherwise None]
No-Fix Reason: [reason when no_reasonable_fix; otherwise None]
Blocker: [exact blocker when blocked; otherwise None]
Evidence: [observable evidence or None]
Needed Action or Recheck: [one action/fact or None]
```

## Child lifecycle gate

- registry -> agent ID, `execution_id`, role, `running | correction-pending | returned | retired`
- one dispatch -> one substantive child turn; writer/reviewer return-only correction -> `followup_task` same child; other follow-up -> fresh child
- terminal return required; messages/files/partial reports while running -> progress evidence only
- valid terminal return -> capture immutable report, mark `returned`, retire immediately; eligible return defect -> capture original, mark `correction-pending`, request return-only correction
- replaced/cancelled/unneeded -> interrupt, await terminal, capture late evidence, retire before replacement
- hung -> inspect task-owned edits, interrupt, await terminal, retire, restore writer barrier, then replace
- exit -> `list_agents`; interrupt running descendants; await terminal; recheck. Complete requires zero running + all registry entries retired

Repeated struggle -> [Repeated-struggle takeover](#repeated-struggle-takeover). Quiet reporting: kickoff, material checkpoint/fix/validation/blocker/completion, required heartbeat only.

## Worker -> reviewer barrier

Reviewer dispatch requires every covered writer:

1. Terminal return; child not running.
2. Capture `status: complete`, identity, checks, `Changed Paths:`; compare exact report to `git diff --name-only` for writer slice. Apply [generated output gate](#generated-output-gate); outside owned set otherwise -> reject, barrier open.
3. Mark `returned`, retire; covered registry has zero running.
4. Close writer barrier. Build generated-output coverage evidence from every changed authoritative output, owned or inventory-exception. Regeneration -> separate commit before `review_base_sha`; stage/commit reviewed source paths only; resolve exact `frozen_sha`; attach coverage evidence to reviewer; verify scope + unrelated status. If separation impossible, declare exact builder-generated excluded slice; raw generated slice stays outside raw review, never semantic-evidence review.
5. Dispatch reviewer bound to `review_base_sha..frozen_sha`.

Per-worker checkpoint -> one writer. Grouped checkpoint -> every named writer + join condition. Fix re-review uses same barrier. Timing/group rules -> [Review checkpoints](#review-checkpoints).

## Review checkpoints

Default -> one checkpoint/implementation worker. Worker -> reviewer barrier before dependent work. Grouped checkpoint permitted only when plan names covered writers, join, rationale: producer/consumer contract, coordinated code/serialized wiring, or state impossible to review partially. Throughput never rationale. Missing grouping -> per-worker.

Fan-out ready disjoint siblings. Before the first writer dispatch, create each sibling's immutable contract and bind the complete `bound_concurrent_fanout_peer_owned_paths` map into all of them, including siblings dispatched later. Before any later sibling dispatch, verify every already-running writer that can potentially overlap its paths already has that sibling's exact owned/generated-output path slice in its bound map; if not, wait for those writers or serialize the lane. Per-worker terminal -> [Worker -> reviewer barrier](#worker---reviewer-barrier) immediately; unrelated lanes continue. Grouped waits named returns + join. Cross-lane dependency, overlapping path, shared validation environment -> serialize.

Fix re-review sole repository rule:

- `fix_loc` -> added + deleted text rows from `git diff --numstat <pre_fix_frozen_sha>..<post_fix_frozen_sha>`, source rows only; binary rows and builder-generated outputs zero LOC, stay scope
- builder-generated output -> generated-output classifier result from authoritative inventory + exact task declarations. Regeneration reserialization is semantic review surface, never line count -> [`AGENTS.md`](../../../AGENTS.md) `Unity asset safety`
- `finding_count` -> originating review Critical/High findings before disposition
- `fix_loc > 200` or `finding_count > 3` -> fresh exact `sol_medium` `fix-re-review`; otherwise advance accepted head
- re-review -> same slice, `review_base_sha = pre_fix_frozen_sha`, `frozen_sha = post_fix_frozen_sha`, normal reviewer scope
- re-review finding -> fresh fix writer -> apply gate again

Orchestrator assigns checkpoint-scoped finding IDs.

## Check ledger

Declared check schema -> [`Check contract`](../write-orchestrator-coding-plan/SKILL.md#check-contract). Workflow owns `check-ledger.json`; state records absolute path + SHA-256 only. Execution adds `status`, `executed_sha`, `validated_sha`, `evidence_path`, `evidence_digest`, `subsumed_checks`. One owner binds every production-final row.

Final verification runs pending/invalidated rows only; exact-SHA evidence reusable. Consume workflow `invalidation_paths` from ledger. Merge/fix path intersection invalidates row; lighting-input intersection follows production-bake gate. Workers claim only assigned checks.

## Execution loop

1. Parse graph/tasks/checkpoints; for each ready disjoint fan-out group, bind the complete concurrent/fan-out ownership/generated-output map into every sibling contract before its first writer dispatch. Dispatch a later sibling only after every already-running potentially overlapping writer is already bound to protect that sibling's exact paths; otherwise wait or serialize. Then dispatch every ready disjoint sibling, otherwise next serial writer.
2. Process every terminal writer -> inspect `Unowned Churn`, then verify report/files/Git/scope/checks/identity. Handle recurrence before next writer dispatch; same unowned-churn path set across two dispatches -> investigator directly, not writer replacement. Eligible complete writer -> [Worker -> reviewer barrier](#worker---reviewer-barrier) -> reviewer. Repeated issue -> [Repeated-struggle takeover](#repeated-struggle-takeover).
3. Reviewer result -> verify reviewer return acceptance; accept verdict + qualifying Critical/High only. No accepted finding -> checkpoint accepted. Accepted finding -> fresh narrow fix writer.
4. Fix -> barrier -> scope verify -> commit/freeze -> rerun invalidated rows -> [Review checkpoints](#review-checkpoints) fix re-review gate. Fan-in waits accepted checkpoints.
5. Final exact committed `HEAD`: pending/invalidated checks, plan artifact integrity, ancestry, owned diff plus [generated output gate](#generated-output-gate), clean status, initial unrelated status, branch, dependencies, requirements.

Required unowned non-generated edit, decomposition change, dependency drift, plan artifact mismatch, out-of-plan decision -> `blocked`. LP receives one action in `lp-dispatched`; user receives one action in `user-direct`.

## Completion routing

Return exact template only:

```markdown
# Execution Orchestrator Result

Status: complete | blocked
Mode: lp-dispatched | user-direct
Run ID: [run_id or None]
Plan ID: [plan_id]
Attempt ID: [attempt_id]
Assigned Agent: [exact agent identity]
Role: execution orchestrator
Profile: [exact profile]
Plan Artifact: [exact path] -> accepted [digest]/[bytes] -> observed [digest]/[bytes]
Plan Source Provenance: [exact path in user-direct; otherwise None]
Start SHA: [exact 40-character lowercase SHA]
Dependencies: [full SHA list or None]
Branch: [exact branch]
Worktree: [exact path]
Launch Checkout: [path -> branch -> status snapshot in user-direct; otherwise None]
Initial Unrelated Status: [paths or None]
Final SHA: [clean committed exact SHA when complete; otherwise None]
Changed Path Count: [n]

## Checkpoints
- [checkpoint_id] -> covered [execution IDs] -> [review_cycle_id] [initial | fix-re-review] [review_base_sha]..[frozen_sha] -> [verdict] -> findings [count] -> fix LOC [n] -> re-review [yes | no] -> [dispositions or None]

## Checks
- [command/workflow] -> [working directory] -> [observed result] -> [evidence path] -> [exact SHA]

## Blocker
- blocker: [exact blocker when blocked; otherwise None]
- evidence: [observable evidence or None]
- needed authority action or recheck: [one action/fact or None]
```

`lp-dispatched` -> LP verifies facts, derives final changed-path set/count, dispatches merger. `user-direct` -> user; integration remains explicit authority.

`complete` requires matching plan artifact, committed descendant `HEAD`, owned paths clean, initial unrelated status preserved, owned diff plus generated-output gate, every writer through checkpoint review/fix/re-review, final checks passing, lifecycle gate satisfied.
