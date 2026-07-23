# idd-factory-execute-task

## Purpose

Implement exactly one explicit `.active.md` task from the current Factory run.
This is an isolated worker operation, not a coordinator.

## Inputs

Read:

- the explicit active task;
- `.idd/factory/current/request.md`;
- `.idd/intent/README.md`, `.idd/intent/INDEX.md`, and only relevant current
  intent;
- current diff and repository evidence needed for this task.

Use applicable project skills and instructions through the Coding Agent's
normal skill mechanism. Factory does not duplicate their catalog.

## Rules

- Confirm that exactly the supplied task is active and that required intent is
  sufficient.
- On resume, inspect the current diff and verification evidence before editing;
  do not duplicate already completed work.
- Make the smallest coherent implementation that satisfies the task goal and
  preservation boundaries.
- Add or update tests only where they protect affected behavior or a mechanical
  contract.
- Run focused verification from the task.
- Stop with `INTENT_REQUIRED` when implementation needs missing, unclear, or
  contradictory durable behavior.
- Return `BLOCKED` for another concrete condition the worker cannot safely
  resolve.
- Do not select another task, rename task files, create Factory tasks, perform
  final review, clean the workspace, create a commit message, or change
  `.idd/intent/` unless a separate intent workflow is explicitly invoked.
- Do not broaden scope to adjacent work unless the active task cannot otherwise
  satisfy current intent.

## Output

Return `DONE`, `BLOCKED`, or `INTENT_REQUIRED`, followed by a compact summary of
the implemented result, focused verification, and material concerns. Do not
write task status or completion sections; the coordinator owns Factory state.
