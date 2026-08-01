# idd-factory-coordinate-step

## Purpose

Process exactly one logical action of the current Factory run in a fresh,
isolated coordinator context. `.idd/factory/current/` is the authoritative
memory between steps; never rely on a caller transcript for previous results.
This is an internal skill whose normal caller is `idd-factory-run`. When a
platform cannot nest fresh workers to the required depth, preserve the same
persisted one-step transition with the smallest platform-compatible dispatch;
do not claim isolation that the platform did not provide and do not introduce a
new Factory outcome.

## Fresh Context and Inputs

Receive only the repository/worktree path, instruction to continue the current
run, and, when applicable, a confirmed answer to the current blocker. Do not
inherit the original user conversation, previous coordinator messages, worker
transcripts, or detailed test logs.

First list `current/`, read optional `run-context.md`, and read the active or
lowest ready item. Validate filename-based state before changing it. Read
`request.md` only for replanning, confirmed clarification, intent orchestration,
or final integrated review. Read covered completed Subtasks only for a
checkpoint and all completed work items only for final review.

## One-Step Rules

- Process at most one Subtask, Review checkpoint, replanning action, intent
  orchestration action, or final-review/finalization action, then persist and
  return. After saving a result, do not begin the next work item.
- Filenames and only filenames are authoritative for `ready`, `active`,
  `completed`, and `blocked`. Stop as `CORRUPT_FACTORY_STATE` on invalid state;
  never guess repairs. Completed items are immutable.
- Activate the lowest ready item and process it in the same step. Only this
  coordinator may rename work-item files or alter their sequence.
- For an active Subtask, call `idd-factory-execute-subtask` in its existing
  isolated worker context. Persist `DONE`, `NEEDS_REPLAN`, `BLOCKED`, or
  `INTENT_REQUIRED` using the established Completion and Blocker contracts.
- For a Review checkpoint, call `idd-factory-review-checkpoint` in an
  independent reviewer context using only its `Covers` items and focused
  evidence. On `needs-fix`, atomically insert its self-contained correction
  before the checkpoint, update `Covers`, return the checkpoint to ready,
  renumber only active/ready items, validate, and stop the step.
- For `NEEDS_REPLAN` or `needs-replan`, verify the prerequisite belongs to the
  request and current intent, read only required active/ready contracts, make
  the minimum replan, preserve completed items, validate, persist, and stop.
- For `INTENT_REQUIRED` or `intent-required`, persist the blocker, perform the
  existing intent workflow outside the list, reread intent, update only
  affected active/ready contracts and `run-context.md`, persist, and stop. If a
  user decision is required, return its exact question.
- A blocked item without a new applicable answer must return its saved `Resume
  when` and must not dispatch later work. With an applicable answer, append it
  under `## Resolved Clarifications` in `request.md`, update affected active or
  ready contracts, reactivate only that item, process it, then stop.
- When all work items are completed, call `idd-factory-review-task` in a fresh
  independent context. On `approved`, immediately call `idd-factory-finalize-run`,
  verify `commit-message.md`, and return finished. On `needs-fix`, persist one
  new ready corrective Subtask and stop. Handle `blocked` and `intent-required`
  through their established boundaries.
- If a worker ends before returning a result, leave the item active. A later
  fresh step inspects diff and evidence and may use verification-only resume.

## Persist Before Return

Before `ADVANCED`, ensure Completion or Blocker, status filename, coverage,
corrective contract, and numbering are fully written and valid. Never return a
full worker report, file list, test log, work-item content, or prior history.

## Output

Return only one compact result:

```text
Step result: ADVANCED
Processed: <work-item filename or coordination action>
Persisted state: <compact resulting state>
Next: <next work item or final review>
```

```text
Step result: STOPPED
Factory outcome: <COMPLETED | FOCUSED_HANDOFF | NEEDS_CLARIFICATION | INTENT_REQUIRED | BLOCKED | CORRUPT_FACTORY_STATE>
Reason: <one compact reason>
Resume when: <exact condition or user question>
```

```text
Step result: FINISHED
Factory outcome: COMPLETED
Result: <commit-message path>
```

`ADVANCED`, `STOPPED`, and `FINISHED` are internal step results, never Factory
outcomes.
