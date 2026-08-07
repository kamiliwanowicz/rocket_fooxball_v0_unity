# State And Recovery Contract

Reference for durable run state, transitions, retries, stalls, and recovery. Main procedure owns orchestration order.

## Truth And Ownership

- Durable state: one restricted-YAML ledger at `loop-runs/{run_id}/state.yaml` on control branch.
- Ledger writer: lead process (LP) only.
- Control worktree and branch: ledger-only. Never hold product changes. Never merge into product branch.
- Integration worktree and branch: accepted product commits.
- Code truth: exact full Git SHAs. Names, reports, working-tree contents, and agent assertions remain references only.
- Control head: Git commit containing loaded ledger, derived from control ref. `repository.control_parent_sha` records parent of containing control commit. Never store containing control SHA inside ledger.
- State-content digest: `completion.final_state_digest` uses `sha256:` plus SHA-256 of ledger UTF-8 bytes after CRLF-to-LF normalization, replacement of unique `  final_state_digest:` line value with `null`, and final-LF normalization. All other bytes unchanged.
- Ledger update: read revision -> reconcile Git, capacity, reservations, dispatch, attempt, and lease -> set `repository.control_parent_sha` to current control head -> apply one accepted transition -> increment revision -> append immutable event -> recompute required state digest -> validate -> commit control branch.
- Transition durability: accepted only after control-branch commit contains updated ledger.
- Secrets and sensitive logs: excluded. Store redacted summaries or safe artifact references.

## Restricted YAML And Closed Schema

- Indentation: exactly 2 spaces per level. Spaces only.
- Values: mappings, lists, strings, integers, booleans, or `null` only.
- Quote every timestamp, full SHA, path, branch, digest, ID, enum, and ambiguous scalar.
- Ambiguous scalar examples: words resembling booleans or null, numeric-looking strings, leading-zero values, date-like values.
- Timestamps: quoted explicit UTC strings.
- SHAs: quoted full Git object IDs. Never abbreviate.
- Disallowed: anchors, aliases, tags, merge keys, implicit timestamps, duplicate keys, custom types.
- Root `schema_version`: integer `1`.
- Root `revision`: monotonically increasing integer used for compare-and-swap (CAS).
- Schema closure: every mapping contains exactly keys shown below. Missing or unknown key invalid. Every registry list accepts only corresponding record layout. Empty list valid.
- Nullability: only fields shown as `null` or explicitly permitted null accept null. All fields required.
- Writer validation: parsed types, exact keys, enums, unique IDs, references, counts, revision chain, Git invariants, and state invariants after every update and before commit.
- Existing `events` entries immutable. Each committed update appends exactly one event with prior and resulting revision.

## Schema Version 1 Root

Initial ledger uses revision `1` plus one matching initialization event. Placeholder SHAs, paths, branches, digests, timestamps, and IDs below must become observed values before first commit.

