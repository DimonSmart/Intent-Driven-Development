# IDD Factory Root (internal)

This is the **internal** Factory Root protocol, loaded by a fresh generic native
child of the public `idd-factory-run` Supervisor. It owns the existing Factory
preflight, planning, implementation, provenance, execution policy, and verification.
Do not invoke this Skill as a public command or automatically start a new Factory
run merely because it was loaded. A complete bounded Supervisor assignment and
verified current run mode are required.

Factory is a thin semantic orchestration skill over native child-agent
capabilities. It is not a workflow engine, process runtime, MCP transport,
retry engine, or durable implementation database.
This Root NEVER spawns another Root. Its parent Supervisor does that.

## Mandatory bounded batch assignment

Accept only a fresh native Supervisor child assignment from the same installed
Factory version. **Before any project mutation**, require:

- absolute shared repository path and explicit mode (New, Continue, Answer,
  Cancel or Restart);
- positive `batch_ordinal` and `max_workers_per_batch` from Supervisor;
- complete unchanged New/Restart request including attachments, exact persisted
  request path on Continue, or applicable exact Q&A/recovery context;
- repository/Skill access, authorized Preflight or verified phase-specific
  baseline/provenance.

If missing, ambiguous or contradictory, return `Status: BLOCKED`,
`CompletedWorkers: 0` and a concise reason. Never infer request identity
from a summary. Each fresh Root starts `completed_workers = 0`;
batch counts/ordinals are transient and never written to Factory state.
Read normative references from this installed Root package and verify
Planner/Worker versions match.

## Required references

Read `references/intent-preflight.md` before starting or replacing a Factory
run. Read `references/engineering-guardrails.md` before using an optional
`.idd/engineering/` layer. Read `references/project-verification.md` before
project verification. Read `references/factory-execution-policy.md` before
interpreting `ExecutionProfile` or `.idd/execution.yaml`.

## Required host capabilities

Require native fresh children without inherited transcript, shared project
and installed Skill access, bounded assignments and reliable blocking
terminal results. Planner is read-only; native read-only sandboxing is
preferred when available. The host owns emergency interruption and OS
process cleanup, which are not preflight Factory gates. No fallback runtime,
MCP transport or polling loop.

## Installed internal Skills and generic child-agent transport

Resolve the **real installed absolute path** of this Root Skill and the
sibling `skills/idd-internal-factory-planner/SKILL.md` and
`skills/idd-internal-factory-worker/SKILL.md` in the same installed
`idd-factory` package. Verify each Skill and required references before
durable writes. Do not guess plugin caches, use checkout copies or read,
copy, quote or relay the full child protocols.

Inspect the actual native child-agent schema. Require fresh generic children
without inherited transcript, installed Skill/repository access, bounded
assignment, correlated trustworthy native terminal waits. Factory-specific
`agent_type` is not required.
Check model/effort override support only for a Worker whose explicit policy
mapping needs it. Prefer a real read-only sandbox for Planner.

Spawn with `fork_turns: "none"` (or equivalent), unique native task names,
the absolute installed child Skill path and an instruction to read its
**entire** Skill/references before project work. Supply only the phase-specific
bounded packet, not parent/other child transcripts, entire Intent trees or
protocol bodies. Invalid Skill, reference or packet returns BLOCKED.

An elapsed native wait interval alone is not a child failure. Re-arm a
long native wait for the SAME active Planner/Worker; do not interrupt, replace
or start another Writer solely because `timed_out: true` was returned.
On a genuine host failure, explicit interruption or unconfirmed termination,
preserve the pending Task and end as BLOCKED / CHILD_UNCONFIRMED if native
waiting cannot safely continue. The host owns descendant/OS process cleanup;
no shell kills, process-tree tracking, short status polling or retry engine.
Later explicit recovery reconciles saved state and requires no active Writer.

## Factory Preflight — Root entrypoint

`references/intent-preflight.md` is the sole normative contract for request
identity, modes, scope, owner actions, Git baseline/provenance, recovery,
questions, activation and replacement. Read it in full. Root alone owns
Preflight; Supervisor only dispatches.

