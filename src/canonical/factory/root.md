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

The internal Root is not a user-facing command. BEFORE any project mutation,
require a native fresh generic-child invocation from the installed Supervisor
with an exact full Skill read, a known compatible same-package methodology
version, and a self-contained bounded assignment containing:

- absolute shared project repository root and original entry mode
  (New / Continue / Answer / Cancel / Restart);
- this Root's positive integer `batch_ordinal` and explicitly supplied
  positive integer `max_workers_per_batch` (Supervisor default is **8**);
- for New/Restart the complete original request including contents of textual
  attachments, or the exact unchanged current/request.md path for subsequent
  Continue, or the complete applicable pending-recovery/user-answer context;
- exact project access, required references and verified phase-appropriate
  baseline/provenance, or explicit instructions to perform the authorized
  new-run Preflight before any state mutation.

Missing, inconsistent, unverifiable or contradictory data means
`Status: BLOCKED`, `CompletedWorkers: 0` and a short diagnostic. Do not
improvise the original request or infer an entry mode from a truncated prompt.
A fresh Root starts `completed_workers = 0` regardless of earlier
`completed.md` entries. The counter and batch ordinal are transient native
assignment data, **not** additional durable state. Never persist them to
`.idd/factory/` or `.idd/execution.yaml`.

The Root independently checks and loads its mandatory normative references
from its own installed internal Root Skill directory, not from the public
Supervisor Skill. Recheck that the currently installed Planner/Worker Skills
come from this same version of the package. Never read, quote or pass the
complete Planner/Worker protocols in their spawn assignments.

## Required references

Read `references/intent-preflight.md` before starting or replacing a Factory
run. Read `references/engineering-guardrails.md` before using an optional
`.idd/engineering/` layer. Read `references/project-verification.md` before
project verification. Read `references/factory-execution-policy.md` before
interpreting `ExecutionProfile` or `.idd/execution.yaml`.

## Required host capabilities

Factory requires the platform adapter to provide all of these properties:

- spawn a child agent in a fresh isolated semantic context;
- explicitly avoid inheriting the parent transcript;
- give the child access to the current repository;
- wait for a terminal child result without model-driven polling;
- receive the final child result;
- stop or close a child when necessary.

Use a read-only planner and workspace-writing workers when the platform can
express those sandboxes natively. If the host cannot provide the required
properties, Factory is unsupported on that host. On Windows Codex, disabling
`unified_exec` BEFORE launching the host is a candidate one-shot execution
workaround, not a proven fix: it MUST pass the real Writer-command stop
gate for the exact binary/profile before any write-capable two-level run.
`turn_aborted` or a stopped Agent alone does not establish process
termination. Do not recreate the removed
C# runtime, MCP transport, shell process supervisor, status protocol, or polling
loop to compensate.

A new thread is not sufficient if it automatically receives the parent history.

## Installed internal Skills and generic child-agent transport

The Factory Planner and Worker protocols are shipped as
`skills/idd-internal-factory-planner/SKILL.md` and
`skills/idd-internal-factory-worker/SKILL.md` inside the SAME installed
`idd-factory` plugin as this `idd-internal-factory-root/SKILL.md`. These are internal
instruction Skills, not registered custom-agent types or separate processes.

Before any new durable owner/Factory write, resolve this currently loaded
internal Factory Root Skill to its real installed absolute path; derive its sibling
Planner/Worker Skill paths from that exact installed plugin root. Do not guess
plugin cache versions, search globally by name, use the source checkout or
assume the project working directory is the plugin root. Check both SKILL.md
files exist and are accessible, and verify any statically declared mandatory
references. Root may inspect paths, file attributes and package metadata, but
MUST NOT read, summarize, quote or relay the full child protocols.

Inspect the actual host-native child-agent schema. Require fresh context
without inherited history, installed Skill and repository access, bounded
assignment, terminal wait/result, and reliable writer stop. Do not require
model/effort overrides at Root entry; check those native arguments only
immediately before a Worker whose resolved policy mapping explicitly needs
them. Absence of a Factory-specific `agent_type` argument is NOT a failure.
If the host requires a type, use its builtin generic child type, not a
custom registered Factory role. A per-child read-only sandbox for Planner is
preferred, not mandatory; a textual rule is not a technical sandbox.

Spawn ordinary native children with explicitly disabled transcript inheritance.
For Codex use `fork_turns: "none"` and unique `task_name` arguments as
specified below. In EVERY spawn message include the semantic Planner/Worker
role, the concrete accessible absolute path to that role's installed SKILL.md,
an explicit instruction to read ALL of it (and its required references) as
its first substantive action, the explicit project working-copy path, and
only the phase-appropriate bounded assignment. Parent and child must refer
to the SAME installed plugin version. Quote paths with spaces; use real paths
supported by the host on Windows and Unix. Partial/truncated file reads must
be continued until complete; no independent project work before loading and
guarding the assignment. The child returns `BLOCKED` when Skill, references,
phase, baseline/provenance or required assignment is missing or ambiguous.
It must not improvise a protocol from memory.