```yaml
schema_version: 1
revision: 1
run:
  id: "run_id"
  status: "INIT"
  created_at: "1970-01-01T00:00:00Z"
  updated_at: "1970-01-01T00:00:00Z"
  started_at: null
  ended_at: null
repository:
  root_path: "absolute/repository/path"
  product_branch: "product_branch"
  control_branch: "control_branch"
  control_worktree_path: "absolute/control/worktree/path"
  control_parent_sha: "0000000000000000000000000000000000000000"
  integration_branch: "integration_branch"
  integration_worktree_path: "absolute/integration/worktree/path"
  integration_head_sha: "0000000000000000000000000000000000000000"
  initial_product_sha: "0000000000000000000000000000000000000000"
budgets:
  plan_ceiling: 20
  elapsed_limit_minutes: 360
  replans_limit: 2
  same_failure_retry_limit: 2
  transient_retry_limit: 3
  review_fix_cycle_limit: 1
  elapsed_minutes: 0
  replans_used: 0
  review_fix_cycles_used: 0
capacity:
  configured_limit: 20
  effective_limit: 20
  live_count: 0
  reserved_count: 0
  available_count: 20
  live_agent_ids: []
  measured_at: "1970-01-01T00:00:00Z"
authorization:
  scope_digest: "sha256:0000000000000000000000000000000000000000000000000000000000000000"
  allowed_action_ids: []
  forbidden_action_ids: []
  user_decision_required: false
  user_decision_reason: null
breakdown:
  status: "pending"
  baseline_sha: "0000000000000000000000000000000000000000"
  artifact_path: null
  artifact_digest: null
  requirement_ids: []
  plan_ids: []
  active_dispatch_id: null
  accepted_result_id: null
  gate_id: null
  evidence_ids: []
  checked_at: null
requirements: []
evidence: []
findings: []
blockers: []
plans: []
lanes: []
work_packets: []
waves: []
reservations: []
dispatches: []
attempts: []
leases: []
results: []
integration:
  status: "pending"
  active_wave_id: null
  wave_baseline_sha: "0000000000000000000000000000000000000000"
  accepted_plan_ids: []
  integrated_plan_ids: []
  integrated_commit_shas: []
  head_sha: "0000000000000000000000000000000000000000"
  clean: true
  active_dispatch_id: null
  accepted_result_id: null
  combined_review_gate_id: null
  final_verify_gate_id: null
  validation_status: "pending"
  validation_exact_state_sha: null
  validation_evidence_ids: []
gates: []
incidents: []
events:
  - id: "initial_event_id"
    prior_revision: 0
    resulting_revision: 1
    at: "1970-01-01T00:00:00Z"
    actor_id: "lp_agent_id"
    kind: "initialized"
    phase: "INIT"
    entity_kind: "run"
    entity_id: "run_id"
    dispatch_id: null
    attempt_id: null
    result_id: null
    from_state: null
    to_state: "INIT"
    exact_state_sha: "0000000000000000000000000000000000000000"
    evidence_ids: []
    summary: "initialized durable run"
completion:
  ready_for_user_merge: false
  final_state_digest: null
  final_integration_sha: null
  requirements_satisfied_ids: []
  unresolved_blocker_ids: []
  handoff_dispatch_id: null
  handoff_result_id: null
  handoff_evidence_ids: []
  completed_at: null
```

## Closed Record Layouts

Each registry record uses exact layout below. Layout labels describe types; labels never appear inside ledger records.

### Requirement Record

```yaml
id: "requirement_id"
summary: "bounded requirement"
status: "pending"
owner_plan_id: null
dependency_requirement_ids: []
acceptance_evidence_ids: []
waiver_evidence_id: null
```

### Evidence Record

```yaml
id: "evidence_id"
kind: "artifact"
status: "valid"
phase: "BREAKDOWN"
subject_kind: "run"
subject_id: "run_id"
exact_state_sha: "0000000000000000000000000000000000000000"
artifact_path: null
artifact_digest: null
command: null
outcome: "accepted"
recorded_at: "1970-01-01T00:00:00Z"
invalidated_at_revision: null
invalidation_reason: null
```

### Finding Record

```yaml
id: "finding_id"
phase: "COMBINED_REVIEW"
subject_kind: "integration"
subject_id: "run_id"
severity: "blocking"
status: "open"
summary: "redacted finding"
evidence_ids: []
blocker_id: null
fix_plan_id: null
occurrence_count: 1
disposition_evidence_id: null
```

### Blocker Record

```yaml
id: "blocker_id"
fingerprint: "sha256:0000000000000000000000000000000000000000000000000000000000000000"
status: "open"
phase: "EXECUTION_WAVE"
entity_kind: "plan"
entity_id: "plan_id"
owner_id: "agent_id"
summary: "redacted blocker"
evidence_ids: []
occurrence_count: 1
first_seen_at: "1970-01-01T00:00:00Z"
last_seen_at: "1970-01-01T00:00:00Z"
recheck_condition: "observable condition"
resolution_evidence_id: null
```

### Plan Record

```yaml
id: "plan_id"
status: "pending"
wave_id: null
wave_number: null
dependency_plan_ids: []
requirement_ids: []
plan_path: null
plan_digest: null
plan_baseline_sha: null
branch: null
worktree_path: null
forecast_ownership: []
accepted_ownership: []
supervisor_id: null
active_dispatch_id: null
active_attempt_id: null
active_lease_id: null
convergence_gate_id: null
timing:
  started_at: null
  last_progress_at: null
  heartbeat_at: null
  expires_at: null
accepted:
  head_sha: null
  commit_shas: []
  clean: false
  result_id: null
  accepted_at: null
review:
  status: "pending"
  reviewed_head_sha: null
  finding_ids: []
  evidence_ids: []
validation:
  status: "pending"
  exact_state_sha: null
  evidence_ids: []
  validated_at: null
blocker_ids: []
incident_ids: []
updated_revision: 1
```

### Lane Record