For New, detect request-level blockers before writes, store preparation before
Engineering then Product owner mutations, verify durable coverage and run the
first Planner while inactive. Only validated Tasks activate Factory by writing
`current/request.md` LAST. Question, Done, BLOCKED or invalid output never
activates. For other modes use the reference's distinct reconciliation and
authorization rules; never replay uncertain changes, reset baseline,
apply New-run cleanliness gates to Continue or undo durable changes.

## Minimal temporary state

The presence of `.idd/factory/current/` alone is not active state.
Only `.idd/factory/current/request.md` is the authoritative active-run
marker. One pending preparation per working tree is sufficient:

```text
.idd/factory/preparation/
    request.md               # full materialized base request
    baseline.md              # immutable original commit and repository identity
    provenance.md            # bounded owner/Worker attribution checkpoints
    answers.md               # chronological exact question/answer pairs, optional
    question.md              # outstanding exact question, optional
    verification-failure.md  # bounded final-check diagnostic, optional

.idd/factory/current/
    plan.md                  # validated remaining Task batch
    baseline.md              # unchanged original baseline
    provenance.md            # preflight, Worker and unknown-change boundaries
    answers.md               # exact answers transferred from preparation, optional
    request.md               # active marker, written LAST
    completed.md             # active-run summaries, as needed
    question.md              # active-run question, optional
    verification-failure.md  # active-run failure, optional
```

A baseline commit is not a snapshot of the entire working tree: pre-existing
dirty code/tests are allowed and must not be attributed to Factory. Temporary
baseline and provenance are diagnostic, not normative or new active markers.

The repository is the authoritative implementation reality. `plan.md`
contains only the remaining implementation/research tasks. `completed.md`
contains bounded semantic summaries, not proof of completed code. Exact Q/A
are kept separately from the unchanged base request. The preparation directory
is ignored by Git through the existing `.idd/factory` rule.

`.idd/execution.yaml`, when present, is deliberately outside
`.idd/factory/current/`. It is project-owned execution policy that survives
Factory runs, not temporary Factory state or workflow/runtime configuration.

Do not introduce retry counters, process IDs, new lifecycle statuses or a
universal Git log. Only bounded path/hash/hunk provenance needed for recovery
and review is permitted; Git remains the sole normative history.

If a legacy `.idd/factory/current/state.json` exists, do not deserialize,
migrate, or continue its old state machine. Preserve product changes. An
explicit restart may reuse a valid self-contained legacy `request.md` as the
replacement request and may archive the old directory for diagnostics.

## User-level operations

Follow `references/intent-preflight.md`:

- **New:** prepare durable knowledge and activate only a validated Task batch.
- **Continue:** reconcile saved request, exact Q&A, Git and provenance.
- **Answer:** require a saved question, append exact Q&A, then replan.
- **Cancel:** require no known ongoing Factory operation or uncertain owner action;
  return `COMPLETED / CANCELLED` only after safe cleanup, preserving uncertain
  evidence and without undoing changes or claiming implementation completion.
- **Restart:** reconcile state BEFORE replacing it; preserve the active run
  until authorized replacement preparation succeeds.

Automatically reconcile only unambiguous outcomes. Genuine user-owned
ambiguity returns NEEDS_INPUT; uncertain writer termination returns BLOCKED.
No new lifecycle state or retry counter.

## Original Git baseline and bounded provenance — Root operations

Use `Original Git baseline and preflight provenance`, `Request identity`
and `Replacement semantics` in `references/intent-preflight.md`. Its
NUL-delimited managed-IDD Git status gate and real `HEAD^{commit}`
apply to New only; the original baseline never moves on continuation.
Dirty unrelated code alone is not a blocker.

Persist bounded pending provenance before each mutating owner/Worker;
record authorized before/after evidence only after trusted completion.
`external-unknown` or partial activation preserves recovery evidence:
never blindly replay an owner, reset a baseline, undo changes, or infer
an active run without `current/request.md`.

## Preparation planner and activation — Root operations

After approved durable coverage and saved preparation, spawn fresh read-only
Planner with full request, exact Q&A, repository, phase and verified
baseline/provenance. Validate the **entire correlated terminal result**:

- `# Question`: save exact question and await answer. Renew authorized
  owner changes and coverage, then run a fresh Planner.
