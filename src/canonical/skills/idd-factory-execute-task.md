# idd-factory-execute-task

## Purpose

Implement exactly one explicit `.active.md` task in an isolated worker context.

## Inputs

Read the active task, including a resumed `Blocker`; `request.md`; only relevant
current intent; and repository evidence needed for this task. Use project skills
normally; Factory does not duplicate their catalog.

## Rules

- Confirm that exactly the supplied task is active and intent is sufficient.
- On resume, inspect the diff and evidence first and preserve completed work.
- In explicit verification-only mode for a confirmed unchanged diff, preserve
  code and `Verified` evidence and perform only `Not verified`. Leave this mode
  only if the code changed or new evidence reveals an implementation defect.
- Make the smallest coherent change satisfying the task and preservation
  boundaries. Add tests only for affected behavior or mechanical contracts.
- Run the task's focused verification.
- Return `NEEDS_REPLAN` when completion or verification requires adjacent work
  inside the Factory request but outside this task. Report the minimum
  prerequisite; do not implement later tasks.
- Return `INTENT_REQUIRED` for missing, unclear, or conflicting durable behavior.
- Return `BLOCKED` only for an external condition or non-intent user decision
  the worker cannot resolve safely.
- Do not select tasks, rename Factory files, create tasks, run final review,
  clean state, prepare a commit message, or update intent without its workflow.

## Output

Return `DONE`, `NEEDS_REPLAN`, `BLOCKED`, or `INTENT_REQUIRED`, followed by:

```text
Implementation:
<implemented or preserved result>

Verification:
<conclusive and missing evidence>

Concerns:
<none or material concern>
```

For `NEEDS_REPLAN`, also append:

```text
Dependency:
<minimum prerequisite outside the active task>
```

The coordinator owns task contents, status, `Completion`, and `Blocker`.