```yaml
id: "lane_id"
plan_id: "plan_id"
status: "pending"
dependency_lane_ids: []
work_packet_ids: []
supervisor_id: null
forecast_ownership: []
accepted_ownership: []
active_dispatch_id: null
blocker_ids: []
updated_revision: 1
```

### Work Packet Record

```yaml
id: "work_packet_id"
lane_id: "lane_id"
plan_id: "plan_id"
status: "pending"
dependency_work_packet_ids: []
requirement_ids: []
baseline_sha: null
branch: null
worktree_path: null
forecast_ownership: []
accepted_ownership: []
active_dispatch_id: null
active_attempt_id: null
active_lease_id: null
accepted_result_id: null
accepted_head_sha: null
accepted_commit_shas: []
clean: false
evidence_ids: []
finding_ids: []
blocker_ids: []
updated_revision: 1
```

### Wave Record

```yaml
id: "wave_id"
number: 1
status: "pending"
dependency_wave_ids: []
plan_ids: []
baseline_sha: "0000000000000000000000000000000000000000"
integrated_plan_ids: []
integrated_commit_shas: []
resulting_head_sha: null
integration_gate_id: null
validation_evidence_ids: []
completed_at: null
```

### Reservation Record

```yaml
id: "reservation_id"
status: "held"
phase: "PLANNING"
entity_kind: "plan"
entity_id: "plan_id"
dispatch_id: "dispatch_id"
slot_count: 1
created_at: "1970-01-01T00:00:00Z"
expires_at: "1970-01-01T00:00:00Z"
released_at: null
release_reason: null
```

### Dispatch Record

Dispatch records remain role-neutral. `phase`, `role`, and entity reference cover breakdown, breakdown check, planner, plan supervisor, wave merge, combined review, integration fix, final verify, and handoff work.

```yaml
id: "dispatch_id"
idempotency_key: "dispatch_idempotency_key"
status: "recorded"
phase: "PLANNING"
role: "planner"
entity_kind: "plan"
entity_id: "plan_id"
expected_revision: 1
baseline_sha: "0000000000000000000000000000000000000000"
input_evidence_ids: []
reservation_id: "reservation_id"
active_attempt_id: null
accepted_result_id: null
recorded_at: "1970-01-01T00:00:00Z"
spawned_at: null
closed_at: null
```

### Attempt Record

```yaml
id: "attempt_id"
dispatch_id: "dispatch_id"
status: "active"
entity_kind: "plan"
entity_id: "plan_id"
ordinal: 1
supervisor_id: "agent_id"
started_at: "1970-01-01T00:00:00Z"
heartbeat_at: "1970-01-01T00:00:00Z"
last_progress_at: "1970-01-01T00:00:00Z"
soft_stale_at: "1970-01-01T00:00:00Z"
grace_expires_at: "1970-01-01T00:00:00Z"
hard_expires_at: "1970-01-01T00:00:00Z"
active_lease_id: "lease_id"
accepted_result_id: null
failure_fingerprint: null
same_failure_count: 0
transient_retry_count: 0
checkpoint_evidence_ids: []
preserved_commit_shas: []
ended_at: null
```

### Lease Record

```yaml
id: "lease_id"
attempt_id: "attempt_id"
status: "active"
entity_kind: "plan"
entity_id: "plan_id"
acquired_at: "1970-01-01T00:00:00Z"
expires_at: "1970-01-01T00:00:00Z"
released_at: null
release_reason: null
```

### Result Record

```yaml
id: "result_id"
idempotency_key: "result_idempotency_key"
dispatch_id: "dispatch_id"
attempt_id: "attempt_id"
status: "submitted"
phase: "PLANNING"
entity_kind: "plan"
entity_id: "plan_id"
expected_revision: 1
baseline_sha: "0000000000000000000000000000000000000000"
exact_state_sha: "0000000000000000000000000000000000000000"
head_sha: null
commit_shas: []
clean: true
artifact_path: null
artifact_digest: null
evidence_ids: []
finding_ids: []
blocker_ids: []
reported_outcome: "plan_ready"
submitted_at: "1970-01-01T00:00:00Z"
accepted_at: null
accepted_revision: null
rejection_reason: null
```

### Gate Record

```yaml
id: "gate_id"
kind: "plan_convergence"
status: "pending"
exact_state_sha: "0000000000000000000000000000000000000000"
subject_ids: []
evidence_ids: []
accepted_revision: null
accepted_at: null
```