- `# Done`: final verification while inactive, with bounded failure evidence
  and replanning if needed. Root never repairs directly.
- `# Task`: require 1–4 complete new Tasks, one valid ExecutionProfile each,
  current Intent/Conditional Engineering IDs and freshness. Do not truncate
  or auto-retry invalid plans. Longer saved legacy plans remain resumable.
- `BLOCKED`, malformed or untrusted result: retain preparation, no activation.

For Tasks, transfer plan, Q&A, baseline and provenance before writing
`current/request.md` LAST. Reconcile partial activation conservatively.

## Per-Root worker checkpoint target and safe plan boundary

The Supervisor supplies an effective positive `max_workers_per_batch`
(default **8**) for THIS Root only. It is a **minimum target for normal
rotation**, not a hard maximum. Start `completed_workers = 0` in each
fresh Root even when resuming an earlier Root's incomplete plan. Finish all
remaining tasks in the current validated plan before normal rotation,
even when the local count has reached or exceeded the target. A new
Planner may return only 1–4 Tasks, but a previously persisted plan with
more than four Tasks remains eligible for existing recovery/continuation
and MUST NOT be truncated solely due to this new limit.

Increment `completed_workers` ONLY after the existing trusted checkpoint:

1. Persist pending provenance before each Worker; wait for its genuinely
   successful normal terminal native result (not an abort or timeout).
2. Verify the Worker after-state, authorized scope, immutable baseline and
   bounded provenance. A process exit or changed files alone is not proof.
3. Append the verified bounded summary to `completed.md` and remove its
   Task from `plan.md` in the existing safe checkpoint order.
4. Confirm the pending checkpoint is closed; all corresponding provenance,
   `completed.md` and remaining `plan.md` entries agree; that Task
   cannot be replayed in an ordinary Continue; no Writer is known active.
5. ONLY NOW increase the Root-local counter by one. Never count a Planner,
   unsuccessful/interrupted Worker, project verification or other action.

After EVERY Worker checkpoint, proceed with any remaining tasks of the
CURRENT plan regardless of the counter. Only after its last Task, confirm
that **the whole plan is finished**: all Workers returned trusted normal
successful native terminals; no Task or pending checkpoint remains;
`completed.md`, `plan.md` and `provenance.md` agree with those results
and no contradictory external-unknown changes remain. An empty `plan.md`
ALONE is not sufficient. Root owns this complete checkpoint verification;
the Supervisor cannot and must not reconstruct it from nested traces or
revalidate the plan/provenance files.

At this verified plan boundary:
- If `completed_workers >= max_workers_per_batch`, do NOT start
  another Planner, Worker, final verification or Intent Refinement Review
  in this Root. Preserve the active state and return exactly:

      Status: CONTINUE
      CompletedWorkers: <actual confirmed successful Workers in this Root>
      Reason: BATCH_LIMIT

- If the target has not been reached, continue normal flow with a fresh
  Planner (which may return `# Task`, `# Question` or `# Done`).

When a Root begins with an empty pending plan and its local count is zero,
it invokes a fresh Planner normally. Finishing a plan exactly at the
target rotates before another Planner; the next Root may obtain `# Done`
and finalize. Do not create an extra Root after ordinary `COMPLETED`
when the target was never reached.

An unsuccessful Worker, untrusted/incomplete checkpoint, invalid or stale
remaining task, unknown writer status, missing capability or insufficient
Root context must follow the EXISTING safe BLOCKED/NEEDS_INPUT recovery
rules, NOT BATCH_LIMIT, even when the target has been reached. Preserve
the remaining Tasks and confirmed checkpoints; never claim completion or
force an invalid Task merely to finish the plan. Uncertain native completion
forbids any subsequent writer/Root in this invocation. Do not automatically roll back,
replace state, retry, or introduce a plan transaction. The at-least-once
execution/reconciliation semantics remain unchanged.

For Planner `# Done` before reaching the target, run the existing Project
Verification, Intent Refinement Review and normal closure. Do not loop
indefinitely after an identical unverified/failed final check without a
trusted change; return BLOCKED/VERIFICATION_NOT_CONFIRMED instead.

## Strict terminal result for Supervisor

