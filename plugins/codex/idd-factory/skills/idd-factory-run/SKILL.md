---
name: idd-factory-run
description: Prepare required durable Product Intent and explicit Engineering policy before starting a new run, then orchestrate incremental planning and isolated native-agent workers while mechanically applying project-owned execution-profile mappings.
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

## Factory Preflight

For a new run, first resolve one complete self-contained logical request. Preserve
the user request text and materialize pasted text, explicitly supplied textual
content, and request-local textual documents that the user identifies as part of
the task. Host-local paths and attachment identifiers are transport metadata, not
durable semantics. The materialized request is authoritative for both preflight
and the later Factory run; do not replace it with a route summary, generated
summary, implementation plan, Engineering summary, or Intent diff.

Resolve the requested scope before durable work:

- `route-only`: report routing/diagnostics only; perform no durable mutation and
  do not start Factory;
- `intent-only`: durable Product Intent preparation and Engineering management
  are allowed when required, but implementation and Factory orchestration do not
  start;
- `implementation-only`: neither `.idd/intent/*` nor
  `.idd/engineering/*` may be mutated. If the request requires either durable
  change, stop with the required durable-workflow diagnostic;
- `end-to-end`: durable preparation may be followed by implementation and
  verification.

Run the existing Product Intent classification from Intent Preflight before any
durable write:

```text
AlreadyCovered
ExplicitIntentChange
MissingIntentDecision
ImplementationOnly
```

For `ExplicitIntentChange`, determine the safe Intent handoff/owner, but defer
the actual Product Intent mutation until Engineering management has completed
successfully or as a no-op. Product Intent mutations remain owned by
`idd-intent-change` and its normal `idd-intent-new-document` handoff.

Separately classify only whether Engineering management is a durable concern:

```text
NoEngineeringManagementNeeded
EngineeringManagementRequired
EngineeringDecisionMissing
```

`EngineeringManagementRequired` means that the complete request explicitly
contains one or more durable implementation-only project decisions. It does not
authorize Factory to determine `new`, semantic equivalence, owning ENG ID,
add/modify/remove/no-op actions, allocator changes, or INDEX edits. Those remain
exclusively owned by `idd-engineering-change`.

Do not promote task-local implementation details into Engineering policy. A
technology name, class/file name, DI wiring step, implementation order, test
step, temporary migration instruction, or local library-use instruction is not
by itself a durable project-wide Engineering decision.

Before any durable write, detect every request-level blocker that can be decided
without invoking a mutation owner, including `MissingIntentDecision`,
`EngineeringDecisionMissing`, conflicting requirements inside the request,
requested-scope violations, inability to materialize the complete request, and
an Engineering-changing replacement while an active Factory request exists. A
request-level blocker means no Product mutation, no Engineering mutation, and no
new Factory state.

For a new `end-to-end` run, the required order is:

```text
1. materialize complete logical request
2. resolve requested scope
3. non-mutating Product Intent analysis
4. Factory-level Engineering disposition
5. detect request-level blockers

6. if EngineeringManagementRequired:
       invoke idd-engineering-change with the complete logical request

7. handle Engineering result:
       success   -> continue
       no-op     -> continue
       ambiguous -> stop before Factory state creation
       blocked   -> stop before Factory state creation

8. apply required Product Intent mutation through existing intent workflows
9. validate durable coverage against the complete logical request
10. create .idd/factory/current/request.md
11. start a fresh Factory planner
```

The Engineering step deliberately precedes the Product Intent write because
`idd-engineering-change` owns authoritative Engineering ownership/ambiguity
planning and has no separate dry-run protocol. This ordering avoids changing
Product Intent first and discovering an Engineering ownership ambiguity only
afterward. It is not a transaction: if Engineering succeeds and a later Intent
workflow fails mechanically, report the blocker and do not create Factory state;
do not add rollback machinery.

Coverage validation is against the complete logical request, not a generated
summary. Confirm that required Product behavior is represented, every explicit
Engineering concern was handled by `idd-engineering-change` with `success` or
`no-op`, material safety/compatibility constraints and explicit non-goals were
not lost, implementation-only details did not leak into Product Intent,
task-local details did not leak into Engineering, the two durable layers do not
contradict one another, and no required durable decision remains unresolved.
When `.idd/engineering/` exists, its structure must also be valid. Absence of
the Engineering layer is valid and produces no warning.

Only after successful durable preparation and coverage validation may a new run
create `.idd/factory/current/request.md`. Persist the complete self-contained
logical request itself, not a preflight report or rewrite.

Continuation of an existing simplified run does not repeat initial preflight.

For an explicit replacement request, resolve the complete replacement request
while leaving the current Factory state untouched. Replacement that needs no
Engineering mutation keeps the existing replacement Product Intent semantics:
run replacement preflight/coverage first and replace temporary state only after
success. If the active replacement request is
`EngineeringManagementRequired`, stop before any durable write. Do not bypass
the `idd-engineering-change` active-run guard, auto-cancel/restart the current
run, archive/unarchive it as a trick, or create a suspended/pending lifecycle.
Report that the current run must be completed or cancelled and the complete
replacement request then started as a new Factory run.

