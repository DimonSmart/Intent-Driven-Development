---
name: idd-factory-run
description: Prepare durable Intent/Engineering policy and orchestrate fresh generic child agents that fully load installed internal Factory Skills for bounded Planner and Worker assignments.
---

# IDD Factory Run

Use Factory for an implementation request that benefits from temporary
multi-task orchestration. Factory is a thin semantic orchestration skill over
native child-agent capabilities. It is not a workflow engine, process runtime,
MCP transport, retry engine, or durable implementation database.

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
properties, Factory is unsupported on that host. Do not recreate the removed
C# runtime, MCP transport, shell process supervisor, status protocol, or polling
loop to compensate.

A new thread is not sufficient if it automatically receives the parent history.

## Installed internal Skills and generic child-agent transport

The Factory Planner and Worker protocols are shipped as
`skills/idd-internal-factory-planner/SKILL.md` and
`skills/idd-internal-factory-worker/SKILL.md` inside the SAME installed
`idd-factory` plugin as this `idd-factory-run/SKILL.md`. These are internal
instruction Skills, not registered custom-agent types or separate processes.

Before any new durable owner/Factory write, resolve this currently loaded
Factory Run Skill to its real installed absolute path; derive its sibling
Planner/Worker Skill paths from that exact installed plugin root. Do not guess
plugin cache versions, search globally by name, use the source checkout or
assume the project working directory is the plugin root. Check both SKILL.md
files exist and are accessible, and verify any statically declared mandatory
references. Root may inspect paths, file attributes and package metadata, but
MUST NOT read, summarize, quote or relay the full child protocols.

Inspect the actual top-level host-native child-agent schema, including direct
operations that might not appear inside a nested helper tool list. Require
fresh child context without inherited history, child access to installed files
and the shared project working copy, bounded task delivery, final result,
native blocking/event-driven waiting, stop/interrupt and confirmation that a
writer has stopped, and exact model/effort overrides when required by policy.
Absence of a Factory-specific `agent_type` argument is NOT a failure.
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
state and pending Task; never declare successful completion. On wait failure,
stop or interrupt the child, confirm Writer termination before another write,
and end this attempt with recoverable state. No status polling loop or runtime
fallback. These are checks for necessary host capabilities, not a requirement
to prove a future child has already read its Skill before spawning it.

## Factory Preflight — Root entrypoint

`references/intent-preflight.md` is the **sole detailed normative contract**
for logical request identity, Requested scope, Product classification,
Factory-level Engineering disposition, request-level blockers, Git baseline,
owner order, durable coverage, preparation recovery, initial Planner outcomes,
activation and replacement. Read it **in full** before starting/replacing a
run. Do not reconstruct its protocol from this abbreviated Root checklist.
If unavailable, stop as an incomplete installation.

Factory is implementation-capable only: `route-only` and `intent-only`
stop before Factory orchestration; `intent-preserving` cannot authorize
mutating Product/Engineering preflight. A direct invocation bypassing
`idd-route` does not bypass Preflight. For new, continuing, recovering,
cancelled or explicitly replaced runs, select the applicable mode from
`intent-preflight.md`, not a fabricated new state machine.

**Before any durable owner/Factory write**, Root must validate the actual native
child-agent capabilities and both installed internal Skill paths described
above. Missing required tools/files block before preparation; an absent
`agent_type` or unregistered legacy Factory role does not. Recheck the
chosen Skill path and required capabilities before each spawn.

For a **new independent end-to-end run**, execute these named Preflight
phases in the normative order, with Root responsible for the concrete actions:

1. Materialize the FULL self-contained original request, including
   explicitly included textual attachments; resolve Requested scope and
   independently analyze Product Intent and Engineering dispositions.
2. Evaluate **all determinable request-level blockers** before ANY
   durable write: missing normative decisions, contradictions, forbidden
   scope mutation, incomplete input and prohibited active replacement.
   On a blocker, do not invoke an owner or create `preparation/`.
3. Check clean managed IDD paths and the original real `HEAD^{commit}`
   using the exact Git gate in the reference. A dirty unrelated code tree
   alone is not a blocker. Never auto-commit or invent a baseline.
4. Persist `preparation/request.md`, `baseline.md` and `provenance.md`
   BEFORE invoking a potentially mutating owner. Record bounded pending
   provenance and verify trusted after-state for each owner action.