A finished internal Root MUST return exactly one each of these separate
plain-text fields in its genuine final child message:

    Status: CONTINUE|COMPLETED|NEEDS_INPUT|BLOCKED
    CompletedWorkers: <nonnegative actual confirmed Root-local integer>
    Reason: <short nonempty reason>

CompletedWorkers may exceed the target for ANY terminal status when
supported by actual confirmed Worker checkpoints in this Root.
CONTINUE is legal ONLY after full **plan-boundary** checkpoint with
`CompletedWorkers >= max_workers_per_batch > 0` and Reason BATCH_LIMIT.
Require the active Factory marker, no Tasks remaining from the current
plan, no pending checkpoint and trusted normal Worker terminal results;
neither terminal text nor an empty plan proves this alone. COMPLETED has two
mutually exclusive meanings: Reason DONE requires existing `# Done`, verified
Project Verification, Intent Refinement Review and normal finalization;
Reason CANCELLED requires explicit Cancel, no known ongoing Factory/owner
operation and safe temporary-state cleanup, without claiming implementation
completion or requiring Planner `# Done`/final verification. NEEDS_INPUT must preserve a saved question and
provide an additional `QuestionFile: .idd/factory/preparation/question.md`
or `QuestionFile: .idd/factory/current/question.md` when actually present,
and may omit that field only for a request-level question before any
preparation write. BLOCKED must state a specific reason, such as
HOST_UNSUPPORTED, CHILD_UNCONFIRMED, CHECKPOINT_INCONSISTENT,
VERIFICATION_NOT_CONFIRMED or PREFLIGHT_DECISION. Never emit conflicting
intermediate terminal fields, a false completed result, a full transcript,
unbounded logs or internal Skill bodies.

The fields are Root's attestation of its own confirmed checkpoints. Supervisor
checks the correlated successful native Root terminal, field syntax/count and
active request marker; it does not certify individual Worker outcomes or
reinspect `plan.md`, `completed.md` or `provenance.md`.
They describe this Root's local successful Workers, not historical
completed entries from earlier Roots. Do not generate CONTINUE from a
saved `completed.md` count or from empty tasks; count only completed and
verified Workers launched in this native Root. Preserve all the existing
durable files and phase semantics. No Supervisor-specific state files or
retry counters.

## Orchestration loop

Root runs one Planner/Worker step at a time:

1. With no executable Task left, verify the full plan boundary. Return
   `CONTINUE / BATCH_LIMIT` at the Worker target **before** another
   Planner; otherwise spawn a fresh Planner and validate its terminal form.
2. A validated Question suspends, Done triggers final verification, and
   valid Tasks become the next saved plan; failure preserves state.
3. For the first remaining Task, resolve scope, Engineering and
   ExecutionProfile, persist pending provenance and spawn one fresh Worker.
4. After trusted normal Worker completion, reconcile the after-state,
   commit the complete Task checkpoint and increment the
   local Worker counter.
5. Finish all Tasks of the same Planner plan before rotating, regardless
   of count. Never overlap writing Workers or rotate on partial evidence.

This is orchestration guidance, not a separate runtime or state machine.

### Codex MultiAgentV2 spawn identity

Codex uses the Supervisor-assigned `idd_factory_root_bNNNN` batch ordinal
for all descendant native `task_name` values, e.g.
`idd_factory_planner_b0001_0001` and
`idd_factory_worker_b0001_0001`. The suffix identifies a LOCAL attempt;
rejected attempts consume names, and names never repeat in visible host
history. Historical flat names are reporter-only.

Pass `task_name` in spawn arguments, **not** in the child's Task packet.
Every Planner/Worker spawn explicitly sets `fork_turns: "none"`, uses the
absolute installed Skill path, bounded assignment, and correlated native
parent/child results. A task name is not proof of Skill load, completion
or model selection. Explicit model/effort overrides must be honored where
configured; never substitute a model or switch execution backend.

### Fresh planner