## Minimal temporary state

Use only the temporary files needed for continuation:

```text
.idd/factory/current/
    request.md
    plan.md
    completed.md
    answers.md
    question.md                # only while waiting for the user
    verification-failure.md    # only when relevant
```

`request.md` is the complete self-contained logical request that passed Factory
Preflight. `plan.md` contains only the remaining implementation/research tasks in
the current batch. `completed.md` contains short semantic summaries useful to
later planners. `answers.md` contains exact user answers.

The repository is the authoritative implementation reality. These files are
best-effort scheduling and semantic context, not proof that a change exists.

`.idd/execution.yaml`, when present, is deliberately outside
`.idd/factory/current/`. It is project-owned execution policy that survives
Factory runs, not temporary Factory state or workflow/runtime configuration.

Do not persist attempt IDs, process IDs, retry counters, workflow states,
transition history, changed-path snapshots, runtime ownership, verification
graphs, or schema versions for a Factory state machine.

If a legacy `.idd/factory/current/state.json` exists, do not deserialize,
migrate, or continue its old state machine. Preserve product changes. An
explicit restart may reuse a valid self-contained legacy `request.md` as the
replacement request and may archive the old directory for diagnostics.

## User-level operations

New:
- materialize the complete self-contained logical request;
- run Factory Preflight, including required Product Intent and Engineering
  preparation;
- validate durable coverage;
- only then create the minimal current state;
- enter the orchestration loop.

Continue:
- read `request.md` and the minimal current state;
- continue from repository reality;
- do not repeat initial preflight.

Cancel:
- archive or remove the temporary Factory state;
- never roll back repository or durable-knowledge changes automatically.

Restart / explicit replacement:
- resolve one complete replacement request while preserving the active state;
- run replacement preflight;
- if the replacement requires Engineering management, return the active-run
  blocker without changing Product Intent, Engineering, or Factory state;
- otherwise replace temporary state only after successful replacement preflight
  and coverage validation.

These are skill-level operations. There are no `factory_run`,
`factory_continue`, `factory_restart`, `factory_retry`, `factory_cancel`,
or `factory_status` transport operations.

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
        Tasks    -> persist the planner output for the current batch in plan.md

    take the first remaining task
    read its ExecutionProfile, defaulting missing metadata to standard
    structurally validate .idd/execution.yaml when present
    mechanically resolve the active-platform profile mapping
    deterministically re-read the current Engineering INDEX
    enumerate all current Always ENG IDs
    spawn the same fresh worker with the resolved native model override
    wait natively for its terminal result

    if no trusted completed result:
        leave the task first in plan.md
        stop

    append a short result to completed.md
    remove the completed task from plan.md
```

This is conceptual behavior, not a requirement to implement a new programmatic
state machine.

### Fresh planner

Invoke `idd-factory-decompose-task` in a fresh semantic context. Give it only
the self-contained request, current repository access, prior user answers, short
completed summaries, and the latest bounded project-verification failure when
present. The planner discovers current durable intent through the normal
`.idd/intent/README.md` and `INDEX.md` flow and reads only relevant documents.

When `.idd/engineering/` exists, also give the planner access to its README and
INDEX. The planner semantically selects only relevant Conditional rules and
emits them as `TaskRelatedEngineering`. It does not emit Always rules. Full
Always documents are not automatically loaded into planner context, though the
planner may read one when its content is necessary for correct decomposition.

Do not forward the parent transcript or automatically load the complete intent
or Engineering trees.

The planner creates only implementation/research tasks. It must not create tasks
to update Product Intent, create/modify Engineering Rules, run
`idd-engineering-change`, import the request, or otherwise perform durable
preparation. Factory Preflight finishes that work before active state exists.

### Worker execution profile

Immediately before every worker spawn, apply the bounded lookup from
`references/factory-execution-policy.md`:

```text
planner ExecutionProfile
-> default missing profile to standard
-> project configuration lookup
-> inherit OR exact configured active-platform model/settings
-> native child-agent spawn
```

This is a mechanical protocol step. Do not reconsider task complexity, compare
candidate models, optimize cost, upgrade/downgrade the profile, or choose a
model that is not the exact configured mapping.

When `.idd/execution.yaml` is absent, all profiles inherit. For partial
configuration, a missing profile or missing active-platform mapping also
inherits. `inherit` means omit Factory-specific model and reasoning overrides.

Malformed explicit configuration blocks the worker spawn with a clear
diagnostic; never silently fall back to `inherit`. If the native host rejects a
configured model/reasoning value or cannot honor an explicit per-child override,
stop without spawning a substitute worker and suggest rerunning
`idd-factory-configure`.

Model selection changes only native spawn settings. The worker skill is always
`idd-factory-execute-subtask`.

### Fresh worker

For the first remaining task, invoke `idd-factory-execute-subtask` in another
fresh semantic context. Give it:

```text
Task
TaskRelatedIntent IDs, when present
TaskRelatedEngineering IDs, when present
AlwaysEngineering IDs, mechanically enumerated immediately before this worker
current repository
```

Do not give it previous worker transcripts. The worker resolves selected
Intent and Engineering IDs from the current repository and performs reasonable
task-local checks.

`AlwaysEngineering` is not planner output and does not need to be persisted in
`plan.md`. Before every worker execution, mechanically enumerate all current
INDEX entries whose Applicability is `Always`, validate that they resolve
uniquely to matching current rule documents, and pass those IDs separately.
Always rules are enumerated, never semantically selected.

Workers run sequentially against the shared workspace. Do not add parallel
write workers in this workflow.

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

## Planner outcomes

The planner returns exactly one of:

- one or more `# Task` sections with optional `# ExecutionProfile`, optional
  `# TaskRelatedIntent`, and optional `# TaskRelatedEngineering`;
