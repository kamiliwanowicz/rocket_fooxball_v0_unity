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

Same material issue survives two failed approaches/rechecks by current child -> orchestrator diagnoses repository/evidence, reproduces failure, gives targeted guidance. Guided recheck fails -> interrupt child, wait terminal, retire result, close lane barrier, restore only verified task-owned edits to dispatch snapshot, dispatch fresh role-appropriate child with new `execution_id`.

Current child stays assigned for isolated blocker, rescue, scope correction, confusion, large task/context. Wider recurring issue -> investigator; `fix_found` -> fresh standard writer from its contract; exposed wider in-scope recovery -> fresh exact `sol_high` recovery writer. `no_reasonable_fix`, failed/out-of-bounds recovery -> `blocked`.

## Proof environment

Writer contract carries `read_paths`, `validation_environment`, `unity_mutation`, `expensive_proof_owner`, `expensive_proof_execution`. Enum: `same_dispatch | orchestrator_phase | None`. `same_dispatch` -> named task owner runs proof before return. `orchestrator_phase` -> execution orchestrator runs shared proof only after plan `checks` names checkpoint/final trigger, every declared source producer reaches trigger, generated outputs reach generated-output gate, checkpoint review/fixes accepted. `None` -> no expensive proof. Trigger derives from plan checks plus exact declared outputs; never narrative run-point or workflow path-selection flag. Planner names one production-final owner after source fan-in, review, fixes. Worker checks default fast/local unless task owns development proof. Expensive-proof reduction never relaxes review.

Source-only writer for `unity_mutation: true` -> return Unity compile proof before terminal return. Source/proof split without this proof -> prohibited. Relevant Unity test/import or compile-only Unity batch qualifies; `dotnet build` does not.

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

Classifier inputs -> current authoritative builder inventory + exact task-declared generated outputs in `owns`, each traced to builder source. Normalize union. Classify each changed generated output: source `inventory | declared-new | both`; scope `owned | inventory-exception`. Ownership changes scope decision only; every class needs same evidence. Changed generated-looking path outside union, declaration without builder-source evidence, or inventory-unknown unowned path -> reject/`blocked`.

Coverage evidence -> exact changed-output set, source/scope class per path, comparator-selected path set, comparator output path headers, `SEMANTIC:`, `DANGLING:`, GUID-stability result, asset/`.meta` pairing result. Every changed authoritative generated output must have exact path coverage at writer self-check, checkpoint barrier, reviewer dispatch, final verification. Comparator-supported path -> run `Tools/Validation/Compare-GeneratedYaml.ps1 -Base <writer-slice-base-sha> -Head WORKTREE -FailOnDangling` with exact coverage; require matching output header. Comparator-unsupported path -> record exact path + unsupported reason; `blocked` until supported evidence exists. Never omit, infer coverage from broad glob, or treat raw-diff exclusion/separate regeneration commit as evidence. Comparator failure, incomplete coverage, increased dangling, GUID churn, or broken pairing -> reject/`blocked`.

## Child dispatch contract

Every dispatch gets unique `execution_id`. Dispatch/return text: terse AI-to-AI, exact paths/symbols/commands/SHAs, no narration. Worker receives bounded task only; never full plan dump.

### Writer

Implementation/fix writer contract:

- `execution_id`; identity/profile/role
- bounded task + done condition; objective + exclusions
- exact worktree; files/symbols; owned/protected paths
- product writes: owned paths; builder-generated outputs -> generated output gate
- scope self-check before every expensive proof: `git status --porcelain` -> each changed path inside owned set or [generated output gate](#generated-output-gate). Other unowned path -> revert it or return `blocked`; never spend Unity/workflow proof on out-of-scope tree
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

Child returns exact template only; unrepresentable fact -> `Blocker`. Reject late, interrupted, replaced, duplicate, foreign, out-of-scope, role-mutation-inconsistent result; preserve evidence only. Sole correction: reported writer `Execution ID` mismatch -> correct only when live registry agent ID matches `Assigned Agent` and reported `Changed Paths` exactly match its bound Git writer slice. All other mismatch -> fatal. Reviewer/investigator result remains eligible when unrelated lanes move status/`HEAD` after its frozen range was bound.

Writer:

```markdown
# Worker Result

Status: complete | blocked
Execution ID: [execution_id]
Assigned Agent: [exact agent identity]
Role: implementation worker | fix worker
Profile: [exact profile]
Changed Paths: [exact paths or None]
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

- registry -> agent ID, `execution_id`, role, `running | returned | retired`
- one dispatch -> one child turn; follow-up -> fresh child
- terminal return required; messages/files/partial reports while running -> progress evidence only
- returned -> capture immutable report, mark `returned`, retire immediately
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

Fan-out ready disjoint siblings. Per-worker terminal -> [Worker -> reviewer barrier](#worker---reviewer-barrier) immediately; unrelated lanes continue. Grouped waits named returns + join. Cross-lane dependency, overlapping path, shared validation environment -> serialize.

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

1. Parse graph/tasks/checkpoints; dispatch every ready disjoint sibling, otherwise next serial writer.
2. Process terminal writer -> verify report/files/Git/scope/checks/identity -> [Worker -> reviewer barrier](#worker---reviewer-barrier) -> reviewer. Repeated issue -> [Repeated-struggle takeover](#repeated-struggle-takeover).
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