Spawn a generic native child with a new context and no inherited history.
Pass the absolute installed `idd-internal-factory-planner/SKILL.md` path,
full original request (or its accessible persisted path), preparation/active
phase, chronological exact Q/A, original baseline, verified provenance,
repository root and phase-specific optional completed summaries/verification
failure. The child reads its entire installed Skill as its first substantive
action, validates assignment completeness and required references, then
semantically selects relevant current Intent and Conditional Engineering.
The Planner is read-only: it must not mutate project or Factory state, read
execution policy, or inherit Root/other agent transcripts. Use native read-only
permissions when available. Do not require active-only files in preparation.
Root does not copy the protocol, full Intent tree or prior transcripts.
A missing/unreadable Skill or invalid packet gives `BLOCKED` without planning.

### Worker execution profile

Immediately before each Worker spawn, perform the complete mechanical
lookup and validation in `references/factory-execution-policy.md`.
New Tasks require exactly one `ExecutionProfile`; an existing saved Task
without it defaults to `standard` without rewriting the plan.
Missing policy/mapping means inherit host model and reasoning settings,
omitting native overrides without testing their support. For an explicit
mapping require its native model and any effort argument actually configured;
malformed or unsupported settings block this Worker without substitution.

Root alone maps the profile immediately before spawn. Planner and Worker
do not inspect `.idd/execution.yaml`; all profiles use the same Worker Skill.

### Fresh worker

Immediately before every Worker spawn, verify the installed internal Worker
Skill path and material current Intent/Engineering constraints, request/answer
freshness and baseline/provenance. Replan stale Task batches. Prepare exactly
one complete self-contained Task and the current TaskRelatedIntent,
Planner-selected Conditional TaskRelatedEngineering, mechanically enumerated
AlwaysEngineering, authorized scope and explicit user prohibitions. Resolve
exact stable IDs and provide accessible project/context paths. Do not create a
second `# Task` header in the bootstrap.

Spawn a fresh generic child with no inherited transcript. Pass the absolute
installed `idd-internal-factory-worker/SKILL.md` path and instruct the child
to read it entirely first, validate the one-Task packet and required references,
then implement only that Task. The Skill is the Worker's protocol; Root never
reads or relays its body. The worker never selects a model or reads execution
policy. For an inherited mapping omit model/effort overrides without checking
whether the host supports them. For an explicit mapping, require only the
native model argument and any effort argument actually configured; block
that Worker if the host cannot honor either. Never silently substitute.

Use this bounded task structure (omit empty optional sections):

```text
--- Factory worker assignment ---
# Task
<exactly one complete Task>
# TaskRelatedIntent
<selected IDD-NNNN identifiers>
# TaskRelatedEngineering
<planner-selected Conditional ENG-NNNN identifiers>
# AlwaysEngineering
<all current Always ENG-NNNN identifiers, if any>
--- End Factory worker assignment ---
```

Each Worker mechanically resolves its supplied Intent/Engineering IDs, performs
focused task-local checks, and returns a short semantic result. Workers share
the working copy and execute sequentially. A failed Skill load or guard keeps
the Task pending; no replacement Writer starts on uncertain completion.

### Waiting and interruption

Use native blocking/event-driven terminal waits for Planner and Worker; do
not wake the Root for frequent model-driven progress checks. In Codex request
`wait_agent(timeout_ms: 600000)` (10 minutes) when supported, otherwise the
longest blocking interval supported by the actual host schema. Prefer genuine
event-driven indefinite waiting if available. This is one wait interval, not
a Planner/Worker execution deadline.

A wait returning `timed_out: true`, or a completed wait tool call without a
correlated successful child terminal, does NOT establish child completion,
failure or cancellation. If the same child is still running, re-arm its
long native wait, keeping every other Writer stopped. A timeout by itself
must NEVER call `interrupt_agent`, reset a Task, launch a new child or end
as `CHILD_UNCONFIRMED`. Do not use rapid status polling, shell loops or
an alternate process runtime to implement waiting.

Only on a genuine wait/transport failure, explicit user cancellation,
confirmed abnormal child termination or lost/unverifiable native child
identity, stop starting Writers. Request host-native interruption only when
a stop is actually required; never use timeout alone as justification.
Preserve the pending Task, provenance and actual Git changes. If waiting
cannot safely be re-established, end this invocation as
`BLOCKED / CHILD_UNCONFIRMED` with a concrete reason and no replacement
Writer. The host owns exceptional cancellation and OS process cleanup.

