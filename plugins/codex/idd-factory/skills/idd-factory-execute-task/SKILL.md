---
name: idd-factory-execute-task
description: Implement exactly one explicit active Factory execution task from its self-contained contract and current intent, then return focused changes and verification evidence.
---

# idd-factory-execute-task

## Purpose

Implement one explicit active execution task in an isolated worker context.

## Inputs

Read the active execution task (including resumed `Blocker`), optional
`run-context.md`, relevant intent, current diff, and focused repository evidence.
Use project skills normally. Do not read `request.md`, checkpoints, or other
execution tasks; the coordinator owns decomposition and must provide a sufficient
local contract.

## Rules

- Confirm the supplied item is the only active item and is an execution task, not
  a review checkpoint.
- Confirm current intent is sufficient.
- If the item asks to edit `.idd/intent/`, invoke an intent-changing workflow, or
  own an intent update, return `NEEDS_REPLAN`; do not perform that scope.
- Inspect diff and evidence first; preserve completed work on resume.
- In explicit verification-only mode for an unchanged diff, preserve code and
  `Verified`, perform only `Not verified`, and leave the mode only for changed
  code or a newly revealed defect.
- Make the smallest coherent change, preserve named boundaries, add only affected
  tests, and run task-focused verification.
- Do not run broad checkpoint or final integrated verification unless the task
  contract explicitly requires it for safe completion.
- Return `NEEDS_REPLAN` when the task and run context are insufficient,
  contradictory, contain intent-editing scope, or require adjacent work outside
  the task. Name the minimum prerequisite or contract correction; do not inspect
  the original request or perform later tasks.
- Return `INTENT_REQUIRED` only for missing durable behavior discovered while
  implementing current intent.
- Return `BLOCKED` only for an external condition or non-intent user decision.
- Do not select or rename items, create Factory work, update intent, run a review
  checkpoint or final review, clean state, or prepare a commit message.

## Output

Return `DONE`, `NEEDS_REPLAN`, `BLOCKED`, or `INTENT_REQUIRED`.

For `DONE`, return compact sections:

```text
Implementation:
Changes:
Verification:
Concerns:
```

`Changes` lists only paths, public symbols, contracts, or other evidence needed
to focus a later review checkpoint.

For `NEEDS_REPLAN`, append `Dependency`. For `BLOCKED` or `INTENT_REQUIRED`,
append `Reason` and `Resume when`; when a user decision is needed, make
`Resume when` the exact question.

The coordinator owns item contents, status, `Completion`, and `Blocker`.