### Incident Record

```yaml
id: "incident_id"
kind: "stale_attempt"
status: "open"
phase: "EXECUTION_WAVE"
entity_kind: "plan"
entity_id: "plan_id"
attempt_id: null
occurrence_count: 1
summary: "redacted incident"
evidence_ids: []
opened_at: "1970-01-01T00:00:00Z"
resolved_at: null
resolution_evidence_id: null
```

Event records use exact keys shown by initialization event. Later event `from_state` and `to_state` use run-state enums or null where transition has no run-state change.

## Enums And References

- Run states: `INIT`, `BREAKDOWN`, `BREAKDOWN_CHECK`, `PLANNING`, `PLAN_CONVERGENCE`, `EXECUTION_WAVE`, `WAVE_INTEGRATION`, `COMBINED_REVIEW`, `INTEGRATION_FIX`, `FINAL_VERIFY`, `READY_FOR_USER_MERGE`, `COMPLETE`, `BLOCKED`, `AWAITING_USER`, `STALE`, `REPLANNING`, `BUDGET_EXHAUSTED`, `FAILED`, `CANCELLED`.
- Phase enums: `INIT`, `BREAKDOWN`, `BREAKDOWN_CHECK`, `PLANNING`, `PLAN_CONVERGENCE`, `EXECUTION_WAVE`, `WAVE_INTEGRATION`, `COMBINED_REVIEW`, `INTEGRATION_FIX`, `FINAL_VERIFY`, `READY_FOR_USER_MERGE`, `BLOCKED`, `AWAITING_USER`, `STALE`, `REPLANNING`, `BUDGET_EXHAUSTED`, `FAILED`, `CANCELLED`.
- Entity kinds: `run`, `breakdown`, `requirement`, `plan`, `lane`, `work_packet`, `wave`, `integration`, `finding`, `blocker`, `verification`, `handoff`.
- Roles: `breaker`, `breakdown_reviewer`, `planner`, `plan_supervisor`, `implementation_worker`, `reviewer`, `review_fix_worker`, `integrator`, `combined_reviewer`, `integration_fixer`, `verifier`, `handoff_recorder`.
- Plan statuses: `pending`, `planning`, `awaiting_convergence`, `ready_to_execute`, `executing`, `ready_to_integrate`, `integrated`, `blocked`, `failed`, `cancelled`, `superseded`.
- Lane statuses: `pending`, `active`, `complete`, `blocked`, `failed`, `cancelled`, `superseded`.
- Work-packet statuses: `pending`, `ready`, `executing`, `ready_to_review`, `changes_required`, `ready_to_integrate`, `integrated`, `blocked`, `failed`, `cancelled`, `superseded`.
- Breakdown statuses: `pending`, `active`, `complete`, `checked`, `blocked`, `failed`, `cancelled`.
- Requirement statuses: `pending`, `covered`, `satisfied`, `waived`, `blocked`, `failed`.
- Evidence kinds: `artifact`, `commit`, `diff`, `review`, `validation`, `checkpoint`, `authorization`, `handoff`, `state_reconciliation`, `capacity`, `reservation`, `result`, `incident`, `other`.
- Evidence statuses: `valid`, `invalidated`.
- Evidence outcomes: `accepted`, `passed`, `failed`, `observed`, `waived`, `not_run`, `not_due`, `not_applicable`.
- Finding severities: `blocking`, `nonblocking`.
- Finding statuses: `open`, `fix_approved`, `fixed`, `waived`, `rejected`.
- Blocker statuses: `open`, `resolved`, `awaiting_user`, `failed`.
- Wave and integration statuses: `pending`, `active`, `integrating`, `complete`, `blocked`, `failed`, `cancelled`.
- Reservation statuses: `held`, `consumed`, `released`, `expired`.
- Dispatch statuses: `recorded`, `spawned`, `active`, `accepted`, `rejected`, `interrupted`, `expired`, `failed`, `cancelled`.
- Attempt statuses: `active`, `stale`, `interrupted`, `expired`, `completed`, `failed`, `cancelled`.
- Lease statuses: `active`, `released`, `expired`, `revoked`.
- Result statuses: `submitted`, `accepted`, `rejected`, `superseded`.
- Review statuses: `pending`, `accepted`, `changes_required`, `invalidated`, `waived`, `failed`.
- Validation statuses: `pending`, `pass`, `fail`, `waived`, `invalidated`, `not_run`, `not_due`.
- Gate kinds: `breakdown_check`, `plan_convergence`, `wave_integration`, `combined_review`, `final_verify`.
- Gate statuses: `pending`, `accepted`, `invalidated`, `failed`, `not_run`, `not_due`.
- Incident statuses: `open`, `resolved`.
- Incident kinds: `stale_attempt`, `ambiguous_side_effect`, `git_mismatch`, `capacity_mismatch`, `ownership_violation`, `orphaned_commit`, `retry_exhausted`, `policy_violation`, `other`.
- Event kinds: `initialized`, `transition`, `dispatch_recorded`, `attempt_started`, `heartbeat_recorded`, `progress_recorded`, `result_accepted`, `result_rejected`, `lease_closed`, `reservation_changed`, `state_reconciled`, `incident_recorded`, `evidence_invalidated`, `cancelled`.
- Result outcome enums: `ready`, `needs_user`, `blocked`, `plan_ready`, `implementation_complete`, `review_passed`, `changes_required`, `fix_complete`, `wave_complete`, `complete`, `verification_passed`, `verification_failed`, `not_run`, `not_due`.