- exactly one `# Question`;
- exactly `# Done`.

Blank output is not `Done`. Do not mix forms.

When a question is returned, save it to `question.md` and stop. After the user
answers, classify the exact answer before resuming:

- if it changes Product Intent, use the normal outer Product Intent workflow when
  allowed, then append the exact Q/A and start a fresh planner;
- if it is an ordinary implementation decision, change no durable knowledge,
  append the exact Q/A, and start a fresh planner;
- if it introduces a new durable Engineering decision, do not invoke
  `idd-engineering-change` while `request.md` marks the run active. Keep Factory
  paused and report that the user must complete/cancel this run and start a new
  complete request containing the original request plus the Engineering decision.

Never patch an Engineering Rule directly, turn the decision into a worker task,
weaken the active-run guard, auto-cancel/restart the run, or continue the old
planner thread.

## Project verification and completion

Workers perform focused checks needed for their own tasks.

When the planner returns `# Done`, use the existing project verification
configuration if one is configured. Factory does not own a separate verification
engine.

On verification success, Factory is complete. Remove the active state or move it
best-effort to `.idd/factory/results/<run-name-or-time>/` as diagnostic history.

On verification failure, write only a bounded diagnostic to
`verification-failure.md`:

```text
check/command
exit result
bounded relevant diagnostic
```

Then invoke a fresh planner. The planner decides whether a correction task is
needed.

Do not create verification attempts, retry budgets, confirmation state,
correction workflows, or final-review roles. If project verification is not
configured, planner `# Done` after ordinary worker checks completes Factory.

## Design boundary

Semantic relevance belongs to agents. Mechanical operations belong to code or
the host.

Factory entry preflight may coordinate durable preparation before active state
exists, but Product Intent workflows remain the only Product Intent mutation
owners and `idd-engineering-change` remains the ordinary Engineering mutation
owner. Factory planners and workers own implementation against already prepared
durable knowledge and never mutate `.idd/intent/*` or `.idd/engineering/*`.

Do not solve semantic tasks with deterministic code. Execution-profile
classification and Conditional Engineering applicability are model decisions.
Profile-to-model lookup is mechanical and must follow the project policy exactly.
Conditional Engineering applicability is a model decision. Do not ask an LLM to do simple mechanical
resolution, persistence, structural validation, or Always-rule enumeration that
the host can perform directly. When the host has no native non-LLM hook, execute
those operations as strict protocol steps without semantic selection. Any proposed Factory feature that requires a new lifecycle
state, retry class, transition type, or recovery protocol should be presumed
outside Factory unless separately justified.

## Codex native-agent orchestration

Use native Codex child-agent delegation for Factory semantic work.

- Spawn every planner and worker in a fresh semantic context with
  parent-history inheritance explicitly disabled. Use
  `fork_turns = "none"` or the host-equivalent setting; never rely
  on an inheritance default.
- Prefer a read-only planner and a workspace-writing worker when the
  host exposes those sandbox choices.
- Use the native `spawn_agent` operation (or its current native
  equivalent) and a blocking/event-driven `wait_agent` operation.
  Prefer one long wait on the critical path. Do not build a
  short-wait/status-polling loop that consumes parent model turns.
- If a wait fails or the host times out, do not start a replacement
  worker while the previous child may still write to the workspace.
  Stop or close the child natively when possible, leave the task in
  `plan.md`, and end the current Factory invocation.
- If Codex cannot provide fresh/no-parent-history children, shared
  repository access, terminal waiting without model polling, final
  child results, and stop/close lifecycle control, Factory is
  unsupported on that host. Do not emulate the missing capability
  with a packaged runtime, MCP transport, shell process supervisor,
  or polling protocol.

## Codex Factory execution policy

Immediately before each worker spawn, map the planner's
`ExecutionProfile` through the validated project-owned
`.idd/execution.yaml` policy.

- `inherit` means omit Factory-specific model and reasoning
  overrides so the normal host/session/default model applies.
- For an explicit active-platform mapping, pass the configured
  `codex.model` and optional `codex.reasoningEffort` through
  the host's native per-child override controls.
- Do not reconsider task complexity or substitute a different
  model. The configured mapping is authoritative.
- If the host rejects the configured model/settings or cannot
  honor an explicit per-child override, stop with a diagnostic
  and suggest `idd-factory-configure`; never pick a fallback
  model automatically.
