# idd-factory-execute-task

For an execution subtask, resolve its recorded IDs from the current
`.idd/verification.md` using context `subtask`. Run exactly those recorded IDs;
do not add checks selected only for checkpoint or final contexts. A
missing/changed referenced ID blocks the item. Confirmation refusal, user
instructions without confirmation, and unavailable checks are `Not verified`.
If any assigned check remains `Not verified`, return `BLOCKED`, never `DONE`,
with `Reason`, `Verified`, `Not verified`, and `Resume when`. If actual changes
escape the contracted verification scope, return `NEEDS_REPLAN`; do not expand
checks yourself.

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
  tests, and run exactly the check IDs assigned to the execution subtask.
- Do not add checks selected only for checkpoint or final contexts, even when a
  broader command appears useful.
- Return `BLOCKED`, not `DONE`, whenever any assigned verification check remains
  `Not verified`.
- Return `NEEDS_REPLAN` when the task and run context are insufficient,
  contradictory, contain intent-editing scope, require adjacent work outside
  the task, or the actual changed scope escapes the contracted verification
  scope. Name the minimum prerequisite or contract correction; do not inspect
  the original request or perform later tasks.
- Return `INTENT_REQUIRED` only for missing durable behavior discovered while
  implementing current intent.
- Return `BLOCKED` only for an external condition, a required verification result
  that is not yet available, or a non-intent user decision.
- Do not select or rename items, create Factory work, update intent, run a review
  checkpoint or final review, clean state, or prepare a commit message.

## Output

Return `DONE`, `NEEDS_REPLAN`, `BLOCKED`, or `INTENT_REQUIRED`.

Return `DONE` only when implementation is complete and every assigned check has
conclusive evidence. For `DONE`, return compact sections:

```text
Implementation:
Changes:
Verification:
Concerns:
```

`Changes` lists only paths, public symbols, contracts, or other evidence needed
to focus a later review checkpoint.

For `NEEDS_REPLAN`, append `Dependency`.

For `BLOCKED`, append:

```text
Reason:
Verified:
Not verified:
Resume when:
```

For `INTENT_REQUIRED`, append `Reason` and `Resume when`; when a user decision is
needed, make `Resume when` the exact question.

The coordinator owns item contents, status, `Completion`, and `Blocker`.
