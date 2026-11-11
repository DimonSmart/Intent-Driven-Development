---
name: idd-factory-run
description: Prepare required durable Product Intent and explicit Engineering policy before starting a new run, then orchestrate incremental planning and isolated native-agent workers while mechanically applying project-owned execution-profile mappings.
user-invocable: true
disable-model-invocation: false
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

## Registered native agent gate and assignment transport

Internal native agents are `idd-internal-factory-planner` and
`idd-internal-factory-worker`. On Claude Code the plugin-scoped identifiers
are `idd-factory:idd-internal-factory-planner` and
`idd-factory:idd-internal-factory-worker`; use the exact name reported by
the installed host. On Codex use their registered custom-agent `agent_type`,
never a generic child with a role-like `task_name`.
Use the actual top-level host tool schema for role selection and lifecycle
checks. A nested tool inventory is not authoritative for the root tool surface;
absence from that inventory alone is not a capability failure.

Before **any durable owner or Factory write** during new-run preflight, inspect
the installed host's native child-agent capabilities and resolve BOTH registered
roles unambiguously: exact type identity, host-loaded developer/system
instructions, fresh context, repository access, terminal wait and stop/close.
Repeat the concrete role-resolution check immediately before every spawn. A
missing, overridden, ambiguous, corrupt or unresolvable role blocks Factory;
neither Root nor a generic agent may substitute the role or discover a child
Skill from a short prompt. Internal names are not authorization.

Root must not read, reproduce, quote, or transmit the Planner/Worker protocol
bodies. The host supplies the registered instructions to the selected child.
Both child definitions contain their complete canonical role protocols,
including role-specific Engineering obligations. Root reads
`references/engineering-guardrails.md` when required by its workflow,
but never forwards the full methodology to children.

Every spawn carries only the bounded Factory assignment and accessible paths
(or exact materialized request/answers where needed). Resolve paths against
the **child repository working directory**, not the plugin source tree or a
parent-only variable. Require the child's assignment guard and all required
references. A role without a complete verified packet returns `BLOCKED`
without work. Missing host capabilities, incorrect child identity, absent
instructions, malformed results, denied model overrides or unconfirmed child
termination are fail-closed: report host, role, phase and reason; preserve
pending work and forbid concurrent replacement writers. Do not switch to a
legacy Root-relayed protocol or custom runtime.


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

**Before any durable owner/Factory write**, Root must complete the host
capability gate and resolve **both** registered native Planner/Worker roles
as described in `Registered native agent gate and assignment transport`.
Stop before preparation if either role or required capability is missing,
ambiguous or inaccessible. Repeat the identity/readiness gate before each
spawn. Do not substitute generic agents, relay full protocols from Root,
or assume a child can read parent-only paths.

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
7. Spawn a fresh **registered** initial Planner while inactive, using the
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
   `preparation/{request,baseline,provenance}.md`, resolve the registered
   Planner identity again. Spawn the same fresh isolated native Planner
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
    verify registered worker type and prepare the isolated worker assignment
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
A `task_name` identifies an attempt, not its `agent_type`; supply both
when the host supports these arguments.
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

Resolve and spawn the **registered** `idd-internal-factory-planner`
native agent (Claude plugin-scoped name where applicable). Host-loaded role
instructions must be current and complete. Root supplies only the bounded
Planner assignment, never a copy or citation of the full Planner protocol.
Require no parent transcript, and verify the actual child type in native trace.
Malformed `Tasks`, `Question`, or `Done` is not a successful result.

Give the planner only:

- the complete unchanged base Factory request (from preparation/request.md or current/request.md);
- current repository access;
- chronological exact question AND answer pairs (preparation/answers.md or current/answers.md);
- bounded completed summaries (active runs only);
- the latest bounded project-verification failure from the same phase, when present;
- original immutable baseline and bounded verified preflight/Worker
  provenance, including any unknown changes or deferred contract issues;
- whether the optional Engineering layer exists and the validated discovery
  paths; the child's host-loaded Planner protocol contains its Engineering
  obligations.

The planner discovers current durable intent through the normal
`.idd/intent/README.md` and `INDEX.md` flow and reads only relevant documents.
When `.idd/engineering/` exists, also give it access to the repository's
Engineering README and INDEX. The planner semantically selects only relevant
Conditional rules and emits them as `TaskRelatedEngineering`. It does not emit
Always rules. Full Always documents are not automatically loaded into planner
context, though the planner may read one when its content is necessary for
correct decomposition.

Do not rely on child Skill discovery or inherited Root references. Do not
forward the Root transcript, earlier child transcripts, full role protocols,
or complete Intent/Engineering trees. Fail closed if host-loaded role
instructions or required repository references cannot be established.

The planner creates only implementation/research tasks. It must not execute
implementation or create tasks to update Product Intent, create/modify
Engineering Rules, run `idd-engineering-change`, import the request, or
perform durable preparation. Before activation, Planner questions may expose
new durable decisions; only Factory entry orchestration and normal durable
owners can resolve those decisions while the marker is absent.

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

