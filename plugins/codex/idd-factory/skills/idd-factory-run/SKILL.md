---
name: idd-factory-run
description: Prepare durable product intent when required, then orchestrate incremental planning and isolated native-agent workers using minimal temporary Factory state.
---

# IDD Factory Run

Use Factory for an implementation request that benefits from temporary
multi-task orchestration. Factory is a thin semantic orchestration skill over
native child-agent capabilities. It is not a workflow engine, process runtime,
MCP transport, retry engine, or durable implementation database.

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

## Intent Preflight

For a new request, first resolve one complete self-contained logical request and
run the existing Intent Preflight. Update durable product intent before
implementation when the request requires it. Factory workers never update
durable intent themselves, and intent changes are not Factory tasks.

Continuation of an existing simplified run does not repeat initial preflight.

For an explicit replacement request, resolve the complete replacement request,
run replacement preflight, and only then replace the temporary Factory state. If
the user supplied only a semantic delta and a complete replacement cannot be
formed without guessing, ask for the complete replacement request.

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

`request.md` is the persisted original request. `plan.md` contains only the
remaining tasks in the current batch. `completed.md` contains short semantic
summaries useful to later planners. `answers.md` contains exact user answers.

The repository is the authoritative implementation reality. These files are
best-effort scheduling and semantic context, not proof that a change exists.

Do not persist attempt IDs, process IDs, retry counters, workflow states,
transition history, changed-path snapshots, runtime ownership, verification
graphs, or schema versions for a Factory state machine.

If a legacy `.idd/factory/current/state.json` exists, do not deserialize,
migrate, or continue its old state machine. Preserve product changes. An
explicit restart may reuse a valid self-contained legacy `request.md` as the
replacement request and may archive the old directory for diagnostics.

## User-level operations

New:
- materialize the self-contained request;
- run Intent Preflight;
- create the minimal current state;
- enter the orchestration loop.

Continue:
- read `request.md` and the minimal current state;
- continue from the repository reality;
- do not repeat initial preflight.

Cancel:
- archive or remove the temporary Factory state;
- never roll back repository changes automatically.

Restart:
- resolve one complete replacement request;
- run replacement Intent Preflight;
- archive or remove prior temporary state;
- create a new simplified run.

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
        Tasks    -> persist the current batch in plan.md

    take the first remaining task
    run one fresh worker
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

Do not forward the parent transcript or automatically load the complete intent
tree.

### Fresh worker

For the first remaining task, invoke `idd-factory-execute-subtask` in another
fresh semantic context. Give it:

```text
Task
TaskRelatedIntent IDs, when present
current repository
```

Do not give it previous worker transcripts. The worker resolves selected intent
IDs from the current repository and performs reasonable task-local checks.

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

- one or more `# Task` sections with optional `# TaskRelatedIntent`;
- exactly one `# Question`;
- exactly `# Done`.

Blank output is not `Done`. Do not mix forms.

When a question is returned, save it to `question.md` and stop. After the user
answers, let the normal IDD workflow decide whether durable intent changes.
After any required intent update, append the exact question and answer to
`answers.md`, remove `question.md`, and invoke a new fresh planner. Never
continue the old planner thread.

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

Do not solve semantic tasks with deterministic code. Do not ask an LLM to do
simple mechanical resolution, persistence, or file operations that the host can
perform directly. Any proposed Factory feature that requires a new lifecycle
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
