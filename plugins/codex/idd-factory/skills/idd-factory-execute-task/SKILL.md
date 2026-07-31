---
name: idd-factory-execute-task
description: Implement exactly one explicit active Factory task from the request snapshot and current intent, then return focused verification evidence.
---

# idd-factory-execute-task

## Purpose

Implement one explicit `.active.md` task in an isolated worker context.

## Inputs

Read the active task (including resumed `Blocker`), optional `run-context.md`,
relevant intent, current diff, and focused repository evidence. Use project
skills normally. Do not read `request.md` or other task files; the coordinator
owns decomposition and must provide a sufficient active task contract.

## Rules

- Confirm the supplied task is the only active task and intent is sufficient.
- Inspect diff and evidence first; preserve completed work on resume.
- In explicit verification-only mode for an unchanged diff, preserve code and
  `Verified`, perform only `Not verified`, and leave the mode only for changed
  code or a newly revealed defect.
- Make the smallest coherent change, preserve named boundaries, add only affected
  tests, and run focused verification.
- Return `NEEDS_REPLAN` when the active task and run context are insufficient,
  contradictory, or require adjacent work outside the task contract. Name the
  minimum missing prerequisite or contract correction; do not inspect the
  original request or perform later tasks.
- Return `INTENT_REQUIRED` for unknown durable behavior and `BLOCKED` only for an
  external condition or non-intent user decision.
- Do not select or rename tasks, create Factory work, update intent, run final
  review, clean state, or prepare a commit message.

## Output

Return `DONE`, `NEEDS_REPLAN`, `BLOCKED`, or `INTENT_REQUIRED` with compact
`Implementation`, `Verification`, and `Concerns` sections.

For `NEEDS_REPLAN`, append `Dependency`. For `BLOCKED` or `INTENT_REQUIRED`,
append `Reason` and `Resume when`; when a user decision is needed, make
`Resume when` the exact question.

The coordinator owns task contents, status, `Completion`, and `Blocker`.