5. Invoke `idd-engineering-change` (only when required) before the
   authorized Product Intent owner; pass the complete request, not a
   summary. `success`/`no-op` continue; `ambiguous`/`blocked`
   or an unknown after-state stop and preserve recoverable preparation.
6. Validate durable coverage against full request, exact Q/A, both
   applicable durable layers, safety, compatibility and explicit non-goals.
   Record the verified boundary in provenance.
7. Spawn a fresh generic child for the initial Planner while inactive, using the
   ordinary bounded assignment. Handle `# Question`, `# Done` or a
   complete validated `# Task` batch according to the Preflight reference
   and the operational activation section below. No Planner outcome
   overrides a request-level blocker.
8. Only after validated executable Tasks and transfer of verified plan,
   exact answers, baseline and provenance, write
   `current/request.md` **LAST**. Then enter the sequential Worker loop.

Do not re-run the new-independent-run cleanliness/baseline gate on an
active continuation, valid preparation recovery or genuine active
replacement. For recovery reconcile exact saved request/Q/A and pending
evidence before retrying a mutating action; never blindly replay owners.
For explicit active replacement, preserve old active state until authorized
replacement preflight/coverage succeeds, and block any Engineering-changing
replacement before writing. Timeouts or interrupted owners are not proof of
termination, success, authorization, or safe rollback.

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

New:
- materialize the full logical base request and resolve requested scope;
- execute existing durable preflight and coverage validation;
- persist preparation, run the fresh initial planner and handle its result;
- activate only on a validated Task batch, then enter the worker loop.

Continue:
- if `current/request.md` exists, use ordinary active-run continuation
  without repeating entry preflight or initial planning;
- otherwise if `preparation/request.md` exists, restore the full base request,
  exact Q/A and pending question without depending on parent transcript;
- treat a precise reply as the answer to the saved `question.md`; when a new
  independent request appears, ask whether to continue or explicitly replace
  the pending preparation instead of silently combining two requests;
- incomplete preparation blocks automatic execution. Explain which source is
  missing rather than reconstructing it from memory.

Cancel:
- remove pending preparation, or archive/remove the active state as appropriate;
- never roll back repository or durable-knowledge changes automatically.

Restart / explicit replacement:
- preserve existing active replacement semantics and guard;
- an independent new request cannot silently overwrite pending preparation;
- on explicit replacement of preparation, discard only that temporary directory
  and perform new preflight. Existing active runs retain their own rules.

These are skill-level operations, not new transport operations or state schemas.


## Original Git baseline and bounded provenance — Root operations

Follow `Original Git baseline and preflight provenance`, `Request
identity`, and `Replacement semantics` in
`references/intent-preflight.md`. The concrete new-independent-run
commands from the repository root are:

```text
git status --porcelain=v1 -z --untracked-files=all -- .idd/intent/ .idd/engineering/
git rev-parse --verify "HEAD^{commit}"
```

Parse the status as **NUL-delimited**, including paired rename paths;
check staged, unstaged, untracked and deleted managed files. Missing
`.idd/engineering/` is valid; changes outside the managed paths are not
by themselves a blocker. Require a full real HEAD SHA. Preserve the
original baseline on recovery/continuation/replacement.

Before each mutating owner or Worker, durably checkpoint the bounded
pending action and original authorization in `provenance.md`; after a
trusted result record affected paths and before/after evidence.
`external-unknown` is not confirmed authorization. An unavailable
original Git object, ambiguous provenance or partial activation blocks
unsafe writes/refinement: preserve evidence and diagnose; no automatic
rollback or baseline reset. Validate inactive plan, answers and
provenance before writing the sole active marker
`current/request.md` last. Neither a stray `current/plan.md` nor
legacy `state.json` marks an active run.


## Preparation planner and activation — Root operations

Use `New-run ordering`, `Durable coverage validation`, `Preparation
questions and renewed coverage`, and `Request identity` in
`references/intent-preflight.md`. This section specifies Root's
native-agent and temporary-file operations, not a second Preflight policy.

1. After approved durable coverage and saved
   `preparation/{request,baseline,provenance}.md`, recheck the installed
   Planner Skill path. Spawn a fresh generic child as Planner
   used in active runs with the unchanged request, chronological exact Q/A,
   repository paths, current Engineering discovery context and bounded
   preparation verification failure. No active marker or `completed.md`
   exists before Workers.
