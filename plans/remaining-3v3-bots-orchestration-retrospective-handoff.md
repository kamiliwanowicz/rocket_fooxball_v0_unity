# Remaining 3v3 Bots Orchestration Retrospective Handoff

Status: decisions adopted 2026-08-19; sections below carry `Decision:` entries superseding prior `Investigate:` lists
Audience: agents reviewing orchestration and planning skills
Run: `remaining-3v3-bots` / `20260816-0710c140`
Outcome: complete, validated, fast-forward merged into `bots`

## Bound Artifacts

- plan source: `plans/remaining-3v3-bots-coding-plan.md`
- pinned plan: `.git/orchestrate-implementation/remaining-3v3-bots/executions/20260816-0710c140.md`
- run evidence: `.git/orchestrate-implementation/remaining-3v3-bots/evidence/20260816-0710c140/`
- orchestration skill: `.agents/skills/orchestrate-implementation/SKILL.md`
- planning skill: `.agents/skills/write-orchestrator-coding-plan/SKILL.md`
- start SHA: `b1cd22fc0f4f1d310eced7e0d438d5c268072753`
- final SHA: `490ad49ddbacb53d7ee6ae6ece3ece79a6d54e52`
- product baseline ancestor: `c275c092ff0a042241034a062fe5a4beb389a4a4`
- execution worktree: `C:\wt\r3v3-0710c140`

## Result Facts

- active run window: about 3h41m, pinned execution creation `16:37:40` -> final workflow evidence `20:18:31`
- final diff: 71 paths, 18,357 insertions, 5,633 deletions
- commits: 25 -> 13 `feat`, 10 `fix`, 2 `chore`
- final EditMode: 114 passed, 0 failed, 0 skipped
- final generated comparator: 0/0 changed, semantic identical, dangling 0, GUID churn 0, broken pairs 0, unsupported 0
- final ProductionValidate: complete at exact final SHA
- merge: clean fast-forward; validated SHA equals merged SHA

## Confirmed Strengths

- isolated branch/worktree preserved launch checkout
- exact-SHA validation maintained after every source invalidation
- generated-output classifier/comparator enforced; no GUID or asset-pair regressions
- fresh workers handled review findings
- reviewers reported only Critical/High findings per repository rule
- failed gates never waived
- early disjoint tasks used parallel fan-out
- T9 per-slice reviews caught real defects; combined integration review caught cross-slice defects

## Adopted Decisions 2026-08-19 (scope)

Review verdict: Unity safety gates earned, keep. Orchestration ceremony partly self-inflicted cost. Skill set stays; ceremony shrinks.

- keep all skills: `loop-orchestrator`, `task-breakdown`, `write-orchestrator-coding-plan`, `orchestrate-implementation`, merging agent. No skill deletion, no single-skill consolidation.
- cut plan-artifact SHA-256 + byte-size ceremony: no repeated rehash/rebind at artifact gate, execution dispatch, final return, resume. Create-once path + Git commit provide integrity. Remove digest/size fields from state shape, handoff contracts, acceptance steps.
- return templates: keep as requested output shape. Deviation from template never blocks, never triggers correction dispatch. Delete return-only correction protocol (`orchestrate-implementation` child return contract); orchestrator reads report as-is, extracts facts.
- drop child lifecycle registry (`running | correction-pending | returned | retired` bookkeeping). Replacement rule: dependent dispatch waits for terminal child return; hung child -> interrupt, await terminal, replace. No retirement state, no retirement turns.
- generated-output rule dedup: one canonical statement in `AGENTS.md` `Unity asset safety`. `loop-orchestrator` builder-generated output evidence section, `orchestrate-implementation` generated output gate, `code-reviewer.md` step 3 -> link to canonical rule, keep only role-specific command/scope line. Four divergent restatements -> drift risk.

## Primary Issue: Planning Decomposition

Original T9 assigned full `BotController` integration to one worker and one checkpoint. Final `BotController.cs`: 2,082 lines. T9 mixed:

- lifecycle + sensing schedule
- reacted snapshot ownership
- target candidate generation
- reachability probing + selection
- navigation steering
- aim + launch-pose probing
- combat evaluation + consumer translation

User required mid-run split:

- slice 1: lifecycle/sensing -> review -> cadence fix
- slice 2: target/navigation -> review -> opponent-probing fix
- slice 3: combat actions -> review -> pass
- combined CP9 integration review -> pose/prediction fix

Conclusion: reviewer rounds not root waste. Missing pre-run task decomposition caused exception. Runtime-created slices became separate implementation units; each needed review. Future plan must split T9-equivalent work before execution.

Other oversized candidates:

- T6: about 1,921 inserted lines across graph, A*, navigator, tests
- T7: about 1,531 inserted lines across perception, pickup memory, role coordinator, tests
- T3: about 1,098 inserted lines across role + target rules/tests
- T11: 31 changed paths; implementation contract over 10k characters

Decision 2026-08-19: adopt as soft tripwire in planner prose, not new gate/schema. Rule (~4 lines in `write-orchestrator-coding-plan` plan shape): task expected `>600` changed LOC, or `>3` named behaviors, or implementation contract `>4k` chars -> split or record explicit rationale. Keep: one owned file != one worker when file joins independent behaviors; split before plan acceptance. Drop: formal lint machinery, final-file-size metrics.

## Primary Issue: Validation Sequencing

Plan forced:

