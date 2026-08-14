# Orchestration Skills — Context Mismanagement Audit Handoff

Scope: `.agents/skills/loop-orchestrator`, `.agents/skills/orchestrate-implementation`, `.agents/skills/write-orchestrator-coding-plan`.
Axis: context hygiene only (payload size, storage location, duplication, unconsumed fields). No correctness/design review.
Filter: CRITICAL + HIGH only. Findings, no fixes.
Shorthand: LP = loop-orchestrator loop parent. OI = orchestrate-implementation. WP = write-orchestrator-coding-plan.

## CRITICAL

### C1 — orchestrators ingest whole plan artifacts into long-lived context
- OI `SKILL.md:41,45,115,229` + WP `SKILL.md:192` -> orchestrator reads full plan snapshot, binds all task bodies, retypes assigned files/symbols per worker prompt.
  - cost: `plan_size × (1 + worker_count)`
  - full `implementation` blocks resident whole attempt
  - pointer sufficiency proven: OI `agents/code-reviewer.md:17` reads `implementation` block from snapshot by section; workers denied same pointer
- LP `SKILL.md:76-80,84` -> LP reads whole accepted plan into "execution snapshot", re-reads snapshot to regenerate `owned`/`protected`/`read_paths` before every execution dispatch.
  - copy+digest = file op, needs no comprehension
  - LP = longest-lived context in system
  - growth: total plan bytes × attempts

### C2 — cited source bodies enter LP context, then re-derived by subagent hired to keep them out
- LP `SKILL.md:37` vs `agents/task-breakdown.md:11,31`
- LP INIT reads request + repo instructions + cited sources + dirty paths + checks -> breakdown agent inspects same set again
- LP should hold pointers + `run_id` + baseline only

## HIGH

### H1 — passed, never consumed
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
- OI `SKILL.md:263` + LP `agents/merging.md:84` -> full `Changed Paths` enumeration returned upward; parent re-derives from `start_sha..final_sha` and re-verifies Git facts (LP `SKILL.md:98`, `state-and-recovery.md:19,208`)
  - Unity `.meta`/generated churn -> hundreds of rows per return
- WP `SKILL.md:210-213` -> planner forecasts changed paths (union of per-task `owns`); OI computes real set (`SKILL.md:263,235`); stale by construction
- LP `SKILL.md:43` + `state-and-recovery.md:70-77` vs LP `SKILL.md:84` -> breakdown-forecast path sets + proof fields written to state at INIT/BREAKDOWN, then mandatorily regenerated from plan snapshot and reconciled -> stored, rewritten, discarded; path sets can be large glob lists

### H3 — digest obtained by reloading bytes into context
- OI `SKILL.md:41`, repeated `:60,235` -> "reopen snapshot; capture SHA-256 and byte size"
- second full-file load purely for digest+size; `git hash-object` / `Get-FileHash` yield same without content in context

### H4 — state/ledger duplication
- LP `agents/merging.md:24` -> dispatch inlines row owners, tier, run points, input/environment digests, mutation flags, invalidation paths, subsumption, for agent that opens and digest-verifies ledger itself; contradicts `state-and-recovery.md:113` ("state stores only pointer plus digest; LP never copies or rewrites rows") -> whole executed-ledger contents duplicated into prompt
- LP `state-and-recovery.md:88,107` -> per-plan + integration `checks` rows duplicated into state alongside authoritative ledger pointer+sha256; LP rewrites wholesale per dispatch/acceptance; grows with checks × plans
- LP `state-and-recovery.md:23-31` -> whole-document reserialize + reread per dispatch and per accepted result; targeted patch demoted to secondary (`:32`); scales plans × attempts × events even for one-field change
- OI `SKILL.md:100` -> workflow result JSON consumed whole (all command args/exits/logs/elapsed, probe records, bake count, per-asset generated inventory+hashes, changed generated paths, check ledger, manifest digest); decision needs only status + `exactSha` + evidence path + manifest digest; breaks pointer discipline set by same file at `:216`

### H5 — same policy loaded from many places
- OI `SKILL.md:84 / 168 / 230` -> repeated-struggle/takeover policy stated 3x in always-loaded file (~10 lines each)
- OI `SKILL.md:177-189` vs `:231`; `:195-199` vs `:231` -> worker->reviewer barrier sequence + checkpoint rules restated inside execution loop after owning sections define them; contradicts file's own anti-restate rule at `:201`
- LP `state-and-recovery.md:115-119,122-126` + LP `SKILL.md:86` + `agents/merging.md:37,40-41` + `AGENTS.md:69,76` -> workflow harness precondition, exact `Invoke-MovementLabWorkflow.ps1` arg spec, production-final ordering, bake-gate rules restated 4x; lighting-input path list (`merging.md:41`) duplicates AGENTS.md bake-gate scope -> drift risk + repeated token cost
- OI `agents/code-reviewer.md:20` -> every fresh reviewer reloads full 106-line `AGENTS.md` (mostly Unity execution/validation policy irrelevant to read-only diff review) plus inline duplicate of same failure classes; slice (Architecture + Unity asset safety + Code clarity) suffices

### H6 — plan format forces restatement (WP)
- `SKILL.md:158-167` Repository Findings + Decisions -> no consumer; workers get files/symbols only (OI `SKILL.md:115`), so facts must be restated inside per-task `implementation` (`:192`) to reach consumer -> global sections = second copy loaded by orchestrator
- `SKILL.md:177-197` -> `objective` + `slice_boundary` + `done when` + `checks` + `proof` = 4 overlapping natural-language restatements of same bound per task; `proof: [discriminatory evidence]` collides by name and content with `checks: proof: <command> -> <expected>` (`:122`)
- `SKILL.md:183,190` -> `owns` demands transitive generated-output enumeration (large for Unity bake/builder outputs per AGENTS.md:48,76), `proof_invalidation_paths` repeats it; OI `SKILL.md:218` says consume workflow-declared invalidation paths instead of duplicating
- `SKILL.md:215-221` Done Criteria -> planner's own authoring checklist emitted into every artifact; restates `:84-90,112,131,250-255`; consumer orchestrator has own gate (OI `SKILL.md:281`)
- `SKILL.md:228-246` LP result template -> echoes Run ID, Plan ID, Attempt ID, Covered Requirements, Baseline SHA, Dependencies, Profile back to LP; all supplied by LP in dispatch (`:24-35`); novel fields only = Status, Artifact Path, Owned/Protected, Question, Blocker, Evidence, Needed Action
