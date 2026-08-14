# Orchestration Skills — Context Mismanagement Audit Handoff

Scope: `.agents/skills/loop-orchestrator`, `.agents/skills/orchestrate-implementation`, `.agents/skills/write-orchestrator-coding-plan`.
Axis: context hygiene only (payload size, storage location, duplication, unconsumed fields). No correctness/design review.
Filter: CRITICAL + HIGH only.
Shorthand: LP = loop-orchestrator loop parent. OI = orchestrate-implementation. WP = write-orchestrator-coding-plan.

Status 2026-08-14: findings + accepted user decisions. Each finding carries `DECISION` line -> `FIX` or `WONTFIX`. Downstream consumer = plan-writing agent; see `## Implementation brief` at end for constraints, edit matrix, and verification. Read decisions as binding; do not re-litigate `WONTFIX`.

Line numbers valid at `217ab449a496dc90581e0a4b4dc73bd90868af5d`. Edits shift them -> re-locate every site by quoted content/section name, never by line number alone.

## CRITICAL

### C1 — orchestrators ingest whole plan artifacts into long-lived context

DECISION 2026-08-14: WONTFIX. No change to plan-slicing. Every considered remedy either moves pollution from orchestrator context into worker context (pointer + self-read = soft guarantee; worker over-reads whole plan) or requires split-artifact/mechanical-carve machinery judged over-engineered for PoC. Orchestrator keeps reading full snapshot and retyping per-worker slices. Do not re-open without new evidence.

Downstream: H6 items stay in scope on their own merits, not as C1 enablers.

<details>
<summary>original finding (retained, not actioned)</summary>

- OI `SKILL.md:41,45,115,229` + WP `SKILL.md:192` -> orchestrator reads full plan snapshot, binds all task bodies, retypes assigned files/symbols per worker prompt.
  - cost: `plan_size × (1 + worker_count)`
  - full `implementation` blocks resident whole attempt
  - pointer sufficiency proven: OI `agents/code-reviewer.md:17` reads `implementation` block from snapshot by section; workers denied same pointer
- LP `SKILL.md:76-80,84` -> LP reads whole accepted plan into "execution snapshot", re-reads snapshot to regenerate `owned`/`protected`/`read_paths` before every execution dispatch.
  - copy+digest = file op, needs no comprehension
  - LP = longest-lived context in system
  - growth: total plan bytes × attempts

</details>

### C2 — cited source bodies enter LP context, then re-derived by subagent hired to keep them out

DECISION 2026-08-14: WONTFIX. LP must know what it orchestrates. Eager full read at INIT retained deliberately: LP owns blocker resolution, material-scope-change detection, user relay, and final requirement-coverage judgement; structured breakdown REQ list alone is thinner grounding, and its fidelity to cited sources is otherwise unverified by any party. Resident cost accepted. Do not re-open without new evidence.

<details>
<summary>original finding (retained, not actioned)</summary>

- LP `SKILL.md:37` vs `agents/task-breakdown.md:11,31`
- LP INIT reads request + repo instructions + cited sources + dirty paths + checks -> breakdown agent inspects same set again
- LP should hold pointers + `run_id` + baseline only

</details>

## HIGH

### H1 — passed, never consumed

DECISION 2026-08-14: FIX ALL. Split single dispatch envelope into per-role field sets (writer | reviewer | investigator); each carries only fields its role can act on. Delete dead fields listed below.

Added scope (beyond audit):