2. For `# Question`, require exactly one concrete question, write its
   exact text to `preparation/question.md`, and stop **without** creating
   `current/request.md` or `plan.md` or spawning a Worker. On reply,
   append exact question AND answer chronologically to
   `preparation/answers.md`, remove the pending question, and perform
   the renewed blocker/owner/coverage checks from Preflight before a
   NEW isolated Planner. Never inherit the previous child's transcript.
3. For `# Done` before activation, run project verification in the
   `final` context. Success removes preparation and completes with
   **no active marker and no empty plan**. Failure writes only bounded
   `preparation/verification-failure.md` and goes to a fresh Planner.
   Do not call an unverified check success, retry unchanged failure
   endlessly, or run fixes from Root.
4. For `# Task`, validate the WHOLE fresh executable batch:
   self-contained contracts, one explicit `ExecutionProfile` from
   `economy | standard | strong` per new Task, unique existing
   `TaskRelatedIntent` owners, existing unique Conditional
   `TaskRelatedEngineering` owners (not Always), structural Engineering
   validity and durable/repository freshness. Malformed, mixed,
   empty, stale or unresolved output is a blocking protocol diagnostic,
   never an executable task.
5. Recheck no `current/request.md` exists. Reconcile orphaned inactive
   files with trusted preparation evidence; write `current/plan.md` from
   the validated batch and transfer exact Q/A, immutable baseline and
   provenance. Write the complete original `current/request.md` LAST
   (prefer a same-directory atomic rename). Only then launch the normal
   Worker loop. Never claim multi-file atomicity or overwrite a competing
   active marker.

An unfinished legacy run with explicit implementation-only scope keeps
its no-Intent-write boundary; ambiguous legacy scope/provenance forbids
Worker Intent writes. The installed plugin version does not grant permission.
A clarification cannot silently expand `intent-preserving` scope.

## Orchestration loop

Conceptually:

```text
while true:
    if waiting for user:
        stop

    if plan.md has no remaining task:
        run a fresh planner

        Question -> persist question.md and stop
        Done     -> run project verification
        Tasks    -> require one canonical ExecutionProfile per new task, then persist in plan.md

    take the first remaining task
    read its ExecutionProfile; missing metadata in existing plan.md means standard
    reject empty, repeated, or unknown explicit profiles
    if .idd/execution.yaml is absent, inherit host settings
    otherwise re-read and structurally validate .idd/execution.yaml
    mechanically resolve the active-platform profile mapping
    if that profile/platform mapping is absent, inherit host settings
    deterministically re-read the current Engineering INDEX
    enumerate all current Always ENG IDs
    verify installed Worker Skill path and prepare the isolated worker assignment
    record pending Worker before-checkpoint in provenance.md
    spawn a fresh native worker with the resolved native model override
    wait natively for its terminal result

    if no trusted completed result:
        leave the task first in plan.md
        stop

    record bounded Worker after-checkpoint, including unknown edits
    append a short result to completed.md
    remove the completed task from plan.md
```

This is conceptual behavior, not a requirement to implement a new programmatic
state machine.

### Codex MultiAgentV2 spawn identity

On Codex MultiAgentV2, **every Factory-owned native `spawn_agent` call**
(planner and worker) MUST include a unique explicit `task_name` argument:
`idd_factory_planner_01`, `idd_factory_planner_02`, ... for planners,
or `idd_factory_worker_01`, `idd_factory_worker_02`, ... for workers.
Only names matching `^idd_factory_(planner|worker)_[0-9]+$` are valid.
A `task_name` identifies an attempt, not a role type or evidence that
its Skill was loaded.
Use a fresh name for **each attempt**, including a spawn rejected by the host.
Keep names unique within the root-session/Factory invocation and any shared
host namespace; inspect visible native calls before selecting the next suffix.
Do not persist counters or create another Factory runtime/state machine.

Pass `task_name` in native spawn arguments, **not** in child instructions or
worker assignments. This is a trace-visible role marker, not evidence that a
child was accepted, completed or used its requested model. Also explicitly set
`fork_turns: "none"` for **planner and worker** spawns; a fresh context must
not inherit the parent or an earlier child's transcript. Apply exact model and
reasoning overrides when execution policy requires them; if the host cannot
express a required argument or honor an override, block with a host-compatibility
diagnostic rather than changing model or backend. Other platform adapters are
unchanged when they lack `task_name`.

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