`T12 ProductionPrepare -> generated commit/review -> EditMode tests -> ProductionValidate`

Plan also prohibited earlier compile. Result:

1. first ProductionPrepare launch -> Unity compile failure
2. compile fix + review
3. ProductionPrepare r1 -> success, 3 generated paths changed
4. CP12 r1 -> pass
5. EditMode -> 3 deterministic aim/intercept failures
6. math fix -> review found High normalization regression
7. fresh math fix -> fresh review pass
8. ProductionPrepare r2 -> success, 0 generated paths changed
9. empty generated-boundary commit + CP12 r2
10. final EditMode + ProductionValidate -> pass

Transient compile errors:

- missing `SetLayerMask`
- `Transform` passed where `GameObject` required in `MovementLabBotPipeline.cs`

Required future order:

`pure source fan-in -> targeted EditMode rules tests -> Editor compile-only gate -> ProductionPrepare -> generated review -> final full EditMode -> ProductionValidate`

ProductionPrepare must not become first compiler/test runner. Cheap source proof before generated boundary prevents generated-review invalidation.

Decision 2026-08-19: adopt order above as written. ~2-line rule change in planning skill validation authoring; replaces early-compile prohibition. Highest wall-clock ROI; implement first.

## Generated No-Op Boundary

ProductionPrepare r2 reported zero changed paths. Orchestration still created empty commit `490ad49` named `chore: regenerate MovementLab outputs` because plan required separate generated commit.

Decision 2026-08-19: simplest form only. Comparator selects 0 paths -> no commit; `sourceFreezeSha` is final generated boundary; CP12 review evidence-only. No attestation mode. ~2 lines.

## Evidence and Context Gaps

Durable run evidence: 28 files, about 5.61 MiB. Missing:

- child dispatch contracts
- prompt hashes/sizes
- child start/end timestamps
- immutable worker results
- reviewer reports + finding IDs
- finding -> fix -> re-review links
- lifecycle registry transitions
- per-attempt failed EditMode XML/log

Final EditMode rerun overwrote earlier failed XML/log. Three failures remain only in conversation summary. Prompt scope and per-agent wall time cannot be audited.

Decision 2026-08-19: drop ledger proposal — prompt hashes, timestamps, result hashes, fix-link graph, completion metrics = telemetry for audience of one; also reintroduces lifecycle bookkeeping dropped below. Keep one piece: attempt-specific evidence filenames; never overwrite failed proof (~1 line). Prompt-scope/wall-time audit accepted as non-goal.

## Lifecycle Inefficiency

Stale `cp5_review` entry required follow-up turn returning `Status: retired`. Registry bookkeeping triggered unnecessary model work.

Decision 2026-08-19: delete registry, not improve it. Retirement text exists only because skill invented registry tools do not back. Registry entry held only agent ID, `execution_id`, role, state `running | correction-pending | returned | retired`; all facts observable live from agent tools + Git.

Delete: four-state machine; explicit `returned`/`retired` marking; `correction-pending` state (dies with correction protocol); completion condition `all registry entries retired`; reviewer-acceptance cross-check against registry.

Keep as plain rules, no registry backing:

- dependent dispatch waits for terminal child return
- hung child -> interrupt, await terminal, replace
- replaced/cancelled child -> interrupt, await terminal before dispatching replacement (prevents two writers on same paths)
- completion requires zero running children, checked live via agent tools

Zero bookkeeping turns.

## Baseline Ambiguity

Pinned plan says worktree starts exactly at `c275c092...`; actual start SHA `b1cd22fc...`. Delta contains only planning skill + accepted plan. Product risk: none. Contract ambiguity: real.

Decision 2026-08-19: drop 3-field schema. Product risk was none; 3 fields per plan describe docs-only delta. Replacement one-liner in planning skill: `Baseline = execution start_sha; planning/docs paths may differ from product ancestor`.

## Explicit Non-Goal: Review LOC Rule

Do not change existing fix re-review LOC/finding thresholds.

Reason:

- T9 review pattern was exception caused by bad original task sizing
- user requested three runtime slices and one reviewer per slice after orchestrator initially intended one review
- correct fix: split tasks before run; each planned task gets normal checkpoint review
- no evidence requiring global LOC-rule change

Preserve current rule in `.agents/skills/orchestrate-implementation/SKILL.md`:

- `fix_loc > 200` or `finding_count > 3` -> fresh fix re-review
- otherwise advance accepted head

## Implementation Priority (decided 2026-08-19)

1. validation ladder reorder + no-op generated boundary (~4 lines total, immediate wall-clock payback)
2. ceremony removal: plan-artifact digest/size ceremony, child lifecycle registry, return-template correction protocol
3. generated-output rule dedup to `AGENTS.md`
4. task-size tripwire in planner + attempt-specific evidence filenames

## Acceptance for Follow-Up Skill Work

- planner splits or explicitly justifies T9-sized single-worker task via size tripwire
- runtime decomposition rule unnecessary for equivalent future plan because slices predeclared
- targeted tests + compile complete before ProductionPrepare
- zero-change generation creates no commit; `sourceFreezeSha` serves as boundary
- failed and successful proof attempts both retained via attempt-specific filenames
- no plan-artifact rehash/rebind steps remain in skills or state shape
- no child registry states or retirement turns remain; template deviation never blocks or dispatches correction
- generated-output rule stated once in `AGENTS.md`; skills link only
- existing LOC/finding re-review thresholds unchanged