- LP `SKILL.md:63` planner dispatch omits `design scope` + `produces` + `size check`, while WP `SKILL.md:31,36` marks them required -> missing required field -> planner entitled to `blocked` on field LP never sends. Correctness defect, fix same pass. Resolution -> add `design scope` + `produces` to LP planner dispatch (breakdown-decided obligations planner must settle); `size check` dropped with WP `size` field.
- Reviewer + investigator gain bounded scratch-write capability. Read-only-everything is wrong: investigator return already requires `Reproduced: [exact command/path -> observed failure]` (OI `SKILL.md:154`) while OI `SKILL.md:85,117` bars it from executing anything; reviewer barred from tests (`code-reviewer.md:11`) yet must judge compile hazards statically.
  - allow -> create/execute temp scripts inside unique dispatch-bound scratch root outside product worktree; run non-project-mutating commands; read product worktree at frozen SHA
  - bar -> product-path writes, Git mutation (stage/commit/branch/worktree), LP/OI state edits, Unity project mutation (no Editor launch, no `Library/` write, no lease)
  - invariant -> product worktree stays clean and at frozen SHA across review/investigation; orchestrator verifies clean status after return
  - reviewer still never edits product or proposes fixes as edits; findings-only output discipline unchanged
  - Unity-requiring reproduction -> request through orchestrator (needs zero writers + one lease); never self-granted

- OI `SKILL.md:115` -> single ~20-field dispatch envelope to every child role.
  - reviewer/investigator read-only (`code-reviewer.md:11,23`) -> proof/validation/unity/ledger fields unusable
  - writers hold `Allowed Git operations: None` -> unrelated-status exclusions + SHAs unusable
  - no return template (OI `SKILL.md:127-161`, `code-reviewer.md:45-69`) consumes any of them
- OI `SKILL.md:115` -> snapshot identity/path/digest to implementation/fix workers; same line forbids full plan dump, orchestrator is sole plan authority, no digest field in worker return
- WP `SKILL.md:180` `size` + `:94-104` bucket section -> OI never references; skill self-declares "nothing audits it" (`:102`); split decision already carried by `slice_boundary` + Execution Graph
- WP `SKILL.md:191` `focused_reads` + per-path reason prose -> OI consumes only `read_paths` (OI `SKILL.md:115,92`)
- WP `SKILL.md:198` `return_evidence` -> worker return format fixed by OI `SKILL.md:127-141`; workers never see plan
- LP `agents/task-breakdown.md:56-58,79-80,84` -> returns Evidence bullets, size check, split rationale, done condition, design scope; no acceptance criterion (LP `SKILL.md:49`), no state field (`references/state-and-recovery.md:54-61`), no dispatch field (LP `SKILL.md:63`) reads them
- WP `SKILL.md:148-149,184,239-241` -> baseline + dependency SHAs stored 4x and verified (`:220,253`), while OI `SKILL.md:55` states plan-declared revision metadata never gates execution; HEAD authoritative

### H2 — re-derived anyway -> transmission is duplicate

DECISION 2026-08-14: FIX, with one carve-out. `Changed Paths` is not purely redundant -> doubles as claim-vs-Git cross-check. Resolution by level:

- worker return (OI `SKILL.md:135`) -> KEEP verbatim. Real consumer at OI `SKILL.md:182` (reported paths vs `git diff --name-only`; path outside owned set -> reject report). Slice-sized.
- orchestrator return (OI `SKILL.md:263`) + merging return (`agents/merging.md:84`) -> REPLACE enumeration with `Changed Path Count: [n]`. LP compares one integer against own `git diff` count; per-worker cross-check already ran one level down. Kills Unity `.meta`/generated churn rows.
- WP `SKILL.md:210-213` `## Handoff` -> DELETE forecast `changed paths` line. KEEP `residual risks` (LP `SKILL.md:140` requires it; no other source) + `authority`.
- LP `SKILL.md:43` + `state-and-recovery.md:70-77` -> STOP persisting breakdown-forecast path/proof sets to state. Forecast becomes planner-dispatch input only; state records path authority once, at planning acceptance. LP `SKILL.md:84` snapshot regeneration unchanged (C1 WONTFIX).

- OI `SKILL.md:263` + LP `agents/merging.md:84` -> full `Changed Paths` enumeration returned upward; parent re-derives from `start_sha..final_sha` and re-verifies Git facts (LP `SKILL.md:98`, `state-and-recovery.md:19,208`)
  - Unity `.meta`/generated churn -> hundreds of rows per return