Before **every** Worker spawn, resolve the registered native
`idd-internal-factory-worker` (`idd-factory:idd-internal-factory-worker`
for Claude) and validate its host-loaded instructions and effective tool/model
capabilities. If missing, corrupt, ambiguous or unreadable, block the spawn
and leave the Task first in `plan.md`. Never reconstruct the protocol,
invoke a public Worker Skill or fall back to a generic agent.

Before every Worker spawn, recheck material Intent/Engineering constraints,
request/answer freshness and the effective contract. A stale Task batch is
replanned by a fresh Planner rather than run with outdated commitments.
For the first remaining task, prepare one self-contained worker assignment.
Immediately before execution, check whether `.idd/engineering/` exists.
Absence is normal: do not bootstrap the layer, invent ENG identifiers, or
require a nonexistent INDEX. When present, mechanically validate its structure,
re-read the current INDEX, enumerate every `Always` entry, and ensure that
each ID resolves uniquely to a valid matching document. Pass those IDs
separately from the planner-selected Conditional `TaskRelatedEngineering`
IDs. A missing, duplicated, malformed, or metadata-inconsistent mandatory
rule blocks execution without repairing the Engineering layer. Do not
semantically reselect Conditional rules.

The host-loaded Worker protocol contains its Engineering obligations; pass
the Worker verified baseline/provenance context or paths to the saved files,
and carry explicit user document prohibitions and conservative legacy
scope. Do not add another `# Task` section. The root resolves the execution
profile and native model settings before spawn; the worker never selects a model or reads execution
policy.

Use this logical native spawn instruction structure, keeping role instructions
separate from the one-task assignment:

```text
--- Factory worker assignment ---
# Task
<exactly one self-contained Task>
# TaskRelatedIntent
<optional IDD-NNNN identifiers>
# TaskRelatedEngineering
<optional planner-selected Conditional ENG-NNNN identifiers>
# AlwaysEngineering
<all current Always ENG-NNNN identifiers, if Engineering exists>
--- End Factory worker assignment ---
```

Omit empty optional sections. The delimiters define a delivery boundary for
native traces, not a persistent Factory task schema. The native adapter sends
this packet as the child task, and the host adds its registered role instructions
separately. Never put role bodies in the packet or task metadata into the
canonical role definition.

Spawn a **fresh registered native Worker** with only its one-task assignment.
Use the shared repository without inheriting the parent conversation, Planner
transcript or earlier Workers. Verify the child identity and actual model/effort
when evidence is available. `inherit` omits Factory-specific overrides;
explicit model/effort must match the validated mapping exactly. A host that
cannot apply required exact settings must block the affected Worker without
fallback and recommend `idd-factory-configure`.

The worker mechanically resolves and reads every supplied current Intent and
Engineering ID before implementation, performs focused task-local checks, and
returns a short semantic result. Workers run sequentially against the shared
workspace; no next writer may start until a trusted terminal result is received
and the prior child is no longer writing.

`AlwaysEngineering` is not planner output and is never persisted in
`plan.md`. Preserve the existing task format and continuation state.

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

## Claude registered Factory agents

Spawn only `idd-factory:idd-internal-factory-planner` and
`idd-factory:idd-internal-factory-worker` via native
`subagent_type` (or the host equivalent). Confirm both
definitions before any durable preflight and again before
each role spawn. Never substitute a generic subagent or
ask a child to discover a Skill. Definitions are loaded
into the child by the host; Root passes only the bounded
assignment packet. Do not send the full protocol.

## Claude native-agent capability

Use a native Claude fork/subagent mechanism only when the host can
provide every Factory capability required by the canonical skill:
a fresh semantic context with controlled input and no substantial
inherited parent transcript, shared repository access, terminal
waiting without model-driven polling, a final child result, and
stop/close lifecycle control.

When those properties cannot be guaranteed, report Factory as
unsupported on the current Claude host. Do not restore or emulate
Factory through the removed .NET runtime, a packaged CLI launcher,
MCP transport, process supervision, or a status-polling loop.

## Claude Factory execution policy

Immediately before each worker spawn, map the planner's
`ExecutionProfile` through the validated project-owned
`.idd/execution.yaml` policy.

- `inherit` means omit Factory-specific model and effort overrides.
- For an explicit active-platform mapping, pass the configured
  `claude.model` and optional `claude.effort` through the native
  Agent/subagent invocation. If the host has no corresponding
  explicit per-child parameter, block the worker instead of
  embedding an override request in its prompt.
- Do not reconsider task complexity or substitute a different
  model. The configured mapping is authoritative.
- If the host rejects the configured settings or cannot honor an
  explicit per-child override, stop with a diagnostic that names
  the profile, requested settings, and rejection reason; suggest
  `idd-factory-configure` and never pick a fallback model.