Immediately before every worker spawn, apply the bounded lookup from
`references/factory-execution-policy.md`:

```text
planner ExecutionProfile
-> missing ExecutionProfile in an existing plan.md means standard
-> reject empty, repeated, or unknown explicit profiles
-> re-read one project configuration document when present
-> absent policy or absent profile/platform mapping means inherit
-> otherwise use exact configured active-platform model/settings
-> native child-agent spawn
```

New planner output must include exactly one canonical profile per task; validate
it before saving a new batch. When continuing an existing run, pending tasks in
`.idd/factory/current/plan.md` without `# ExecutionProfile` remain valid and use
`standard`. Do not require a restart, migrate the plan, or reconsider complexity
solely because that legacy metadata is absent.

This is a mechanical protocol step. Do not reconsider task complexity, compare
candidate models, optimize cost, upgrade/downgrade the profile, or choose a
model that is not the exact configured mapping.

If the file is absent, inherit normal host model/reasoning settings without
writing a default file. Partial mappings are valid: a missing profile or missing
active-platform mapping also inherits host model/reasoning settings. Validate
all present mappings. A malformed existing policy blocks the affected worker
with a concrete diagnostic, as do unknown profile/platform names and empty or
repeated explicit task profiles. Inheritance omits Factory-specific model and reasoning
overrides; it does not copy the current model into the policy.

Malformed explicit configuration blocks the worker spawn with a clear
diagnostic; never silently fall back to `inherit`. If the native host rejects a
configured model/reasoning value or cannot honor an explicit per-child override,
stop without spawning a substitute worker and suggest rerunning
`idd-factory-configure`.

Model selection changes only native spawn settings. The same canonical Factory
worker protocol is used for every execution profile; profiles change native
child-agent settings, never worker instructions. The worker does not choose its
own model.

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
policy. Use exact policy-resolved native model/effort arguments or omit both
when inheriting; never silently substitute.

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

1. do not start another worker for the same task;
2. do not begin status polling;
3. stop or close the still-running child natively when possible;
4. leave the task first in `plan.md`;
5. end the current Factory invocation.

Never run a replacement writer while the previous child may still be writing to
the same workspace.

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

For the legal `# Task`, `# Question`, `# Done` forms and
pre-activation handling, use `references/intent-preflight.md` and
`Preparation planner and activation` above. A fresh planner returns
exactly one of these forms; blank or mixed output is invalid.
New `# Task` contracts require exactly one `ExecutionProfile`.
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

## Codex generic Factory subagents

Use the host's native generic child-agent spawn. A Factory-specific
agent_type is not required. Resolve the internal Planner and Worker
SKILL.md files relative to THIS installed Factory Run Skill; pass
absolute paths and a bounded assignment to each new child. Children
read the complete installed Skill themselves before other work.
Use fork_turns: "none" for every spawn, including Planner.
Never send protocol bodies or previous child transcripts.

Inspect the direct host-visible tool schema, including tools not
exposed inside functions.exec/ALL_TOOLS. An absent agent_type is
NOT a capability failure. If the host requires a built-in generic
agent type, use that built-in type, never a registered Factory role.
Check fresh context, shared workspace/installed files, terminal
results, native wait and stop/interrupt. Prefer read-only Planner
permissions if available but do not confuse a text guard with a
technical sandbox. Diagnose missing mandatory capabilities precisely.

Reserve unique task_name arguments idd_factory_planner_01,
idd_factory_worker_01, etc. Rejected attempts consume their names.
Example without model overrides:
{"task_name":"idd_factory_worker_01","fork_turns":"none","message":"Read the entire installed Worker SKILL.md at <absolute path>, then perform exactly this bounded Factory assignment: <one Task packet>"}.
Replace placeholders with real paths and task data.
Never add an unsupported spawn argument.

If an explicit Codex policy mapping is active, pass the exact
codex.model as model and codex.reasoningEffort as reasoning_effort;
inherit omits both overrides. No prompt-based switching or fallback.
Wait natively without short model-driven polling. On timeout or
failure, stop/interrupt the writer and confirm termination before
starting another; preserve the pending Task. A final Planner Done
always requires the configured final verification checks.
