# idd-factory-coordinate-step

## Required Reference

Read `references/project-verification.md` before coordinating a worker whose
verification is unresolved or falls back to repository/platform checks.

## Purpose

Process exactly one logical action of the current Factory run in a fresh,
isolated coordinator context. `.idd/factory/current/` is the authoritative
memory between steps; never rely on a caller transcript for previous results.
This is an internal skill whose normal caller is `idd-factory-run`.

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

If a read-only command is rejected by execution policy or fails because of its
form, make at most two alternative attempts. Each must be narrower and simpler:
first split a compound command, then remove recursion or wildcards, then read a
specific directory or file; an equivalent read-only tool is allowed. Never
repeat the same command, elevate permissions, change approval or sandbox policy,
or switch to writing. Return `BLOCKED` only after these alternatives are
exhausted and the information remains required. Persist only `Reason`, `Not verified`, and
`Resume when` in that blocker.

## One-Step Rules

- Process at most one Subtask, Review checkpoint, replanning action, intent
  orchestration action, or final-review/finalization action, then persist and
  return. After saving a result, do not begin the next work item.
- Filenames and only filenames are authoritative for `ready`, `active`,
  `completed`, and `blocked`. Stop as `CORRUPT_FACTORY_STATE` on invalid state;
  never guess repairs. Completed items are immutable.
- Activate the lowest ready item and process it in the same step. Only this
  coordinator may rename work-item files or alter their sequence.
- For implementation, checkpoint review, or final review, dispatch only the
  corresponding specialized Factory skill and apply its result contract. Do
  not duplicate that worker's detailed scope or perform it here.
- A Subtask becomes completed only when its required verification is confirmed.
  Otherwise persist its Blocker and return `BLOCKED`.
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
- Apply the persisted transition required for checkpoint correction or final
  review, then stop. Completed items remain unchanged.
- If a required specialized worker cannot be dispatched or ends without a
  result, preserve the current item and return `BLOCKED` with the actual reason.
  Do not implement or review in this coordinator context.

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