- WP `SKILL.md:210-213` -> planner forecasts changed paths (union of per-task `owns`); OI computes real set (`SKILL.md:263,235`); stale by construction
- LP `SKILL.md:43` + `state-and-recovery.md:70-77` vs LP `SKILL.md:84` -> breakdown-forecast path sets + proof fields written to state at INIT/BREAKDOWN, then mandatorily regenerated from plan snapshot and reconciled -> stored, rewritten, discarded; path sets can be large glob lists

### H3 — digest obtained by reloading bytes into context

DECISION 2026-08-14: FIX ALL. Unaffected by C1 WONTFIX -> orchestrator's one comprehension read stays; integrity re-reads are separate and pure waste.

- OI `SKILL.md:41`, repeated `:60,235` -> "reopen snapshot; capture SHA-256 and byte size"
- LP-side same pattern -> LP `SKILL.md:67,80`; `state-and-recovery.md:177`
- second/third full-file load purely for digest+size

Resolution -> pin exact commands at all five sites; snapshot creation + digest verification are filesystem ops; plan bytes never enter agent context for either:

- digest -> `(Get-FileHash -Algorithm SHA256 -Path $p).Hash.ToLowerInvariant()`
- size -> `(Get-Item $p).Length`
- snapshot creation -> `Copy-Item` into create-once path; never read-then-write

Gotchas to encode:

- `Get-FileHash` emits UPPERCASE; every template specifies `[lowercase digest]` -> case-skew comparison -> spurious digest mismatch -> false `blocked`. `.ToLowerInvariant()` mandatory.
- one command both sides; producer/consumer hash skew -> disagreement on identical bytes.
- `git hash-object` rejected here despite AGENTS.md:77 preference -> emits SHA-1; templates require SHA-256. Snapshot is untracked file in Git common dir -> raw-byte `Get-FileHash` correct for both sides.

### H4 — state/ledger duplication

DECISION 2026-08-14: FIX ALL. Resolution per item:

- `agents/merging.md:24` -> DELETE inlined row fields; dispatch carries ledger path + SHA-256 only. Direct contradiction of `state-and-recovery.md:113`; merging agent already digest-verifies and opens ledger itself.
- `state-and-recovery.md:88,107` -> state `checks` fields hold pending/blocked check IDs only (LP routing need). Results + evidence paths + SHAs stay in ledger behind existing pointer+digest fields.
- `state-and-recovery.md:23-31` -> targeted field patch becomes DEFAULT; full-document rebuild only for structural change (new plan section, phase transition). Atomic-replace mechanism unchanged — it prevents torn state. Real defect = "build complete next bytes" performed in agent context instead of as scripted file op. Retain `:32` safety rule (section + field key match exactly once; zero or multiple -> stop). Post-write verification reopen stays as today — no scoped/partial read instructions.
- OI `SKILL.md:100` -> define decision subset read on success: `status`, `exactSha`, evidence path, evidence-manifest SHA-256, `bakeCount`, lock-release proof. Extract named fields; never read whole JSON. Full JSON read only on failure, where `AGENTS.md:68` requires exit code + logs + workflow JSON for classification.

Do not drop when trimming workflow JSON:

- `bakeCount` -> production-bake budget (`state-and-recovery.md:124`) sums it; `>=2` -> `blocked`
- lock-release proof -> `AGENTS.md:67` requires confirmed lock release