Source report -> durable `reported_outcome` mapping:

- `breaker` task breakdown: `ready -> ready`; `needs_user -> needs_user`; `blocked -> blocked`.
- `breakdown_reviewer`: `pass -> review_passed`; `findings -> changes_required`.
- `planner`: `complete -> plan_ready`; `blocked -> blocked`.
- `plan_supervisor`: `complete -> implementation_complete`; `blocked -> blocked`.
- `implementation_worker`: `complete -> implementation_complete`; `blocked -> blocked`.
- `reviewer`: `pass -> review_passed`; `findings -> changes_required`.
- `review_fix_worker`: `complete -> fix_complete`; `blocked -> blocked`.
- `integrator`: `wave_complete -> wave_complete`; `complete -> complete`; `blocked -> blocked`.
- `combined_reviewer`: `pass -> review_passed`; `findings -> changes_required`.
- `integration_fixer`: `complete -> fix_complete`; `blocked -> blocked`.
- `verifier`: `pass -> verification_passed`; `fail -> verification_failed`; `not_run -> not_run`; `not_due -> not_due`; `blocked -> blocked`.
- `handoff_recorder`: `complete -> complete`; `needs_user -> needs_user`; `blocked -> blocked`.

Mapping rules: read raw enum from immutable accepted report artifact; select exact pair by linked dispatch role; store mapped target only; retain report path, digest, and evidence. Unlisted role/source pair invalid. Case folding, synonym substitution, fallback, and cross-role mapping invalid.
- Collection members: `*_ids` lists contain quoted IDs; `*_shas` lists contain quoted full SHAs; ownership lists contain quoted paths or globs; `live_agent_ids` contains external agent IDs; action-ID lists contain external authorization IDs.
- Global uniqueness: every registry record ID and event ID unique across run. Idempotency keys unique within record type.
- Typed references: every `*_id` and `*_ids` field resolves matching registry record unless null or explicitly external agent/action identity. Entity mapping: `run | breakdown | integration | verification | handoff` -> `run.id`; `requirement` -> `requirements`; `plan` -> `plans`; `lane` -> `lanes`; `work_packet` -> `work_packets`; `wave` -> `waves`; `finding` -> `findings`; `blocker` -> `blockers`.
- Digest format: every nonnull digest and blocker fingerprint matches `sha256:` plus 64 lowercase hexadecimal characters.
- Control-parent invariant: containing control commit parent equals `repository.control_parent_sha`; containing SHA derives from control ref and remains absent from ledger.
- State-digest invariant: nonnull `completion.final_state_digest` matches defined normalized ledger bytes. Digest recomputed for every later completion-state revision.
- Plan nullability: `pending` or `planning` permits null `wave_id`, `wave_number`, `plan_path`, `plan_digest`, `plan_baseline_sha`, `branch`, and `worktree_path`. `awaiting_convergence` and later requires nonnull values for all. `ready_to_execute` and later requires accepted `convergence_gate_id` targeting exact baseline and current digest.
- Work-packet nullability: `pending` permits null `baseline_sha`, `branch`, and `worktree_path`. `ready` and later requires nonnull values. `ready_to_review` and later requires accepted result, head, commits, cleanliness, and evidence consistent with status.
- Completion nullability: `final_state_digest` and `final_integration_sha` remain null before `READY_FOR_USER_MERGE`; both required in `READY_FOR_USER_MERGE` and `COMPLETE`.
- Attempt lease nullability: `active_lease_id` required for `active`; explicitly null after attempt becomes nonactive.
- Active-pointer symmetry: dispatch, attempt, lease, plan, integration, and reservation pointers agree bidirectionally.
- Lifecycle-role invariant: attempt and result entity pairs match linked dispatch; accepted result artifact raw enum maps exactly to durable outcome through linked dispatch role.
- Capacity invariant: `0 <= effective_limit <= configured_limit`; `available_count = effective_limit - live_count - reserved_count`; counts nonnegative; `reserved_count` equals held reservation slot sum; `live_count` equals live agent ID count.
- Convergence-gate invariant: accepted plan gate targets current integration SHA; valid gate evidence matches each subject plan `plan_digest` and `plan_baseline_sha`.
- Validation invariant: `pass`, `fail`, or `waived` requires exact state SHA and evidence. Per-check `not_due` legal only for `intermediate` stage plus `final` check kind. `not_due` illegal for intermediate integration checks and every final-stage check. Aggregate `integration.validation_status: not_due` legal only during intermediate stage. `not_run` valid only when earlier blocker prevents due check; aggregate final `not_run` requires earlier blocker preventing every final check.
- Blocked-before-check invariant: final verifier result uses `not_run`; `integration.validation_status` and final-verify gate use `not_run`; blocker record targets `verification`; run enters explicit side state. No validation evidence claims execution.
- Revision invariant: event count equals revision only when every revision starts at initialization and no history compaction exists. Schema v1 forbids history compaction.
- Git invariant: every nonnull SHA exists and is full Git SHA; accepted commit list descends from recorded baseline; integration SHA matches integration branch head at acceptance.