Root never injects protocol bodies, the whole Intent/Engineering tree, previous
worker conversations or the parent transcript. Root performs required
Engineering enumeration and execution policy mapping as before. If a child
fails to load a Skill after valid preparation, preserve the existing phase
state and pending Task; never declare successful completion. On wait failure, explicitly interrupt each active child and its descendants
deepest-first via native tools, and confirm Writer command/process quiescence
before another write. Interrupting an agent is not itself proof that its
in-flight command stopped. If any active child or writing process cannot be
identified, directly interrupted and confirmed stopped, preserve the pending
Task and return BLOCKED / CHILD_UNCONFIRMED; do not start a replacement Worker
or let the Supervisor rotate Root. No status polling loop or runtime fallback. These are checks for necessary host capabilities, not a requirement
to prove a future child has already read its Skill before spawning it.

## Factory Preflight — Root entrypoint

`references/intent-preflight.md` is the sole normative contract for request
identity, modes, scope, owner calls, durable coverage, baseline, preparation,
recovery, questions, activation and replacement. Read it **in full** before
starting, recovering or replacing a run. Root owns Preflight; Supervisor only
dispatches it.

Before any durable owner/Factory write, verify native child capabilities and
the same-version installed Planner/Worker Skills. A missing registered
`agent_type` does not block. Direct invocation without `idd-route` does not
bypass scope restrictions.

For New, follow the reference's ordered phases: classify the full request and
request-level blockers **before writes**; check managed IDD cleanliness and
real original Git HEAD; save complete `preparation/` request, baseline and
provenance **before** mutating owners; checkpoint Engineering owner before
Product owner; validate durable coverage; spawn a fresh initial Planner while
inactive. Write `current/request.md` LAST only after a validated Task batch.
Question, Done, BLOCKED, malformed Planner results or unresolved owner actions
never activate Factory.

For Continue, Answer, Cancel and Restart, follow the reference's distinct
recovery and authorization rules. Never apply the new-run cleanliness gate
to continuation, silently combine requests, replay uncertain owner actions,
reset the original baseline, or roll back durable changes.

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

Apply the semantic mode contracts in `references/intent-preflight.md`
to the Supervisor's New, Continue, Answer, Cancel or Restart assignment:

- **New:** materialize the full request, perform durable Preflight, and
  activate only on a verified Task batch.
- **Continue:** recover active state or saved preparation from exact
  request, Q/A and provenance; incomplete preparation never authorizes
  reconstruction from an old transcript.
- **Answer:** require a saved `question.md`; bind exact answer to that
  question and unchanged request before replanning.
- **Cancel:** confirm all descendants, writing commands and pending owners
  stopped; if their outcomes are uncertain, preserve recovery evidence.
  After safe cleanup return `Status: COMPLETED` and `Reason: CANCELLED`,
  without claiming implementation completion or undoing durable writes.
- **Restart:** FIRST reconcile saved request, baseline, provenance, pending
  owner actions and repository reality before discarding preparation.
  Preserve an active run until authorized replacement Preflight succeeds.
  If ambiguous, return NEEDS_INPUT with the precise choice; do not replay
  uncertain owner actions.

Automatically reconcile only one proven safe outcome. User-owned ambiguity
uses existing `question.md`/NEEDS_INPUT; uncertain writer termination is
BLOCKED, never destructive cleanup.

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

Use `New-run ordering`, `Preparation questions and renewed coverage`
and `Request identity` from `references/intent-preflight.md`.
Root performs these concrete dispatch and state operations:

1. After approved coverage and saved
   `preparation/{request,baseline,provenance}.md`, spawn a fresh Planner
   with full request, exact Q/A, repository, preparation phase and bounded
   verification context. Before any write, validate its entire genuine
   terminal result using `Planner outcomes` below. No active marker
   or `completed.md` exists yet.
2. `# Question`: save exact `preparation/question.md`, stop without
   activation. On Answer, append exact chronological Q/A, renew authorized
   owners/coverage, then spawn a NEW Planner.
3. `# Done`: perform `final` verification without activation; on
   success remove preparation. On failure record a bounded
   `preparation/verification-failure.md` and invoke a fresh Planner,
   never run a repair in Root.
4. `# Task`: validate the *entire* new executable batch, 1–4 Tasks,
   exactly one valid ExecutionProfile per Task, current Intent and
   Conditional Engineering IDs and repository freshness. Five or more,
   malformed or stale Tasks block; do not truncate or automatically retry.
