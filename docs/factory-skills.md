# IDD Factory Skills Reference

When present, `.idd/verification.md` assigns `subtask` checks to execution
subtasks, `checkpoint` checks to reviews, and `final` checks to integrated
review. An execution subtask never broadens its own checks.

Most users should invoke only:

```text
idd-factory-run
```

The other Factory skills are bounded workers used by the coordinator. This page
documents them for transparency, advanced inspection, and troubleshooting.

## Normal Workflow

```text
idd-factory-run
  → idd-factory-decompose-work (intent preflight)
  → when INTENT_REQUIRED:
      idd-intent workflow
      idd-factory-decompose-work again
  → create ordered execution tasks and review checkpoints
  → idd-factory-execute-task for each execution item
  → idd-factory-review-task only at checkpoints
  → repeat corrections when necessary
  → idd-factory-review-work-result
  → idd-factory-finish-work
```

Execution completion does not automatically invoke independent review.
Checkpoints are explicit work items, and the final integrated review remains
mandatory.

## `idd-factory-run`

### Purpose

Public entry point for starting, continuing, or cancelling one Factory run.

It owns intent preflight, workspace validation, clarification, execution-task
sequencing, checkpoint dispatch, status transitions, correction insertion, final
review, and finalization.

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
decomposition. The saved sequence contains implementation-only execution tasks
and optional review checkpoints.

### Continue an interrupted run

```text
Continue the current IDD Factory work.
```

Factory identifies whether the active item is an execution task or review
checkpoint and resumes the corresponding worker.

### Cancel the current run

```text
Cancel the current IDD Factory work.
```

Cancellation removes temporary current state but does not revert code changes.

## `idd-factory-decompose-work`

### Purpose

Analyze one request and return the smallest safe ordered set of execution tasks
and review checkpoints.

It determines whether the request is ready, small enough for focused
implementation, blocked by missing clarification, blocked by missing intent, or
blocked by another condition.

Execution tasks are small self-contained implementation contracts. Review
checkpoints are separate boundaries that may cover several adjacent execution
tasks.

The decomposer uses the fewest checkpoints that protect dependent later work. It
does not add a terminal checkpoint that only duplicates final review.

### Normal caller

`idd-factory-run`.

### Advanced manual use

```text
Use idd-factory-decompose-work to show how ./migration-plan.md would be divided
into execution tasks and review checkpoints. Do not execute them.
```

The skill does not write Factory state, code, tests, or product intent.

## `idd-factory-execute-task`

### Purpose

Implement exactly one active execution task.

It reads the active self-contained task, optional shared `run-context.md`,
relevant current intent, current diff, and focused repository evidence. It does
not read the original request, checkpoints, or other execution tasks.

A successful result returns compact `Implementation`, `Changes`,
`Verification`, and `Concerns`. The coordinator completes the execution task
without invoking `idd-factory-review-task`.

### Normal caller

`idd-factory-run`.

### Advanced manual use

```text
Use idd-factory-execute-task for
.idd/factory/current/002-update-storage.active.md only. Do not advance the
Factory workflow.
```

The skill does not select another item, rename files, perform checkpoint or final
review, create a commit message, or clean the workspace.

## `idd-factory-review-task`

### Purpose

Independently review one active review checkpoint.

The skill name is retained for compatibility. It does not review every execution
task.

It reads the checkpoint, the completed execution tasks listed in `Covers`,
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
execution task. The coordinator inserts it immediately before the checkpoint,
updates checkpoint coverage, and reviews the group again after correction.

For `needs-replan`, the coordinator corrects coverage, checkpoint placement,
contracts, or ordering.

### Advanced manual use

```text
Use idd-factory-review-task to review the current active review checkpoint. Do
not modify code or Factory files.
```

The reviewer is read-only.

## `idd-factory-review-work-result`

### Purpose

Independently review the complete integrated result after all execution tasks and
review checkpoints are completed.

It checks the original request, optional run context, every execution-task
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
corrective execution task. The next final review is its review gate; Factory does
not add a redundant terminal checkpoint.

### Advanced manual use

```text
Use idd-factory-review-work-result to review the completed current Factory run.
Do not create the result handoff or clear current state.
```

The reviewer is read-only.

## `idd-factory-finish-work`

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

### Execution task format

An execution task contains:

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

Execution-task `Completion` contains:

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
preceding completed execution tasks since the previous checkpoint.

Checkpoint `Completion` contains:

```text
Result
Verification
Concerns
```

A checkpoint never covers another checkpoint.

On `needs-fix`, completed tasks remain immutable. Factory inserts a new
corrective execution task before the checkpoint and adds it to `Covers`.

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
behavior inside an execution task.

When durable behavior is missing or contradictory, Factory returns:

```text
INTENT_REQUIRED
```

Before state creation, resolve intent and repeat decomposition. During an
existing run, the coordinator resolves intent outside the work-item list and
updates only implementation contracts and checkpoints.

Factory requests, shared context, execution tasks, checkpoints, blockers,
completions, and commit-message handoffs remain temporary execution artifacts.
