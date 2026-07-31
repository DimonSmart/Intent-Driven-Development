# IDD Factory Skills Reference

Most users should invoke only:

```text
idd-factory-run
```

The other Factory skills are bounded workers used by the coordinator. This page documents them for transparency, advanced inspection, and troubleshooting.

## Normal Workflow

```text
idd-factory-run
  → idd-factory-decompose-work (intent preflight)
  → when INTENT_REQUIRED:
      idd-intent workflow
      idd-factory-decompose-work again
  → create implementation-only Factory tasks
  → idd-factory-execute-task
  → idd-factory-review-task
  → repeat tasks when necessary
  → idd-factory-review-work-result
  → idd-factory-finish-work
```

The coordinator normally continues through this sequence automatically. Intent
changes happen before task-state creation or as coordinator-owned orchestration,
never as Factory tasks.

## `idd-factory-run`

### Purpose

Public entry point for starting, continuing, or cancelling one Factory run.

It owns intent preflight, workspace validation, clarification, implementation
task sequencing, worker dispatch, status transitions, review loops, final review,
and finalization.

### Start a complete run

```text
Use idd-factory-run to implement the task described in ./ui-audit.md.
```

Or:

```text
Use idd-factory-run to replace the legacy storage implementation, migrate existing callers, preserve compatibility, and verify the result.
```

One invocation is expected to complete the workflow unless clarification, missing intent, an external blocker, or an unexpected interruption prevents progress.

Before creating `.idd/factory/current/`, Factory resolves missing or conflicting
durable intent and repeats decomposition. The saved task list contains only
implementation work.

### Continue an interrupted run

```text
Continue the current IDD Factory work.
```

Use this only after an existing run was interrupted.

### Cancel the current run

```text
Cancel the current IDD Factory work.
```

Cancellation removes temporary Factory task state but does not revert code changes.

## `idd-factory-decompose-work`

### Purpose

Analyze one request and return the smallest safe ordered set of implementation tasks.

It determines whether the request is ready, small enough for focused implementation, blocked by missing clarification, blocked by missing intent, or blocked by another condition.

Intent preflight is part of decomposition. When durable behavior is missing or
conflicting, it returns `INTENT_REQUIRED` and no partial task plan. It never
creates a task for updating, linting, auditing, or otherwise changing
`.idd/intent/`.

After intent is resolved, decomposition runs again against the complete original
request and creates self-contained implementation task contracts. Task-specific
requirements stay in the owning task. Substantial constraints shared by multiple
tasks may be placed in a compact optional `run-context.md`. Neither tasks nor run
context copy the complete original request.

### Normal caller

`idd-factory-run`.

### Advanced manual use

Use it manually only to inspect a proposed decomposition without implementing anything:

```text
Use idd-factory-decompose-work to show how ./migration-plan.md would be divided into implementation tasks. Do not execute them.
```

The skill does not write Factory state, code, tests, or product intent.

## `idd-factory-execute-task`

### Purpose

Implement exactly one active Factory task.

It reads the active self-contained implementation task, optional shared
`run-context.md`, relevant current intent, the current diff, and repository
evidence required for that task. It does not read the original `request.md` or
other task files.

If an invalid task asks to change intent, the executor returns `NEEDS_REPLAN`
instead of performing that scope. `INTENT_REQUIRED` is reserved for missing or
conflicting durable behavior discovered during implementation.

### Normal caller

`idd-factory-run`.

### Advanced manual use

Manual invocation is generally discouraged because the coordinator owns task order and status.

For controlled troubleshooting of an already active task:

```text
Use idd-factory-execute-task for .idd/factory/current/002-update-storage.active.md only. Do not advance the Factory workflow.
```

The skill does not select another task, rename task files, perform final review, create a commit message, or clean the workspace.

## `idd-factory-review-task`

### Purpose

Independently review one active task after implementation.

It checks the implementation task contract, optional shared run context,
relevant intent, public contracts, implementation quality, verification
evidence, and safety for later tasks. It does not reread the original request or
other task files.

An intent-changing task contract receives `needs-replan`, not approval.
`intent-required` is reserved for missing or conflicting durable behavior
discovered while reviewing implementation.

### Normal caller