## Primary State Machine

`INIT -> BREAKDOWN -> BREAKDOWN_CHECK -> PLANNING -> PLAN_CONVERGENCE -> EXECUTION_WAVE -> WAVE_INTEGRATION`

After `WAVE_INTEGRATION`:

- No remaining wave -> `COMBINED_REVIEW`.
- Next dependency wave lacks plan artifact, or artifact path/digest/baseline is absent or stale against current integration head -> `PLANNING`.
- Current artifacts exist but convergence acceptance is absent, invalidated, or tied to older integration SHA -> `PLAN_CONVERGENCE`.
- Every next-wave plan is `ready_to_execute`, references accepted `convergence_gate_id` for current digest and integration SHA, and has `plan_baseline_sha` equal exact current integration head -> `EXECUTION_WAVE`.

Final path: `COMBINED_REVIEW -> INTEGRATION_FIX -> FINAL_VERIFY -> READY_FOR_USER_MERGE -> COMPLETE`.

- `INIT` complete: revision `1`; exactly one immutable `initialized` event maps `0 -> 1`; run ID unique; authorization captured; capacity measured; control and integration branches/worktrees exist; exact initial, control-parent, and integration SHAs reconcile; ledger committed.
- `BREAKDOWN` complete: bounded requirement records and plan candidates recorded; dependencies, ownership forecast, validation needs, and budget feasibility explicit; artifact path, digest, baseline, accepted role-neutral result, and evidence reconcile.
- `BREAKDOWN_CHECK` complete: every requirement covered exactly or linked blocker recorded; dependency graph acyclic; ownership conflicts resolved or blocked; plan count within ceiling; accepted gate targets current breakdown digest and integration SHA.
- `PLANNING` complete: every target plan has identity, requirements, dependencies, wave, path, digest, exact baseline SHA, branch, worktree, ownership forecast, completion evidence criteria, and accepted planner result.
- `PLAN_CONVERGENCE` complete: plans jointly cover authorized scope; dependencies and waves valid; same-wave ownership disjoint or serialized; dependent plan artifacts refreshed to current baseline; accepted gate targets current plan digests and integration SHA; plans become `ready_to_execute`.
- `EXECUTION_WAVE` complete: every active-wave plan is `ready_to_integrate`, explicit side-state blocked, or accepted failed; each ready plan has frozen exact head, committed clean tree, reconciled commits, closed accepted attempt and lease, accepted ownership, review, and evidence.
- `WAVE_INTEGRATION` complete: every ready plan integrated once by exact SHA with ancestry proven; integration head and cleanliness reconcile; wave validation targets exact resulting SHA; wave, result, gate, and event committed. Apply branch rules above for next state.
- `COMBINED_REVIEW` complete: combined scope reviewed at exact integration SHA; findings classified; evidence recorded; each finding cleared, nonblocking, authorized waiver, or member of one approved fix set; accepted result and gate reconcile.
- `INTEGRATION_FIX` complete: empty fix set -> no-change result against unchanged exact head. Nonempty fix set -> one authorized fix cycle produces committed clean head, records finding dispositions and ownership, invalidates affected evidence, reruns affected review, and leaves zero unresolved blocking findings.
- `FINAL_VERIFY` complete: all required checks pass or carry authorized waiver at one exact integration SHA; requirements, plans, incidents, findings, capacity, reservations, tree cleanliness, results, gates, and ledger reconcile; no unresolved blocker.
- `READY_FOR_USER_MERGE` complete: final exact integration SHA frozen; state-content digest, control parent, and handoff evidence recorded; merge instructions target exact integration SHA; control branch excluded; all attempts, leases, dispatches, and reservations closed.
- `COMPLETE` complete: user merge outcome or authorized terminal handoff recorded; final integration SHA, state-content digest, and control parent reconcile; completion timestamp set; completion event committed.

