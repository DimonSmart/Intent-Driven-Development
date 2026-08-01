# IDD Factory Skills Reference

When present, `.idd/verification.md` assigns `subtask` checks to Subtasks,
`checkpoint` checks to Review checkpoints, and `final` checks to final Task
review. A Subtask never broadens its own checks.

Most users should invoke only:

```text
idd-factory-run
```

The other Factory skills are bounded workers used by the coordinator. This page
documents them for transparency, advanced inspection, and troubleshooting.

## Normal Workflow

```text
idd-factory-run
  → idd-factory-decompose-task (intent preflight)
  → when INTENT_REQUIRED:
      idd-intent workflow
      idd-factory-decompose-task again
  → create ordered Subtasks and Review checkpoints
  → fresh idd-factory-coordinate-step (one action, persisted)
  → fresh idd-factory-coordinate-step (next action, persisted)
  → … until final review and finalization
```

Execution completion does not automatically invoke independent review.
Checkpoints are explicit work items, and the final integrated review remains
mandatory.

Each fresh coordinator step performs one action and uses persisted Factory
state as its only memory. If it cannot dispatch the required specialized
worker, it returns resumable `BLOCKED` without taking that worker's scope.

## `idd-factory-run`

### Purpose

Public entry point for starting, continuing, or cancelling one Factory run.

It owns intent preflight, workspace validation, clarification, initial state,
cancel semantics, and dispatch of fresh one-step coordinators. Persisted Factory
state, rather than its context, carries sequencing, status transitions,
correction insertion, final review, and finalization between steps.

### Start a complete run

```text
Use idd-factory-run to implement the task described in ./ui-audit.md.
```

Or:

```text
Use idd-factory-run to replace the legacy storage implementation, migrate
existing callers, preserve compatibility, and verify the result.
```

Before state creation, Factory resolves missing durable intent and repeats
decomposition. The saved sequence contains implementation-only Subtasks
and optional Review checkpoints.

### Continue an interrupted run

```text
Continue the current IDD Factory work.
```

Factory dispatches a fresh step coordinator, which identifies the active item
from persisted state and resumes the corresponding worker.

## `idd-factory-coordinate-step`

### Purpose

Internal one-step coordinator. It restores `.idd/factory/current/`, performs
one Subtask, checkpoint, replan, intent action, or final-review/finalization
operation, saves the result atomically, returns a compact result, and ends.
It never begins the next work item after saving state.

### Normal caller

`idd-factory-run`. Normal users do not invoke it directly; manual use is only
for advanced troubleshooting.

### Cancel the current run

```text
Cancel the current IDD Factory work.
```

Cancellation removes temporary current state but does not revert code changes.

## `idd-factory-decompose-task`

### Purpose

Analyze one request and return the smallest safe ordered set of Subtasks
and Review checkpoints.

It determines whether the request is ready, small enough for focused
implementation, blocked by missing clarification, blocked by missing intent, or
blocked by another condition.

Subtasks are small self-contained implementation contracts. Review
checkpoints are separate boundaries that may cover several adjacent Subtasks.

The decomposer uses the fewest checkpoints that protect dependent later work. It
does not add a terminal checkpoint that only duplicates final review.

### Normal caller

`idd-factory-run`.

### Advanced manual use

```text
Use idd-factory-decompose-task to show how ./migration-plan.md would be divided
into Subtasks and Review checkpoints. Do not execute them.
```

The skill does not write Factory state, code, tests, or product intent.

## `idd-factory-execute-subtask`

### Purpose

Implement exactly one active Subtask.

It reads the active self-contained Subtask, optional shared `run-context.md`,
relevant current intent, current diff, and focused repository evidence. It does
not read the original request, checkpoints, or other Subtasks.

A successful result returns compact `Implementation`, `Changes`,
`Verification`, and `Concerns`. The coordinator completes the Subtask
without invoking `idd-factory-review-checkpoint`.

### Normal caller

`idd-factory-coordinate-step`.

### Advanced manual use

```text
Use idd-factory-execute-subtask for
.idd/factory/current/002-update-storage.active.md only. Do not advance the
Factory workflow.
```

The skill does not select another item, rename files, perform checkpoint or final
review, create a commit message, or clean the workspace.

## `idd-factory-review-checkpoint`

### Purpose

Independently review one active Review checkpoint.

It reviews only its active Review checkpoint and covered completed Subtasks.