`idd-factory-run`.

### Verdicts

```text
approved
needs-fix
needs-replan
blocked
intent-required
```

For `needs-fix`, the coordinator keeps the task active, records the current actionable findings, and invokes implementation again.

For `needs-replan`, the coordinator corrects insufficient task boundaries,
intent-editing scope, missing contract information, or ordering without asking
the worker to recover requirements from `request.md`.

### Advanced manual use

```text
Use idd-factory-review-task to review the current active Factory task. Do not modify code or task files.
```

The reviewer is read-only.

## `idd-factory-review-work-result`

### Purpose

Independently review the complete integrated result after all tasks are completed.

It checks the original request, optional run context, every completed
implementation task contract, current product intent, the full diff, cross-task
integration, preservation boundaries, public contracts, and verification
sufficiency. This is the stage that verifies decomposition did not omit
requirements from the original request.

### Normal caller

`idd-factory-run`.

### Verdicts

```text
approved
needs-fix
blocked
intent-required
```

When the verdict is `needs-fix`, the coordinator creates a new self-contained
implementation-only corrective task rather than reopening completed task
history. When the verdict is `intent-required`, the coordinator resolves intent
outside the task list before creating any required implementation correction.

### Advanced manual use

```text
Use idd-factory-review-work-result to review the completed current Factory run. Do not create the result handoff or clear current state.
```

The reviewer is read-only.

## `idd-factory-finish-work`

### Purpose

Finalize an approved Factory run.

It creates a collision-safe timestamped result directory, writes `commit-message.md`, verifies the file, and only then clears the current Factory workspace.

### Normal caller

`idd-factory-run`.

### Result

```text
.idd/factory/results/<work-slug>_<yyyy-MM-dd_HH-mm-ssZ>/commit-message.md
```

The timestamp is captured once in UTC during finalization. If the complete directory name already exists, Factory appends `-2`, then `-3`, and so on.

### Advanced manual use

Use it manually only when all tasks are completed, final review already approved the actual result, and the normal workflow was interrupted before finalization:

```text
Use idd-factory-finish-work to finish the approved current Factory run.
```

The skill stops without cleanup if it cannot confirm the preconditions.

## Task Files and Statuses

The current run is stored under:

```text
.idd/factory/current/
```

It contains `request.md`, optional `run-context.md`, and a flat numbered
implementation task sequence:

```text
request.md
run-context.md
001-first-outcome.completed.md
002-next-outcome.active.md
003-final-outcome.ready.md
```

`request.md` preserves the complete original request. `run-context.md` is
created only for compact context shared by multiple tasks. Each numbered task is
a self-contained local implementation and review contract when read with the
optional run context.

Factory tasks never own edits to `.idd/intent/`, intent workflow invocation, or
intent updates as completion conditions.

A task contains these required sections:

```text
Goal
Context
Scope
Requirements
Done When
Verification
```

It may also contain concrete `Out of Scope`, `Preservation Boundaries`,
`Dependencies`, and `Intent References` sections. Empty optional sections are
omitted. Tasks must not rely on vague references back to `request.md`.

The filename suffix is the only task-status source.

Supported states:

```text
ready
active
completed
blocked
```

Factory allows at most one active or blocked task. Completed tasks are not reopened; final-review corrections become new tasks.

## Choosing Factory or Focused Implementation

Use `idd-code-implement` when one bounded pass is sufficient:

```text
Use idd-code-implement to remove the obsolete overload and update its callers.
```

Use Factory when sequencing or review boundaries make a single pass unsafe:

```text
Use idd-factory-run to migrate the storage subsystem, update all consumers, preserve saved-data compatibility, and independently review the integrated result.
```

The number of changed files alone does not determine the choice.

## Product Intent Boundary

Factory may read `.idd/intent/`, but it must not invent product behavior.

When durable behavior is missing or contradictory, Factory returns:

```text
INTENT_REQUIRED
```

Before state creation, resolve the product decision through an `idd-intent`
workflow and repeat decomposition. During an existing run, the coordinator
resolves it outside the task list and then updates only implementation
contracts. Intent changes are never Factory task completions.

Factory requests, run context, task files, statuses, review findings, and commit-message handoffs remain temporary execution artifacts.