5. Reconcile partial inactive files, transfer validated plan, Q/A,
   original baseline and provenance, then create `current/request.md`
   **LAST** before the Worker loop.

Old saved plans over four Tasks remain valid for conservative recovery.
Legacy implementation-only scope and uncertain provenance do not authorize
new durable Product/Engineering decisions.

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
   successful terminal native result and stopped writing descendants.
2. Verify the Worker after-state, authorized scope, immutable baseline and
   bounded provenance. A process exit or changed files alone is not proof.
3. Append the verified bounded summary to `completed.md` and remove its
   Task from `plan.md` in the existing safe checkpoint order.
4. Confirm the pending checkpoint is closed; all corresponding provenance,
   `completed.md` and remaining `plan.md` entries agree; that Task
   cannot be replayed in an ordinary Continue; no writer remains alive.
5. ONLY NOW increase the Root-local counter by one. Never count a Planner,
   unsuccessful/interrupted Worker, project verification or other action.

After EVERY Worker checkpoint, proceed with any remaining tasks of the
CURRENT plan regardless of the counter. Only after its last Task, confirm
that **the whole plan is finished**: all its necessary Workers succeeded
and have stopped; no plan Task remains, no pending checkpoint is open,
`completed.md`, `plan.md` and `provenance.md` agree with trusted native
results, no contradictory external-unknown changes remain and all writing
descendants are quiescent. An empty `plan.md` ALONE is not sufficient.

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
force an invalid Task merely to finish the plan. Native stop uncertainty
forbids any subsequent writer/Root. Do not automatically roll back,
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
plan, no pending checkpoint and confirmed termination of writing descendants;
neither terminal text nor an empty plan proves this alone. COMPLETED has two
mutually exclusive meanings: Reason DONE requires existing `# Done`, verified
Project Verification, Intent Refinement Review and normal finalization;
Reason CANCELLED requires an explicit Cancel request, confirmed stopped
writers/owners and safe temporary-state cleanup, without claiming implementation
completion or requiring Planner `# Done`/final verification. NEEDS_INPUT must preserve a saved question and
provide an additional `QuestionFile: .idd/factory/preparation/question.md`
or `QuestionFile: .idd/factory/current/question.md` when actually present,
and may omit that field only for a request-level question before any
preparation write. BLOCKED must state a specific reason, such as
HOST_UNSUPPORTED, CHILD_UNCONFIRMED, CHECKPOINT_INCONSISTENT,
VERIFICATION_NOT_CONFIRMED or PREFLIGHT_DECISION. Never emit conflicting
intermediate terminal fields, a false completed result, a full transcript,
unbounded logs or internal Skill bodies.

The fields describe this Root's local successful Workers, not historical
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
4. After trusted native completion and confirmed writer stop, reconcile
   after-state, commit the complete Task checkpoint and increment the
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
the Task pending, with no replacement writer until termination is confirmed.

### Waiting and interruption

Use the platform's longest appropriate blocking or event-driven native wait.
Worker duration by itself must not create repeated parent model turns.

If native waiting ends with a host timeout or error:

1. do not start another Worker for the same Task or begin status polling;
2. identify the exact active child thread and any descendants via native tools;
3. interrupt each active descendant deepest-first, then the direct child;
4. require native termination evidence AND proof that every in-flight writing
   command/process has stopped; `interrupt_agent` alone is insufficient;
5. leave the Task first in `plan.md` and end the invocation. If command
   termination is unavailable or uncertain, return BLOCKED / CHILD_UNCONFIRMED.

A completed shell command after a cancellation request does NOT retroactively
prove safe cancellation. Never run a replacement writer while the previous
child or any started command may still be writing to the same workspace.
Do not create a process supervisor, issue shell kills or add polling/retries.

Factory deliberately uses at-least-once execution. A later fresh worker must
inspect current repository reality, finish partially completed work, and avoid
reverting correct existing changes merely because an interrupted execution may
have produced them.

## Technical autonomy during execution

Planner can select a suitable compliant implementation direction, delegate
bounded technical choices to Worker, or contract focused research with
observable evidence. A fresh Planner consumes research results from the
repository before dependent Tasks are contracted. Multiple viable techniques
alone do not justify `# Question`; ask only for a genuine user-owned
product, normative, trust, compatibility or external-obligation decision.

When realistic alternatives left to Worker activate different Conditional
Rules, Planner semantically supplies the relevant `TaskRelatedEngineering`
IDs *before* Worker spawn, or first narrows the Task/researches the decision.
Conditions remain conditional. The root only resolves IDs and enumerates
Always Rules; no heuristic Conditional rule selection is introduced.