- LP `agents/merging.md:24` -> dispatch inlines row owners, tier, run points, input/environment digests, mutation flags, invalidation paths, subsumption, for agent that opens and digest-verifies ledger itself; contradicts `state-and-recovery.md:113` ("state stores only pointer plus digest; LP never copies or rewrites rows") -> whole executed-ledger contents duplicated into prompt
- LP `state-and-recovery.md:88,107` -> per-plan + integration `checks` rows duplicated into state alongside authoritative ledger pointer+sha256; LP rewrites wholesale per dispatch/acceptance; grows with checks × plans
- LP `state-and-recovery.md:23-31` -> whole-document reserialize + reread per dispatch and per accepted result; targeted patch demoted to secondary (`:32`); scales plans × attempts × events even for one-field change
- OI `SKILL.md:100` -> workflow result JSON consumed whole (all command args/exits/logs/elapsed, probe records, bake count, per-asset generated inventory+hashes, changed generated paths, check ledger, manifest digest); decision needs only status + `exactSha` + evidence path + manifest digest; breaks pointer discipline set by same file at `:216`

### H5 — same policy loaded from many places

DECISION 2026-08-14: FIX by deduplication only. Constraint: NO scoped/partial-file read instructions anywhere in this pass — telling an agent "read only sections X,Y of file Z" is unreliable in practice. Dedupe text; keep reads whole. Keep fixes structurally simple; no new agent logic.

- OI `SKILL.md:84,168,230` -> one owning statement for repeated-struggle/takeover; `:168` + `:230` become links.
- OI `SKILL.md:231` -> link `## Worker -> reviewer barrier` + `## Review checkpoints` instead of restating; honours file's own anti-restate rule at `:201`.
- workflow invocation policy 4x (`state-and-recovery.md:115-119,122-126`; LP `SKILL.md:86`; `agents/merging.md:37,40-41`; `AGENTS.md:69,76`) -> single owning section in `state-and-recovery.md` holding harness pre-gate + exact `Invoke-MovementLabWorkflow.ps1` arg spec + production-final ordering; LP `SKILL.md:86`, `agents/merging.md:37`, OI `SKILL.md:102` link to it. Bake gate links `AGENTS.md:76` for marker/skip semantics instead of restating.
- `agents/merging.md:41` hardcoded lighting-input path list -> DELETE. Correctness risk, not just cost: list drifts from `AGENTS.md` bake scope -> wrong skip/rebuild decision. OI `SKILL.md:218` already mandates consuming workflow-declared invalidation paths.
- `agents/code-reviewer.md:20` -> reviewer keeps reading `AGENTS.md` WHOLE (no slice instruction). DELETE inline failure-class enumeration -> redundant once full file is read.
  - audit's proposed slice (`Architecture` + `Unity asset safety` + `Code clarity`) rejected on coverage grounds too: inline list's "validator scope vs owner scope" -> `AGENTS.md:94` (`Validation`); "raw-worktree-byte digests" -> `AGENTS.md:77` (`Unity execution`). Slice would silently drop two review checks.
- OI `SKILL.md:84 / 168 / 230` -> repeated-struggle/takeover policy stated 3x in always-loaded file (~10 lines each)
- OI `SKILL.md:177-189` vs `:231`; `:195-199` vs `:231` -> worker->reviewer barrier sequence + checkpoint rules restated inside execution loop after owning sections define them; contradicts file's own anti-restate rule at `:201`
- LP `state-and-recovery.md:115-119,122-126` + LP `SKILL.md:86` + `agents/merging.md:37,40-41` + `AGENTS.md:69,76` -> workflow harness precondition, exact `Invoke-MovementLabWorkflow.ps1` arg spec, production-final ordering, bake-gate rules restated 4x; lighting-input path list (`merging.md:41`) duplicates AGENTS.md bake-gate scope -> drift risk + repeated token cost
- OI `agents/code-reviewer.md:20` -> every fresh reviewer reloads full 106-line `AGENTS.md` (mostly Unity execution/validation policy irrelevant to read-only diff review) plus inline duplicate of same failure classes; slice (Architecture + Unity asset safety + Code clarity) suffices

### H6 — plan format forces restatement (WP)

DECISION 2026-08-14: FIX ALL. C1 WONTFIX sharpens this — workers never open plan, so top-level fact sections reach no actor; duplication must be cut here, not resolved by giving workers plan access.