## Side States

- `BLOCKED`: concrete in-scope dependency prevents progress; blocker, affected entities, evidence, owner, and recheck condition recorded.
- `AWAITING_USER`: user authority or decision required; exact question, safe options, preserved state, and resume condition recorded.
- `STALE`: soft-stale protocol active or lease expired; last proven progress, requested checkpoint, grace deadline, and preservation status recorded.
- `REPLANNING`: accepted plan invalidated by new evidence; cause, affected requirements, previous digest, budget use, and replacement target recorded.
- `BUDGET_EXHAUSTED`: applicable limit reached; completed work, preserved SHAs, remaining work, and escalation choice recorded.
- `FAILED`: unrecoverable or policy-selected terminal failure; evidence, preserved commits, impact, and remediation recorded.
- `CANCELLED`: authorized cancellation recorded; agents interrupted, leases and reservations closed, commits preserved, and worktrees reconciled.

Side-state exit requires revision-CAS transition with evidence and reconciled Git. `BLOCKED`, `AWAITING_USER`, `STALE`, or `REPLANNING` resumes recovery-selected first incomplete valid primary state. `BUDGET_EXHAUSTED` exits only through new authorization or terminal `FAILED`. `FAILED` and `CANCELLED` remain terminal unless user starts explicit recovery through new revision and event.

## Transition Acceptance

Accept transition only when all checks pass:

- Submitted expected revision equals durable ledger revision.
- Entity, dispatch, attempt, lease, reservation, and result references match active records.
- Capacity counts and global reservations reconcile with live agents and intended side effect.
- Baseline, branch, worktree, head, commits, cleanliness, and ancestry reconcile with Git.
- Evidence belongs to reported exact head or exact integration state SHA and remains `valid`.
- Accepted report artifact digest reconciles; raw source enum parses under role contract; exact role/source mapping equals stored `reported_outcome`.
- Report is neither stale, superseded, previously accepted, nor from interrupted or closed attempt.
- Target-state observable completion condition holds.
- Update passes closed-schema and restricted-YAML validation.
- IDs, idempotency keys, enums, references, counters, and event revision chain hold.

Reject transition without mutating accepted result state. Record rejection through separate CAS update and immutable event. Agent assertion never creates completion; reconciled evidence does.

## Idempotency

- Record unique reservation and dispatch before spawn side effect.
- One active attempt per entity. One active lease per attempt. One held reservation per dispatch.
- Accept each result ID and exact result state once.
- Before merge, fetch/reconcile refs and prove exact SHA exists, belongs to expected branch history, descends from baseline, and remains current.
- Already integrated exact SHA with proven ancestry -> mark integration complete; skip merge side effect.
- Validation valid only for recorded exact state SHA. State-changing commit invalidates affected evidence and gate through CAS.
- Replacement dispatch allowed only after lease expiry or confirmed interrupt. Preserve prior attempt and commits.
- Before spawn, interrupt, merge, branch mutation, or validation: reconcile ledger revision, Git heads, capacity, reservations, active records, and prior outcome.
- Ambiguous outcome -> inspect actual state first; repeat only when absence proven.

## Baselines, Worktrees, And Freeze

