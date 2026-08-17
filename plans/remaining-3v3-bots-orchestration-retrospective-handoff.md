# Remaining 3v3 Bots Orchestration Retrospective Handoff

Status: investigation handoff
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

Planning skill investigation:

- add task-size lint based on behavioral concerns, owned source files, expected changed LOC, final-file size, implementation-contract size
- one owned file != one worker when file joins several independent behaviors
- preserve one state owner while extracting pure helpers where useful
- require explicit grouping rationale when task crosses multiple concerns
- split before plan acceptance; avoid runtime graph mutation

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

## Generated No-Op Boundary

ProductionPrepare r2 reported zero changed paths. Orchestration still created empty commit `490ad49` named `chore: regenerate MovementLab outputs` because plan required separate generated commit.

Investigate:

- add no-op generation attestation mode
- allow `sourceFreezeSha` as final generated boundary when comparator selects 0 paths
- bind workflow/comparator/transition evidence without empty commit
- use evidence-only CP12 review for no-op generation

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

Orchestration skill investigation:

- persist append-only run ledger under run evidence root
- record execution ID, agent/profile/role, prompt hash + bytes, dispatch/return timestamps, frozen SHAs, result hash, changed paths, checks, findings, fix links, lifecycle state
- use attempt-specific evidence filenames; never overwrite failed proof
- emit completion metrics: critical path, agent wait time, retry count, invalidated checks, review finding rate

## Lifecycle Inefficiency

Stale `cp5_review` entry required follow-up turn returning `Status: retired`. Registry bookkeeping triggered unnecessary model work.

Investigate:

- terminal child return -> local/persisted registry state `retired`
- no follow-up model turn solely for retirement text
- align skill lifecycle language with available collaboration tools

## Baseline Ambiguity

Pinned plan says worktree starts exactly at `c275c092...`; actual start SHA `b1cd22fc...`. Delta contains only planning skill + accepted plan. Product risk: none. Contract ambiguity: real.

Planning schema improvement:

- `product_baseline_sha`: product dependency ancestor
- `launch_head_sha`: actual execution start containing accepted plan
- `allowed_bootstrap_delta`: exact planning/docs paths between both SHAs

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

## Investigation Priority

1. planning task-size/decomposition gate
2. source-green-before-generation validation ladder
3. durable orchestration ledger + immutable failed evidence
4. no-op generated-boundary semantics
5. tool-compatible child retirement
6. product baseline vs launch SHA schema

## Acceptance for Follow-Up Skill Work

- planner rejects or explicitly justifies T9-sized single-worker task
- runtime decomposition rule unnecessary for equivalent future plan because slices predeclared
- targeted tests + compile complete before ProductionPrepare
- zero-change generation creates no misleading regeneration commit
- failed and successful proof attempts both retained
- retrospective reconstructs agent timing/findings from durable ledger without chat history
- existing LOC/finding re-review thresholds unchanged