- `:158-167` -> DELETE `## Repository Findings`. Collapse `## Decisions` to `assumption` + `decision` + `question` only. Rationale: `observed`/`gap`/`constraint`/`proposed` bullets must be restated in task `implementation` to reach worker; retained trio has non-duplicate readers (user; LP `needs_user` routing + material-scope-change detection).
- `:177-197` -> collapse per-task boundary fields to `objective` (absorbs slice bound) + `done when` + `checks`. DELETE `slice_boundary`; update `:112` which orders planner to record it.
- `:196` -> DELETE task-level `proof:` field. Name+content collision with `proof:` keyword inside check line format (`:122` `proof: <command> -> <expected>`). Discriminatory evidence belongs in check's `-> <expected>` half.
- `:183` -> `owns` uses directory globs (e.g. `Assets/_Game/Lighting/**`), not transitive generated-output enumeration. Scope check unaffected -> `git diff --name-only` matches globs.
- `:190` -> DELETE `proof_invalidation_paths`. OI `SKILL.md:218` already mandates consuming workflow-declared invalidation paths.
- `:215-221` -> DELETE `## Done Criteria` from output template. Skill-side `## Final check` (`:248-254`) already owns planner checklist; consumer gate is OI `SKILL.md:281`.
- `SKILL.md:158-167` Repository Findings + Decisions -> no consumer; workers get files/symbols only (OI `SKILL.md:115`), so facts must be restated inside per-task `implementation` (`:192`) to reach consumer -> global sections = second copy loaded by orchestrator
- `SKILL.md:177-197` -> `objective` + `slice_boundary` + `done when` + `checks` + `proof` = 4 overlapping natural-language restatements of same bound per task; `proof: [discriminatory evidence]` collides by name and content with `checks: proof: <command> -> <expected>` (`:122`)
- `SKILL.md:183,190` -> `owns` demands transitive generated-output enumeration (large for Unity bake/builder outputs per AGENTS.md:48,76), `proof_invalidation_paths` repeats it; OI `SKILL.md:218` says consume workflow-declared invalidation paths instead of duplicating
- `SKILL.md:215-221` Done Criteria -> planner's own authoring checklist emitted into every artifact; restates `:84-90,112,131,250-255`; consumer orchestrator has own gate (OI `SKILL.md:281`)

## Implementation brief

Consumer: plan-writing agent. All edits = Markdown contract files. No product code, no Unity, no builder run, no `Tools/**` change.

### Files in scope

- `.agents/skills/loop-orchestrator/SKILL.md`
- `.agents/skills/loop-orchestrator/references/state-and-recovery.md`
- `.agents/skills/loop-orchestrator/agents/task-breakdown.md`
- `.agents/skills/loop-orchestrator/agents/merging.md`
- `.agents/skills/orchestrate-implementation/SKILL.md`
- `.agents/skills/orchestrate-implementation/agents/code-reviewer.md`
- `.agents/skills/write-orchestrator-coding-plan/SKILL.md`

No edits: `*/agents/openai.yaml` x3 -> interface metadata only, zero field references. `AGENTS.md` -> repo policy stays sole owner of Unity rules; skills link, never restate. `.agents/skills/use-blender/`.

### Global constraints

- style -> `/llm-oriented-markdowns` for every edited file; caveman terse, arrow relations, no tables, no mermaid
- NO scoped/partial-file read instructions anywhere ("read only section X of file Y" unreliable in practice) -> dedupe text, keep reads whole
- keep simple -> no new agent logic, no new artifact types, no new machinery; PoC posture per `AGENTS.md` `Delivery posture`
- deletion-biased -> prefer removing field/section over adding qualifier; expect deletions > insertions overall
- single owner per policy -> one statement + links elsewhere; never two copies
- no back-compat burden -> newest run `3v3-phase1-20260811T212524642Z-ff53e4a6` is `READY_FOR_USER_MERGE`, merge already accepted; zero in-flight runs; state/plan/template formats may change freely
- preserve fenced strict-result templates as fences; agents parse them literally
- producer + consumer of any changed field must land in same change -> no half-applied field rename/deletion