A later **explicit** Continue/Restart/Cancel requires no ongoing host
operation and reconciles the actual repository with saved checkpoints.
If an old Writer might still be active, remain BLOCKED and request manual
host inspection. A fresh Worker may retain correct partial work and finish
the pending Task; a new Planner may replan only after reconciliation.
For a genuine blocker, keep Root's strict terminal fields unchanged and
make `Reason` specific enough for Supervisor to explain the safest user
options without claiming knowledge of an unconfirmed child.

## Technical autonomy during execution

Planner selects compliant technical approaches, relevant Conditional
Engineering Rules and bounded research; Worker makes local decisions within
its Task and mandatory Rules. Root only resolves stable IDs and mechanically
enumerates Always Rules before every Worker. Detailed authority and discovery
rules live in `references/engineering-guardrails.md` and the child Skills.
Root never invents new Product/Engineering decisions or broadens Task scope.

## Planner outcomes

Use the **Planner Skill's authoritative output contract** and
`references/intent-preflight.md`. Trust only the complete correlated native
terminal result: 1–4 valid new Tasks (with ExecutionProfile and current
Intent/Conditional Engineering IDs), one Question, Done, or diagnostic
`BLOCKED`. Legacy saved plans with more than four Tasks remain resumable.

A Planner BLOCKED maps to `PLANNER_BLOCKED`; malformed, empty, mixed,
incomplete or oversized output to `PLANNER_PROTOCOL_INVALID`; unconfirmed
child/writer termination to `CHILD_UNCONFIRMED`. Preserve phase and pending
Task, do not truncate, repair or retry automatically. Only an actual
Question triggers saved exact Q&A; Done triggers project verification.

## Project verification and completion

On genuine Planner `# Done`, apply `references/project-verification.md`
in `final` context. Workers perform task-local checks separately.
If any assigned check fails or is unverified, save bounded
`verification-failure.md` (check, exit result, relevant diagnostic),
then invoke a fresh Planner. Root never repairs failed checks itself,
retries unchanged failures or invents a verification engine.
An unchanged unverified failure blocks with `VERIFICATION_NOT_CONFIRMED`.

On verified completion, record Implementation Result, perform the Intent
Refinement Review below and remove `current/request.md`, preserving
necessary ignored `.idd/factory/results/` review evidence. If no project
verification is configured, `# Done` after ordinary Worker checks
suffices per the referenced policy.

## Intent Refinement Review

After final verification, re-read committed and uncommitted changes against
the immutable baseline, exact owner authorizations, preflight boundary,
Worker checkpoints and current Git status. Separate authorized preflight,
later authorized Intent workflow changes, candidate monotonic Worker edits,
and external/unknown changes. Unknown origin stays unknown and cannot become
approved merely because it was committed. Explain durable verified findings,
changed IDD paths, why the approved contract remains equivalent and how to
inspect the Git diff. No refinements => no empty approval ceremony.

Offer normal user/Git review choices: accept all or selected refinements,
reject isolated edits, request correction or defer. Implementation Result
and Intent Refinement Review are independent: pending valid refinements do
not fail a verified implementation, but an unresolved normative conflict does.
For selective rejection, re-read current files, check independent edits
sharing a hunk and code dependencies, then remove only safely isolated
changes and rerun affected checks. Never `git reset --hard`, blindly
restore a whole file or revert an unrelated commit. If safe separation is
impossible, stop with an explicit conflict and controlled correction.

Deferred context may be kept in ignored `.idd/factory/results/` with
baseline/authorization/necessary hunks; it is machine/worktree-local and
depends on available Git objects. Review later must revalidate current
patch applicability. Do not create a permanent approval DB or leave an
active `current/request.md` for deferred review.

## Design boundary

Semantics and applicability belong to agents; mechanical ID resolution,
Always-rule enumeration, validation, persistence and policy lookup belong
to deterministic host operations. Product mutations belong to authorized
Intent owners, Engineering Rule mutations to `idd-engineering-change`.
Planner is read-only; Worker may make only proven monotonic refinements
to existing Product Intent. Introduce no lifecycle state, retry budget,
runtime or recovery subsystem without an independently approved contract.
