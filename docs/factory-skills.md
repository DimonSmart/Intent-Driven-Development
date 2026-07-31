# IDD Factory Skills Reference

Most users should invoke only:

```text
idd-factory-run
```

The other Factory skills are bounded workers used by the coordinator. This page documents them for transparency, advanced inspection, and troubleshooting.

## Normal Workflow

```text
idd-factory-run
  → idd-factory-decompose-work
  → idd-factory-execute-task
  → idd-factory-review-task
  → repeat tasks when necessary
  → idd-factory-review-work-result
  → idd-factory-finish-work
```

The coordinator normally continues through this sequence automatically.

## `idd-factory-run`

### Purpose

Public entry point for starting, continuing, or cancelling one Factory run.

It owns workspace validation, clarification, task sequencing, worker dispatch, status transitions, review loops, final review, and finalization.

### Start a complete run

```text
Use idd-factory-run to implement the task described in ./ui-audit.md.
```

Or:

```text
Use idd-factory-run to replace the legacy storage implementation, migrate existing callers, preserve compatibility, and verify the result.
```

One invocation is expected to complete the workflow unless clarification, missing intent, an external blocker, or an unexpected interruption prevents progress.

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

It reads the active task, the original request, relevant current intent, the current diff, and repository evidence required for that task.

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

It checks the task goal, completion conditions, relevant intent, public contracts, implementation quality, verification evidence, and safety for later tasks.

### Normal caller

`idd-factory-run`.

### Verdicts

```text
approved
needs-fix
blocked
intent-required
```

For `needs-fix`, the coordinator keeps the task active, records the current actionable findings, and invokes implementation again.

### Advanced manual use

```text
Use idd-factory-review-task to review the current active Factory task. Do not modify code or task files.
```

The reviewer is read-only.

## `idd-factory-review-work-result`

### Purpose

Independently review the complete integrated result after all tasks are completed.

It checks the original request, every task goal, current product intent, the full diff, cross-task integration, preservation boundaries, public contracts, and verification sufficiency.

### Normal caller

`idd-factory-run`.

### Verdicts

```text
approved
needs-fix
blocked
intent-required
```

When the verdict is `needs-fix`, the coordinator creates a new corrective task rather than reopening completed task history.

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

It contains `request.md` and a flat numbered task sequence:

```text
001-first-outcome.completed.md
002-next-outcome.active.md
003-final-outcome.ready.md
```

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

When durable behavior is missing or contradictory, Factory stops with:

```text
INTENT_REQUIRED
```

Resolve the product decision through an `idd-intent` workflow before Factory continues.

Factory requests, task files, statuses, review findings, and commit-message handoffs remain temporary execution artifacts.