### Edit matrix — deletions

Each entry -> field/section, then EVERY site that must change together.

- WP `size` task field `:180` -> WP `### Size buckets` `:94-104` (keep ~3 split-anchor lines inside `Plan shape`, delete rest); WP `Done Criteria` size line (section deleted anyway); WP required dispatch field `size check` `:31`; breakdown `size check` `:79`; breakdown status rule "each size check passes" `:98`
- WP `focused_reads` `:191` -> fold per-path reason into `read_paths` `:185`; no other site
- WP `return_evidence` `:198` -> delete only
- WP `slice_boundary` `:178` -> fold into `objective` `:177`; update WP `:112` ("Record slice boundary in template")
- WP task-level `proof` `:196` -> delete; discriminatory evidence moves into check `-> <expected>` half (`:122`)
- WP task `depends_on` `:184` -> delete; intra-plan order owned by Execution Graph, cross-plan by plan header `Dependencies`
- `proof_invalidation_paths` -> WP `:190`; breakdown `:77`; state shape `:77`; LP `SKILL.md:43`; OI `SKILL.md:92` candidate contract; OI `SKILL.md:115` envelope. OI `SKILL.md:218` (consume workflow-declared) STAYS
- breakdown forecast fields `:72,74,75,76` -> delete `read_paths`, `unity_mutation`, `expensive_proof_owner`, `expensive_proof_run_point`; fold `validation_environment` `:73` into existing `validation boundary` `:78`. KEEP objective, covered requirements, design scope, depends on, produces, wave, baseline rule, owned paths, protected paths, validation boundary
- breakdown `## Evidence` `:56-58` -> delete; per-REQ evidence pointer already in `## Requirements` `:54`; LP read sources itself (C2 WONTFIX)
- breakdown `done condition` `:64` -> fold into `objective` `:63`
- breakdown `split rationale` `:84` -> delete; LP acceptance `SKILL.md:49` is structural only
- state shape `:72-77` -> delete `read_paths`, `validation_environment`, `unity_mutation`, `expensive_proof_owner`, `expensive_proof_run_point`, `proof_invalidation_paths`. Consequence -> LP `SKILL.md:84` regenerates/reconciles owned+protected ONLY (drop `read_paths` from that set); LP `SKILL.md:43` drops field-binding list, KEEPS semantic rules (one production-final owner; multi-worker candidate requires disjoint paths + stable validation environments)
- state `checks` `:89,107` -> pending/blocked check IDs only; results/evidence/SHAs stay in ledger behind existing pointer+digest fields
- WP `## Handoff` changed-paths line `:211` -> delete; KEEP `residual risks` (LP `SKILL.md:140` sole source) + `authority`
- WP `## Repository Findings` `:158-163` -> delete; `## Decisions` `:164-167` keeps `assumption` + `decision` + `question`
- WP `## Done Criteria` `:215-221` -> delete from output template
- WP LP result template `:228-246` -> delete `Profile`, `Baseline SHA`, `Covered Requirements`, `Dependencies`; KEEP Status, Run/Plan/Attempt ID, Assigned Agent, Artifact Path, Owned Paths, Protected Paths, Question, Blocker, Evidence, Needed LP Action. Consequence -> WP `:42` `ready` prose lists returned fields; update to match
- OI `Changed Paths` `:263` + merging `:84` -> replace with `Changed Path Count: [n]`. Worker template OI `:135` UNCHANGED (real consumer at OI `:182`)
- merging `:24` inlined ledger row fields -> ledger path + SHA-256 only
- merging `:41` hardcoded lighting-input path list -> delete
- code-reviewer `:20` inline failure-class enumeration -> delete; whole-file `AGENTS.md` read + review-script step 4 pointer STAY