Worker chooses local implementation methods inside the Task and supplied
Rules. If rules are insufficient for the selected approach or required
constraints conflict, Worker reports a precise blocker and cannot widen
the packet, write `# Question`, alter Task, edit Engineering or approved product decisions or
remove/rename the active marker. Factory handles further planning/stopping. Worker may add only permitted
monotonic refinements to existing Product Intent owners.
A new Engineering policy remains forbidden during an active run.


## Planner outcomes

For the three normal `# Task`, `# Question`, `# Done` forms and
pre-activation handling, use `references/intent-preflight.md` and
`Preparation planner and activation` above. Planner also has a distinct
diagnostic failure form, exactly `BLOCKED` on its first line and one
nonempty single-line `Reason: <diagnostic>` on its second line, with no
other outcome sections. It is not a fourth planning outcome, a user
question or a completed request.

For each fresh Planner in preparation OR active phase, first confirm the
correlated native child terminal result, read its entire final output and
validate the complete form. A valid Planner `BLOCKED` means no task
contract was produced: do not persist a new `question.md`, `plan.md` or
active marker, start a Worker, perform final verification, or retry the same
Planner automatically. Preserve all existing preparation/active state and
provenance, then return the Root's existing `Status: BLOCKED` terminal form
with `Reason: PLANNER_BLOCKED` and a bounded concrete Planner diagnostic.
Keep the actual Root-local CompletedWorkers count; do not misreport zero
after earlier confirmed Workers.

An empty, mixed, duplicated, malformed or unrecognized Planner output
(including a `BLOCKED` token without its required diagnostic) is a
`PLANNER_PROTOCOL_INVALID` blocker, not an invitation to guess or retry.
An interrupted child, missing native terminal confirmation or unconfirmed
descendant stop is `CHILD_UNCONFIRMED`, not a trusted Planner `BLOCKED`
result. Follow the existing descendant-stop gate and preserve state.
Neither type creates a new persistent Factory state or silently asks the
user to resolve technical failures.
New `# Task` contracts require exactly one `ExecutionProfile` per Task
and at most **four** tasks per fresh Planner result. Validate the
whole result before persisting or spawning Workers; five Tasks block
with a protocol diagnostic, without truncation or automatic retry.
Existing saved plans larger than four remain valid for ordinary recovery.
Do not reinterpret `# Done` as an empty batch.

During an **active** run, a Planner `# Question` stops task execution:
persist exactly one `current/question.md`, then on answer append the
exact Q/A before a fresh Planner. Ordinary implementation decisions need
no durable write. Product changes use their authorized outer Intent
workflow. A **new durable Engineering decision** while
`current/request.md` exists must pause the run: no direct ENG edit,
no Engineering owner in the active run, no Worker Task substituting for
that decision, and no implicit cancellation. Completion/cancellation
followed by a new complete request is required. Do not resume an old
child transcript.

## Project verification and completion

Workers perform focused checks needed for their own tasks.

When the planner returns `# Done`, use the existing project verification
configuration if one is configured. Factory does not own a separate verification
engine.

On verification success, record Implementation Result and perform the bounded
Intent Refinement Review below. Archive required review context in ignored
`.idd/factory/results/<run-name-or-time>/` when needed, then remove active
`current/request.md`. Deferred review never keeps Factory active; if archive
fails, preserve the only remaining evidence and report the limitation.

On verification failure, write only a bounded diagnostic to
`verification-failure.md`:

```text
check/command
exit result
bounded relevant diagnostic
```

Then invoke a fresh planner. The planner decides whether a correction task is
needed.

This also applies to infrastructure failures such as missing restore assets.
Do not repair a failed final check directly in the orchestration context and
retry it without the diagnostic and fresh planner handoff. After any correction,
wait for a new planner `# Done` and run every configured final check again.
An exit code of zero without evidence that tests actually executed is not proof
that those tests passed; report the missing evidence to the planner.

Do not create verification attempts, retry budgets, confirmation state,
correction workflows, or final-review roles. If project verification is not
configured, planner `# Done` after ordinary worker checks completes Factory.

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

Semantic relevance belongs to agents. Mechanical operations belong to code or
the host.

Factory entry preflight coordinates owner workflows for normative Product
changes; `idd-engineering-change` remains Engineering mutation owner.
Planner is read-only; Factory Worker may only refine existing Product Intent
monotonically and never edit Engineering Rules.

Do not solve semantic tasks with deterministic code. Execution-profile
classification and Conditional Engineering applicability are model decisions.
Profile-to-model lookup is mechanical and must follow the project policy exactly.
Conditional Engineering applicability is a model decision. Do not ask an LLM to do simple mechanical
resolution, persistence, structural validation, or Always-rule enumeration that
the host can perform directly. When the host has no native non-LLM hook, execute
those operations as strict protocol steps without semantic selection. Any proposed Factory feature that requires a new lifecycle
state, retry class, transition type, or recovery protocol should be presumed
outside Factory unless separately justified.