It reads the checkpoint, the completed Subtasks listed in `Covers`,
optional shared run context, relevant current intent, and checkpoint-local diff
and verification evidence. It does not read the original request, unrelated
tasks, or later work.

### Verdicts

```text
approved
needs-fix
needs-replan
blocked
intent-required
```

For `needs-fix`, the reviewer returns one complete self-contained corrective
Subtask. The coordinator inserts it immediately before the checkpoint,
updates checkpoint coverage, and reviews the group again after correction.

For `needs-replan`, the coordinator corrects coverage, checkpoint placement,
contracts, or ordering.

### Advanced manual use

```text
Use idd-factory-review-checkpoint to review the current active Review checkpoint. Do
not modify code or Factory files.
```

The reviewer is read-only.

## `idd-factory-review-task`

### Purpose

Independently review the complete integrated result after all Subtasks and
Review checkpoints are completed.

It checks the original request, optional run context, every Subtask
contract and completion, every checkpoint result, current product intent, the
full diff, cross-task integration, preservation boundaries, public contracts,
and verification sufficiency.

This stage verifies that decomposition did not omit requirements and that grouped
checkpoint reviews did not hide incomplete integration.

### Verdicts

```text
approved
needs-fix
blocked
intent-required
```

When the verdict is `needs-fix`, the coordinator appends one self-contained
corrective Subtask. The next final review is its review gate; Factory does
not add a redundant terminal checkpoint.

### Advanced manual use

```text
Use idd-factory-review-task to review the completed current Factory run.
Do not create the result handoff or clear current state.
```

The reviewer is read-only.

## `idd-factory-finalize-run`

### Purpose

Finalize an approved Factory run.

It creates a collision-safe timestamped result directory, writes
`commit-message.md`, verifies the file, and only then clears the current Factory
workspace.

### Result

```text
.idd/factory/results/<work-slug>_<yyyy-MM-dd_HH-mm-ssZ>/commit-message.md
```

The timestamp is captured once in UTC. If the complete directory name already
exists, Factory appends `-2`, then `-3`, and so on.

## Work Items and Statuses

The current run is stored under:

```text
.idd/factory/current/
```

It contains `request.md`, optional `run-context.md`, and a flat numbered work
sequence:

```text
request.md
run-context.md
001-create-foundation.completed.md
002-migrate-consumer.completed.md
003-review-foundation.active.md
004-finish-migration.ready.md
```

The filename suffix is the only status source.

Supported states:

```text
ready
active
completed
blocked
```

Factory allows at most one active or blocked item.

### Subtask format

A Subtask contains:

```text
Goal
Context
Scope
Requirements
Done When
Verification
```

It may also contain concrete `Out of Scope`, `Preservation Boundaries`,
`Dependencies`, and `Intent References`. It never owns intent changes.

Subtask `Completion` contains:

```text
Result
Changes
Verification
Concerns
```

### Review checkpoint format

A checkpoint contains:

```text
Review Checkpoint
Covers
Review Scope
Verification
```

It may also contain `Intent References`. `Covers` names a contiguous group of
preceding completed Subtasks since the previous checkpoint.

Checkpoint `Completion` contains:

```text
Result
Verification
Concerns
```

A checkpoint never covers another checkpoint.

On `needs-fix`, completed tasks remain immutable. Factory inserts a new
corrective Subtask before the checkpoint and adds it to `Covers`.

## Choosing Checkpoints

Use a checkpoint when later work should not proceed without independent review
of a risky earlier result, such as:

- a foundational abstraction;
- a public contract;
- persisted-data compatibility;
- security or concurrency behavior;
- a grouped migration with meaningful regression risk.

Group adjacent mechanical changes under one checkpoint. Omit checkpoints where
the mandatory final integrated review is sufficient. Never create a final
checkpoint solely to duplicate final review.

## Choosing Factory or Focused Implementation

Use `idd-code-implement` when one bounded pass is sufficient.

Use Factory when sequencing, small execution contexts, explicit checkpoint
boundaries, or integrated review make one pass unsafe.

The number of changed files alone does not determine the choice.

## Product Intent Boundary

Factory may read `.idd/intent/`, but it must not invent or change product
behavior inside a Subtask.

When durable behavior is missing or contradictory, Factory returns:

```text
INTENT_REQUIRED
```

Before state creation, resolve intent and repeat decomposition. During an
existing run, the coordinator resolves intent outside the work-item list and
updates only implementation contracts and checkpoints.

Factory requests, shared context, Subtasks, checkpoints, blockers,
completions, and commit-message handoffs remain temporary execution artifacts.