### Edit matrix — additions and rewrites

- LP `SKILL.md:63` planner dispatch += `design scope`, `produces` -> resolves contradiction with WP `:31,36` (planner may currently `blocked` on required field LP never sends)
- OI `SKILL.md:115` -> replace single envelope with per-role field sets: writer | reviewer | investigator; each carries only fields its role can act on; drop snapshot path/digest from writer sets (workers never open plan; C1 WONTFIX)
- reviewer + investigator scratch-write capability -> OI `SKILL.md:85,117`; `code-reviewer.md:11`; investigator return `Reproduced` `:154`; writer-only clauses OI `SKILL.md:79`. Bounds per H1 note
- H3 command pins -> OI `SKILL.md:41,60,235`; LP `SKILL.md:67,80`; `state-and-recovery.md:177`
- H4 targeted-patch default -> `state-and-recovery.md:23-32`
- H4 workflow-JSON decision subset -> OI `SKILL.md:100`
- H5 repeated-struggle single owner -> OI `SKILL.md:84` owns; `:168`, `:230` link
- H5 barrier/checkpoint restatement -> OI `SKILL.md:231` links `## Worker -> reviewer barrier` + `## Review checkpoints`
- H5 workflow invocation single owner -> EXTEND existing `state-and-recovery.md` `## Workflow Harness Precondition` to own pre-gate + exact `Invoke-MovementLabWorkflow.ps1` arg spec + production-final ordering. Keep heading unchanged -> anchor `#workflow-harness-precondition` survives, zero linker churn. Then LP `SKILL.md:86`, merging `:37`, OI `SKILL.md:102` link instead of restating

### Link and anchor integrity

Never rename a linked heading; heading change -> update every linker in same change.

- OI -> `state-and-recovery.md#production-bake-gate` (`:85,104`); `write-orchestrator-coding-plan/SKILL.md#check-contract` (`:216`); `agents/code-reviewer.md` (`:81,123,193,232`); `AGENTS.md` (`:94,102`)
- LP -> `state-and-recovery.md#workflow-harness-precondition` (`:86`); `#production-bake-gate` (`:86,100`); `#target-drift-recovery` (`:102`); `orchestrate-implementation/SKILL.md#lp-handoff-contract` (`:80`); `agents/task-breakdown.md`; `agents/merging.md`; `write-orchestrator-coding-plan/SKILL.md`
- merging -> `../references/state-and-recovery.md#workflow-harness-precondition` (`:37`); `#production-bake-gate` (`:41`); `#target-drift-recovery` (`:57`); `../../orchestrate-implementation/SKILL.md#review-checkpoints` (`:44`)
- WP -> `orchestrate-implementation/SKILL.md#review-checkpoints` (`:90`); `use-blender/SKILL.md` (`:126`); `AGENTS.md` (`:16,122,127`)
- code-reviewer -> `../../../../AGENTS.md` (`:20`)

### Shape

Cross-file coupling is heavy (one deleted field touches up to 6 sites across 4 files). Prefer ONE worker owning whole change in one pass, then independent review. Do not fan out per-finding — parallel lanes would split producer/consumer of same field across writers.

### Verification

Documentation-only -> no Unity launch (`AGENTS.md` `Validation`).

- inspect `git diff`
- every Markdown link + anchor target resolves
- grep each deleted field name across `.agents/skills/**` -> expect zero orphan references
- every strict-result template still fenced; producer template fields match consumer expectations
- net-subtractive overall (deletions > insertions)
- zero `AGENTS.md` edits; zero `Tools/**` edits
- `SKILL.md:228-246` LP result template -> echoes Run ID, Plan ID, Attempt ID, Covered Requirements, Baseline SHA, Dependencies, Profile back to LP; all supplied by LP in dispatch (`:24-35`); novel fields only = Status, Artifact Path, Owned/Protected, Question, Blocker, Evidence, Needed Action