- Same-wave plans start from same recorded integration SHA.
- Dependent wave plans start just-in-time from latest integration head containing all dependencies.
- Early dependent plan artifact with old baseline -> `PLANNING` for refresh. Current refreshed artifact lacking acceptance -> `PLAN_CONVERGENCE`.
- Accepted implementation head: clean and fully committed. Untracked or modified owned files prevent acceptance.
- `ready_to_integrate` acceptance freezes exact head and commits, accepts result once, sets attempt `completed`, releases lease, closes dispatch, releases reservation, and clears all active plan pointers in one CAS update.
- Accepted lease grants no post-freeze mutation authority. Mutation under accepted or closed lease rejected.
- Post-freeze branch mutation requires fresh reservation, dispatch, attempt, and lease recorded through CAS before write. Same CAS transition clears accepted head, commits, result, and timestamp; sets plan `executing`; invalidates plan review, validation, affected evidence, and affected gates.
- Integrate and validate after each wave. Skip intermediate wave integration only for one independent wave.
- Accepted ownership derives from observed final diff. Out-of-scope paths require authorization or rejection.

## Progress, Stall, Retry, And Escalation

Defaults unless run authorization sets stricter values:

- Plan ceiling: 20.
- Elapsed limit: 360 minutes.
- Replans: 2.
- Same-failure retries: 2.
- Heartbeat interval: 10 minutes.
- Soft stale: 20 minutes without proven progress.
- Soft-stale request: exact current state, evidence, and next checkpoint.
- Grace after request: 10 minutes.
- Grace expiry: interrupt attempt, preserve reachable commits, expire lease, release reservation, reconcile worktree, then create replacement records if budget and capacity permit.
- Hard attempt limit: 120 minutes unless recent active checkpoint proves bounded forward progress and ledger records extension.
- Transient infrastructure retries: 3, bounded delay and same idempotency key.
- Review-fix cycles: 1.
- Third occurrence of same blocker or exhausted applicable budget -> `AWAITING_USER` when user choice can unblock; otherwise `FAILED`.

Heartbeat alone is liveness, not progress. Proven progress requires at least one: new committed exact SHA, material artifact with digest, validation evidence tied to exact SHA, narrowed blocker with new evidence, or accepted state transition.

Retry classification:

- Transient infrastructure failure -> bounded retry, same logical operation ID, reconcile before each retry.
- Same deterministic failure -> at most 2 retries after materially changed input or fix; increment occurrence count.
- Plan invalidation -> `REPLANNING`, consume replan budget, preserve superseded plan and commits.
- Agent stall -> `STALE` protocol; replacement only after expiry or confirmed interrupt.
- Authorization gap -> `AWAITING_USER`; no speculative side effect.

## Recovery Bootstrap

After LP restart, context loss, or ambiguous side effect:

1. Load highest valid committed `loop-runs/{run_id}/state.yaml` from control branch. Validate restricted YAML, closed schema, revision `1` initialization, event chain, and immutable history.
2. Derive control head from containing commit. Verify containing commit parent against `repository.control_parent_sha`; verify recorded integration head against Git; recompute nonnull state-content digest.
3. Enumerate worktrees, branches, refs, reachable commits, live agents/processes, and pending external outcomes.
4. Reconcile capacity and reservations. Reconcile every dispatch, attempt, lease, result, accepted report artifact digest, raw source enum mapping, active pointer, worktree, branch head, cleanliness, timestamp, and reachable commit.
5. Reject queued reports from stale, expired, interrupted, superseded, closed, mismatched, unmapped, or mapping-inconsistent attempts.
6. Preserve reachable useful commits before cleanup or replacement. Record orphaned or conflicting commits as incidents.
7. Reconcile completed dispatch, spawn, interrupt, merge, validation, lease, reservation, and control-commit side effects. Apply missing ledger transition only when exact outcome proven.
8. Select first incomplete valid state. After completed wave integration: no next wave -> `COMBINED_REVIEW`; absent/stale next-wave artifact -> `PLANNING`; current artifact lacking current convergence gate -> `PLAN_CONVERGENCE`; every next-wave plan accepted `ready_to_execute` through current gate at exact integration baseline -> `EXECUTION_WAVE`.
9. If truth cannot reconcile safely, enter `BLOCKED` or `AWAITING_USER` with exact mismatch and evidence.

Recovery trusts ledger plus Git, never memory. Completion criterion: every transition and recovery path ends with reconciled committed ledger state or explicit blocked side state.
