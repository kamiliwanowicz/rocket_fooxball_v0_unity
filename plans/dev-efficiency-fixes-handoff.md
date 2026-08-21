# Dev Efficiency Fixes — Issues Handoff

Status: implementation complete + two-section `sol_high` review clear.

Implementation location:

- worktree: `C:/wt/devfix`
- branch: `dev-efficiency`
- final reviewed HEAD: `24453b8`
- base: `db26e4b`

## Why implementation took long

- required order: seven numbered items -> one commit + green checks per item
- Unity constraint: one Editor process per project -> compile, EditMode, bake, build, validate serialized
- Production lighting bake: multi-minute operation
- T2.5 plan assumption wrong: Development bake creates 2 atlases; Production requires 4
- Development -> Production transition exposed output deletion + GUID churn
- first code review found five High issues -> fresh workers fixed -> same reviewers rechecked
- stale zero-byte `Temp/UnityLockfile` delayed one retry until workflow timeout

## Resolved issues

### Shell hook latency

Symptom: every Claude shell hook cost ~950 ms.

Cause: PowerShell process spawned before unrelated-event skip.

Fix: Node dispatcher skips unrelated events before PowerShell launch. Measured skip ~58–70 ms.

Commit: `806b890`.

### Generated-YAML comparator hang

Symptom: stalled Git child could hang comparator forever.

Cause: redirected streams drained sequentially; timeout reached only after blocking read.

Fix: stdout + stderr drain asynchronously before timed wait; timeout kills child; final wait flushes streams.

Commits: `c8ac421`, review correction `21815c8`.

### Duplicate workflow staleness cache

Symptom: PowerShell cache duplicated C# stage staleness, skipped cheap launches only, could produce missing-probe failure.

Fix: remove PowerShell reuse/digest logic. Ledger remains write-only execution audit. Merge/fix orchestration contracts now require rerunning applicable checks.

Important retained helpers: `Get-ChangedHashPaths`, `Test-StringSetEqual`. Live postflight provenance callers still need them.

Commit: `5223462`; consumer-contract correction: `21815c8`.

### Production bake audit status

Symptom: valid C# bake skip classified `reused`, then overwritten to `executed`.

Fix: preserve resolved `reused` result; normal bake stays `executed`; harness guards both paths.

Commit: `21815c8`.

### Bot Inspector tuning

Symptom: changing valid tuned values disabled bot components.

Cause: composition validation required exact defaults.

Fix: validate finite/range constraints, not equality. Defaults remain serialized initializers.

Commit: `25d1ebd`.

### Duplicated tuning literals

Symptom: one tuning change required matching runtime + editor literal edits.

Fix: runtime owns public defaults; editor builder/validators reference runtime constants. Values unchanged.

Commit: `6a854e8`.

### Dead code

Symptom: ~430 obsolete lines increased search/review cost.

Fix: remove verified unused members. Keep `GeneratedFingerprintPaths`; construction performs `.meta` coverage validation.

Commit: `68df402`.

### Development/Production lightmap topology

Symptom chain:

`Development bake -> 2 atlases` -> old global 4-atlas requirement failed trusted-output identity.

`Development deletes atlas 2/3` -> next Production bake recreated `.meta` files -> GUID churn.

`Bake fails or topology differs` -> initial snapshot fix skipped restoration or could mix Development + Production outputs.

Fix:

- Production topology owner: 4 atlases
- Development topology owner: 2 atlases
- missing atlas 2/3 accepted only while capturing current Development baked-output state
- complete Production atlas 2/3 asset+meta pairs snapshot before Development bake
- snapshots restore atomically in `finally` on success, false return, throw, or topology failure
- Development acceptance requires exactly 2 complete entries with exact expected paths
- Production validation remains strict at 4

Commits: `b25df25`, `7d97391`, `eb76b3f`, review hardening `73fcd7f`.

Final generated output: `24453b8`.

Final comparator: 19/19 coverage; dangling 0; GUID churn 0; broken pairs 0; unsupported 0.

## May still happen

### Slow Unity lighting bake

Status: expected, not bug.

Impact: ProductionPrepare can take several minutes. Development -> Production sequence requires new Production bake.

Response: wait for workflow result + process/lock release. Do not poll Unity manually or start second Editor.

### Stale Unity lockfile

Status: environment issue not eliminated.

Symptom: Unity process exits but zero-byte `Temp/UnityLockfile` remains; workflow waits for cleanup timeout.

Response:

- confirm no Unity/LightBaker process remains
- let workflow lease/timeout release lock
- do not bypass with broad/manual deletion while workflow owns lease
- preserve evidence path + report recurrence

Potential future fix: make workflow distinguish stale zero-byte lock from live Editor lock using PID/process ownership proof, then clear exact stale lock safely. Separate task required.

### Strict clean-worktree gate

Status: intentional safety rule.

Impact: uncommitted non-generated files block builder workflow.

Response: stash only exact pre-existing paths, record hashes/stash identity, run workflow, restore byte-for-byte. Never broad-stash unrelated work.

### Serialized Unity validation cost

Status: intentional safety rule.

Impact: source change can require compile -> EditMode -> authoritative build -> separate-process semantic validate.

Response: run only checks invalidated by final diff. Do not skip separate validation for scene/prefab/builder changes.

### Git LFS comparator appearance

Status: expected.

Symptom: worktree lightmaps show MB-sized binaries while Git base shows ~130-byte blobs.

Cause: worktree contains smudged binary; Git commit contains LFS pointer.

Response: judge acceptance from comparator coverage, GUID/pair/dangling/unsupported results, source-defined staleness, and workflow result. Binary hash/size = provenance only.

### Interactive bot-tuning proof

Status: manual check not run.

Check: set bot coordinator `evaluationInterval = 0.6f`, enter Play, confirm component stays enabled + no composition `LogError`, revert value.

## Final validation evidence

- harness + red fixtures: green
- Unity compile: zero errors
- EditMode: green; latest reported 160/160
- Fast workflow: green
- Development workflow: current, 2 atlases
- ProductionPrepare: current, 1 bake
- separate ProductionValidate: current, 4 atlases
- tooling reviewer: no remaining Critical/High
- Unity reviewer: no remaining Critical/High

## Working-tree ownership

Implementation workers preserved two pre-existing user paths uncommitted:

- `.agents/skills/use-blender/SKILL.md`
- `plans/dev-efficiency-fixes.md`

Do not fold them into implementation commits without explicit user direction.
